using System.IO;
using System.Text.Json;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.DepthChecks;

// Deliberately does not call the engine's ordering implementation or decode Depth.
// Tile centers imply half-open rectangles [x-.5,x+w-.5] × [y-.5,y+h-.5].
internal static class DepthOracle
{
    internal static bool LegacyOrder { get; set; }
    internal static IsoScene Order(IsoScene scene) => LegacyOrder ? scene with
    { Items = scene.Items.OrderBy(i => i.Depth).ThenBy(i => Owner(i), StringComparer.Ordinal).ThenBy(i => i.InstanceId, StringComparer.Ordinal).ToArray() } : scene;
    internal sealed record Violation(string A, string B, string ExpectedTop, string ActualTop,
        string PositionA, string PositionB, string FootprintA, string FootprintB, long KeyA, long KeyB,
        int Left, int Top, int Right, int Bottom)
    { public string FileId => A.Replace('/', '_') + "--" + B.Replace('/', '_'); }
    internal sealed record AuditResult(int OverlappingPairs, List<Violation> Violations);
    private readonly record struct Box(double I0, double J0, double I1, double J1, double Z0, double Z1);
    private static Box Bounds(IsoPlacement f, IsoFurnitureVariant v) => new(f.X - .5, f.Y - .5,
        f.X + v.FootprintW - .5, f.Y + v.FootprintH - .5, 0, Height(f.AssetId));
    // Independent frozen S0 physical extents (not sprite bottom or engine depth).
    private static double Height(string family) => family switch
    {
        "iso/desk" => 40, "iso/shelf" or "iso/fridge" => 44, "iso/floor-lamp" or "iso/water-cooler" => 40,
        "iso/plant" => 34, "iso/coffee-machine" => 28, "iso/tv-cabinet" => 38,
        "iso/chair" => 21, "iso/sofa" => 26, "iso/side-table" => 16, _ => 0
    };
    private static int Relation(Box a, Box b)
    {
        // Separating-plane visibility along camera (+i,+j,+z) axes.
        var planes = new[] { (a.I1, b.I0, b.I1, a.I0), (a.J1, b.J0, b.J1, a.J0), (a.Z1, b.Z0, b.Z1, a.Z0) };
        var behind = planes.Any(p => p.Item1 <= p.Item2);
        var ahead = planes.Any(p => p.Item3 <= p.Item4);
        return behind == ahead ? 0 : behind ? -1 : 1;
    }
    private static string Owner(IsoDrawItem item) => item.InstanceId[..item.InstanceId.IndexOf('/')];
    private sealed record Mask(bool[] Pixels, int W, int L, int T, int R, int B);
    private static readonly Dictionary<(IsoPack Pack, string Sheet, string Clip, int Frame), Mask> masks = [];
    private static Mask Alpha(IsoPack pack, IsoDrawItem item)
    {
        var key = (pack, item.SheetId, item.Clip, item.Frame);
        if (masks.TryGetValue(key, out var mask)) return mask;
        var sheet = pack.Sheet(item.SheetId); var pixels = new bool[sheet.CellW * sheet.CellH];
        var l = sheet.CellW; var t = sheet.CellH; var r = 0; var b = 0;
        for (var y = 0; y < sheet.CellH; y++) for (var x = 0; x < sheet.CellW; x++)
            if (pack.Opaque(item, x, y))
            { pixels[y * sheet.CellW + x] = true; l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x + 1); b = Math.Max(b, y + 1); }
        return masks[key] = new(pixels, sheet.CellW, l, t, r, b);
    }
    private static (int L, int T, int R, int B)? Overlap(IsoPack pack, IsoDrawItem a, IsoDrawItem b)
    {
        var sa = Alpha(pack, a); var sb = Alpha(pack, b);
        var l = Math.Max(a.X + sa.L, b.X + sb.L); var t = Math.Max(a.Y + sa.T, b.Y + sb.T);
        var r = Math.Min(a.X + sa.R, b.X + sb.R); var bottom = Math.Min(a.Y + sa.B, b.Y + sb.B);
        if (l >= r || t >= bottom) return null;
        var x0 = r; var y0 = bottom; var x1 = l; var y1 = t;
        for (var y = t; y < bottom; y++) for (var x = l; x < r; x++)
            if (sa.Pixels[(y - a.Y) * sa.W + x - a.X] && sb.Pixels[(y - b.Y) * sb.W + x - b.X])
            { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x + 1); y1 = Math.Max(y1, y + 1); }
        return x0 < x1 ? (x0, y0, x1, y1) : null;
    }
    internal static AuditResult Audit(IsoPack pack, IsoLayout layout, IsoScene scene)
    {
        var placements = layout.Furniture.ToDictionary(f => f.Id); var violations = new List<Violation>(); var pairs = 0;
        var items = scene.Items.Where(i => i.Layer is IsoLayer.Furniture or IsoLayer.Wall).ToArray();
        for (var i = 0; i < items.Length; i++) for (var j = i + 1; j < items.Length; j++)
        {
            var a = items[i]; var b = items[j];
            if (Overlap(pack, a, b) is not {} overlap) continue;
            pairs++;
            var hasA = placements.TryGetValue(Owner(a), out var fa); var hasB = placements.TryGetValue(Owner(b), out var fb);
            var expected = 0;
            if (hasA && hasB && fa!.Id != fb!.Id)
            {
                var va = pack.Variant(fa.AssetId, fa.Rotation); var vb = pack.Variant(fb.AssetId, fb.Rotation);
                // Desk/chair form the existing S0 attached seat assembly.
                expected = fa.DeskId == fb.Id || fb.DeskId == fa.Id ? 0 : va.Layer == 0 ? -1 : vb.Layer == 0 ? 1 : Relation(Bounds(fa, va), Bounds(fb, vb));
            }
            else if (hasA != hasB) expected = hasA ? 1 : -1; // exterior wall/decor/door pass
            else if (hasA && fa!.Id == fb!.Id && fa.AssetId == "iso/desk")
            {
                static bool Elevated(IsoDrawItem item) => item.InstanceId.Contains("__monitor__") ||
                    item.InstanceId.EndsWith("/screen") || item.InstanceId.EndsWith("__15") || item.InstanceId.EndsWith("__16");
                if (Elevated(a) != Elevated(b)) expected = Elevated(a) ? 1 : -1;
            }
            if (expected <= 0) continue; // a was drawn first, so b is actually on top
            string Pos(IsoPlacement? f) => f == null ? "wall" : $"({f.X},{f.Y})/{f.Rotation}";
            string Foot(IsoPlacement? f) => f == null ? "wall z0..48" : Bounds(f, pack.Variant(f.AssetId, f.Rotation)).ToString();
            violations.Add(new(a.InstanceId, b.InstanceId, a.InstanceId, b.InstanceId, Pos(fa), Pos(fb), Foot(fa), Foot(fb), a.Depth, b.Depth,
                Math.Max(0, overlap.L - 12), Math.Max(0, overlap.T - 12), Math.Min(scene.Width, overlap.R + 12), Math.Min(scene.Height, overlap.B + 12)));
        }
        return new(pairs, violations);
    }
    internal static List<string> WalkerViolations(IsoPack pack, IsoLayout layout, IsoScene scene, string id, Position position)
    {
        var result = new List<string>(); var actorIndex = scene.Items.ToList().FindIndex(i => i.InstanceId == id + "/body");
        var actor = scene.Items[actorIndex];
        foreach (var f in layout.Furniture)
        {
            var v = pack.Variant(f.AssetId, f.Rotation); if (v.Layer == 0) continue;
            var expected = Relation(new(position.X, position.Y, position.X, position.Y, 0, 40), Bounds(f, v));
            for (var i = 0; i < scene.Items.Count; i++)
            {
                var item = scene.Items[i];
                if (item.Layer != IsoLayer.Furniture || Owner(item) != f.Id || Overlap(pack, actor, item) == null) continue;
                if (expected != 0 && expected != (actorIndex < i ? -1 : 1)) result.Add(item.InstanceId);
            }
        }
        return result;
    }
    internal static int CheckAll(IsoPack pack, string output)
    {
        var failures = new List<string>(); var checks = 0;
        var reports = new List<object>();
        var supportCases = 0; var walkerCases = 0;
        void Check(string name, IsoLayout layout)
        {
            var builder = new IsoSceneBuilder(pack, layout); var original = builder.Build([]); var scene = Order(original); checks++;
            var audit = Audit(pack, layout, scene);
            if (audit.Violations.Count > 0) reports.Add(new { name, audit.OverlappingPairs, audit.Violations });
            if (audit.Violations.Count > 0) failures.Add($"{name}: {audit.Violations.Count} order violations");
            if (!ReferenceEquals(original, builder.Build([]))) failures.Add(name + ": stable scene cache lost");
            var shuffled = Order(new IsoSceneBuilder(pack, layout with { Furniture = layout.Furniture.Reverse().ToArray() }).Build([]));
            if (!scene.Items.SequenceEqual(shuffled.Items)) failures.Add(name + ": input-order dependent");
        }
        Check("default", pack.Layout); Check("source-default", IsoLayoutLoader.Default(pack));
        var furnitureCases = 0;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        foreach (var family in pack.Manifest.Furniture!.Where(f => f.Id != "iso/chair"))
        foreach (var otherRotation in new[] { 0, 90, 180, 270 })
        {
            var desk = new IsoPlacement("desk", "iso/desk", 4, 4, rotation); var vd = pack.Variant(desk.AssetId, rotation);
            var vo = pack.Variant(family.Id, otherRotation);
            foreach (var point in new[] { (4 - vo.FootprintW, 4), (4, 4 - vo.FootprintH), (4 + vd.FootprintW, 4), (4, 4 + vd.FootprintH) })
            {
                var fixture = Fixture(desk, new IsoPlacement("other", family.Id, point.Item1, point.Item2, otherRotation));
                try { IsoLayoutLoader.Validate(fixture, pack); } catch (InvalidDataException) { continue; }
                Check($"furniture/{rotation}/{family.Id}/{otherRotation}/{point}", fixture); furnitureCases++;
            }
        }
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var layout = Fixture(new IsoPlacement("desk", "iso/desk", 4, 4, rotation));
            foreach (var state in new[] { "off", "screensaver", "working" }) for (var phase = 0; phase < 4; phase++)
            {
                var f = layout.Furniture[0]; var actors = state == "off" ? Array.Empty<IsoActorPose>() :
                    new[] { new IsoActorPose("owner", pack.Characters[0].Id, "Synthetic", new(1, 1), "se", state == "working" ? "typing" : "thinking", 0, "desk") };
                var scene = Order(new IsoSceneBuilder(pack, layout).Build(actors, elapsedSeconds: phase / 3d)); checks++;
                var audit = Audit(pack, layout, scene);
                if (audit.Violations.Count > 0) failures.Add($"desk/{rotation}/{state}/{phase}: {audit.Violations.Count} violations");
                var items = scene.Items.Where(i => i.Layer == IsoLayer.Furniture).ToArray();
                // Support semantics are checked even when authored masks are disjoint.
                // Keyboard/mouse are baked into rear-14's horizontal surface mask.
                static int Tier(IsoDrawItem item) => item.InstanceId.EndsWith("/screen") ? 3 :
                    item.InstanceId.Contains("__monitor__") || item.InstanceId.EndsWith("__15") || item.InstanceId.EndsWith("__16") ? 2 :
                    item.InstanceId.EndsWith("__14") ? 1 : 0;
                for (var i = 0; i < items.Length; i++) for (var j = i + 1; j < items.Length; j++)
                    if (Tier(items[i]) != Tier(items[j]))
                    {
                        checks++; supportCases++;
                        if (Tier(items[i]) > Tier(items[j])) failures.Add($"support/(4,4)/{rotation}/{state}/{phase}: {items[i].InstanceId} key={items[i].Depth} below {items[j].InstanceId} key={items[j].Depth}");
                    }
            }
            foreach (var family in pack.Manifest.Furniture!.Where(f => f.Id != "iso/rug"))
            {
                var f = new IsoPlacement("furniture", family.Id, 4, 4, rotation); var v = pack.Variant(f.AssetId, rotation);
                var builder = new IsoSceneBuilder(pack, Fixture(f));
                foreach (var position in new[] { new Position(3, 4), new Position(4, 3), new Position(4 + v.FootprintW, 4), new Position(4, 4 + v.FootprintH) })
                foreach (var facing in new[] { "se", "sw", "ne", "nw" })
                {
                    var scene = Order(builder.Build([new("walker", pack.Characters[0].Id, "Synthetic", position, facing, "walk", 0)],
                        new(position, facing, "walk", 0)));
                    foreach (var actor in scene.Items.Where(i => i.Layer is IsoLayer.Character or IsoLayer.Pet))
                    foreach (var item in scene.Items.Where(i => i.Layer == IsoLayer.Furniture))
                    {
                        if (Overlap(pack, actor, item) == null) continue;
                        checks++; walkerCases++; var expected = Relation(new(position.X, position.Y, position.X, position.Y, 0,
                            actor.Layer == IsoLayer.Pet ? 18 : 40), Bounds(f, v));
                        var actual = scene.Items.ToList().IndexOf(actor) < scene.Items.ToList().IndexOf(item) ? -1 : 1;
                        if (expected != 0 && expected != actual) failures.Add($"walker/{family.Id}/(4,4)/{rotation}/{position}/{facing}/{actor.Layer}: {actor.InstanceId} key={actor.Depth} vs {item.InstanceId} key={item.Depth}; expected={expected}; actual={actual}");
                    }
                }
            }
        }
        // Static idle updates must retain the same scene with zero managed allocations.
        var office = new IsoOffice(pack, 17, Fixture(new IsoPlacement("lamp", "iso/floor-lamp", 4, 4))) { PetsEnabled = false };
        var now = DateTimeOffset.Parse("2026-10-04T00:00:00Z"); office.Update([], now, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) office.Update([], now, .001);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        checks++;
        if (allocated != 0) failures.Add("idle allocations: " + allocated);
        var random = new Random(74129); var legal = 0; var attempts = 0;
        while (legal < 100 && attempts++ < 10000)
        {
            var furniture = new List<IsoPlacement>(); var occupied = new HashSet<(int, int)>();
            foreach (var family in pack.Manifest.Furniture!.Where(f => f.Id is not ("iso/rug" or "iso/chair")))
            {
                var rotation = new[] { 0, 90, 180, 270 }[random.Next(4)]; var v = pack.Variant(family.Id, rotation);
                var x = random.Next(2, 13 - v.FootprintW); var y = random.Next(2, 13 - v.FootprintH);
                var cells = Enumerable.Range(0, v.FootprintW * v.FootprintH).Select(k => (x + k % v.FootprintW, y + k / v.FootprintW)).ToArray();
                if (cells.Any(occupied.Contains)) continue;
                occupied.UnionWith(cells); furniture.Add(new(family.Id[4..], family.Id, x, y, rotation));
            }
            var layout = Fixture(furniture.ToArray()) with { WidthTiles = 14, HeightTiles = 14, Tiles = Enumerable.Repeat(1, 196).ToArray() };
            try { IsoLayoutLoader.Validate(layout, pack); } catch (InvalidDataException) { continue; }
            Check("random-" + legal++, layout);
        }
        if (legal != 100) failures.Add("Insufficient legal randomized layouts");
        File.WriteAllLines(Path.Combine(output, "failures.txt"), failures);
        File.WriteAllText(Path.Combine(output, "fixture-violations.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"depth checks={checks}; furniture fixtures={furnitureCases}; support pairs={supportCases}; walker/pet overlaps={walkerCases}; random legal layouts={legal}; idle bytes={allocated}; failures={failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }
    internal static IsoLayout Fixture(params IsoPlacement[] furniture) => new(10, 10, new(0, 0), furniture,
        Enumerable.Repeat(1, 100).ToArray(), [new("workspace", 0, 0, 10, 10), new("pantry", 0, 0, 10, 1),
            new("lounge", 0, 9, 10, 1), new("door", 0, 0, 1, 1)]);
}
