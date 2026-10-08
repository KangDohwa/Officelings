using System.Collections.Immutable;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;
using Xunit;

namespace Officelings.Tests;

/// Core and objects were built apart: core against a test-owned adapter, objects against the frozen contract.
/// These checks run the editor on the pack that the production importer actually merges.
public class IsoEditorIntegrationTests
{
    private static readonly Lazy<IsoPack> Merged = new(() =>
    {
        var result = new LocalIsoCustomObjectImporter().Merge(IsoPack.LoadDefault(), new IsoCustomObjectCatalog(ImmutableArray<IsoCustomObjectEntry>.Empty));
        Assert.True(result.Succeeded, string.Join("; ", result.Issues.Select(i => i.RuleId + " " + i.Message)));
        return result.Pack!;
    });

    [Fact]
    public void MergedPackExposesTheVirtualComputerWithDeskRotations()
    {
        var pack = Merged.Value;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var computer = pack.Variant("iso/computer", rotation); var desk = pack.Variant("iso/desk", rotation);
            Assert.Equal(desk.FootprintW, computer.FootprintW); Assert.Equal(desk.FootprintH, computer.FootprintH);
            Assert.Equal(40, computer.HeightPx); Assert.Empty(computer.BlockedCells ?? []);
        }
    }

    [Fact]
    public void MountedRealAdapterSuppressesOnlyItsPublishedParts()
    {
        var pack = Merged.Value; var layout = IsoEditorCoreFixture.Raw("valid/linked-computer.json");
        var editor = new IsoEditor(pack, layout);
        Assert.Empty(editor.Validate().Where(i => i.Severity == IsoIssueSeverity.Error));
        var scene = new IsoSceneBuilder(pack, editor.State.Layout).Build([]);
        var adapter = pack.Variant("iso/computer", 0);
        Assert.NotEmpty(adapter.Parts);
        foreach (var part in adapter.Parts)
        {
            Assert.DoesNotContain(scene.Items, i => i.InstanceId == "desk-a/" + part.PartId);
            Assert.Contains(scene.Items, i => i.InstanceId == "computer-a/" + part.PartId);
        }
        var kept = pack.Variant("iso/desk", 0).Parts.Where(p => !adapter.Parts.Any(a => a.PartId == p.PartId)).ToArray();
        Assert.NotEmpty(kept);
        Assert.All(kept, p => Assert.Contains(scene.Items, i => i.InstanceId == "desk-a/" + p.PartId));
    }

    [Fact]
    public void PlacingTheRealAdapterMountsOnTheDeskAndUndoRestoresIt()
    {
        var editor = new IsoEditor(Merged.Value, IsoEditorCoreFixture.Canonical); var before = editor.State.Layout;
        var placed = editor.Place("iso/computer", new(4, 4), hostDeskId: "desk-a");
        Assert.True(placed.Succeeded, string.Join("; ", placed.Issues.Select(i => i.RuleId)));
        Assert.Contains(editor.State.Layout.Furniture, f => f.AssetId == "iso/computer" && f.DeskId == "desk-a");
        Assert.True(editor.Undo().Succeeded);
        Assert.Equal(before.Furniture.Count, editor.State.Layout.Furniture.Count);
    }
}
