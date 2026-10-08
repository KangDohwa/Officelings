using Xunit;

namespace Officelings.Tests;

public sealed class IsoSeatedOcclusionTests
{
    [Fact] public void EveryTypingOrientationAndPhaseCoversLowerBody() => IsoSeatedOcclusionChecks.TypingLowerBody();
    [Fact] public void LowerRaiseAndWalkerKeepCorrectOcclusion() => IsoSeatedOcclusionChecks.SeatTransitions();
    [Fact] public void EmptyChairAndHeadHandVisibilityArePreserved() => IsoSeatedOcclusionChecks.ChairUnionAndVisibility();
}
