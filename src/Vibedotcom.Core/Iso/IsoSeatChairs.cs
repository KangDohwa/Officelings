namespace Vibedotcom.Core.Iso;

public sealed partial class IsoSceneBuilder
{
    // Layout-time entries retain the static slots, ID, depth, hit target and
    // destination. Playback selects cached chair/desk entries with one lookup.
    private readonly Dictionary<(string Desk, string Sheet, int Row, int Column),
        (int[] Slots, Entry[] Entries, Entry? DeskFront)> seatChairs = [];

    private readonly Dictionary<string, int> staticSlots = new(StringComparer.Ordinal);
    private void CacheSeatChairs()
    {
        for (var k = 0; k < staticItems.Count; k++) staticSlots[staticItems[k].Item.InstanceId] = k;
        foreach (var chair in layout.Furniture.Where(f => f.AssetId == "iso/chair" && f.DeskId != null))
        {
            var parts = pack.Variant(chair.AssetId, chair.Rotation, chair.State).Parts;
            var slots = parts.Select(p => staticSlots.GetValueOrDefault(chair.Id + "/" + p.PartId, -1)).ToArray();
            if (slots.Length != 3 || slots.Any(s => s < 0)) continue; // Unchanged v2/source packs.
            // Bind only a requested actor frame; immutable pack bindings are already compiled.
            chairSlots[chair.DeskId!] = (chair, parts, slots);
        }
    }
    private readonly Dictionary<string, (IsoPlacement Chair, IReadOnlyList<IsoPart> Parts, int[] Slots)> chairSlots = new(StringComparer.Ordinal);
    private void CacheSeatChair(string desk, string sheet, IsoClip clip, int frame)
    {
        if (!chairSlots.TryGetValue(desk, out var data) || clip.ChairFrames == null) return;
        var (chair, parts, slots) = data;
        if (clip.ChairFrames.Count != clip.Frames) throw new InvalidDataException("Chair frame count differs from actor clip.");
        var binding = clip.ChairFrames[frame];
        if (binding.Clips.Count != 3) throw new InvalidDataException("Chair sandwich must have three parts.");
        var entries = new Entry[3];
        for (var k = 0; k < 3; k++)
        {
            var c = pack.Clip("iso-chair-sandwich", binding.Clips[k]);
            if (c.Direction != clip.Direction || c.PartId != parts[k].PartId ||
                c.IsoZOrder != parts[k].IsoZOrder || c.SeatGroupId != "seat")
                throw new InvalidDataException("Chair sandwich changes assembly identity/depth.");
            var original = staticItems[slots[k]];
            entries[k] = original with { Item = original.Item with
            { SheetId = "iso-chair-sandwich", Clip = c.Key, Frame = 0 } };
        }
        var selectedSlots = slots.ToList(); var selectedEntries = entries.ToList(); Entry? deskFront = null;
        if (binding.DeskFront != null)
        {
            var replacements = binding.DeskClips ?? throw new InvalidDataException("Desk partition lacks replacements.");
            if (replacements.Count % 2 != 0) throw new InvalidDataException("Invalid desk replacement pairs.");
            for (var k = 0; k < replacements.Count; k += 2)
            {
                var source = pack.Clip("iso-desk-parts", replacements[k]);
                var replacement = pack.Clip("iso-desk-sandwich", replacements[k + 1]);
                if (source.PartId != replacement.PartId || source.Direction != replacement.Direction ||
                    source.IsoZOrder != replacement.IsoZOrder || source.SeatGroupId != replacement.SeatGroupId)
                    throw new InvalidDataException("Desk partition changes identity/depth.");
                var slot = staticSlots.GetValueOrDefault(chair.DeskId + "/" + source.PartId, -1);
                if (slot < 0 && SuppressedComputerParts(layout.Furniture.Single(f => f.Id == chair.DeskId)).Contains(source.PartId!)) continue;
                if (slot < 0) throw new InvalidDataException("Missing desk partition source.");
                var original = staticItems[slot]; selectedSlots.Add(slot);
                selectedEntries.Add(original with { Item = original.Item with
                    { SheetId = "iso-desk-sandwich", Clip = replacement.Key, Frame = 0 } });
            }
            var c = pack.Clip("iso-desk-sandwich", binding.DeskFront);
            var surface = staticItems.First(e => e.Key.InstanceId == chair.DeskId &&
                pack.Clip(e.Item.SheetId, e.Item.Clip).SeatGroupId == "seat");
            if (c.IsoZOrder != 10 || c.SeatGroupId != "seat" || c.Direction != IsoGeometry.Orientation(layout.Furniture.Single(f => f.Id == chair.DeskId).Rotation))
                throw new InvalidDataException("Invalid desk front contact/rank.");
            var key = new IsoDepthKey(surface.Key.Pass, surface.Key.GroundDepth, 10, chair.DeskId!, c.PartId!);
            deskFront = new(surface.Item with { InstanceId = chair.DeskId + "/" + c.PartId,
                SheetId = "iso-desk-sandwich", Clip = c.Key, Frame = 0, Depth = key.Encoded }, key);
        }
        seatChairs.TryAdd((desk, sheet, clip.Row, clip.Column + frame),
            (selectedSlots.ToArray(), selectedEntries.ToArray(), deskFront));
    }
    private void SelectSeatChair(IsoActorPose actor, string sheet, IsoClip clip, int frame)
    {
        if (actor.SeatPhase == null || actor.DeskId == null) return;
        if (!seatChairs.ContainsKey((actor.DeskId, sheet, clip.Row, clip.Column + frame)))
            CacheSeatChair(actor.DeskId, sheet, clip, frame);
        if (!seatChairs.TryGetValue((actor.DeskId, sheet, clip.Row, clip.Column + frame), out var choice)) return;
        for (var k = 0; k < choice.Slots.Length; k++) work[choice.Slots[k]] = choice.Entries[k];
        if (choice.DeskFront != null) work.Add(choice.DeskFront);
    }

    private void SortWithSeatChairs() => SortEntries(work);
    internal static void SortEntries(List<Entry> entries)
    {
        // Encoded is already the checked, 12-decimal pass/ground/rank key.
        // Reusing it avoids enum boxing and recomputing static ground keys for
        // each comparison. The original ordinal owner/part ties are preserved.
        entries.Sort((a,b) =>
        {
            var c=a.Item.Depth.CompareTo(b.Item.Depth); if(c!=0)return c;
            c=StringComparer.Ordinal.Compare(a.Key.InstanceId,b.Key.InstanceId);
            return c!=0?c:StringComparer.Ordinal.Compare(a.Key.PartId,b.Key.PartId);
        });
    }
}
