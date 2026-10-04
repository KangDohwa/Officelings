namespace Vibedotcom.Core.Iso;

public sealed partial class IsoSceneBuilder
{
    // Layout-time entries retain the static slots, ID, depth, hit target and
    // destination. Playback is one dictionary lookup plus three assignments.
    private readonly Dictionary<(string Desk, string Sheet, int Row, int Column),
        (int[] Slots, Entry[] Entries)> seatChairs = [];

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
                    seatChairs.TryAdd((chair.DeskId!, design.SheetId, clip.Row, clip.Column + frame), (slots, entries));
                }
            }
        }
    }

    private void SelectSeatChair(IsoActorPose actor, string sheet, IsoClip clip, int frame)
    {
        if (actor.SeatPhase == null || actor.DeskId == null ||
            !seatChairs.TryGetValue((actor.DeskId, sheet, clip.Row, clip.Column + frame), out var choice)) return;
        for (var k = 0; k < choice.Slots.Length; k++) work[choice.Slots[k]] = choice.Entries[k];
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
