using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Officelings.Core;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;

namespace Officelings.App;

// Explicit UI doubles. They script responses, not acceptance evidence for Core/objects behavior.
internal sealed class IsoEditorUiFake : IIsoEditor
{
    private readonly IsoPack pack;
    public IsoEditorState State { get; private set; }
    public ImmutableArray<IsoEditableAsset> Palette { get; }
    public ImmutableArray<IsoWallDecorDefinition> WallPalette { get; }
    public ImmutableArray<string> ReferencedAssetIds => State.Layout.Furniture.Select(f=>f.AssetId).Distinct().ToImmutableArray();
    internal IsoEditPreview NextPreview { get; set; }
    internal ImmutableArray<IsoValidationIssue> Validation { get; set; } = [];
    internal IsoEditResult? NextResult { get; set; }
    internal List<IsoEditorCommand> Commands { get; } = [];
    internal int PreviewCalls { get; private set; }
    internal int SaveCalls { get; private set; }
    internal int UndoCalls { get; private set; }
    internal int RedoCalls { get; private set; }
    internal int PackCalls { get; private set; }
    internal IsoEditorUiFake(IsoPack pack,IsoLayout? layout=null)
    {
        this.pack=pack;
        State=new(layout ?? pack.Layout,0,false,false,false);
        var floorPalette=(pack.Manifest.Furniture ?? []).Select(a=>
        {
            var v=a.Variants.OrderBy(v=>v.State,StringComparer.Ordinal).ThenBy(v=>v.Rotation).First(); var p=v.Parts.First();
            return new IsoEditableAsset(a.Id,a.Id[4..].Replace('-',' '),a.Kind ?? "prop",a.Variants.ToImmutableArray(),
                p.SheetId,pack.Clip(p.SheetId,p.Animation ?? p.Clip!,p.Direction).Key,a.Id=="iso/desk",a.Id=="iso/computer");
        }).ToImmutableArray();
        var desk=floorPalette.First(a=>a.AssetId=="iso/desk");
        var monitors=desk.Variants.Select(v=>v with { Parts=v.Parts.Where(p=>p.PartId?.Contains("monitor",StringComparison.Ordinal)==true ||
            p.PartId?.Contains("status",StringComparison.Ordinal)==true).ToArray(),BlockedCells=[],SeatProfiles=[],Points=[],Shadow=null }).ToImmutableArray();
        var monitor=monitors.First(v=>v.Screen!=null).Screen!;
        Palette=floorPalette.Any(a=>a.AssetId=="iso/computer") ? floorPalette : floorPalette.Add(new("iso/computer","computer (UI fake)","computer",monitors,
            monitor.SheetId,pack.Clip(monitor.SheetId,monitor.Animation ?? monitor.Clip!,monitor.Direction).Key,CanMountComputer:true));
        WallPalette=(pack.Manifest.WallModules ?? []).Where(m=>m.Slot!=null).GroupBy(m=>WallFamily(m.Id))
            .Select(g=>new IsoWallDecorDefinition(g.Key,g.Key[4..].Replace('-',' '),g.First().Face,g.OrderBy(m=>EdgeNumber(m.Id)).Select(m=>m.Id).ToImmutableArray())).ToImmutableArray();
        NextPreview=new(State.Layout,[],[],[],true);
    }
    private static string WallFamily(string id) => id.LastIndexOf("-edge-",StringComparison.Ordinal) is var index && index>=0 ? id[..index] : id;
    private static int EdgeNumber(string id) => int.TryParse(id[(id.LastIndexOf('-')+1)..],out var index) ? index : 0;
    internal void ScriptState(IsoEditorState state) => State=state;
    private IsoEditResult Command(IsoEditorCommand command)
    {
        Commands.Add(command); var result=NextResult ?? new(true,false,State,Validation);
        if (result.Succeeded) State=result.State;
        return result;
    }
    public IsoEditResult Place(string assetId,Tile origin,int rotation=0,string state="default",string? hostDeskId=null) => Command(new IsoPlaceCommand(assetId,origin,rotation,state,hostDeskId));
    public IsoEditResult Move(string id,Tile origin) => Command(new IsoMoveCommand(id,origin));
    public IsoEditResult Rotate(string id,int? rotation=null) => Command(new IsoRotateCommand(id,rotation));
    public IsoEditResult Delete(string id) => Command(new IsoDeleteCommand(id));
    public IsoEditResult Duplicate(string id,Tile? origin=null) => Command(new IsoDuplicateCommand(id,origin));
    public IsoEditResult SetSeat(string desk,string chair,string? computer=null) => Command(new IsoSetSeatCommand(desk,chair,computer));
    public IsoEditResult ClearSeat(string desk) => Command(new IsoClearSeatCommand(desk));
    public IsoEditResult PlaceWall(string asset,string host) => Command(new IsoPlaceWallCommand(asset,host));
    public IsoEditResult MoveWall(string id,string host) => Command(new IsoMoveWallCommand(id,host));
    public IsoEditResult DuplicateWall(string id,string host) => Command(new IsoDuplicateWallCommand(id,host));
    public IsoEditResult Undo() { UndoCalls++; return new(true,false,State,Validation); }
    public IsoEditResult Redo() { RedoCalls++; return new(true,false,State,Validation); }
    public IsoEditResult Save(string path) { SaveCalls++; return new(!Validation.Any(i=>i.Severity==IsoIssueSeverity.Error),false,State,Validation); }
    public IsoEditResult ResetDefault() => new(true,false,State,Validation);
    public ImmutableArray<IsoValidationIssue> Validate() => Validation;
    public IsoEditPreview Preview(IsoEditorCommand command) { PreviewCalls++; return NextPreview; }
    public IsoEditResult UpdatePack(IsoPack pack) { PackCalls++; return new(true,false,State,Validation); }
    public ImmutableArray<IsoValidationIssue> PreviewPack(IsoPack pack) => [];
    public IsoScene GetPickingScene(IsoPixel camera) => new IsoSceneBuilder(pack,State.Layout).Build([]);
}

