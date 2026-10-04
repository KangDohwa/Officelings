using System.Collections.Immutable;

namespace Vibedotcom.Core.Iso.Editing;

/// <summary>WPF-free command facade for spec §§2–6. This handoff is intentionally unimplemented.</summary>
public sealed class IsoEditor : IIsoEditor
{
    /// <summary>Spec §§2, 4.2: copies all input collections and materializes legacy seats in memory.</summary>
    public IsoEditor(IsoPack pack, IsoLayout layout) => throw new NotImplementedException();
    /// <summary>Spec §4.2: immutable document and history flags.</summary>
    public IsoEditorState State => throw new NotImplementedException();
    /// <summary>Spec §8: built-in and successfully merged user objects, never individual render parts.</summary>
    public ImmutableArray<IsoEditableAsset> Palette => throw new NotImplementedException();
    /// <summary>Spec §§3.2, 8: complete wall assemblies grouped from compiled edge modules.</summary>
    public ImmutableArray<IsoWallDecorDefinition> WallPalette => throw new NotImplementedException();
    /// <summary>Spec §7.4: replaces the active pack only after every current/history reference resolves.</summary>
    public IsoEditResult UpdatePack(IsoPack pack) => throw new NotImplementedException();
    /// <summary>Spec §4.1 Place: creates one instance; a desk is undesignated until SetSeat links a chair.</summary>
    public IsoEditResult Place(string assetId, Tile origin, int rotation = 0, string state = "default",
        string? hostDeskId = null) => throw new NotImplementedException();
    /// <summary>Spec §4.1 Move: absolute grid origin of the resolved assembly root.</summary>
    public IsoEditResult Move(string instanceId, Tile origin) => throw new NotImplementedException();
    /// <summary>Spec §4.1 Rotate: selects exact authored geometry; null cycles current-state rotations.</summary>
    public IsoEditResult Rotate(string instanceId, int? rotation = null) => throw new NotImplementedException();
    /// <summary>Spec §4.1 Delete: removes root, links and designation in one edit.</summary>
    public IsoEditResult Delete(string instanceId) => throw new NotImplementedException();
    /// <summary>Spec §4.1 Duplicate: remaps all instance/seat links; never copies a runtime reservation.</summary>
    public IsoEditResult Duplicate(string instanceId, Tile? origin = null) => throw new NotImplementedException();
    /// <summary>Spec §§3, 4.1 SetSeat: derives contacts from IsoContacts.Desk, never from pointer pixels.</summary>
    public IsoEditResult SetSeat(string deskId, string chairId, string? computerId = null) => throw new NotImplementedException();
    /// <summary>Spec §4.1 ClearSeat: idempotently removes only designation.</summary>
    public IsoEditResult ClearSeat(string deskId) => throw new NotImplementedException();
    /// <summary>Spec §4.1 PlaceWall: mounts one complete authored decor assembly; walls are fixed.</summary>
    public IsoEditResult PlaceWall(string assetId, string hostWallId) => throw new NotImplementedException();
    /// <summary>Spec §4.1 MoveWall: absolute primary host, same face; preserves attachment IDs.</summary>
    public IsoEditResult MoveWall(string instanceId, string hostWallId) => throw new NotImplementedException();
    /// <summary>Spec §4.1 DuplicateWall: new assembly and edge IDs at an explicit primary host.</summary>
    public IsoEditResult DuplicateWall(string instanceId, string hostWallId) => throw new NotImplementedException();
    /// <summary>Spec §4.2: restores one complete snapshot, advancing revision.</summary>
    public IsoEditResult Undo() => throw new NotImplementedException();
    /// <summary>Spec §4.2: restores one complete snapshot, advancing revision.</summary>
    public IsoEditResult Redo() => throw new NotImplementedException();
    /// <summary>Spec §§2.2, 4.1: restores the active bundled twelve-seat room as one undoable edit.</summary>
    public IsoEditResult ResetDefault() => throw new NotImplementedException();
    /// <summary>Spec §5: aggregate independent errors in stable rule/target order, without mutation.</summary>
    public ImmutableArray<IsoValidationIssue> Validate() => throw new NotImplementedException();
    /// <summary>Spec §6: same candidate and issues as command dispatch, without side effects.</summary>
    public IsoEditPreview Preview(IsoEditorCommand command) => throw new NotImplementedException();
    /// <summary>Spec §2.2: validates before any write; atomic sibling temporary/backup; no undo step.</summary>
    public IsoEditResult Save(string path) => throw new NotImplementedException();
    /// <summary>Spec §7.4: all asset IDs used by draft, Undo and Redo, for safe catalog removal.</summary>
    public ImmutableArray<string> ReferencedAssetIds => throw new NotImplementedException();
}
