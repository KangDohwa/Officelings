using Vibedotcom.Core.Iso;
using Vibedotcom.DepthChecks;

namespace Vibedotcom.Tests;

internal static class IsoSeatOrderChecks
{
    // Check the actual mixed queue: disjoint assembly parts still retain their
    // ranks while surrounding furniture keeps the independent footprint order.
    internal static void OccupiedLayouts()
    {
        var pack = IsoPack.LoadDefault(); var cases = 0; var pixels = 0;
        foreach (var layout in new[] { pack.Layout, IsoLayoutLoader.Default(pack) })
        {
            var builder = new IsoSceneBuilder(pack, layout);
            var desks = layout.Furniture.Where(f => f.AssetId == "iso/desk").ToArray();
            var attached = layout.Furniture.SelectMany(f => pack.Variant(f.AssetId, f.Rotation, f.State).Parts
                .Where(p => p.SeatGroupId != null).Select(p => f.Id + "/" + p.PartId)).ToHashSet();
            foreach (var action in new[] { "idle", "typing", "thinking", "sleep", "sit-down", "stand-up" })
            for (var frame = 0; frame < 4; frame++)
            {
                var poses = desks.Select((desk, n) =>
                {
                    var seat = IsoContacts.Desk(desk.X, desk.Y, desk.Rotation);
                    var design = pack.Characters[n % pack.Characters.Count];
                    var clip = pack.Clip(design.SheetId, action, seat.Facing);
                    return new IsoActorPose("sitter-" + n, design.Id, "Synthetic", new(seat.Approach.X, seat.Approach.Y),
                        seat.Facing, action, frame / clip.Fps, desk.Id,
                        action switch { "idle" => "turn", "sit-down" => "lower", "stand-up" => "raise", _ => "seated" }, frame);
                }).ToArray();
                var scene = builder.Build(poses);
                // Attached strips use the S0 seat contact plane, not the full
                // desk rectangle assumed by the ordinary furniture oracle.
                var ordinary = scene with { Items = scene.Items.Where(i => !attached.Contains(i.InstanceId)).ToArray() };
                var violations = DepthOracle.Audit(pack, layout, ordinary).Violations;
                if (violations.Count != 0)
                    throw new InvalidOperationException("Occupied layout inverted surrounding furniture: " + action + "/" + frame + ": " + string.Join("; ", violations.Take(5)));
                foreach (var pose in poses)
                {
                    var chair = layout.Furniture.Single(f => f.DeskId == pose.DeskId && f.AssetId == "iso/chair");
                    var body = scene.Items.Single(i => i.InstanceId == pose.AgentId + "/body");
                    var deskParts = pack.Variant("iso/desk", desks.Single(d => d.Id == pose.DeskId).Rotation).Parts
                        .Where(p => p.SeatGroupId != null).Select(p => pose.DeskId + "/" + p.PartId).ToHashSet();
                    var assembly = scene.Items.Where(i => i == body || deskParts.Contains(i.InstanceId) ||
                        i.Layer == IsoLayer.Furniture && i.InstanceId.StartsWith(chair.Id + "/", StringComparison.Ordinal)).ToArray();
                    static string Owner(IsoDrawItem i) => i.InstanceId[..i.InstanceId.IndexOf('/')];
                    static string Part(IsoDrawItem i) => i.InstanceId[(i.InstanceId.IndexOf('/') + 1)..];
                    var expected = assembly.OrderBy(i => i.Depth).ThenBy(Owner, StringComparer.Ordinal).ThenBy(Part, StringComparer.Ordinal);
                    if (!assembly.SequenceEqual(expected)) throw new InvalidOperationException("Attached assembly rank inversion: " + pose.AgentId);
                    // Reuse the independent geometry masks without other sitters
                    // or desks, preserving the order from the complete scene.
                    var local = scene with { Items = assembly.Where(i => i == body || Owner(i) == chair.Id)
                        .Select(i => i == body ? i : i with { InstanceId = "chair/" + Part(i) }).ToArray() };
                    pixels += IsoSeatAssemblyChecks.CheckPixels(pack, local);
                }
                cases++;
            }
        }
        Console.WriteLine($"  occupied layouts: {cases} mixed scenes, {pixels} chair intersections; assembly ranks and ordinary furniture oracle preserved");
    }
}
