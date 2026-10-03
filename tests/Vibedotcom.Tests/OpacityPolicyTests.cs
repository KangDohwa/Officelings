using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class OpacityPolicyTests
{
    [Theory]
    [InlineData(1, 255)]
    [InlineData(.85, 217)]
    [InlineData(.70, 179)]
    [InlineData(.50, 128)]
    [InlineData(.30, 77)]
    public void PresetsUseTheSameTargetAndAlphaForEverySurface(double opacity, int alpha)
    {
        foreach (var perPixel in new[] { false, true })
            foreach (var embedded in new[] { false, true })
            {
                var plan = WindowOpacityPlan.Create(opacity,perPixel,embedded);
                Assert.Equal(opacity,plan.Opacity);
                Assert.Equal((byte)alpha,plan.ExpectedAlpha);
                Assert.Equal(embedded,plan.Embedded);
                Assert.Equal(perPixel ? WindowOpacityPath.PerPixelContent : WindowOpacityPath.LayeredAttributes,plan.Path);
            }
    }

    [Theory]
    [InlineData(-1, .25, 64)]
    [InlineData(0, .25, 64)]
    [InlineData(.24, .25, 64)]
    [InlineData(.25, .25, 64)]
    [InlineData(2, 1, 255)]
    [InlineData(double.NaN, 1, 255)]
    [InlineData(double.PositiveInfinity, 1, 255)]
    [InlineData(double.NegativeInfinity, 1, 255)]
    public void TargetsAreClampedAndNonfiniteValuesRestoreFullOpacity(double opacity, double expected, int alpha)
    {
        var plan = WindowOpacityPlan.Create(opacity,true,true);
        Assert.Equal(expected,plan.Opacity);
        Assert.Equal((byte)alpha,plan.ExpectedAlpha);
        Assert.Equal(expected,WindowModePlan.Create("desktop",false,false,false,opacity).Opacity);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("topmost")]
    [InlineData("desktop")]
    public void ModeAndInteractionFlagsDoNotChangeTheOpacityPath(string mode)
    {
        foreach (var through in new[] { false, true })
            foreach (var borderless in new[] { false, true })
                foreach (var transparent in new[] { false, true })
                {
                    var window = WindowModePlan.Create(mode,through,borderless,transparent,.30);
                    var usesPerPixel = mode == "desktop" || transparent;
                    var plan = WindowOpacityPlan.Create(window.Opacity,usesPerPixel,mode == "desktop");
                    Assert.Equal(.30,plan.Opacity);
                    Assert.Equal((byte)77,plan.ExpectedAlpha);
                    Assert.Equal(usesPerPixel ? WindowOpacityPath.PerPixelContent : WindowOpacityPath.LayeredAttributes,plan.Path);
                }
    }

    [Fact]
    public void RegularChildWindowsKeepTheNativeAlphaPath()
    {
        var plan = WindowOpacityPlan.Create(.50,false,true);
        Assert.Equal(WindowOpacityPath.LayeredAttributes,plan.Path);
        Assert.Equal((byte)128,plan.ExpectedAlpha);
    }

    [Fact]
    public void LiveChangesRestoreFullAlphaWithoutChangingPaths()
    {
        foreach (var perPixel in new[] { false, true })
        {
            var sequence = new[] { 1d, .30, 1d }.Select(value => WindowOpacityPlan.Create(value,perPixel,true)).ToArray();
            Assert.Equal(new byte[] { 255,77,255 },sequence.Select(plan => plan.ExpectedAlpha));
            Assert.All(sequence,plan => Assert.Equal(sequence[0].Path,plan.Path));
        }
    }
}
