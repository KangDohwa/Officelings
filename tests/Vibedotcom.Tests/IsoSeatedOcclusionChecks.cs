using System.Security.Cryptography;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.Tests;

// The complete runtime queue is authoritative, including ordinary desk depths
// and the attached chair's seat depth. No simplified furniture-only ordering.
internal static class IsoSeatedOcclusionChecks
{
    private static void Require(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); }

    internal static IsoSceneBuilder Fixture(IsoPack pack, int rotation)
    {
        var seat = IsoContacts.Desk(3, 3, rotation);
        return new(pack, new(10, 10, new(0, 4), [new("desk", "iso/desk", 3, 3, rotation),
            new("chair", "iso/chair", seat.Approach.X, seat.Approach.Y,
                IsoGeometry.Rotation(seat.Facing), DeskId: "desk")], Enumerable.Repeat(1, 100).ToArray(),
            [new("workspace", 1, 1, 8, 8), new("pantry", 0, 0, 10, 1),
                new("lounge", 0, 7, 3, 3), new("door", 0, 4, 1, 1)]));
    }

    private static IsoDrawItem? Top(IsoPack pack, IsoScene scene, int x, int y) =>
        scene.Items.LastOrDefault(i => i.Layer is not (IsoLayer.Floor or IsoLayer.Shadow) &&
            pack.Opaque(i, x - i.X, y - i.Y));

    private static int CheckLowerBody(IsoPack pack, IsoScene scene, string facing, int pelvisY, string context)
    {
        var body = scene.Items.Single(i => i.Layer == IsoLayer.Character);
        var chair = scene.Items.Where(i => i.InstanceId.StartsWith("chair/") && i.Layer == IsoLayer.Furniture).ToArray();
        var desk = scene.Items.Where(i => i.InstanceId.StartsWith("desk/") && i.Layer == IsoLayer.Furniture).ToArray();
        var overlaps = 0;
        // Chair coverage is geometric and bidirectional; the previous blanket
        // y>=64 assertion incorrectly hid every cushion/base overlap.
        if (pack.Clip(body.SheetId, body.Clip).ChairFrames != null)
            overlaps += IsoSeatAssemblyChecks.CheckPixels(pack, scene);
        for (var y = pelvisY - 1; y < 64; y++) for (var x = 0; x < 32; x++)
        {
            if (!pack.Opaque(body, x, y)) continue;
            var px = body.X + x; var py = body.Y + y;
            // The cushion begins at chair-cell y=64. The far arm above it is
            // rear and may sit behind a rising pelvis; front parts always occlude.
            if (pack.Clip(body.SheetId, body.Clip).ChairFrames == null && facing is "ne" or "nw" && chair.Any(i => (i.Depth > body.Depth || py - i.Y >= 64) &&
                pack.Opaque(i, px - i.X, py - i.Y)))
            {
                overlaps++;
                Require(Top(pack, scene, px, py)?.InstanceId != body.InstanceId,
                    $"{context}: pelvis/leg ({x},{y}) is above the chair");
            }
            // Horizontal surface/input parts are intentionally rear (-20).
            // Physical desk fronts and ordinary near legs must cover the sitter.
            foreach (var part in desk.Where(i => i.Depth > body.Depth && pack.Opaque(i, px - i.X, py - i.Y)))
            {
                overlaps++;
                Require(Top(pack, scene, px, py)?.InstanceId != body.InstanceId,
                    $"{context}: lower body ({x},{y}) is above {part.InstanceId}");
            }
        }
        return overlaps;
    }

    internal static void TypingLowerBody()
    {
        var pack = IsoPack.LoadDefault(); var cases = 0; var overlaps = 0;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var seat = IsoContacts.Desk(3, 3, rotation); var builder = Fixture(pack, rotation);
            foreach (var design in pack.Characters) for (var phase = 0; phase < 4; phase++)
            {
                var context = $"{design.Id}/{seat.Facing}/typing/{phase}";
                var scene = builder.Build([new("actor", design.Id, "Synthetic", new(seat.Approach.X, seat.Approach.Y),
                    seat.Facing, "typing", phase / 6d, "desk", "seated")]);
                var body = scene.Items.Single(i => i.Layer == IsoLayer.Character);
                Require(body.Frame == phase, context + ": wrong typing frame");
                // Check ranks independently of the final ownership assertion:
                // lowering a front to rear must not turn the test into a no-op.
                foreach (var part in pack.Variant("iso/desk", rotation).Parts)
                {
                    var item = scene.Items.Single(i => i.InstanceId == "desk/" + part.PartId);
                    if (part.SeatGroupId != null)
                        Require(item.Depth == body.Depth + part.IsoZOrder, context + ": desk seat rank drift");
                    if (part.PartId!.Contains("__front__"))
                        Require(part.IsoZOrder == 10 && (seat.Facing is "ne" or "nw" || item.Depth > body.Depth),
                            context + ": desk front rank drift");
                }
                var count = CheckLowerBody(pack, scene, seat.Facing, 44, context);
                Require(count > 0, context + ": no furniture/body intersection tested");
                overlaps += count; cases++;
            }
        }
        Console.WriteLine($"  seated lower-body ownership: {cases} frames, {overlaps} intersecting pixels");
    }

    internal static void SeatTransitions()
    {
        var pack = IsoPack.LoadDefault(); var cases = 0; var overlaps = 0;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var seat = IsoContacts.Desk(3, 3, rotation); var builder = Fixture(pack, rotation);
            foreach (var design in pack.Characters)
            {
                IsoDrawItem? lowered = null, raised = null;
                foreach (var action in new[] { "sit-down", "stand-up" }) for (var phase = 0; phase < 4; phase++)
                {
                    var clip = pack.Clip(design.SheetId, action, seat.Facing); var contact = clip.PhaseContacts![phase];
                    var scene = builder.Build([new("actor", design.Id, "Synthetic", new(seat.Approach.X, seat.Approach.Y),
                        seat.Facing, action, 0, "desk", action == "sit-down" ? "lower" : "raise", phase)]);
                    overlaps += CheckLowerBody(pack, scene, seat.Facing, (int)contact.SeatAnchor.Y,
                        $"{design.Id}/{seat.Facing}/{action}/{phase}");
                    var body = scene.Items.Single(i => i.Layer == IsoLayer.Character);
                    Require(body.Y + contact.FootY == builder.Camera.Y + IsoGeometry.Ground(seat.Approach.X, seat.Approach.Y).Y,
                        "Transition sole left the floor");
                    if (action == "sit-down" && phase == 3) lowered = body;
                    if (action == "stand-up" && phase == 0) raised = body;
                    cases++;
                }
                Require(lowered!.X == raised!.X && lowered.Y == raised.Y && pack.SamePixels(lowered, raised),
                    "Lower/raise seated endpoints pop");
                foreach (var offset in new[] { -.5, 0, .5 })
                {
                    var position = new Position(seat.Approach.X + offset, seat.Approach.Y);
                    var scene = builder.Build([new("walker", design.Id, "Synthetic", position, seat.Facing, "walk", 0)]);
                    var body = scene.Items.Single(i => i.Layer == IsoLayer.Character);
                    Require(body.Depth == new IsoDepthKey(IsoPass.Scene, position.X + position.Y, 0, "walker", "body").Encoded,
                        "Walker borrowed seat rank");
                    cases++;
                }
            }
        }
        Console.WriteLine($"  seat transition/walker ownership: {cases} frames, {overlaps} intersecting pixels");
    }

    private static byte[] Pixels(IsoPack pack, IsoDrawItem item)
    {
        var frame = pack.Frame(item);
        return IsoPngMask.Read(pack.Images[item.SheetId]).CopyCell(frame.X, frame.Y, frame.Width, frame.Height);
    }

    internal static void ChairUnionAndVisibility()
    {
        var pack = IsoPack.LoadDefault(); var cases = 0; var minimumBack = 1d; var minimumFront = 1d;
        string[] facings = ["se", "sw", "ne", "nw"];
        // Reviewed 40% slimmer z10.5..12 cushion/z21 back (lane/chairslim).
        // These source RGBA goldens are independent of PNG encoding and retain
        // the exact union gate; S0 heights and the three part ranks are unchanged.
        string[] emptyHashes = ["F03780927795EE35DA094F5BB9ABD6BB8371F0E14E92B276F1D67399CE8D61A8",
            "4D62DFC34FE61E538A802F7BA48D438FBC975B52005C9512AF1D7A115C756310",
            "57459960C8E4C896DE447044703454496B02240C9379DC3B37C5CFB4069607E2",
            "1CCBA50DC5F16A51047E37FCAEF942F7D0E9CDD06A9C61A215085590E2A69F05"];
        for (var f = 0; f < facings.Length; f++)
        {
            var parts = pack.Variant("iso/chair", IsoGeometry.Rotation(facings[f])).Parts;
            var union = new byte[128 * 128 * 4];
            foreach (var part in parts)
            {
                var bytes = Pixels(pack, new("part", IsoLayer.Furniture, part.SheetId,
                    pack.Clip(part.SheetId, part.Animation!, part.Direction).Key, 0, 0, 0, 0));
                for (var p = 0; p < bytes.Length; p += 4) if (bytes[p + 3] != 0)
                {
                    Require(union[p + 3] == 0, "Chair parts have duplicate pixels");
                    bytes.AsSpan(p, 4).CopyTo(union.AsSpan(p, 4));
                }
            }
            Require(Convert.ToHexString(SHA256.HashData(union)) == emptyHashes[f], "Empty chair lost source pixels");
        }
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var seat = IsoContacts.Desk(3, 3, rotation); var builder = Fixture(pack, rotation);
            var back = seat.Facing is "ne" or "nw";
            foreach (var design in pack.Characters)
            {
                var punk = design.Id.EndsWith("punk-girl"); var hoodie = design.Id.EndsWith("dev-hoodie-v2");
                // Head bounds come from the idle source rig, before furniture
                // overlap. Include the neck boundary for a conservative head ROI.
                var glasses = design.Id.EndsWith("glasses-bun"); var hanbok = design.Id.EndsWith("hanbok-modern");
                var braid = design.Id.EndsWith("red-braid");
                var headEnd = glasses || hanbok || braid ? 40 : hoodie ? (back ? 39 : 41) : punk || !back ? 38 : 36;
                var idle = new IsoDrawItem("head", IsoLayer.Character, design.SheetId, "idle/" + seat.Facing, 0, 0, 0, 0);
                var idlePixels = Pixels(pack, idle);
                int[] skin = glasses ? [0xDBAA72, 0xF4BC7B, 0xF7CD94, 0xFCEAC7] :
                    hanbok ? [0xC9896A, 0xFBD3A8, 0xFBDDBC] : braid ? [0xF39E62, 0xF6DEB8, 0xFBC68D] :
                    hoodie ? [0x6D3713, 0x8B4D15, 0xAC5F2D, 0xBA672C, 0xC97E32] :
                    punk ? [0xE36854, 0xF19B5B, 0xFBC186, 0xF7C4B7] : [0x744B31, 0xC49F7C, 0xEFA763, 0xF3B06B, 0xF6AD62];
                foreach (var action in new[] { "typing", "sit-down", "stand-up" }) for (var phase = 0; phase < 4; phase++)
                {
                    var transition = action != "typing";
                    var scene = builder.Build([new("actor", design.Id, "Synthetic", new(seat.Approach.X, seat.Approach.Y), seat.Facing,
                        action, phase / 6d, "desk", transition ? (action == "sit-down" ? "lower" : "raise") : "seated", phase)]);
                    var body = scene.Items.Single(i => i.Layer == IsoLayer.Character); var bytes = Pixels(pack, body);
                    var shift = glasses || braid ? 0 : transition ? (punk || hanbok ? (action == "sit-down" ? (phase == 0 ? 0 : 1) : (phase == 3 ? 0 : 1)) :
                        action == "sit-down" ? phase : 3 - phase) : punk ? 1 : 3;
                    if (!transition && hanbok) shift = 1;
                    var visibleHead = 0; var totalHead = 0; var hands = 0;
                    var clip = pack.Clip(body.SheetId, body.Clip);
                    for (var y = 0; y < 64; y++) for (var x = 0; x < 32; x++)
                    {
                        var index = (y * 32 + x) * 4; if (bytes[index + 3] == 0) continue;
                        var head = y + shift < headEnd && idlePixels[((y + shift) * 32 + x) * 4 + 3] != 0;
                        var visible = Top(pack, scene, body.X + x, body.Y + y)?.InstanceId == body.InstanceId;
                        if (head) { totalHead++; if (visible) visibleHead++; }
                        var rgb = bytes[index] * 65536 + bytes[index + 1] * 256 + bytes[index + 2];
                        if (!transition && !head && skin.Contains(rgb) && clip.HandAnchors!.Any(h => Math.Abs(h.X - x) <= 2 && Math.Abs(h.Y - y) <= 2))
                        { Require(visible, $"{design.Id}/{seat.Facing}/{phase}: typing hand ({x},{y}) covered by {Top(pack,scene,body.X+x,body.Y+y)?.InstanceId}"); hands++; }
                    }
                    Require(totalHead > 0 && visibleHead >= totalHead * (back ? .85 : .90), "Head visibility below contract");
                    if (!transition) Require(hands > 0, "No hand ROI tested");
                    var fraction = (double)visibleHead / totalHead;
                    if (back) minimumBack = Math.Min(minimumBack, fraction); else minimumFront = Math.Min(minimumFront, fraction);
                    cases++;
                }
            }
        }
        Console.WriteLine($"  chair RGBA union: 4 views; visibility: {cases} frames, back {minimumBack:P2}, front {minimumFront:P2}, typing hands 100%");
    }
}
