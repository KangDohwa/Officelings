using System;
using Xunit;

namespace Vibedotcom.Tests;

// Acceptance placeholders for docs/iso-editor-spec.md; remove Skip only with meaningful assertions.
public sealed class IsoEditorUiTests
{
    [Fact(Skip = "implemented by lane ui: U001 PublishedMaskAndRootHitTesting")]
    public void U001_PublishedMaskAndRootHitTesting() => new IsoEditorUiBehaviorTests().HandlePrecedesInjectedCorePicking();

    [Fact(Skip = "implemented by lane ui: U002 PureDiamondGhostAndCanonicalContacts")]
    public void U002_PureDiamondGhostAndCanonicalContacts() => new IsoEditorUiBehaviorTests().PreviewIsExactPureAndCached();

    [Fact(Skip = "implemented by lane ui: U003 SameOfficeLifecycleAndModeRestore")]
    public void U003_SameOfficeLifecycleAndModeRestore() => new IsoEditorUiBehaviorTests().ModeRestoreKeepsEditorAndRuntime();

    [Fact(Skip = "implemented by lane ui: U004 KeyboardFocusAndTextBoxImmunity")]
    public void U004_KeyboardFocusAndTextBoxImmunity() => new IsoEditorUiBehaviorTests().KeyboardIgnoresTextAndCancelAddsNoCommand();

    [Fact(Skip = "implemented by lane ui: U005 DpiCameraZoomPanAndBounds")]
    public void U005_DpiCameraZoomPanAndBounds() => new IsoEditorUiBehaviorTests().PhysicalCameraIsInvertedOnceAtAllDpis();

    [Fact(Skip = "implemented by lane ui: U006 SearchPaletteAndTransactionalImportDialog")]
    public void U006_SearchPaletteAndTransactionalImportDialog() => new IsoEditorUiBehaviorTests().PaletteSearchCoversLogicalFamilies();

    [Fact(Skip = "implemented by lane ui: U007 AutomationContrastAndScaledText")]
    public void U007_AutomationContrastAndScaledText() => new IsoEditorUiBehaviorTests().ScaledControlsHaveAutomationAndReachableSaveDone();

    [Fact(Skip = "implemented by lane ui: U008 ClosedIdleWorkAndMeasuredBudgets")]
    public void U008_ClosedIdleWorkAndMeasuredBudgets() => new IsoEditorUiBehaviorTests().IdlePreviewDoesNotRebuildAndDetachRemovesOverlay();
}