internal sealed class IsoEditorUiImporterFake(IsoPack pack) : IsoCustomObjectImporter
{
    public IsoCustomValidationPreview ValidatePreview(string path,System.Threading.CancellationToken cancellation) => new LocalIsoCustomObjectImporter().ValidatePreview(path,cancellation);
    public IsoCustomObjectPlan PrepareImport(IsoCustomImportRequest request,System.Threading.CancellationToken cancellation) => throw new InvalidOperationException("UI-only double does not publish objects.");
    public IsoCustomObjectPlan PrepareRemove(IsoCustomRemoveRequest request) => throw new InvalidOperationException("UI-only double does not publish objects.");
    public IsoCustomObjectResult Commit(IsoCustomObjectPlan plan,System.Threading.CancellationToken cancellation=default) => throw new InvalidOperationException("UI-only double does not publish objects.");
    internal ImmutableArray<IsoValidationIssue> Validation { get; set; } = [];
    internal IsoCustomObjectCatalog Catalog { get; set; } = new([]);
    internal int Imports { get; private set; }
    internal int Validations { get; private set; }
    internal int Removals { get; private set; }
    public ImmutableArray<IsoValidationIssue> Validate(string path) { Validations++; return Validation; }
    public IsoCustomObjectResult Import(IsoCustomImportRequest request)
    { Imports++; return new(!Validation.Any(i=>i.Severity==IsoIssueSeverity.Error),Catalog,Validation); }
    public IsoCustomObjectResult LoadCatalog(IsoEditorPaths paths) => new(true,Catalog,[]);
    public IsoCustomObjectResult Remove(IsoCustomRemoveRequest request)
    {
        Removals++;
        if (request.ReferencedAssetIds.Contains(request.AssetId))
            return new(false,request.Catalog,[new("O011",IsoIssueSeverity.Error,$"Custom object '{request.AssetId}' is still referenced by a layout or editor history.",[request.AssetId])]);
        Catalog=new(request.Catalog.Objects.Where(o=>o.AssetId!=request.AssetId).ToImmutableArray()); return new(true,Catalog,[]);
    }
    public IsoPackMergeResult Merge(IsoPack builtIn,IsoCustomObjectCatalog catalog) => new(true,pack,[]);
}

internal sealed class IsoEditorUiRuntimeFake : IIsoEditorRuntime
{
    public long AppliedEditorRevision => Applied.LastOrDefault()?.Revision ?? -1;
    public ImmutableArray<IsoValidationIssue> PreviewEditorPack(IsoPack pack) => [];
    public ImmutableArray<IsoEditorSeatStatus> GetEditorSeatStatus() => [];
    internal List<IsoEditorState> Applied { get; } = [];
    public ImmutableArray<IsoValidationIssue> ApplyEditorState(IsoEditorState state) { Applied.Add(state); return []; }
    public ImmutableArray<IsoValidationIssue> UpdateEditorPack(IsoPack pack) => [];
}
