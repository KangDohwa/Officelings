using System;
using Xunit;

namespace Vibedotcom.Tests;

// Acceptance placeholders for docs/iso-editor-spec.md; remove Skip only with meaningful assertions.
public sealed class IsoEditorCommandTests
{
    [Fact(Skip = "implemented by lane core: C001 UnknownCommandTargets")]
    public void C001_UnknownCommandTargets() => throw new NotImplementedException("iso-editor-spec C001: UnknownCommandTargets");

    [Fact(Skip = "implemented by lane core: C002 UnknownOrWrongKindAssets")]
    public void C002_UnknownOrWrongKindAssets() => throw new NotImplementedException("iso-editor-spec C002: UnknownOrWrongKindAssets");

    [Fact(Skip = "implemented by lane core: C003 AuthoredRotationAndStatePreconditions")]
    public void C003_AuthoredRotationAndStatePreconditions() => throw new NotImplementedException("iso-editor-spec C003: AuthoredRotationAndStatePreconditions");

    [Fact(Skip = "implemented by lane core: C004 CanonicalSeatAndMountPreconditions")]
    public void C004_CanonicalSeatAndMountPreconditions() => throw new NotImplementedException("iso-editor-spec C004: CanonicalSeatAndMountPreconditions");

    [Fact(Skip = "implemented by lane core: C005 EditableWallCommandPreconditions")]
    public void C005_EditableWallCommandPreconditions() => throw new NotImplementedException("iso-editor-spec C005: EditableWallCommandPreconditions");

    [Fact(Skip = "implemented by lane core: C006 MountedSlotAndPackReferences")]
    public void C006_MountedSlotAndPackReferences() => throw new NotImplementedException("iso-editor-spec C006: MountedSlotAndPackReferences");

    [Fact(Skip = "implemented by lane core: C007 EmptyUndoAndRedo")]
    public void C007_EmptyUndoAndRedo() => throw new NotImplementedException("iso-editor-spec C007: EmptyUndoAndRedo");

    [Fact(Skip = "implemented by lane core: C008 InvalidSaveHasZeroWrites")]
    public void C008_InvalidSaveHasZeroWrites() => throw new NotImplementedException("iso-editor-spec C008: InvalidSaveHasZeroWrites");

    [Fact(Skip = "implemented by lane core: C009 SaveIoFailureRetainsHistoryAndBaseline")]
    public void C009_SaveIoFailureRetainsHistoryAndBaseline() => throw new NotImplementedException("iso-editor-spec C009: SaveIoFailureRetainsHistoryAndBaseline");

    [Fact(Skip = "implemented by lane core: Place RejectsUnknownAsset")]
    public void Place_RejectsUnknownAsset() => throw new NotImplementedException("iso-editor-spec Place: RejectsUnknownAsset");

    [Fact(Skip = "implemented by lane core: Place RejectsAbsentRotationOrState")]
    public void Place_RejectsAbsentRotationOrState() => throw new NotImplementedException("iso-editor-spec Place: RejectsAbsentRotationOrState");

    [Fact(Skip = "implemented by lane core: Place RejectsOutOfBoundsAndBlockingOverlap")]
    public void Place_RejectsOutOfBoundsAndBlockingOverlap() => throw new NotImplementedException("iso-editor-spec Place: RejectsOutOfBoundsAndBlockingOverlap");

    [Fact(Skip = "implemented by lane core: Place DeskStartsWithoutChairOrDesignation")]
    public void Place_DeskStartsWithoutChairOrDesignation() => throw new NotImplementedException("iso-editor-spec Place: DeskStartsWithoutChairOrDesignation");

    [Fact(Skip = "implemented by lane core: Place MountRejectsWrongRootAndOccupiedSlot")]
    public void Place_MountRejectsWrongRootAndOccupiedSlot() => throw new NotImplementedException("iso-editor-spec Place: MountRejectsWrongRootAndOccupiedSlot");

    [Fact(Skip = "implemented by lane core: Move RejectsUnknownTarget")]
    public void Move_RejectsUnknownTarget() => throw new NotImplementedException("iso-editor-spec Move: RejectsUnknownTarget");

    [Fact(Skip = "implemented by lane core: Move ResolvesLinkedPartsAndRejectsWholeGroupBounds")]
    public void Move_ResolvesLinkedPartsAndRejectsWholeGroupBounds() => throw new NotImplementedException("iso-editor-spec Move: ResolvesLinkedPartsAndRejectsWholeGroupBounds");

    [Fact(Skip = "implemented by lane core: Move RetainsSeatIdsAndAllowsNavigationOnlyDraft")]
    public void Move_RetainsSeatIdsAndAllowsNavigationOnlyDraft() => throw new NotImplementedException("iso-editor-spec Move: RetainsSeatIdsAndAllowsNavigationOnlyDraft");

    [Fact(Skip = "implemented by lane core: Rotate RejectsUnknownTarget")]
    public void Rotate_RejectsUnknownTarget() => throw new NotImplementedException("iso-editor-spec Rotate: RejectsUnknownTarget");

    [Fact(Skip = "implemented by lane core: Rotate RejectsSingleRotationAndMissingLinkedVariant")]
    public void Rotate_RejectsSingleRotationAndMissingLinkedVariant() => throw new NotImplementedException("iso-editor-spec Rotate: RejectsSingleRotationAndMissingLinkedVariant");

