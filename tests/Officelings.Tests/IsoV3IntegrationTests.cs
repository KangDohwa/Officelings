using System.Runtime.ExceptionServices;
using Xunit;

namespace Officelings.Tests;

public sealed class IsoV3IntegrationTests
{
    [Fact] public void SeatedContactsAndOcclusion() => IsoV3RegressionChecks.SeatedContactsAndOcclusion();
    [Fact] public void TvScreenGlowStates() => IsoV3RegressionChecks.TvScreenGlowStates();
    [Fact] public void NavigationAndTwelveAgents() => IsoV3RegressionChecks.NavigationAndTwelveAgents();
    [Fact] public void WalkersAndTransitions() => IsoV3RegressionChecks.WalkersAndTransitions();
    [Fact]
    public void WpfPixelsAndBounds()
    {
        Exception? error = null;
        var thread = new Thread(() => { try { IsoV3RegressionChecks.WpfPixelsAndBounds(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
