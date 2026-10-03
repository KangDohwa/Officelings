using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed class OfficeEditorPanel : Border
{
    private readonly OfficeView view;
    private readonly AssetPack assets;
    private readonly ComboBox tools = new() { Margin=new(0,5,0,6) };
    private readonly ListBox palette = new() { Height=240, Background=Brushes.Transparent, BorderThickness=new(0), Foreground=Brushes.White };
    private readonly TextBox search = new() { Margin=new(0,4,0,4) };
    private readonly TextBlock message = new() { TextWrapping=TextWrapping.Wrap, Foreground=Brushes.LightGoldenrodYellow, Margin=new(0,8,0,8), FontSize=11 };
    private readonly TextBlock selection = new() { Foreground=Brushes.White, TextWrapping=TextWrapping.Wrap, FontSize=11, Margin=new(0,4,0,4) };
    private readonly Button undo, redo;
    private Tile? start;
    private Tile dragOffset;
    private bool dragging;
    private string? selectedId;
    private readonly List<(string Name, object Asset)> items = new();
    public OfficeEditor Editor { get; }
    public event Action? LayoutChanged;
    public event Action? ExitRequested;
    public event Action? SaveSucceeded;
    private string Tool => (string?)tools.SelectedItem ?? "Select / move";
    public OfficeEditorPanel(OfficeView view, AssetPack assets, OfficeLayout layout, OfficeEditor? liveEditor = null)
    {
        this.view=view; this.assets=assets; Editor=liveEditor ?? new(assets,layout);
        Width=250; Background=new SolidColorBrush(Color.FromRgb(27,48,51)); Padding=new(12);
        var body=new StackPanel(); Child=new ScrollViewer { Content=body, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text="Edit office", Foreground=Brushes.White, FontSize=17, FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="Grid snap · live preview", Foreground=Brushes.LightGray, FontSize=11, Margin=new(0,2,0,5) });
        foreach(var tool in new[] { "Select / move", "Place furniture", "Tile brush", "Tile rectangle", "Tile fill", "Zone: workspace", "Zone: pantry", "Zone: lounge", "Door / spawn", "Overflow seat", "Desk seat" }) tools.Items.Add(tool);
        tools.SelectedIndex=0; body.Children.Add(tools);
        body.Children.Add(new TextBlock { Text="Filter furniture & tiles",Foreground=Brushes.White,FontWeight=FontWeights.SemiBold });
        body.Children.Add(search); body.Children.Add(palette);
        foreach(var asset in assets.Manifest.Furniture.OrderBy(a=>a.Id,StringComparer.Ordinal)) items.Add((asset.Id,asset));
        foreach(var tile in assets.Manifest.TileTypes) items.Add(($"Tile {tile.Id} · {tile.Kind} · {tile.Animation ?? tile.Color}",tile));
        RefreshPalette(); search.TextChanged+=(_,_)=>RefreshPalette();
        palette.SelectionChanged+=(_,_)=>
        {
            if(palette.SelectedItem is not ListBoxItem item) return;
            if(item.Tag is FurnitureAsset) tools.SelectedItem="Place furniture";
            else if(item.Tag is TileType && !Tool.StartsWith("Tile",StringComparison.Ordinal)) tools.SelectedItem="Tile brush";
        };
        body.Children.Add(selection);
        var history=new WrapPanel(); undo=Button("Undo",()=>Change(()=>Editor.Undo())); redo=Button("Redo",()=>Change(()=>Editor.Redo()));
        history.Children.Add(undo);history.Children.Add(redo);body.Children.Add(history);
        var actions=new WrapPanel();
        actions.Children.Add(Button("Rotate · R",()=>WithSelection(id=>Editor.Rotate(id))));
        actions.Children.Add(Button("Delete · Del",()=>WithSelection(id=>{ Editor.Delete(id);selectedId=null; })));
        actions.Children.Add(Button("Duplicate",()=>WithSelection(id=>selectedId=Editor.Duplicate(id)))); body.Children.Add(actions);
        body.Children.Add(Button("Validate",Validate));
        body.Children.Add(Button("Save office",Save));
        body.Children.Add(Button("Resize office…",ResizeDialog));
        body.Children.Add(Button("Reset to default",()=>Change(()=>Editor.ResetDefault())));
        body.Children.Add(Button("Done editing",()=>ExitRequested?.Invoke()));
        body.Children.Add(message);
        body.Children.Add(new TextBlock { Text="Drag to move. R rotates authored variants. Del deletes. Ctrl+D duplicates. Ctrl+Z / Y undo / redo. Mark a desk seat after selecting its desk. Save validates all paths.", Foreground=Brushes.LightGray, TextWrapping=TextWrapping.Wrap, FontSize=11 });
        view.EditorMode=true;view.HideTooltip();view.PreviewMouseLeftButtonDown+=PointerDown;view.PreviewMouseMove+=PointerMove;view.PreviewMouseLeftButtonUp+=PointerUp;
        RefreshState();
    }
    private static Button Button(string text,Action action)
    {
        var button=new Button { Content=text,Margin=new(0,3,5,3),Padding=new(8,5,8,5),MinHeight=28,HorizontalContentAlignment=HorizontalAlignment.Left };
        button.Click+=(_,_)=>action();return button;
    }
    private void RefreshPalette()
    {
        palette.Items.Clear();
        foreach(var item in items.Where(i=>i.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase)))
        {
            var row=new StackPanel { Orientation=Orientation.Horizontal };
            var thumb=item.Asset is FurnitureAsset f ? view.FurnitureThumbnail(f) : view.TileThumbnail((TileType)item.Asset);
            var image=new Image { Source=thumb,Width=36,Height=36,Stretch=Stretch.Uniform,Margin=new(0,0,7,0) };
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);row.Children.Add(image);
            row.Children.Add(new TextBlock { Text=item.Name,TextWrapping=TextWrapping.Wrap,Width=154,VerticalAlignment=VerticalAlignment.Center,FontSize=11 });
            palette.Items.Add(new ListBoxItem { Content=row,Tag=item.Asset,Padding=new(2),HorizontalContentAlignment=HorizontalAlignment.Stretch });
        }
    }
    private void RefreshState()
    {
        undo.IsEnabled=Editor.CanUndo;redo.IsEnabled=Editor.CanRedo;
        view.EditSelection=selectedId;view.InvalidateVisual();
        selection.Text=selectedId==null ? $"{Editor.Layout.WidthTiles} × {Editor.Layout.HeightTiles} tiles" : $"Selected: {selectedId}";
    }
    private void Change(Action mutation)
    {
        try { mutation(); message.Text="Unsaved changes · Save validates the office.";RefreshState();LayoutChanged?.Invoke(); }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or IOException) { message.Text=e.Message; }
    }
    private void WithSelection(Action<string> action)
    { if(selectedId==null) { message.Text="Select a furniture item first.";return; }Change(()=>action(selectedId)); }
    private Furniture? Hit(Tile tile) => Editor.Layout.Furniture.Where(f=>tile.X>=f.X && tile.Y>=f.Y && tile.X<f.X+f.Width && tile.Y<f.Y+f.Height)
        .OrderByDescending(f=>assets.Variant(f).Layer).ThenBy(f=>f.Id.EndsWith("-chair",StringComparison.Ordinal)).FirstOrDefault();
    private void PointerDown(object sender,MouseButtonEventArgs e)
    {
        var tile=view.WorldTile(e.GetPosition(view));
        if(tile.X<0||tile.Y<0||tile.X>=Editor.Layout.WidthTiles||tile.Y>=Editor.Layout.HeightTiles) return;
        start=tile; dragging=false;e.Handled=true;
        if(Tool=="Select / move")
        {
            var hit=Hit(tile);selectedId=hit?.Id;
            if(hit!=null) { dragOffset=new(tile.X-hit.X,tile.Y-hit.Y);dragging=true;view.CaptureMouse(); }
            RefreshState();return;
        }
        if(Tool is "Tile rectangle" || Tool.StartsWith("Zone:",StringComparison.Ordinal)) { view.CaptureMouse();return; }
        ApplyTile(tile,tile);
        if(Tool=="Tile brush") view.CaptureMouse(); else start=null;
    }
    private void PointerMove(object sender,MouseEventArgs e)
    {
        if(start==null) return; var tile=view.WorldTile(e.GetPosition(view));
        if(dragging) { view.EditPreview=new(tile.X-dragOffset.X,tile.Y-dragOffset.Y);view.InvalidateVisual(); }
        if(Tool=="Tile brush" && e.LeftButton==MouseButtonState.Pressed)
        { ApplyTile(start.Value,tile);start=tile; }
    }
    private void PointerUp(object sender,MouseButtonEventArgs e)
    {
        if(start is not {} from) return;var tile=view.WorldTile(e.GetPosition(view));e.Handled=true;
        if(dragging && selectedId is {} id) Change(()=>Editor.Move(id,new(tile.X-dragOffset.X,tile.Y-dragOffset.Y)));
        else if(Tool=="Tile rectangle" || Tool.StartsWith("Zone:",StringComparison.Ordinal)) ApplyTile(from,tile);
        start=null;dragging=false;view.EditPreview=null;view.ReleaseMouseCapture();RefreshState();
    }
    private void ApplyTile(Tile from,Tile to)
    {
        Change(()=>
        {
            if(Tool=="Place furniture")
            { if((palette.SelectedItem as ListBoxItem)?.Tag is not FurnitureAsset f) throw new InvalidOperationException("Choose furniture from the palette.");selectedId=Editor.Place(f.Id,from); }
            else if(Tool.StartsWith("Tile",StringComparison.Ordinal))
            { if((palette.SelectedItem as ListBoxItem)?.Tag is not TileType t) throw new InvalidOperationException("Choose a tile from the palette.");Editor.Paint(from,to,t.Id,Tool=="Tile rectangle" ? "rect" : Tool=="Tile fill" ? "fill" : "brush"); }
            else if(Tool.StartsWith("Zone:",StringComparison.Ordinal)) Editor.SetZone(Tool[6..],from,to);
            else if(Tool=="Door / spawn") Editor.SetDoor(from);
            else if(Tool=="Overflow seat") Editor.SetOverflowSeat(from,!(Editor.Layout.OverflowSeats ?? Array.Empty<OverflowSeat>()).Any(s=>s.X==from.X && s.Y==from.Y));
            else if(Tool=="Desk seat")
            { if(selectedId==null) throw new InvalidOperationException("Select the desk with Select / move first.");Editor.SetDeskSeat(selectedId,from); }
        });
    }
    public bool HandleKey(KeyEventArgs e)
    {
        if(Keyboard.FocusedElement is TextBox) return false;
        if((Keyboard.Modifiers&ModifierKeys.Control)!=0)
        {
            if(e.Key==Key.Z) { Change(()=>Editor.Undo());return true; }
            if(e.Key==Key.Y) { Change(()=>Editor.Redo());return true; }
            if(e.Key==Key.D) { WithSelection(id=>selectedId=Editor.Duplicate(id));return true; }
            if(e.Key==Key.S) { Save();return true; }
        }
        if(e.Key==Key.R) { WithSelection(id=>Editor.Rotate(id));return true; }
        if(e.Key==Key.Delete) { WithSelection(id=>{ Editor.Delete(id);selectedId=null; });return true; }
        return false;
    }
    private void Validate()
    {
        var errors=Editor.Validate();message.Text=errors.Count==0 ? "Office valid · all desk and overflow seats reachable." : string.Join("\n",errors.Select(s=>"• "+s));
    }
    private void Save()
    {
        var errors=Editor.Validate();if(errors.Count>0) { Validate();return; }
        try { Editor.Save(System.IO.Path.Combine(TrackerOptions.OutputDirectory,"layout.json"));message.Text="Saved office · previous layout kept as layout.json.bak.";SaveSucceeded?.Invoke(); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException) { message.Text="Save failed: "+e.Message; }
    }
    private void ResizeDialog()
    {
        var width=new TextBox { Text=Editor.Layout.WidthTiles.ToString(CultureInfo.InvariantCulture),Margin=new(0,4,0,8) };
        var height=new TextBox { Text=Editor.Layout.HeightTiles.ToString(CultureInfo.InvariantCulture),Margin=new(0,4,0,8) };
        var error=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DarkRed };
        var body=new StackPanel { Margin=new(16) };
        body.Children.Add(new TextBlock { Text="Width in tiles (8–128)" });body.Children.Add(width);
        body.Children.Add(new TextBlock { Text="Height in tiles (8–128)" });body.Children.Add(height);body.Children.Add(error);
        var dialog=new Window { Title="Resize office",Content=body,Width=300,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner };
        body.Children.Add(Button("Resize",()=>
        {
            if(!int.TryParse(width.Text,out var w)||!int.TryParse(height.Text,out var h)) { error.Text="Enter whole tile counts.";return; }
            try { Editor.Resize(w,h);RefreshState();LayoutChanged?.Invoke();dialog.Close(); }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or IOException) { error.Text=ex.Message; }
        }));
        body.Children.Add(Button("Cancel",dialog.Close));dialog.ShowDialog();
    }
    public void Detach()
    {
        view.PreviewMouseLeftButtonDown-=PointerDown;view.PreviewMouseMove-=PointerMove;view.PreviewMouseLeftButtonUp-=PointerUp;
        view.EditorMode=false;view.EditSelection=null;view.EditPreview=null;view.ReleaseMouseCapture();view.InvalidateVisual();
    }
}
