using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

internal sealed class IsoEditorPanel : ScrollViewer, IDisposable
{
    internal static readonly Color Ink=Color.FromRgb(0x17,0x2B,0x30), Accent=Color.FromRgb(0x18,0x5A,0x75);
    private readonly IsoEditorSession session;
    private readonly StackPanel body=new() { Margin=new(12) };
    private readonly TextBox search=new(), x=new(), y=new(), desk=new(), chair=new(), computer=new();
    private readonly ComboBox rotation=new(), state=new(), hosts=new();
    private readonly ListBox palette=new();
    private readonly StackPanel issueList=new();
    private readonly TextBlock selection=new(), status=new(), seats=new(), coordinateError=new() { TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick };
    private readonly Button save,undo,redo;
    private IIsoSheetSource sheets;
    private IsoEditorThumbnails thumbnails;
    private bool refreshing;
    internal event Action? Done;
    internal event Action? Import;
    internal event Action? Manage;
    internal event Action<string>? Navigate;
    internal IEnumerable<Button> Buttons => Descendants(body).OfType<Button>();

    internal IsoEditorPanel(IsoEditorSession session,IIsoSheetSource sheets,double textScale=1)
    {
        this.session=session; this.sheets=sheets;
        thumbnails=new(session.Pack,sheets);
        Background=Brushes.White; Foreground=new SolidColorBrush(Ink);
        FontFamily=SystemFonts.MessageFontFamily; FontSize=SystemFonts.MessageFontSize*textScale;
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto; HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        Content=body; MinWidth=300; KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Continue);
        SetAutomation(this,"Iso office editor","Tools, objects, selection, actions, history, import, validation and Done editing.");
        var tools=new WrapPanel();
        foreach (var tool in Enum.GetValues<IsoEditorTool>())
        {
            var label=tool switch { IsoEditorTool.Select=>"Select/move",IsoEditorTool.Place=>"Place",IsoEditorTool.WorkPosition=>"Work position",_=>"Wall decor" };
            tools.Children.Add(Button(label,()=> { session.SetTool(tool); RefreshPalette(); }));
        }
        body.Children.Add(tools);
        Field("Search objects",search); search.TextChanged+=(_,_)=>RefreshPalette();
        SetAutomation(palette,"Object palette","One entry per furniture family or complete wall decoration.");
        palette.MaxHeight=220; palette.MinHeight=100; palette.SelectionChanged+=(_,_)=>PaletteSelected(); body.Children.Add(palette);
        selection.TextWrapping=TextWrapping.Wrap; body.Children.Add(selection); SetAutomation(selection,"Selected object");
        var coordinates=new Grid(); coordinates.ColumnDefinitions.Add(new()); coordinates.ColumnDefinitions.Add(new());
        var columnX=new StackPanel(); var columnY=new StackPanel(); Grid.SetColumn(columnY,1);
        Field("X coordinate",x,columnX); Field("Y coordinate",y,columnY); coordinates.Children.Add(columnX); coordinates.Children.Add(columnY); body.Children.Add(coordinates);
        Field("Rotation",rotation); Field("State",state); Field("Wall host",hosts);
        rotation.SelectionChanged+=(_,_)=>VariantSelected(); state.SelectionChanged+=(_,_)=>VariantSelected();
        hosts.SelectionChanged+=(_,_)=> { if (!refreshing && hosts.SelectedItem is IsoWallSegment host) session.SetHost(host.Id); };
        hosts.DisplayMemberPath=nameof(IsoWallSegment.Id);
        Row(Button("Preview coordinates",PreviewCoordinates),Button("Apply coordinates",()=> { PreviewCoordinates(); session.CommitPreview(); }));
        body.Children.Add(coordinateError); SetAutomation(coordinateError,"Coordinate input error");
        Field("Desk ID",desk); Field("Aligned chair ID",chair); Field("Computer ID (empty = embedded)",computer);
        Row(Button("Preview work position",PreviewSeat),Button("Enable work position",()=> { PreviewSeat(); session.CommitPreview(); }),
            Button("Clear work position",()=> { var id=string.IsNullOrWhiteSpace(desk.Text) ? session.Selection : desk.Text; if (id!=null) session.Execute(new IsoClearSeatCommand(id)); }));
        seats.TextWrapping=TextWrapping.Wrap; body.Children.Add(seats); SetAutomation(seats,"Enabled work positions");
        Row(Button("Rotate (R)",session.RotateSelection),Button("Delete (Delete)",()=>Command(id=>new IsoDeleteCommand(id))),
            Button("Duplicate (Ctrl+D)",session.Duplicate));
        undo=Button("Undo (Ctrl+Z)",session.Undo); redo=Button("Redo (Ctrl+Y)",session.Redo); Row(undo,redo);
        Row(Button("Import custom object",()=>Import?.Invoke()),Button("Manage custom objects",()=>Manage?.Invoke()));
        save=Button("Save (Ctrl+S)",session.Save); Row(Button("Validate",session.Validate),save,Button("Reset default",session.Reset));
        status.TextWrapping=TextWrapping.Wrap; status.Margin=new(0,8,0,8); body.Children.Add(status);
        SetAutomation(status,"Validation status"); AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
        body.Children.Add(issueList); SetAutomation(issueList,"Validation issues");
        body.Children.Add(Button("Done editing",()=>Done?.Invoke()));
        session.Changed+=Refresh; session.PackChanged+=SetPack; RefreshPalette(); Refresh();
    }
    private void SetPack(IsoPack pack) { sheets=new PackIsoSheetSource(pack); thumbnails=new(pack,sheets); RefreshPalette(); }
    private void Command(Func<string,IsoEditorCommand> command) { if (session.Selection!=null) session.Execute(command(session.Selection)); }
    private void PreviewSeat() => session.SetSeatFields(desk.Text,chair.Text,computer.Text);
    private void PreviewCoordinates()
    {
        coordinateError.Text="";
        if (session.SelectedWall!=null && session.Selection!=null && session.HostId!=null)
        { session.PreviewCommand(session.Pending is IsoDuplicateWallCommand ? new IsoDuplicateWallCommand(session.Selection,session.HostId) : new IsoMoveWallCommand(session.Selection,session.HostId)); return; }
        if (!int.TryParse(x.Text,out var px) || !int.TryParse(y.Text,out var py))
        { session.Cancel(); coordinateError.Text="✕ Enter integer X and Y coordinates."; return; }
        var tile=new Tile(px,py);
        if (session.Tool==IsoEditorTool.Select && session.Selection!=null) session.PreviewCommand(new IsoMoveCommand(session.Selection,tile));
        else session.SetCursor(tile);
    }
    private void VariantSelected()
    {
        if (refreshing || rotation.SelectedItem is not int angle || state.SelectedItem is not string variantState) return;
        if (session.AssetId!=null)
        {
            var available=session.Editor.Palette.FirstOrDefault(a=>a.AssetId==session.AssetId)?.Variants.Where(v=>v.State==variantState).Select(v=>v.Rotation).Distinct().Order().ToArray();
            if (available?.Length>0)
            {
                if (!available.Contains(angle)) angle=available[0];
                refreshing=true; rotation.ItemsSource=available; rotation.SelectedItem=angle; refreshing=false;
            }
            session.SetAsset(session.AssetId,angle,variantState);
        }
        if (session.Tool==IsoEditorTool.Select && session.Selection!=null) session.PreviewCommand(new IsoRotateCommand(session.Selection,angle));
    }
    private void PaletteSelected()
    {
        if (refreshing || palette.SelectedItem is not ListBoxItem { Tag: {} metadata }) return;
        if (metadata is IsoEditableAsset asset)
        {
            var v=asset.Variants.First();
            session.SetAsset(asset.AssetId,v.Rotation,v.State);
            SetVariants(asset.Variants.Select(p=>p.State),asset.Variants.Where(p=>p.State==v.State).Select(p=>p.Rotation),v.State,v.Rotation);
        }
        else if (metadata is IsoWallDecorDefinition decor) session.SetAsset(decor.AssetId,0);
    }
    private void SetVariants(IEnumerable<string> states,IEnumerable<int> rotations,string selectedState,int selectedRotation)
    {
        refreshing=true; state.ItemsSource=states.Distinct().Order(StringComparer.Ordinal).ToArray(); state.SelectedItem=selectedState;
        rotation.ItemsSource=rotations.Distinct().Order().ToArray(); rotation.SelectedItem=selectedRotation; refreshing=false;
    }
    internal void RefreshPalette()
    {
        refreshing=true; palette.Items.Clear();
        if (session.Tool==IsoEditorTool.WallDecor)
        {
            foreach (var decor in session.Editor.WallPalette.Where(a=>a.Title.Contains(search.Text,StringComparison.OrdinalIgnoreCase) || a.AssetId.Contains(search.Text,StringComparison.OrdinalIgnoreCase)))
            {
                var row=new StackPanel { Orientation=Orientation.Horizontal };
                row.Children.Add(Thumbnail(thumbnails.Get(decor)));
                row.Children.Add(new TextBlock { Text=$"{decor.Title} ({decor.ModuleIds.Length} edges)",TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center });
                var item=new ListBoxItem { Content=row,Tag=decor }; SetAutomation(item,decor.Title); palette.Items.Add(item);
            }
        }
        else foreach (var asset in session.Search(search.Text))
        {
            var row=new StackPanel { Orientation=Orientation.Horizontal };
            var image=new Image {Source=thumbnails.Get(asset),Width=48,Height=48,Stretch=Stretch.Uniform,Margin=new(0,2,8,2)};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor); row.Children.Add(image);
            var title=session.Privacy!=PrivacyMode.Off && asset.AssetId.StartsWith("user/",StringComparison.Ordinal) ? asset.AssetId : asset.Title;
            row.Children.Add(new TextBlock { Text=title,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center });
            var item=new ListBoxItem { Content=row,Tag=asset }; SetAutomation(item,title,asset.AssetId+"; "+asset.Kind); palette.Items.Add(item);
        }
        SetAutomation(palette,$"Object palette, {palette.Items.Count} objects"); refreshing=false;
    }
    private static Image Thumbnail(ImageSource source)
    {
        var image=new Image { Source=source,Width=48,Height=48,Stretch=Stretch.Uniform,Margin=new(0,2,8,2) };
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor); return image;
    }
    private void Refresh()
    {
        var snapshot=session.Editor.State;
        selection.Text="Selection: "+(session.Selection ?? "none"); x.Text=session.Cursor.X.ToString(); y.Text=session.Cursor.Y.ToString();
        if (session.SelectedWall!=null) selection.Text+=$" ({(snapshot.Layout.WallSegments ?? []).Count(w=>w.Id==session.Selection || w.AssemblyId==session.Selection)} edges)";
        desk.Text=session.SeatDeskId ?? session.SelectedFloor?.Id ?? ""; chair.Text=session.SeatChairId ?? ""; computer.Text=session.SeatComputerId ?? "";
        if (session.SelectedFloor is {} f && session.Editor.Palette.FirstOrDefault(a=>a.AssetId==f.AssetId) is {} asset)
            SetVariants(asset.Variants.Select(v=>v.State),asset.Variants.Where(v=>v.State==f.State).Select(v=>v.Rotation),f.State,
                session.Pending is IsoRotateCommand rotate && rotate.InstanceId==f.Id ? rotate.Rotation ?? f.Rotation : f.Rotation);
        refreshing=true; hosts.ItemsSource=(snapshot.Layout.WallSegments ?? []).Where(w=>w.HostId==null).ToArray();
        hosts.SelectedItem=(snapshot.Layout.WallSegments ?? []).FirstOrDefault(w=>w.Id==session.HostId); refreshing=false;
        seats.Text=$"Enabled work positions: {snapshot.Layout.WorkSeats?.Count ?? 0}/12";
        var visibleIssues=session.DisplayIssues; var errors=visibleIssues.Any(i=>i.Severity==IsoIssueSeverity.Error);
        status.Text=(errors ? "✕ " : "✓ ")+session.Status; status.Foreground=errors ? Brushes.Firebrick : new SolidColorBrush(Ink);
        save.IsEnabled=session.CanSave; undo.IsEnabled=snapshot.CanUndo; redo.IsEnabled=snapshot.CanRedo;
        issueList.Children.Clear();
        foreach (var group in visibleIssues.GroupBy(i=>i.RuleId).OrderBy(g=>g.Key,StringComparer.Ordinal))
        {
            issueList.Children.Add(new TextBlock { Text=$"{group.Key} ({group.Count()})",FontWeight=FontWeights.SemiBold,Margin=new(0,6,0,2) });
            foreach (var issue in group)
            {
                var label=(issue.Severity==IsoIssueSeverity.Error ? "✕ " : "⚠ ")+session.IssueText(issue);
                var button=Button(label,()=> { if (issue.TargetIds.FirstOrDefault() is {} id) { session.Select(id); Navigate?.Invoke(id); } });
                button.HorizontalContentAlignment=HorizontalAlignment.Left;
                button.Content=new TextBlock { Text=label,TextWrapping=TextWrapping.Wrap };
                issueList.Children.Add(button);
            }
        }
        SetAutomation(issueList,$"Validation issues, {visibleIssues.Length} issues");
    }
    private void Field(string label,Control control,Panel? parent=null)
    {
        (parent ?? body).Children.Add(new Label { Content=label,Target=control,Padding=new(0,6,0,2) });
        control.Margin=new(0,0,4,2); control.MinHeight=28; SetAutomation(control,label); (parent ?? body).Children.Add(control);
    }
    private void Row(params Button[] buttons) { var row=new WrapPanel(); foreach (var button in buttons) row.Children.Add(button); body.Children.Add(row); }
    internal static Button Button(string title,Action action)
    {
        var button=new Button { Content=title,Padding=new(8,5,8,5),Margin=new(0,3,5,3),MinHeight=30 };
        SetAutomation(button,title); button.Click+=(_,_)=>action(); return button;
    }
    internal static void SetAutomation(DependencyObject target,string name,string? help=null)
    { AutomationProperties.SetName(target,name); if (help!=null) AutomationProperties.SetHelpText(target,help); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject target)
    {
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(target);i++)
        { var child=VisualTreeHelper.GetChild(target,i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    public void Dispose() { session.Changed-=Refresh; session.PackChanged-=SetPack; }
}
