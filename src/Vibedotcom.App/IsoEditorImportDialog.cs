using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

internal sealed class IsoEditorImportDialog : Window
{
    private readonly IsoEditorSession session;
    private readonly TextBox path=new();
    private readonly TextBlock metadata=new() { TextWrapping=TextWrapping.Wrap };
    private readonly StackPanel issues=new();
    private readonly Button import;
    private readonly CancellationTokenSource cancellation=new();
    private int generation;
    private IsoCustomValidationPreview? verified;
    private readonly Image thumbnail=new() { Width=128,Height=128 };
    internal bool Validated { get; private set; }
    internal ImmutableArray<IsoValidationIssue> ValidationIssues { get; private set; } = [];
    internal IsoEditorImportDialog(IsoEditorSession session)
    {
        this.session=session; Title="Import custom object"; Width=540; Height=560; MinWidth=360; MinHeight=300;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; FontFamily=SystemFonts.MessageFontFamily; FontSize=SystemFonts.MessageFontSize;
        var body=new StackPanel { Margin=new(16) }; Content=new ScrollViewer { Content=body,Background=SystemColors.WindowBrush,Foreground=SystemColors.WindowTextBrush,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text="Choose a local .iso.json and validate it before importing.",TextWrapping=TextWrapping.Wrap });
        IsoEditorPanel.SetAutomation(path,"Local iso object manifest"); path.MinHeight=28; body.Children.Add(path);
        body.Children.Add(IsoEditorPanel.Button("Choose manifest",Choose));
        body.Children.Add(IsoEditorPanel.Button("Validate preview",async ()=>await ValidateAsync()));
        body.Children.Add(thumbnail); IsoEditorPanel.SetAutomation(thumbnail,"Verified custom object thumbnail");
        body.Children.Add(metadata); IsoEditorPanel.SetAutomation(metadata,"Object validation preview");
        body.Children.Add(issues); IsoEditorPanel.SetAutomation(issues,"Import validation issues"); AutomationProperties.SetLiveSetting(issues,AutomationLiveSetting.Polite);
        import=IsoEditorPanel.Button("Import",async ()=>await ImportAsync()); import.IsEnabled=false; body.Children.Add(import);
        path.TextChanged+=(_,_)=> { generation++; verified=null; thumbnail.Source=null; Validated=false; import.IsEnabled=false; };
        body.Children.Add(IsoEditorPanel.Button("Cancel",Close)); Closed+=(_,_)=>cancellation.Cancel();
    }
    private void Choose()
    {
        var picker=new OpenFileDialog { Filter="Iso object manifest (*.iso.json)|*.iso.json",CheckFileExists=true,Multiselect=false };
        if (picker.ShowDialog(this)==true) path.Text=picker.FileName;
    }
    internal async Task ValidateAsync(string? manifest=null)
    {
        if (manifest!=null) path.Text=manifest;
        var captured=path.Text; var version=++generation;
        Validated=false; import.IsEnabled=false;
        var preview=await Task.Run(()=>session.Importer.ValidatePreview(captured,cancellation.Token));
        var result=preview.Issues;
        if (cancellation.IsCancellationRequested || version!=generation) return;
        ValidationIssues=result; Validated=!result.Any(i=>i.Severity==IsoIssueSeverity.Error);
        ShowIssues(result);
        verified=Validated ? preview : null;
        metadata.Text=Validated ? $"{(session.Privacy==PrivacyMode.Off ? preview.DisplayName : "Custom object")} · {preview.VariantCount} variants\n{preview.SourceIdentityHash}" : "✕ Import blocked; fix the listed rules.";
        if (Validated)
        {
            using var bytes=new MemoryStream(preview.ThumbnailPng.ToArray(),false);
            var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=bytes; image.EndInit(); image.Freeze();
            thumbnail.Source=image;
        }
        import.IsEnabled=Validated;
    }
    internal async Task<IsoCustomObjectResult?> ImportAsync()
    {
        if (!Validated || cancellation.IsCancellationRequested) return null;
        if (session.CommitPack==null)
        {
            metadata.Text="✕ Import is not available in this build.";
            return null;
        }
        var captured=path.Text; var version=generation; import.IsEnabled=false;
        var request=new IsoCustomImportRequest(captured,session.Paths,session.Catalog,session.BuiltInPack,verified);
        var plan=await Task.Run(()=>session.Importer.PrepareImport(request,cancellation.Token));
        if (cancellation.IsCancellationRequested || version!=generation) return null;
        var result=session.CommitPack(plan,cancellation.Token);
        if (result.Succeeded) metadata.Text="✓ Imported custom object";
        session.Report(result.Issues); ShowIssues(result.Issues); return result;
    }
    private void ShowIssues(ImmutableArray<IsoValidationIssue> result)
    {
        issues.Children.Clear();
        foreach (var issue in result)
            issues.Children.Add(new TextBlock { Text=$"{(issue.Severity==IsoIssueSeverity.Error ? "✕" : "⚠")} {issue.RuleId} {session.IssueText(issue)}",TextWrapping=TextWrapping.Wrap,Margin=new(0,4,0,4) });
    }
}

internal sealed class IsoEditorManageDialog : Window
{
    private readonly IsoEditorSession session;
    private readonly StackPanel list=new();
    internal IsoEditorManageDialog(IsoEditorSession session)
    {
        this.session=session; Title="Manage custom objects"; Width=480; Height=440;
        FontFamily=SystemFonts.MessageFontFamily; FontSize=SystemFonts.MessageFontSize;
        Content=new ScrollViewer { Content=list,Background=SystemColors.WindowBrush,Foreground=SystemColors.WindowTextBrush,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(16) };
        Refresh();
    }
    private void Refresh()
    {
        list.Children.Clear(); ImmutableArray<string> references;
        try { references=session.RemovalReferences(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException or System.Collections.Generic.KeyNotFoundException)
        { list.Children.Add(new TextBlock { Text="✕ Cannot read saved/backup references. Removal is blocked.",TextWrapping=TextWrapping.Wrap }); return; }
        foreach (var entry in session.Catalog.Objects)
        {
            var inUse=references.Contains(entry.AssetId);
            var name=session.Privacy==PrivacyMode.Off ? entry.Manifest.DisplayName : entry.AssetId;
            list.Children.Add(new TextBlock { Text=$"{name} · {entry.AssetId}\n{(inUse ? "In use: live, draft, history, saved or backup layout" : "Unused")}",TextWrapping=TextWrapping.Wrap });
            var button=IsoEditorPanel.Button("Remove "+entry.AssetId,()=> { session.Remove(entry.AssetId); Refresh(); });
            button.IsEnabled=!inUse && session.CommitPack!=null; list.Children.Add(button);
        }
        foreach (var issue in session.Issues) list.Children.Add(new TextBlock { Text=issue.RuleId+" "+session.IssueText(issue),TextWrapping=TextWrapping.Wrap });
        list.Children.Add(IsoEditorPanel.Button("Close",Close));
    }
}
