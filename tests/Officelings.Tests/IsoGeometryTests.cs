using Officelings.Core;
using Officelings.Core.Iso;
using Xunit;

namespace Officelings.Tests;

public class IsoGeometryTests
{
    [Theory]
    [InlineData(1, 0, 32, 16, "se", "right")]
    [InlineData(0, 1, -32, 16, "sw", "down")]
    [InlineData(0, -1, 32, -16, "ne", "up")]
    [InlineData(-1, 0, -32, -16, "nw", "left")]
    public void ProjectionAndFacing(int i, int j, int x, int y, string facing, string legacy)
    {
        Assert.Equal(new Position(x, y), IsoGeometry.Project(i, j));
        Assert.Equal(facing, IsoGeometry.Facing(i, j));
        Assert.Equal(facing, IsoGeometry.Facing(legacy));
    }

    [Fact]
    public void RoundTripsFractionalNegativeAndElevatedPoints()
    {
        for (var i = -8; i <= 20; i++) for (var j = -8; j <= 20; j++)
        {
            var a = i / 4d; var b = j / 4d; var p = IsoGeometry.Project(a, b, 18);
            Assert.Equal(new IsoGridPoint(a, b), IsoGeometry.Inverse(p.X, p.Y, 18));
        }
        Assert.Equal(-1, IsoGeometry.Snap(-1.5));
        Assert.Equal(0, IsoGeometry.Snap(-.5));
        Assert.Equal(new Tile(1, 0), IsoGeometry.Pick(16, 8));
        Assert.Equal(new Tile(0, 0), IsoGeometry.Pick(-16, -8));
    }

    [Fact]
    public void HalfOpenMaskOwnsExactly1024TexelsAndTessellates()
    {
        var count = 0;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 64; x++) if (IsoGeometry.TileTexel(x, y)) count++;
        Assert.Equal(1024, count);
        for (var y = -16; y < 80; y++) for (var x = -96; x < 96; x++)
        {
            var owner = IsoGeometry.Pick(x + .5, y + .5); var p = IsoGeometry.Ground(owner.X, owner.Y);
            Assert.True(IsoGeometry.TileTexel(x - p.X + 32, y - p.Y + 16));
        }
        Assert.Equal(new IsoBounds(-320, -16, 320, 304), IsoGeometry.GroundBounds(10, 10));
    }

    [Theory]
    [InlineData(0, 96, 128, 96, 128, 84, 115, 72, "nw")]
    [InlineData(90, 288, 288, 112, 288, 100, 299, 88, "ne")]
    [InlineData(270, 480, 512, 64, 512, 52, 499, 52, "sw")]
    [InlineData(180, 672, 608, 80, 608, 68, 619, 68, "se")]
    public void ExactS0ContactPanels(int rotation, int originX, int groundX, int groundY, int pelvisX, int pelvisY,
        int handX, int handY, string facing)
    {
        var c = IsoContacts.Desk(0, 0, rotation); var g = IsoGeometry.Ground(c.Approach.X, c.Approach.Y);
        Assert.Equal(new IsoPixel(groundX, groundY), new IsoPixel(g.X + originX, g.Y + 80));
        Assert.Equal(new IsoPixel(pelvisX, pelvisY), new IsoPixel(c.Pelvis.X + originX, c.Pelvis.Y + 80));
        Assert.Equal(new IsoPixel(handX, handY), new IsoPixel(c.HandLeft.X + originX, c.HandLeft.Y + 80));
        Assert.Equal(c.HandLeft.X + 2, c.HandRight.X);
        Assert.Equal(facing, c.Facing);
    }

    [Fact]
    public void TransitionSolesStayGroundedAndEndIsRejected()
    {
        for (var phase = 0; phase < 4; phase++)
        {
            var c = IsoContacts.Lowering(phase);
            Assert.Equal(0, c.Offset.Y - c.Anchor.Y + c.FootY);
        }
        Assert.Throws<InvalidDataException>(() => IsoContacts.Desk(0, 0, 0, "end"));
    }

    [Theory]
    [InlineData(0, 1, 1, .25, 1, -24, 8, "nw", "se")]
    [InlineData(90, 1, 1, 1, .25, 24, 8, "ne", "sw")]
    [InlineData(180, -1, 1, -.25, 1, -40, 0, "se", "nw")]
    [InlineData(270, 1, -1, 1, -.25, 40, 0, "sw", "ne")]
    public void SofaContactUsesApproachVectorInsteadOfFinalFacing(int rotation, int ax, int ay, double si, double sj,
        int px, int py, string approach, string seated)
    {
        var contact = IsoSofaContacts.Resolve(0, 0, rotation);
        Assert.Equal(new Tile(ax, ay), contact.Approach); Assert.Equal(new IsoGridPoint(si, sj), contact.SeatGround);
        Assert.Equal(new IsoPixel(px, py), contact.Pelvis); Assert.Equal(approach, contact.ApproachFacing); Assert.Equal(seated, contact.SeatedFacing);
    }
    [Fact]
    public void DepthUsesGroundThenRankThenOrdinalIds()
    {
        var keys = new[] { new IsoDepthKey(IsoPass.Scene, 2, 20, "chair", "front"),
            new IsoDepthKey(IsoPass.Scene, 2, 10, "desk", "front"), new IsoDepthKey(IsoPass.Scene, 2, 0, "actor", "body"),
            new IsoDepthKey(IsoPass.Scene, 2, -20, "chair", "rear"), new IsoDepthKey(IsoPass.Scene, 1.9, 20, "walker", "body") };
        Array.Sort(keys);
        Assert.Equal(new[] { "walker", "chair", "actor", "desk", "chair" }, keys.Select(k => k.InstanceId));
        Assert.True(keys.Zip(keys.Skip(1)).All(p => p.First.Encoded < p.Second.Encoded));
        Assert.True(new IsoDepthKey(IsoPass.Scene, 2, 20, "front", "body").CompareTo(new(IsoPass.Scene, 2.00000001, 0, "walker", "body")) < 0);
        Assert.Equal(6, IsoDepthKey.Coarse(2, 2, 1, 2));
        Assert.True(new IsoDepthKey(IsoPass.Scene, 2, 0, "a", "z").CompareTo(new(IsoPass.Scene, 2, 0, "b", "a")) < 0);
    }

    [Fact]
    public void AStarIsShortestFourNeighborAndReachabilityRejectsBlockedCells()
    {
        var n = new IsoNavigation(6, 6, [new(2, 0), new(2, 1), new(2, 2), new(2, 3)]);
        var path = n.FindPath(new(0, 0), new(5, 0));
        Assert.Equal(13, path.Count);
        var before = new Tile(0, 0);
        foreach (var t in path)
        { Assert.Equal(1, Math.Abs(t.X - before.X) + Math.Abs(t.Y - before.Y)); Assert.True(n.Walkable(t)); before = t; }
        Assert.Equal(new Tile(5, 0), before);
        Assert.Equal(32, n.Reachable(new(0, 0)).Count);
        Assert.Empty(n.FindPath(new(0, 0), new(2, 0)));
        Assert.Empty(new IsoNavigation(3, 3, [new(1, 0), new(1, 1), new(1, 2)]).FindPath(new(0, 0), new(2, 0)));
    }
}