    [Fact(Skip = "implemented by lane core: Rotate CyclesCurrentStateAndRecomputesFourFacingContacts")]
    public void Rotate_CyclesCurrentStateAndRecomputesFourFacingContacts() => throw new NotImplementedException("iso-editor-spec Rotate: CyclesCurrentStateAndRecomputesFourFacingContacts");

    [Fact(Skip = "implemented by lane core: Delete RejectsUnknownOrFixedHostTarget")]
    public void Delete_RejectsUnknownOrFixedHostTarget() => throw new NotImplementedException("iso-editor-spec Delete: RejectsUnknownOrFixedHostTarget");

    [Fact(Skip = "implemented by lane core: Delete RemovesWholeGroupOrWallAssembly")]
    public void Delete_RemovesWholeGroupOrWallAssembly() => throw new NotImplementedException("iso-editor-spec Delete: RemovesWholeGroupOrWallAssembly");

    [Fact(Skip = "implemented by lane core: Duplicate RejectsUnknownTarget")]
    public void Duplicate_RejectsUnknownTarget() => throw new NotImplementedException("iso-editor-spec Duplicate: RejectsUnknownTarget");

    [Fact(Skip = "implemented by lane core: Duplicate RejectsInvalidDefaultTargetWithoutSearching")]
    public void Duplicate_RejectsInvalidDefaultTargetWithoutSearching() => throw new NotImplementedException("iso-editor-spec Duplicate: RejectsInvalidDefaultTargetWithoutSearching");

    [Fact(Skip = "implemented by lane core: Duplicate CopiesEnabledDesignationWithoutReservation")]
    public void Duplicate_CopiesEnabledDesignationWithoutReservation() => throw new NotImplementedException("iso-editor-spec Duplicate: CopiesEnabledDesignationWithoutReservation");

    [Fact(Skip = "implemented by lane core: SetSeat RejectsMissingIds")]
    public void SetSeat_RejectsMissingIds() => throw new NotImplementedException("iso-editor-spec SetSeat: RejectsMissingIds");

    [Fact(Skip = "implemented by lane core: SetSeat RejectsMisalignedOrAlreadyOwnedParts")]
    public void SetSeat_RejectsMisalignedOrAlreadyOwnedParts() => throw new NotImplementedException("iso-editor-spec SetSeat: RejectsMisalignedOrAlreadyOwnedParts");

    [Fact(Skip = "implemented by lane core: SetSeat RejectsCustomRigAndThirteenthSeat")]
    public void SetSeat_RejectsCustomRigAndThirteenthSeat() => throw new NotImplementedException("iso-editor-spec SetSeat: RejectsCustomRigAndThirteenthSeat");

    [Fact(Skip = "implemented by lane core: ClearSeat RejectsUnknownOrNoncanonicalDesk")]
    public void ClearSeat_RejectsUnknownOrNoncanonicalDesk() => throw new NotImplementedException("iso-editor-spec ClearSeat: RejectsUnknownOrNoncanonicalDesk");

    [Fact(Skip = "implemented by lane core: ClearSeat KeepsLinksAndIsIdempotent")]
    public void ClearSeat_KeepsLinksAndIsIdempotent() => throw new NotImplementedException("iso-editor-spec ClearSeat: KeepsLinksAndIsIdempotent");

    [Fact(Skip = "implemented by lane core: PlaceWall RejectsUnknownDefinitionOrOccupiedHost")]
    public void PlaceWall_RejectsUnknownDefinitionOrOccupiedHost() => throw new NotImplementedException("iso-editor-spec PlaceWall: RejectsUnknownDefinitionOrOccupiedHost");

    [Fact(Skip = "implemented by lane core: PlaceWall RejectsPartialTwoEdgeHostRun")]
    public void PlaceWall_RejectsPartialTwoEdgeHostRun() => throw new NotImplementedException("iso-editor-spec PlaceWall: RejectsPartialTwoEdgeHostRun");

    [Fact(Skip = "implemented by lane core: MoveWall RejectsWrongFaceAndPreservesEveryEdgeId")]
    public void MoveWall_RejectsWrongFaceAndPreservesEveryEdgeId() => throw new NotImplementedException("iso-editor-spec MoveWall: RejectsWrongFaceAndPreservesEveryEdgeId");

    [Fact(Skip = "implemented by lane core: DuplicateWall RejectsOccupiedRunAndAllocatesFreshIds")]
    public void DuplicateWall_RejectsOccupiedRunAndAllocatesFreshIds() => throw new NotImplementedException("iso-editor-spec DuplicateWall: RejectsOccupiedRunAndAllocatesFreshIds");

    [Fact(Skip = "implemented by lane core: Preview MatchesDispatchWithoutConsumingIdsOrHistory")]
    public void Preview_MatchesDispatchWithoutConsumingIdsOrHistory() => throw new NotImplementedException("iso-editor-spec Preview: MatchesDispatchWithoutConsumingIdsOrHistory");

    [Fact(Skip = "implemented by lane core: UpdatePack RejectsMissingCurrentUndoRedoAssets")]
    public void UpdatePack_RejectsMissingCurrentUndoRedoAssets() => throw new NotImplementedException("iso-editor-spec UpdatePack: RejectsMissingCurrentUndoRedoAssets");

    [Fact(Skip = "implemented by lane core: Save RejectsOversizeJsonAndUnsafeOutputBeforeWrite")]
    public void Save_RejectsOversizeJsonAndUnsafeOutputBeforeWrite() => throw new NotImplementedException("iso-editor-spec Save: RejectsOversizeJsonAndUnsafeOutputBeforeWrite");
}
