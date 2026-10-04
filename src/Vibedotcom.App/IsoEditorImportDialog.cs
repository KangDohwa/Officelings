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
    internal bool Validated { get; private set; }
    internal ImmutableArray<IsoValidationIssue> ValidationIssues { get; private set; } = [];
    internal IsoEditorImportDialog(IsoEditorSession session)
    {
        this.session=session; Title="Import custom object"; Width=540; Height=560; MinWidth=360; MinHeight=300;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; FontFamily=SystemFonts.MessageFontFamily; FontSize=SystemFonts.MessageFontSize;
        var body=new StackPanel { Margin=new(16) }; Content=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text="Choose a local .iso.json and validate it before importing.",TextWrapping=TextWrapping.Wrap });
        IsoEditorPanel.SetAutomation(path,"Local iso object manifest"); path.MinHeight=28; body.Children.Add(path);
        body.Children.Add(IsoEditorPanel.Button("Choose manifest",Choose));
        body.Children.Add(IsoEditorPanel.Button("Validate preview",async ()=>await ValidateAsync()));
        body.Children.Add(metadata); IsoEditorPanel.SetAutomation(metadata,"Object validation preview");
        body.Children.Add(issues); IsoEditorPanel.SetAutomation(issues,"Import validation issues"); AutomationProperties.SetLiveSetting(issues,AutomationLiveSetting.Polite);
        import=IsoEditorPanel.Button("Import",async ()=>await ImportAsync()); import.IsEnabled=false; body.Children.Add(import);
        path.TextChanged+=(_,_)=> { generation++; Validated=false; import.IsEnabled=false; };
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
        var result=await Task.Run(()=>session.Importer.Validate(captured));
        if (cancellation.IsCancellationRequested || version!=generation) return;
        ValidationIssues=result; Validated=!result.Any(i=>i.Severity==IsoIssueSeverity.Error);
        ShowIssues(result);
        metadata.Text=Validated ? "✓ Validation passed. A verified object image preview is not available yet." : "✕ Import blocked; fix the listed rules.";
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
        var captured=path.Text; import.IsEnabled=false;
        var request=new IsoCustomImportRequest(captured,session.Paths,session.Catalog,session.BuiltInPack);
        var result=await Task.Run(()=>session.Importer.Import(request));
        if (result.Succeeded)
        {
            var merged=await Task.Run(()=>session.Importer.Merge(session.BuiltInPack,result.Catalog));
            if (merged.Succeeded && merged.Pack!=null)
            {
                var commitIssues=session.CommitPack(merged.Pack,result.Catalog);
                if (!commitIssues.Any(i=>i.Severity==IsoIssueSeverity.Error))
                { session.AdoptPack(merged.Pack,result.Catalog); metadata.Text="✓ Imported custom object"; }
                else result=new(false,session.Catalog,commitIssues);
            }
            else result=new(false,session.Catalog,merged.Issues);
        }
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
        Content=new ScrollViewer { Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(16) };
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
