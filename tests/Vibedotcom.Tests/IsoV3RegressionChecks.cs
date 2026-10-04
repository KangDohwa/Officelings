using System.IO;
using Vibedotcom.App;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.Tests;

// No external test dependency: both xUnit and the offline runner call these checks.
internal static class IsoV3RegressionChecks
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static IsoLayout Fixture(params IsoPlacement[] furniture) => new(10, 10, new(0, 4), furniture,
        Enumerable.Repeat(1, 100).ToArray(), [new("workspace", 1, 1, 8, 8), new("pantry", 0, 0, 10, 1),
            new("lounge", 0, 7, 3, 3), new("door", 0, 4, 1, 1)]);
    private static IsoDrawItem Body(IsoScene scene) => scene.Items.Single(i => i.Layer == IsoLayer.Character);
    private static IsoDrawItem? Top(IsoPack p, IsoScene scene, int x, int y) => scene.Items.LastOrDefault(i =>
        i.Layer is not (IsoLayer.Floor or IsoLayer.Shadow) && p.Opaque(i, x - i.X, y - i.Y));

    internal static void SeatedContactsAndOcclusion()
    {
        var p = IsoPack.LoadDefault(); var cases = 0;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var seat = IsoContacts.Desk(3, 3, rotation);
            var desk = new IsoPlacement("desk", "iso/desk", 3, 3, rotation);
            var chair = new IsoPlacement("chair", "iso/chair", seat.Approach.X, seat.Approach.Y,
                IsoGeometry.Rotation(seat.Facing), DeskId: "desk");
            var builder = new IsoSceneBuilder(p, Fixture(desk, chair));
            foreach (var design in p.Characters) for (var phase = 0; phase < 4; phase++)
            {
                var scene = builder.Build([new("actor", design.Id, "Synthetic", new(seat.Approach.X, seat.Approach.Y),
                    seat.Facing, "typing", phase / 6d, "desk", "seated")]);
                var body = Body(scene); var clip = p.Clip(body.SheetId, body.Clip);
                Require(scene.Items.Single(i => i.InstanceId == "desk/screen").Clip == "screen-working/" + IsoGeometry.Orientation(rotation),
                    "Occupied v3 monitor did not select working state");
                Require(body.Frame == phase && body.X + 16 == builder.Camera.X + seat.Pelvis.X &&
                    body.Y + 44 == builder.Camera.Y + seat.Pelvis.Y, "V3 pelvis/phase drift");
                var hands = new[] { seat.HandLeft, seat.HandRight };
                for (var h = 0; h < hands.Length; h++)
                {
                    var x = body.X + (int)clip.HandAnchors![h].X;
                    var y = body.Y + (int)clip.HandAnchors[h].Y;
                    Require(x == builder.Camera.X + hands[h].X && y == builder.Camera.Y + hands[h].Y, "V3 hand contact drift");
                    var input = p.Variant("iso/desk", rotation).Parts.Single(v => v.PartId!.EndsWith("__14") && v.IsoZOrder == -20);
                    var surface = scene.Items.Single(i => i.InstanceId == "desk/" + input.PartId);
                    Require(p.Opaque(surface, x - surface.X, y - surface.Y), "Keyboard/mouse contact floats above the desk");
                    var visible = 0;
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                        if (p.Opaque(body, x + dx - body.X, y + dy - body.Y))
                        {
                            Require(Top(p, scene, x + dx, y + dy)?.InstanceId == body.InstanceId, "V3 chair/desk covers typing hand");
                            visible++;
                        }
                    Require(visible > 0, "No hand pixels at input contact");
                }
                Require(scene.Items.Any(i => i.InstanceId.StartsWith("chair/") && i.Depth < body.Depth) &&
                    scene.Items.Any(i => i.InstanceId.StartsWith("chair/") && i.Depth > body.Depth), "Chair lost rear/front ranks");
                foreach (var part in p.Variant("iso/desk", rotation).Parts.Where(v => v.SeatGroupId == null))
                {
                    var item = scene.Items.Single(i => i.InstanceId == "desk/" + part.PartId);
                    var c = p.Clip(part.SheetId, part.Animation!, part.Direction); var d = (part.DepthAnchor ?? c.DepthAnchor)!.Value;
                    Require(item.Depth == new IsoDepthKey(IsoPass.Scene, 6 + d.I + d.J, part.IsoZOrder, "desk", part.PartId!).Encoded,
                        "Slim monitor attached to seat depth");
                }
                cases++;
            }
            var off = builder.Build([]);
            Require(off.Items.Single(i => i.InstanceId == "desk/screen").Clip.StartsWith("screen-off/"), "Empty monitor is not off");
            var waiting = builder.Build([new("actor", p.Characters[0].Id, "Synthetic", new(seat.Approach.X, seat.Approach.Y),
                seat.Facing, "thinking", 0, "desk", "seated")]);
            Require(waiting.Items.Single(i => i.InstanceId == "desk/screen").Clip.StartsWith("screen-screensaver/"), "Thinking monitor is not screensaver");
        }
        Console.WriteLine($"  seated contacts/ranks: {cases} frames, 96 input contacts");
    }

    internal static void TvScreenGlowStates()
    {
        var p = IsoPack.LoadDefault(); var cases = 0;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        foreach (var state in new[] { "default", "playing", "idle", "static", "frame-00", "frame-01", "frame-02", "frame-03" })
        {
            var builder = new IsoSceneBuilder(p, Fixture(new IsoPlacement("tv", "iso/tv-cabinet", 3, 3, rotation, state)));
            var variant = p.Variant("iso/tv-cabinet", rotation, state);
            for (var phase = 0; phase < 4; phase++)
            {
                var scene = builder.Build([], elapsedSeconds: phase / p.Clip(variant.Screen!.SheetId, variant.Screen.Animation!, variant.Screen.Direction).Fps);
                var screen = scene.Items.Single(i => i.InstanceId == "tv/screen");
                var glow = scene.Items.Single(i => i.InstanceId == "tv/glow");
                var playing = state is "default" or "playing";
                var fixedPhase = state.StartsWith("frame-") ? int.Parse(state[^2..]) : 0;
                Require(screen.Frame == (playing ? phase : 0) && glow.Frame == (playing ? phase : fixedPhase),
                    $"TV screen/glow desynchronized: rotation={rotation}, state={state}, phase={phase}, screen={screen.Frame}, glow={glow.Frame}");
                Require(p.SamePixels(screen, screen with { Clip = "screen-default/" + IsoGeometry.Orientation(rotation),
                    Frame = playing ? phase : fixedPhase }), "TV fixed screen selection drift");
                Require(glow.Alpha == 46 && glow.Depth > screen.Depth, "TV glow opacity/rank drift");
                Require(screen.X == glow.X && screen.Y == glow.Y, "TV screen/glow anchor drift");
                Require(double.IsFinite(builder.NextChangeSeconds) == playing, "TV animation deadline lost");
                var sheet = p.Sheet(screen.SheetId); var hits = 0;
                for (var y = 0; y < sheet.CellH; y++) for (var x = 0; x < sheet.CellW; x++)
                    if (p.Opaque(screen, x, y))
                    { Require(builder.HitTest(screen.X + x, screen.Y + y)?.TargetId == "tv", "TV screen hit target lost"); hits++; }
                Require(hits > 0 && variant.Glow != null, "TV screen/glow missing"); cases++;
            }
        }
        Console.WriteLine($"  TV states/glow/picking: {cases} frames");
    }

    internal static void NavigationAndTwelveAgents()
    {
        var p = IsoPack.LoadDefault(); var nav = IsoLayoutLoader.Navigation(p.Layout, p); var occupied = new HashSet<Tile>();
        foreach (var f in p.Layout.Furniture.Where(f => f.AssetId != "iso/rug"))
        {
            var v = p.Variant(f.AssetId, f.Rotation);
            for (var y = 0; y < v.FootprintH; y++) for (var x = 0; x < v.FootprintW; x++)
                Require(occupied.Add(new(f.X + x, f.Y + y)), "V3 furniture footprint overlap");
            foreach (var point in v.Points ?? []) Require(nav.FindPath(p.Layout.Door, new(f.X + point.X, f.Y + point.Y)).Count > 0,
                "V3 shelf/sofa interaction is unreachable");
        }
        Require(occupied.Count == 49 && nav.Reachable(p.Layout.Door).Count == 63, "V3 changed occupied/walkable cells");
        var office = new IsoOffice(p, 42); var people = IsoSmoke.TwelveAgents; var scene = office.Update(people, Epoch, 0);
        for (var frame = 1; frame <= 400; frame++) scene = office.Update(people, Epoch.AddSeconds(frame * .05), .05);
        Require(scene.Items.Count(i => i.Layer == IsoLayer.Character && i.Clip.StartsWith("typing/")) == 12, "Twelve v3 desks cannot be occupied");
        var gone = people.Select(a => a with { State = AgentState.Gone }).ToArray();
        for (var frame = 401; frame <= 700; frame++) scene = office.Update(gone, Epoch.AddSeconds(frame * .05), .05);
        Require(scene.Labels.Count == 0 && !scene.Items.Any(i => i.Layer == IsoLayer.Character), "Departure did not complete");
    }

    internal static void WalkersAndTransitions()
    {
        var p = IsoPack.LoadDefault(); var cases = 0;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        foreach (var family in new[] { "iso/desk", "iso/chair", "iso/sofa", "iso/shelf", "iso/tv-cabinet" })
        {
            var builder = new IsoSceneBuilder(p, Fixture(new IsoPlacement("furniture", family, 3, 3, rotation)));
            var v = p.Variant(family, rotation);
            foreach (var position in new[] { new Position(2, 3), new Position(3, 2), new Position(3 + v.FootprintW, 3), new Position(3, 3 + v.FootprintH) })
            {
                var scene = builder.Build([new("walker", p.Characters[0].Id, "Walker", position, "se", "walk", 0)]);
                Require(Body(scene).Depth == new IsoDepthKey(IsoPass.Scene, position.X + position.Y, 0, "walker", "body").Encoded,
                    "Walker borrowed furniture depth"); cases++;
            }
        }
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var seat = IsoContacts.Desk(3, 3, rotation);
            var builder = new IsoSceneBuilder(p, Fixture(new("desk", "iso/desk", 3, 3, rotation),
                new("chair", "iso/chair", seat.Approach.X, seat.Approach.Y, IsoGeometry.Rotation(seat.Facing), DeskId: "desk")));
            foreach (var design in p.Characters) foreach (var phase in new[] { "lower", "raise" }) for (var frame = 0; frame < 4; frame++)
            {
                var action = phase == "lower" ? "sit-down" : "stand-up";
                var scene = builder.Build([new("actor", design.Id, "Synthetic", new(seat.Approach.X, seat.Approach.Y), seat.Facing,
                    action, 0, "desk", phase, frame)]);
                var body = Body(scene); var clip = p.Clip(design.SheetId, action, seat.Facing); var contact = clip.PhaseContacts![frame];
                var ground = IsoGeometry.Ground(seat.Approach.X, seat.Approach.Y);
                Require(p.SamePixels(body, body with { Clip = clip.Key, Frame = frame }) &&
                    body.X == builder.Camera.X + ground.X + contact.ContactOffset.X - contact.SeatAnchor.X &&
                    body.Y == builder.Camera.Y + ground.Y + contact.ContactOffset.Y - contact.SeatAnchor.Y,
                    $"V3 transition phase/contact drift: {design.Id}/{rotation}/{phase}/{frame}, returned={body.Clip}/{body.Frame}"); cases++;
            }
        }
        Console.WriteLine($"  independent walker depth + sit/stand: {cases} cases");
    }

    internal static void WpfPixelsAndBounds()
    {
        var p = IsoPack.LoadDefault(); var office = new IsoOffice(p, 42); var people = IsoSmoke.TwelveAgents;
        var scene = office.Update(people, Epoch, 0);
        for (var frame = 1; frame <= 400; frame++) scene = office.Update(people, Epoch.AddSeconds(frame * .05), .05);
        var view = new IsoView(office, new PackIsoSheetSource(p), Path.Combine(Path.GetTempPath(), "iso-v3-synthetic-no-brand"));
        view.Present(scene, people);
        foreach (var item in scene.Items)
        {
            var s = p.Sheet(item.SheetId);
            for (var y = 0; y < s.CellH; y++) for (var x = 0; x < s.CellW; x++)
                if (p.Opaque(item, x, y)) Require(item.X + x >= 0 && item.X + x < scene.Width && item.Y + y >= 0 && item.Y + y < scene.Height,
                    "V3 sprite clipped by scene bounds");
        }
        var bitmap = Vibedotcom.App.Program.RenderBitmap(view, scene.Width, scene.Height);
        var native = new byte[scene.Width * scene.Height * 4]; bitmap.CopyPixels(native, scene.Width * 4, 0);
        foreach (var zoom in new[] { 2, 3 })
        {
            view.SetZoom(zoom); bitmap = Vibedotcom.App.Program.RenderBitmap(view, scene.Width * zoom, scene.Height * zoom);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            for (var y = 0; y < scene.Height; y++) for (var x = 0; x < scene.Width; x++)
            for (var dy = 0; dy < zoom; dy++) for (var dx = 0; dx < zoom; dx++)
                Require(native.AsSpan((y * scene.Width + x) * 4, 4).SequenceEqual(
                    pixels.AsSpan(((y * zoom + dy) * bitmap.PixelWidth + x * zoom + dx) * 4, 4)), "WPF zoom is not exact nearest-neighbor");
        }
    }
}
