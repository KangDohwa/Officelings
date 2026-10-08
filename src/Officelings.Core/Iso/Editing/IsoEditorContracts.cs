using System.Collections.Immutable;

namespace Officelings.Core.Iso.Editing;

/// <summary>Issue severity; spec §5. Warnings never block Save.</summary>
public enum IsoIssueSeverity { Warning, Error }

/// <summary>Stable rule ID and English message template from spec §5 or §7. Target IDs are ordinal.</summary>
public sealed record IsoValidationIssue(string RuleId, IsoIssueSeverity Severity, string Message,
    ImmutableArray<string> TargetIds);

/// <summary>Explicit work position; spec §3. Id equals DeskId; null ComputerId selects the embedded rig.</summary>
public sealed record IsoSeatDesignation(string Id, string DeskId, string ChairId,
    string? ComputerId = null, string Profile = "front");

/// <summary>Complete owned snapshot, deeply frozen at editor boundaries; spec §4.2.</summary>
/// <remarks>Layout includes WorkSeats. It contains no live agent IDs or UI selection. Revision is monotonic,
/// including Undo/Redo; IsDirty compares document content to the last successful Save.</remarks>
public sealed record IsoEditorState(IsoLayout Layout, long Revision, bool CanUndo, bool CanRedo, bool IsDirty);

/// <summary>Spec §4: rejected edits return unchanged State; accepted drafts can carry navigation errors.</summary>
public sealed record IsoEditResult(bool Succeeded, bool Changed, IsoEditorState State,
    ImmutableArray<IsoValidationIssue> Issues, string? PrimaryTargetId = null);

/// <summary>Palette metadata; spec §8. Variant order is state then numeric rotation.</summary>
public sealed record IsoEditableAsset(string AssetId, string Title, string Kind,
    ImmutableArray<IsoFurnitureVariant> Variants, string ThumbnailSheetId, string ThumbnailClip,
    bool CanDesignateSeat = false, bool CanMountComputer = false);

/// <summary>Spec §§3.2, 8: ModuleIds ordered along +j (left) or +i (right); one logical palette entry.</summary>
public sealed record IsoWallDecorDefinition(string AssetId, string Title, string Face,
    ImmutableArray<string> ModuleIds);

/// <summary>World-space ghost marker; spec §6. Elevation is native pixels, never navigation depth.</summary>
public sealed record IsoPreviewPoint(string TargetId, string Kind, IsoGridPoint Ground, double Elevation = 0);

/// <summary>Spec §6: a pure candidate, with no history, reservation, I/O or revision changes.</summary>
public sealed record IsoEditPreview(IsoLayout Candidate, ImmutableArray<Tile> Footprint,
    ImmutableArray<IsoPreviewPoint> Points, ImmutableArray<IsoValidationIssue> Issues, bool CanCommit);

/// <summary>Pure command value consumed by Preview; spec §4.1.</summary>
public abstract record IsoEditorCommand;
/// <summary>Spec §4.1 Place; HostDeskId mounts the built-in computer adapter.</summary>
public sealed record IsoPlaceCommand(string AssetId, Tile Origin, int Rotation = 0,
    string State = "default", string? HostDeskId = null) : IsoEditorCommand;
/// <summary>Spec §4.1 Move: linked targets resolve to their desk root.</summary>
public sealed record IsoMoveCommand(string InstanceId, Tile Origin) : IsoEditorCommand;
/// <summary>Spec §4.1 Rotate: null Rotation cycles authored variants.</summary>
public sealed record IsoRotateCommand(string InstanceId, int? Rotation = null) : IsoEditorCommand;
/// <summary>Spec §4.1 Delete: linked targets delete the complete assembly.</summary>
public sealed record IsoDeleteCommand(string InstanceId) : IsoEditorCommand;
/// <summary>Spec §4.1 Duplicate: null Origin selects the cell right of the root footprint.</summary>
public sealed record IsoDuplicateCommand(string InstanceId, Tile? Origin = null) : IsoEditorCommand;
/// <summary>Spec §4.1 SetSeat links the canonical rig atomically.</summary>
public sealed record IsoSetSeatCommand(string DeskId, string ChairId, string? ComputerId = null) : IsoEditorCommand;
/// <summary>Spec §4.1 ClearSeat retains furniture and assembly links.</summary>
public sealed record IsoClearSeatCommand(string DeskId) : IsoEditorCommand;
/// <summary>Spec §4.1 PlaceWall adds a complete decor assembly at its primary host.</summary>
public sealed record IsoPlaceWallCommand(string AssetId, string HostWallId) : IsoEditorCommand;
/// <summary>Spec §4.1 MoveWall moves the complete attachment, retaining its IDs.</summary>
public sealed record IsoMoveWallCommand(string InstanceId, string HostWallId) : IsoEditorCommand;
/// <summary>Spec §4.1 DuplicateWall requires an explicit free host run.</summary>
public sealed record IsoDuplicateWallCommand(string InstanceId, string HostWallId) : IsoEditorCommand;

/// <summary>In-place runtime bridge; spec §3.3. Does not restart actors, tracking or random streams.</summary>
public interface IIsoEditorRuntime
{
    /// <summary>Spec §3.3: last fully applied editor revision; independent of layout object identity.</summary>
    long AppliedEditorRevision { get; }
    /// <summary>Spec §§3.3, 7.4: pure live/pending layout and actor asset preflight.</summary>
    ImmutableArray<IsoValidationIssue> PreviewEditorPack(IsoPack pack);
    /// <summary>Spec §§3.3, 8: transient reservation state containing no agent names or identifiers.</summary>
    ImmutableArray<IsoEditorSeatStatus> GetEditorSeatStatus();
    /// <summary>Spec §§3.3, 7.4: swaps a complete pack without recreating actors; failures retain old caches.</summary>
    ImmutableArray<IsoValidationIssue> UpdateEditorPack(IsoPack pack);
    /// <summary>Spec §3.3: invalid drafts retain the last valid scene; valid states migrate reservations by ID.</summary>
    ImmutableArray<IsoValidationIssue> ApplyEditorState(IsoEditorState state);
}

/// <summary>Spec §§3.3, 8: privacy-safe transient work-position status.</summary>
public enum IsoEditorSeatOccupancy { Free, Assigned, Away }
/// <summary>Spec §§3.3, 8: seat ID only; never persisted in a document or history.</summary>
public sealed record IsoEditorSeatStatus(string SeatId, IsoEditorSeatOccupancy Status);
