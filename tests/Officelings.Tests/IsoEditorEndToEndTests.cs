using System.Threading;
using Officelings.App;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;
using Xunit;

namespace Officelings.Tests;

public sealed class IsoEditorEndToEndTests
{
    private static void Sta(Action action)
    {
        Exception? error=null; var thread=new Thread(()=> {try {action();} catch(Exception e) {error=e;}});
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact]
    public void EveryBuiltInFamilyAndLinkedWorkRigUsesRealCommands() => Sta(()=>
    {
        using var temp=new IsoEditorCoreFixture.Temp(); Assert.True(IsoEditorAcceptance.VerifyCommands(IsoPack.LoadDefault(),temp.Root)>0);
    });
    [Fact]
    public void SaveRestartBackupInvalidDraftPrivacyAndFlatIsolationUseRealStorage()
    {
        using var temp=new IsoEditorCoreFixture.Temp(); Assert.True(IsoEditorAcceptance.VerifyPersistence(IsoPack.LoadDefault(),temp.Root)>0);
    }
    [Fact]
    public void AssignedAwayReturnAndInvalidPendingCancellationKeepValidLiveLayout()
    {
        using var temp=new IsoEditorCoreFixture.Temp(); Assert.True(IsoEditorAcceptance.VerifyRuntime(IsoPack.LoadDefault(),temp.Root)>0);
    }
    [Fact]
    public void CapturedImportCancellationIdempotencePreflightReferencesAndMissingStartupUseRealCatalog() => Sta(()=>
    {
        using var temp=new IsoEditorCoreFixture.Temp(); Assert.True(IsoEditorAcceptance.VerifyObjects(IsoPack.LoadDefault(),temp.Root,IsoEditorSmoke.Fixture("objects/valid/test-crate/test-crate.iso.json"))>0);
    });
    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void MountedComputerSuppressesEmbeddedPartsAndDescriptorsAtEveryRotation(int angle) => Sta(()=>
    {
        using var temp=new IsoEditorCoreFixture.Temp(); var session=IsoEditorAcceptance.Create(IsoPack.LoadDefault(),temp.Root,true);
        var desk=IsoEditorSmoke.Desk(session,angle); IsoEditorSmoke.VerifyMounted(session.Pack,session.Editor.State.Layout,desk);
    });
}
