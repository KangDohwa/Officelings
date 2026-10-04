using System.Collections.Immutable;

namespace Vibedotcom.Core.Iso.Editing;

/// <summary>UI dependency boundary; spec §§4, 8. UI fakes can render while the concrete facade is a stub.</summary>
public interface IIsoEditor
{
    /// <summary>Spec §4.2: owned immutable current snapshot.</summary>
    IsoEditorState State { get; }
    /// <summary>Spec §8: logical floor objects.</summary>
    ImmutableArray<IsoEditableAsset> Palette { get; }
    /// <summary>Spec §§3.2, 8: complete wall attachments.</summary>
    ImmutableArray<IsoWallDecorDefinition> WallPalette { get; }
    /// <summary>Spec §7.4: current and history asset references.</summary>
    ImmutableArray<string> ReferencedAssetIds { get; }
    /// <summary>Spec §7.4: transactionally replace the active pack.</summary>
    IsoEditResult UpdatePack(IsoPack pack);
    /// <summary>Spec §4.1 Place.</summary>
    IsoEditResult Place(string assetId, Tile origin, int rotation = 0, string state = "default", string? hostDeskId = null);
    /// <summary>Spec §4.1 Move.</summary>
    IsoEditResult Move(string instanceId, Tile origin);
    /// <summary>Spec §4.1 Rotate.</summary>
    IsoEditResult Rotate(string instanceId, int? rotation = null);
    /// <summary>Spec §4.1 Delete.</summary>
    IsoEditResult Delete(string instanceId);
    /// <summary>Spec §4.1 Duplicate.</summary>
    IsoEditResult Duplicate(string instanceId, Tile? origin = null);
    /// <summary>Spec §4.1 SetSeat.</summary>
    IsoEditResult SetSeat(string deskId, string chairId, string? computerId = null);
    /// <summary>Spec §4.1 ClearSeat.</summary>
    IsoEditResult ClearSeat(string deskId);
    /// <summary>Spec §4.1 PlaceWall.</summary>
    IsoEditResult PlaceWall(string assetId, string hostWallId);
    /// <summary>Spec §4.1 MoveWall.</summary>
    IsoEditResult MoveWall(string instanceId, string hostWallId);
    /// <summary>Spec §4.1 DuplicateWall.</summary>
    IsoEditResult DuplicateWall(string instanceId, string hostWallId);
    /// <summary>Spec §4.2 Undo.</summary>
    IsoEditResult Undo();
    /// <summary>Spec §4.2 Redo.</summary>
    IsoEditResult Redo();
    /// <summary>Spec §2.2 undoable reset.</summary>
    IsoEditResult ResetDefault();
    /// <summary>Spec §5 aggregate validation.</summary>
    ImmutableArray<IsoValidationIssue> Validate();
    /// <summary>Spec §6 pure candidate and markers.</summary>
    IsoEditPreview Preview(IsoEditorCommand command);
    /// <summary>Spec §2.2 validated atomic save.</summary>
    IsoEditResult Save(string path);
}
