using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

internal enum IsoEditorTool { Select, Place, WorkPosition, WallDecor }

// Session lifetime is independent of windows and views. No agents, timers or watchers live here.
internal sealed class IsoEditorSession
{
    internal IIsoEditor Editor { get; }
    internal IsoPack Pack { get; private set; }
    internal IsoPack BuiltInPack { get; }
    internal IsoCustomObjectImporter Importer { get; }
    internal IsoEditorPaths Paths { get; }
    internal IsoCustomObjectCatalog Catalog { get; private set; }
    internal IsoLayout LastValidLayout { get; private set; }
    internal IIsoEditorRuntime? Runtime { get; private set; }
    internal IsoEditorTool Tool { get; private set; }
    internal string? Selection { get; private set; }
    internal string? AssetId { get; private set; }
    internal string? HostId { get; private set; }
    internal string? SeatDeskId { get; private set; }
    internal string? SeatChairId { get; private set; }
    internal string? SeatComputerId { get; private set; }
    internal int Rotation { get; private set; }
    internal string VariantState { get; private set; } = "default";
    internal Tile Cursor { get; private set; } = new(2,2);
    internal IsoEditorCommand? Pending { get; private set; }
    internal IsoEditPreview? Preview { get; private set; }
    internal ImmutableArray<IsoValidationIssue> Issues { get; private set; }
    private ImmutableArray<IsoValidationIssue> documentIssues;
    internal bool DraftInvalid => documentIssues.Any(i=>i.Severity==IsoIssueSeverity.Error);
    internal bool Applying { get; private set; }
    internal bool Editing { get; private set; }
    internal Settings? RestoreSettings { get; private set; }
    internal PrivacyMode Privacy { get; set; }
    internal event Action? Changed;
    internal event Action<IsoPack>? PackChanged;
    internal event Action? SeatStatusChanged;
    internal ImmutableArray<IsoEditorSeatStatus> SeatStatus { get; private set; } = [];
    // Must preflight and commit editor/runtime/sheets as one transaction. Supplied by the merge composition.
    internal Func<IsoCustomObjectPlan,CancellationToken,IsoCustomObjectResult>? CommitPack { get; init; }
    internal IIsoSheetSource? CommittedSheets { get; private set; }
    private long applyingRevision;
    internal Func<IsoLayout,IsoPixel,double,double,Tile?> PickTile { get; init; } = IsoHitTest.PickTile;
    internal Func<IsoPack,IsoLayout,IsoScene,int,int,IsoHit?> HitTest { get; init; } = IsoHitTest.HitTest;
    internal string LayoutPath => Path.Combine(Paths.RootDirectory,"iso-layout.json");
    internal bool HasErrors => Issues.Any(i=>i.Severity==IsoIssueSeverity.Error);
    internal ImmutableArray<IsoValidationIssue> DisplayIssues => Issues.AddRange(Preview?.Issues ?? []).Distinct().ToImmutableArray();
    internal bool CanSave => !HasErrors && Preview?.Issues.Any(i=>i.Severity==IsoIssueSeverity.Error)!=true;
    internal string Status => Applying ? "Applying edit" : Preview?.Issues.Any(i=>i.Severity==IsoIssueSeverity.Error)==true ?
        Preview.CanCommit ? "Preview is a draft only; live office unchanged" : "Preview blocked" : DraftInvalid ? "Draft only; live office unchanged" :
        Editor.State.IsDirty ? "Unsaved changes" : "Saved layout";

    internal IsoEditorSession(IIsoEditor editor,IsoPack pack,IsoPack builtInPack,IsoCustomObjectImporter importer,
        IsoEditorPaths paths,IsoCustomObjectCatalog catalog,IsoLayout liveLayout)
    {
        Editor=editor; Pack=pack; BuiltInPack=builtInPack; Importer=importer; Paths=paths;
        Catalog=catalog; LastValidLayout=liveLayout; Issues=editor.Validate();
        documentIssues=Issues.Where(i=>i.RuleId.StartsWith("L",StringComparison.Ordinal)).ToImmutableArray();
    }

