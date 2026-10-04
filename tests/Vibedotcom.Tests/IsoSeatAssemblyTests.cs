using Xunit;

namespace Vibedotcom.Tests;

public sealed class IsoSeatAssemblyTests
{
    [Fact] public void CompiledBindingsLoadWithoutChangingAuthoredRows()
    {
        var pack=Vibedotcom.Core.Iso.IsoPack.LoadDefault();
        foreach(var design in pack.Characters)
        {
            Assert.All(pack.Sheet(design.SheetId).Rows!,c=>Assert.Null(c.ChairFrames));
            foreach(var facing in new[]{"nw","ne","sw","se"})Assert.NotNull(pack.Clip(design.SheetId,"typing",facing).ChairFrames);
        }
    }
    [Fact] public void RealEngineMatchesGeometryForEverySeatedPoseFrame() => IsoSeatAssemblyChecks.AllPoseFrames();
    [Fact] public void TwelveAgentIdleKeepsZeroAllocationFastPath() => IsoSeatAssemblyChecks.IdlePerformance();
    [Fact] public void WrongWholeChairRanksFailBothDirections() => IsoSeatAssemblyChecks.RejectInvertedOwnership();
    [Fact] public void PassingWalkersKeepOrdinaryGroundDepthPixels() => IsoSeatAssemblyChecks.Walkers();
}
