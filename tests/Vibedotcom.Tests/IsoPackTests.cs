using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Xunit;

namespace Vibedotcom.Tests;

public class IsoPackTests
{
    internal static string SourceRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../assets/art/iso"));
    internal static readonly Lazy<IsoPack> Source = new(() => IsoPack.Load(SourceRoot));

    [Fact]
    public void LoadsActualProductionSheetsAndEveryPhysicalAnchor()
    {
        var p = Source.Value;
        Assert.Equal(37, p.Manifest.Sheets.Count(s => s.Id != "iso/core-contact-shadow"));
        Assert.Equal(12, p.Manifest.Furniture!.Count);
        Assert.Equal(3, p.Characters.Count);
        Assert.Single(p.Manifest.Pets!);
        Assert.Equal(4, p.Manifest.CarriedProps!.Count);
        Assert.All(p.Manifest.Sheets, s => Assert.Equal(1, s.Scale));
        Assert.All(p.Manifest.Furniture.SelectMany(f => f.Variants).SelectMany(v => v.Parts), part =>
            Assert.NotNull(p.Clip(part.SheetId, part.Animation!, part.Direction)));
    }

    [Fact]
    public void AllCharacterActionsResolveInSameFacingAndWaitingHoldsNeutralPhase()
    {
        var p = Source.Value;
        foreach (var design in p.Characters) foreach (var facing in new[] { "se", "sw", "ne", "nw" })
        {
            foreach (var action in new[] { "typing", "thinking", "waiting", "pickup", "report-handover", "coffee", "phone", "talk",
                "sleep", "tv", "water", "darts", "wave", "celebrate", "standing-thinking", "standing-waiting" })
                Assert.Equal(facing, p.Clip(design.SheetId, action, facing).Direction);
            var waiting = p.Clip(design.SheetId, "seated-waiting", facing);
            var rest = p.Clip(design.SheetId, "seated-idle", facing);
            Assert.Equal(1, waiting.Frames); Assert.Equal(0, waiting.Column);
            Assert.Equal(p.Clip(design.SheetId, "seated-thinking", facing).Row, waiting.Row);
            Assert.Equal(waiting.Row, rest.Row); Assert.Equal(new PixelPoint(16, 44), waiting.SeatAnchor);
        }
    }

    [Fact]
    public void BlinkAliasesKeepSourcePhaseAndBackFacingNeverBlinks()
    {
        var p = Source.Value;
        foreach (var design in p.Characters)
        {
            var sheet = p.Sheet(design.SheetId); var b = sheet.Blink!;
            Assert.Equal(38, sheet.Rows!.Count); Assert.Equal(24, p.Sheet(b.SheetId).Rows!.Count);
            foreach (var facing in new[] { "se", "sw" })
            {
                var a = p.Clip(sheet.Id, "seated-waiting", facing); var c = p.Clip(b.SheetId, a.Key);
                Assert.Equal(1, c.Frames); Assert.Equal(a.Column, c.Column);
                Assert.Equal(a.Row, b.NormalRows[c.Row]);
            }
            Assert.Throws<InvalidDataException>(() => p.Clip(b.SheetId, "seated-waiting", "nw"));
        }
    }

    [Fact]
    public void FramesRetainClipAnchorsAndDisjointDeskRanks()
    {
        var p = Source.Value;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var v = p.Variant("iso/desk", rotation);
            Assert.Contains(v.Parts, part => part.SeatGroupId != null && part.IsoZOrder == -20);
            Assert.Contains(v.Parts, part => part.SeatGroupId != null && part.IsoZOrder == 10);
            Assert.All(v.Parts.Where(part => part.PartId!.Contains("__monitor__")), part => Assert.Null(part.SeatGroupId));
            var profile = Assert.Single(v.SeatProfiles!); var expected = IsoContacts.Desk(0, 0, rotation);
            Assert.Equal(expected.Approach, profile.Approach);
            Assert.Equal(expected.Facing, profile.IsoFacing);
        }
        var left = p.Frame("iso-env-walls", "plain-l/any", 0);
        var right = p.Frame("iso-env-walls", "plain-r/any", 0);
        Assert.Equal(32, left.AnchorX); Assert.Equal(0, right.AnchorX); Assert.Equal(48, right.AnchorY);
    }
}