    internal void BindRuntime(IIsoEditorRuntime? runtime)
    {
        Runtime=runtime;
        if (runtime!=null && Applying)
        {
            var issues=runtime.ApplyEditorState(Editor.State);
            Applying=!issues.Any(i=>i.Severity==IsoIssueSeverity.Error) && runtime.AppliedEditorRevision<applyingRevision;
        }
        RefreshSeatStatus();
    }
    internal void RefreshSeatStatus()
    {
        var status=Runtime?.GetEditorSeatStatus() ?? [];
        if (status==SeatStatus) return;
        SeatStatus=status; SeatStatusChanged?.Invoke();
    }
    internal Settings Enter(Settings configured)
    {
        if (!Editing) { RestoreSettings=configured; Editing=true; }
        return configured with { Mode="normal",ClickThrough=false,Borderless=false,TransparentBackground=false };
    }
    internal Settings Leave(Settings current)
    {
        var saved=RestoreSettings; Editing=false; RestoreSettings=null; Cancel(); Selection=null;
        SeatDeskId=SeatChairId=SeatComputerId=null;
        return saved==null ? current : current with { Mode=saved.Mode,ClickThrough=saved.ClickThrough,
            Borderless=saved.Borderless,TransparentBackground=saved.TransparentBackground };
    }
    internal void SetTool(IsoEditorTool tool)
    {
        var previous=Tool; Cancel(false); Tool=tool; SeatDeskId=SeatChairId=SeatComputerId=null;
        if (tool is IsoEditorTool.Place or IsoEditorTool.WallDecor) Selection=null;
        if (tool==IsoEditorTool.WallDecor || previous==IsoEditorTool.WallDecor) { AssetId=null; HostId=null; }
        Notify();
    }
    internal void SetAsset(string assetId,int rotation,string state="default")
    { AssetId=assetId; Rotation=rotation; VariantState=state; PreviewCursor(); }
    internal void Select(string? id)
    {
        Selection=id; Cancel(false);
        if (Editor.State.Layout.Furniture.FirstOrDefault(f=>f.Id==id) is {} f)
        { Cursor=new(f.X,f.Y); Rotation=f.Rotation; VariantState=f.State; }
        Notify();
    }
    internal void SetCursor(Tile tile) { Cursor=tile; PreviewCursor(); }
    internal void SetHost(string? id)
    {
        HostId=id;
        if (Pending is IsoDuplicateWallCommand duplicate && id!=null) PreviewCommand(duplicate with { HostWallId=id });
        else PreviewCursor();
    }
    internal IEnumerable<IsoEditableAsset> Search(string query) => Editor.Palette.Where(a=>
        (Privacy==PrivacyMode.Off || !a.AssetId.StartsWith("user/",StringComparison.Ordinal)) &&
        (a.Title.Contains(query,StringComparison.OrdinalIgnoreCase) || a.Kind.Contains(query,StringComparison.OrdinalIgnoreCase) || a.AssetId.Contains(query,StringComparison.OrdinalIgnoreCase)));
    internal string IssueText(IsoValidationIssue issue) => Privacy!=PrivacyMode.Off && issue.RuleId is "C009" or "O009" ?
        "Local operation failed; details hidden by privacy." : issue.Message;
    internal IsoPlacement? SelectedFloor => Editor.State.Layout.Furniture.FirstOrDefault(f=>f.Id==Selection);
    internal IsoWallSegment? SelectedWall => Selection==null ? null : Editor.State.Layout.WallSegments?.FirstOrDefault(w=>w.HostId!=null && (w.Id==Selection || w.AssemblyId==Selection));

