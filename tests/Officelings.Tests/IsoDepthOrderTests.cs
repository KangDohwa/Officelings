using Officelings.Core.Iso;
using Officelings.DepthChecks;
using Xunit;

namespace Officelings.Tests;

[Collection("Iso assets")]
public sealed class IsoDepthOrderTests
{
    [Fact]
    public void FootprintOracleSupportWalkersRandomLayoutsAndDeterminism()
    {
        var output = Path.GetFullPath(Path.Combine(IsoPackTests.SourceRoot, "../../../artifacts/depthsort-xunit"));
        Directory.CreateDirectory(output);
        Assert.Equal(0, DepthOracle.CheckAll(IsoPack.LoadDefault(), output));
    }
}
