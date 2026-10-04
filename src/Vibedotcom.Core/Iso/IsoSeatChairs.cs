namespace Vibedotcom.Core.Iso;

public sealed partial class IsoSceneBuilder
{
    // Layout-time entries retain the static slots, ID, depth, hit target and
    // destination. Playback selects cached chair/desk entries with one lookup.
    private readonly Dictionary<(string Desk, string Sheet, int Row, int Column),
        (int[] Slots, Entry[] Entries, Entry? DeskFront)> seatChairs = [];

    private void CacheSeatChairs()
    {
        foreach (var chair in layout.Furniture.Where(f => f.AssetId == "iso/chair" && f.DeskId != null))
        {
            var parts = pack.Variant(chair.AssetId, chair.Rotation, chair.State).Parts;
            var slots = parts.Select(p => staticItems.FindIndex(e => e.Item.InstanceId == chair.Id + "/" + p.PartId)).ToArray();
            if (slots.Length != 3 || slots.Any(s => s < 0)) continue; // Unchanged v2/source packs.
            foreach (var design in pack.Characters)
            foreach (var clip in pack.Sheet(design.SheetId).Clips ?? pack.Sheet(design.SheetId).Rows!)
            {
                if (clip.Direction != IsoGeometry.Orientation(chair.Rotation) || clip.ChairFrames == null) continue;
                if (clip.ChairFrames.Count != clip.Frames) throw new InvalidDataException("Chair frame count differs from actor clip.");
                for (var frame = 0; frame < clip.Frames; frame++)
                {
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
                            var slot = staticItems.FindIndex(e => e.Item.InstanceId == chair.DeskId + "/" + source.PartId);
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
                    seatChairs.TryAdd((chair.DeskId!, design.SheetId, clip.Row, clip.Column + frame),
                        (selectedSlots.ToArray(), selectedEntries.ToArray(), deskFront));
                }
            }
        }
    }

    private void SelectSeatChair(IsoActorPose actor, string sheet, IsoClip clip, int frame)
    {
        if (actor.SeatPhase == null || actor.DeskId == null ||
            !seatChairs.TryGetValue((actor.DeskId, sheet, clip.Row, clip.Column + frame), out var choice)) return;
        for (var k = 0; k < choice.Slots.Length; k++) work[choice.Slots[k]] = choice.Entries[k];
        if (choice.DeskFront != null) work.Add(choice.DeskFront);
    }

    private void SortWithSeatChairs()
    {
        if (seatChairs.Count == 0) { work.Sort((a,b) => a.Key.CompareTo(b.Key)); return; }
        // Encoded is already the checked, 12-decimal pass/ground/rank key.
        // Reusing it avoids enum boxing and recomputing static ground keys for
        // each comparison. The original ordinal owner/part ties are preserved.
        work.Sort((a,b) =>
        {
            var c=a.Item.Depth.CompareTo(b.Item.Depth); if(c!=0)return c;
            c=StringComparer.Ordinal.Compare(a.Key.InstanceId,b.Key.InstanceId);
            return c!=0?c:StringComparer.Ordinal.Compare(a.Key.PartId,b.Key.PartId);
        });
    }
}