    internal void PreviewCommand(IsoEditorCommand command)
    {
        if (Pending==command) return;
        Pending=command; Preview=Editor.Preview(command);
        if (command is IsoMoveCommand move) Cursor=move.Origin;
        Notify();
    }
    private void PreviewCursor()
    {
        IsoEditorCommand? command=Tool switch
        {
            IsoEditorTool.Place when AssetId!=null => new IsoPlaceCommand(AssetId,Cursor,Rotation,VariantState),
            IsoEditorTool.WallDecor when HostId!=null && AssetId!=null => new IsoPlaceWallCommand(AssetId,HostId),
            _ => null
        };
        if (command!=null) PreviewCommand(command); else Notify();
    }
    internal void Cancel(bool notify=true)
    {
        if (Pending is IsoMoveCommand or IsoRotateCommand && SelectedFloor is {} floor)
        { Cursor=new(floor.X,floor.Y); Rotation=floor.Rotation; VariantState=floor.State; }
        Pending=null; Preview=null; if (notify) Notify();
    }
    internal IsoEditResult? CommitPreview()
    {
        if (Pending==null || Preview?.CanCommit!=true) return null;
        return Execute(Pending);
    }
    internal IsoEditResult Execute(IsoEditorCommand command) => Accept(command switch
    {
        IsoPlaceCommand c=>Editor.Place(c.AssetId,c.Origin,c.Rotation,c.State,c.HostDeskId),
        IsoMoveCommand c=>Editor.Move(c.InstanceId,c.Origin),
        IsoRotateCommand c=>Editor.Rotate(c.InstanceId,c.Rotation),
        IsoDeleteCommand c=>Editor.Delete(c.InstanceId),
        IsoDuplicateCommand c=>Editor.Duplicate(c.InstanceId,c.Origin),
        IsoSetSeatCommand c=>Editor.SetSeat(c.DeskId,c.ChairId,c.ComputerId),
        IsoClearSeatCommand c=>Editor.ClearSeat(c.DeskId),
        IsoPlaceWallCommand c=>Editor.PlaceWall(c.AssetId,c.HostWallId),
        IsoMoveWallCommand c=>Editor.MoveWall(c.InstanceId,c.HostWallId),
        IsoDuplicateWallCommand c=>Editor.DuplicateWall(c.InstanceId,c.HostWallId),
        _=>throw new ArgumentException("Unknown editor command.",nameof(command))
    });
    internal IsoEditResult Accept(IsoEditResult result)
    {
        Issues=result.Issues;
        if (result.Succeeded)
        {
            if (result.Changed) documentIssues=result.Issues.Where(i=>i.RuleId.StartsWith("L",StringComparison.Ordinal)).ToImmutableArray();
            if (result.PrimaryTargetId!=null) Selection=result.PrimaryTargetId;
            Cancel(false);
            if (result.Changed) SyncSelection(result.State.Layout);
            if (result.Changed && Runtime!=null)
            {
                // Invalid drafts also cancel a pending live swap, through the runtime contract.
                Issues=Issues.AddRange(Runtime.ApplyEditorState(result.State)).Distinct().ToImmutableArray();
                applyingRevision=result.State.Revision;
                Applying=!HasErrors && Runtime.AppliedEditorRevision<applyingRevision;
            }
            if (!HasErrors) LastValidLayout=result.State.Layout;
        }
        Notify(); return result;
    }
    private void SyncSelection(IsoLayout layout)
    {
        var floor=layout.Furniture.FirstOrDefault(f=>f.Id==Selection);
        if (floor!=null) { Cursor=new(floor.X,floor.Y); Rotation=floor.Rotation; VariantState=floor.State; }
        else if (Selection!=null && !(layout.WallSegments?.Any(w=>w.Id==Selection || w.AssemblyId==Selection) ?? false)) Selection=null;
        if (SeatDeskId!=null && !layout.Furniture.Any(f=>f.Id==SeatDeskId)) SeatDeskId=SeatChairId=SeatComputerId=null;
        if (SeatChairId!=null && !layout.Furniture.Any(f=>f.Id==SeatChairId)) SeatChairId=null;
        if (SeatComputerId!=null && !layout.Furniture.Any(f=>f.Id==SeatComputerId)) SeatComputerId=null;
    }
    internal void LiveApplied(IsoLayout layout)
    { if (Applying && Runtime?.AppliedEditorRevision>=applyingRevision) { Applying=false; Notify(); } }
    internal void Validate() { Issues=Editor.Validate(); documentIssues=Issues; Notify(); }
    internal void Report(ImmutableArray<IsoValidationIssue> issues) { Issues=issues; Notify(); }
    internal void Undo() => Accept(Editor.Undo());
    internal void Redo() => Accept(Editor.Redo());
    internal void RotateSelection()
    {
        if (Selection==null) return;
        if (Pending is IsoRotateCommand) CommitPreview(); else Execute(new IsoRotateCommand(Selection));
    }
    internal void Save() { if (CanSave) Accept(Editor.Save(LayoutPath)); }
    internal void Reset() => Accept(Editor.ResetDefault());
    internal void SeatPick(string id)
    {
        var f=Editor.State.Layout.Furniture.FirstOrDefault(p=>p.Id==id);
        if (f==null) return;
        if (f.AssetId=="iso/desk")
        {
            SeatDeskId=id;
            SeatChairId=Editor.State.Layout.Furniture.FirstOrDefault(p=>p.AssetId=="iso/chair" && p.DeskId==id)?.Id;
            SeatComputerId=Editor.State.Layout.Furniture.FirstOrDefault(p=>p.AssetId=="iso/computer" && p.DeskId==id)?.Id;
            Select(id);
        }
        else if (f.AssetId=="iso/chair") SeatChairId=id;
        else if (f.AssetId=="iso/computer") SeatComputerId=id;
        PreviewSeat();
    }
    internal void SetSeatFields(string desk,string chair,string? computer)
    { SeatDeskId=desk; SeatChairId=chair; SeatComputerId=string.IsNullOrWhiteSpace(computer) ? null : computer; PreviewSeat(); }
    private void PreviewSeat()
    { if (SeatDeskId!=null && SeatChairId!=null) PreviewCommand(new IsoSetSeatCommand(SeatDeskId,SeatChairId,SeatComputerId)); else Notify(); }
    internal void MoveCursor(int dx,int dy,bool moveSelection)
    {
        if (SelectedWall is {} wall || Tool==IsoEditorTool.WallDecor)
        {
            var current=Editor.State.Layout.WallSegments?.FirstOrDefault(w=>w.Id==HostId) ?? SelectedWall;
            var hosts=(Editor.State.Layout.WallSegments ?? []).Where(w=>w.HostId==null && (current==null || w.Face==current.Face))
                .OrderBy(w=>w.Edge.I+w.Edge.J).ToArray();
            if (hosts.Length==0) return;
            var index=Array.FindIndex(hosts,w=>w.Id==current?.Id || w.Id==current?.HostId);
            HostId=hosts[Math.Clamp(index+(dx+dy>=0 ? 1 : -1),0,hosts.Length-1)].Id;
            if (Selection!=null && SelectedWall!=null) PreviewCommand(Pending is IsoDuplicateWallCommand ?
                new IsoDuplicateWallCommand(Selection,HostId) : new IsoMoveWallCommand(Selection,HostId)); else PreviewCursor();
            return;
        }
        var origin=moveSelection && SelectedFloor is {} f ? new Tile(f.X,f.Y) : Cursor;
        Cursor=new(origin.X+dx,origin.Y+dy);
        if (moveSelection && Selection!=null) Execute(new IsoMoveCommand(Selection,Cursor)); else PreviewCursor();
    }
    internal void Duplicate()
    {
        if (Selection==null) return;
        if (SelectedWall is not {} wall) { Execute(new IsoDuplicateCommand(Selection)); return; }
        var count=(Editor.State.Layout.WallSegments ?? []).Count(w=>w.AssemblyId==Selection || w.Id==Selection);
        var host=Editor.State.Layout.WallSegments?.FirstOrDefault(w=>w.HostId==null && w.Face==wall.Face &&
            w.Edge.I==wall.Edge.I+(wall.Face=="right" ? count : 0) && w.Edge.J==wall.Edge.J+(wall.Face=="left" ? count : 0));
        HostId=host?.Id ?? wall.HostId;
        if (HostId!=null) PreviewCommand(new IsoDuplicateWallCommand(Selection,HostId));
    }
    internal ImmutableArray<string> RemovalReferences()
    {
        var ids=new HashSet<string>(Editor.ReferencedAssetIds,StringComparer.Ordinal);
        ids.UnionWith(LastValidLayout.Furniture.Select(f=>f.AssetId));
        foreach (var path in new[] {LayoutPath,LayoutPath+".bak"})
        {
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length>4194304) throw new InvalidDataException("Persisted layout exceeds the reference limit.");
            using var stream=File.OpenRead(path);
            using var doc=JsonDocument.Parse(stream);
            foreach (var item in doc.RootElement.GetProperty("furniture").EnumerateArray())
                ids.Add(item.GetProperty("assetId").GetString() ?? throw new InvalidDataException("Missing asset reference."));
        }
        return ids.Order(StringComparer.Ordinal).ToImmutableArray();
    }
    internal IsoCustomObjectResult Remove(string id)
    {
        ImmutableArray<string> references;
        try { references=RemovalReferences(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException or KeyNotFoundException)
        {
            var issue=new IsoValidationIssue("O011",IsoIssueSeverity.Error,$"Custom object '{id}' is still referenced by a layout or editor history.",[id]);
            Report([issue]); return new(false,Catalog,[issue]);
        }
        if (CommitPack==null)
        {
            var issue=new IsoValidationIssue("C006",IsoIssueSeverity.Error,"The edit would replace an occupied slot or lose an asset reference.",[id]);
            Report([issue]); return new(false,Catalog,[issue]);
        }
        var plan=Importer.PrepareRemove(new(id,Paths,Catalog,references,BuiltInPack));
        var result=CommitPack(plan,CancellationToken.None);
        Report(result.Issues); return result;
    }
    internal void AdoptPack(IsoPack pack,IsoCustomObjectCatalog catalog,IIsoSheetSource? sheets=null)
    { Pack=pack; Catalog=catalog; CommittedSheets=sheets; Cancel(false); PackChanged?.Invoke(pack); Notify(); }
    private void Notify() => Changed?.Invoke();
}
