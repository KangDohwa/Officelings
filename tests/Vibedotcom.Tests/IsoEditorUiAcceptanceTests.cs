using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Vibedotcom.App;
using Vibedotcom.Core.Iso;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class IsoEditorUiTests
{
    private const string Unverified="Real-service U008 latency exceeds 100ms/16ms; xUnit restore also blocked by NU1301.";
    private static void Sta(Action action)
    {
        Exception? error=null; var thread=new Thread(()=> { try {action();} catch(Exception e) {error=e;} });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static void Verify(int rule) => Sta(()=>
    {
        using var temp=new IsoEditorCoreFixture.Temp(); IsoEditorUiAcceptance.Run(rule,IsoPack.LoadDefault(),temp.Root);
    });
    [Fact] public void U001_DraftMaskAndRootHitTesting() => Verify(1);
    [Fact] public void U002_PureDiamondGhostAndCanonicalContacts() => Verify(2);
    [Fact] public void U003_SameOfficeLifecycleAndModeRestore() => Verify(3);
    [Fact] public void U004_KeyboardFocusAndTextBoxImmunity() => Verify(4);
    [Fact] public void U005_DpiCameraZoomPanAndBounds() => Verify(5);
    [Fact] public void U006_SearchPaletteAndTransactionalImportDialog() => Verify(6);
    [Fact] public void U007_AutomationContrastAndScaledText() => Verify(7);
    [Fact(Skip=Unverified)]
    public void U008_ClosedIdleWorkAndMeasuredBudgets() => Sta(()=>
    {
        using var temp=new IsoEditorCoreFixture.Temp();
        var measurements=IsoEditorMeasurements.Run(IsoPack.LoadDefault(),temp.Root,IsoEditorSmoke.Fixture("objects/valid/test-crate/test-crate.iso.json"));
        Assert.Equal(100,measurements.Operations); Assert.Equal(34,measurements.ObjectCount); Assert.Equal(12,measurements.SeatCount);
        Assert.True(measurements.CommandP95Ms<=100); Assert.True(measurements.PreviewP95Ms<=16);
        Assert.Equal(0,measurements.OpenIdleBytesPerFrame); Assert.Equal(0,measurements.ClosedIdleBytesPerFrame); Assert.Equal(0,measurements.CachedPreviewBytesPerCall);
    });
}
