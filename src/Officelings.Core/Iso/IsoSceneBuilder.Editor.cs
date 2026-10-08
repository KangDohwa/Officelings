using Officelings.Core.Iso.Editing;

namespace Officelings.Core.Iso;

public sealed partial class IsoSceneBuilder
{
    internal IReadOnlyList<Entry> EditorEntries => work;
    internal int EditorEmptyItemCount => staticItems.Count + screens.Count + animatedFurniture.Count;
    private readonly HashSet<string>? enabledSeats;
    internal bool EnabledSeat(string id) => enabledSeats == null || enabledSeats.Contains(id);
    private HashSet<string> SuppressedComputerParts(IsoPlacement desk)
    {
        var mounted = layout.Furniture.FirstOrDefault(f => f.AssetId == "iso/computer" && f.DeskId == desk.Id);
        if (desk.AssetId != "iso/desk" || mounted == null) return [];
        var v = pack.Variant(mounted.AssetId, mounted.Rotation, mounted.State);
        return v.Parts.Concat(new[] { v.Screen, v.Glow }.OfType<IsoPart>()).Select(p =>
            p.PartId ?? pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction).PartId ?? "").ToHashSet(StringComparer.Ordinal);
    }
    private string PartIdentity(IsoPart p) => p.PartId ?? pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction).PartId ?? "";
}
