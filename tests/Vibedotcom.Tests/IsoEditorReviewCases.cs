using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Vibedotcom.App;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.Tests;

// Shared assertions: xUnit and the dependency-free Windows check runner execute
// these exact bodies with synthetic worktree data and unchanged acceptance gates.
internal static class IsoEditorReviewCases
{
    internal static readonly string[] Rules = ["P0", "P0-cache", "P0-equivalence", "R01", "R02", "R03", "R03-count", "R06", "R08", "R09", "R10", "R11", "R13", "R14", "R16", "R17", "R18", "R19", "edwire"];
    private static readonly Lazy<IsoPack> bundled = new(IsoPack.LoadDefault);
    private static readonly LocalIsoCustomObjectImporter importer = new();
    private static string Repository => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string Fixture => Path.Combine(Repository, "tests/fixtures/iso-editor/objects/valid/test-crate/test-crate.iso.json");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Rule(IEnumerable<IsoValidationIssue> issues, string rule) => Require(issues.Any(i => i.RuleId == rule && i.Severity == IsoIssueSeverity.Error), "Missing " + rule);
    private static IsoPack Pack() => importer.Merge(bundled.Value, new([])).Pack!;
    private static IsoLayout Blank(IsoPack pack) => new IsoEditor(pack, pack.Layout).State.Layout with
    { Furniture = [], WorkSeats = [], WallSegments = (pack.Layout.WallSegments ?? []).Where(w => w.HostId == null).ToImmutableArray() };
    private static void Replace(IsoPack pack, Func<IsoFurnitureAsset, IsoFurnitureAsset> edit) =>
        typeof(IsoPack).GetProperty(nameof(IsoPack.Manifest))!.SetValue(pack, pack.Manifest with { Furniture = pack.Manifest.Furniture!.Select(edit).ToImmutableArray() });
    private sealed class Scratch : IDisposable
    {
        internal readonly string Root = Path.Combine(Repository, "artifacts/edfix/c", Guid.NewGuid().ToString("N")[..8]);
        internal string Manifest => Path.Combine(Root, "source/test-crate.iso.json");
        internal Scratch()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Manifest)!);
            File.Copy(Fixture, Manifest); File.Copy(Path.Combine(Path.GetDirectoryName(Fixture)!, "crate.png"), Path.Combine(Root, "source/crate.png"));
        }
        internal void Change(Action<JsonNode> edit)
        {
            var json = JsonNode.Parse(File.ReadAllBytes(Manifest))!; edit(json);
            File.WriteAllBytes(Manifest, JsonSerializer.SerializeToUtf8Bytes(json));
        }
        public void Dispose()
        {
            var allowed = Path.GetFullPath(Path.Combine(Repository, "artifacts/edfix/c")) + Path.DirectorySeparatorChar;
            Require(Path.GetFullPath(Root).StartsWith(allowed, StringComparison.Ordinal), "Scratch escaped worktree");
            Directory.Delete(Root, true);
        }
    }
    private static JsonNode Part(JsonNode n) => n["furniture"]![0]!["variants"]![0]!["parts"]![0]!;
    internal static void Run(string rule)
    {
        using var scratch = new Scratch();
        switch (rule)
        {
            case "P0":
                var m = IsoEditorMeasurements.Run(bundled.Value, scratch.Root, Fixture);
                Console.WriteLine(JsonSerializer.Serialize(m));
                Require(m.Operations == 100 && m.ObjectCount == 34 && m.SeatCount == 12, "Measured fixture changed");
                Require(m.CommandP95Ms <= 100 && m.PreviewP95Ms <= 16, "Latency budget exceeded");
                Require(m.OpenIdleBytesPerFrame == 0 && m.ClosedIdleBytesPerFrame == 0 && m.CachedPreviewBytesPerCall == 0, "Idle allocated");
                break;
            case "P0-cache": CacheIdentity(); break;
            case "P0-equivalence": ExhaustiveEquivalence(); break;
            case "R01":
                foreach (var frame in new[] { -1, 16 })
                {
                    scratch.Change(n => Part(n)["frame"] = frame);
                    Rule(importer.Validate(scratch.Manifest), "O007");
                }
                break;
            case "R02": RenderLimits(scratch); break;
            case "R03":
                var pack = Pack(); var layout = Blank(pack) with { Furniture = Enumerable.Range(0, 50_000).Select(k => new IsoPlacement("f" + k, "iso/desk", 3, 3)).ToImmutableArray() };
                var started = Stopwatch.GetTimestamp(); var issues = new IsoEditor(pack, layout).Validate();
                Require(Stopwatch.GetElapsedTime(started).TotalSeconds < 1, "Oversized input performed quadratic work");
                Require(issues.Length == 1, "Oversized input produced unbounded diagnostics"); Rule(issues, "L017");
                var padded = layout with { Furniture = Enumerable.Range(0, 1024).Select(k => new IsoPlacement(k + new string('x', 4096), "iso/desk", 3, 3)).ToImmutableArray() };
                var bytes = new IsoEditor(pack, padded).Validate(); Require(bytes.Length == 1, "Byte limit ran after collisions"); Rule(bytes, "L017");
                break;
            case "R03-count":
                var oversized = Blank(Pack()) with { Furniture = Enumerable.Range(0, 1025).Select(k => new IsoPlacement("f" + k, "iso/desk", 3, 3)).ToImmutableArray() };
                var limited = new IsoEditor(Pack(), oversized).Validate();
                Require(limited.Length == 1, "Oversized input performed pairwise diagnostics"); Rule(limited, "L017");
                break;
            case "R06":
                var editor = new IsoEditor(Pack(), bundled.Value.Layout); var desk = editor.State.Layout.WorkSeats!.First().DeskId;
                var before = editor.State;
                foreach (var origin in new[] { new Tile(int.MaxValue, 4), new Tile(int.MinValue, 4), new Tile(4, int.MaxValue) })
                {
                    var preview = editor.Preview(new IsoMoveCommand(desk, origin)); Rule(preview.Issues, "L005"); Require(!preview.CanCommit, "Outside preview accepted");
                    var move = editor.Move(desk, origin); Require(!move.Succeeded && !move.Changed && move.State.Revision == before.Revision, "Outside move changed history"); Rule(move.Issues, "L005");
                }
                break;
            case "R08": AuthoredFrames(scratch); break;
            case "R09": FixedHosts(); break;
            case "R10": HiddenPet(); break;
            case "R11": StaticRefresh(scratch); break;
            case "R13": WallDraft(scratch); break;
            case "R14": WallHeight(scratch); break;
            case "R16":
                scratch.Change(n => n["id"] = "/home/alice/private"); var diagnostics = importer.Validate(scratch.Manifest); Rule(diagnostics, "O002");
                Require(diagnostics.All(i => !i.Message.Contains("alice") && i.TargetIds.All(id => !id.Contains("alice"))), "Path leaked into diagnostics");
                var named = new ObjectIssues(); named.Add("O007", "user/test-crate/part/body"); Require(named.All.Single().TargetIds.Contains("user/test-crate/part/body"), "Namespace ID was hidden");
                break;
            case "R17":
                scratch.Change(n => n["displayName"] = "Blue filing cabinet"); var session = IsoEditorAcceptance.Create(bundled.Value, Path.Combine(scratch.Root, "home"), true);
                var plan = session.Importer.PrepareImport(new(scratch.Manifest, session.Paths, session.Catalog, bundled.Value), CancellationToken.None);
                Require(session.CommitPack!(plan, CancellationToken.None).Succeeded, "Import failed");
                Require(session.Search("filing").Single().Title == "Blue filing cabinet", "Display name omitted from palette/search");
                break;
            case "R18": SeatAllocations(); break;
            case "R19": DpiInput(scratch); break;
            case "edwire":
                Require(IsoEditorAcceptance.VerifyCommands(bundled.Value, Path.Combine(scratch.Root, "commands")) > 0, "Composition/ack");
                Require(IsoEditorAcceptance.VerifyObjects(bundled.Value, Path.Combine(scratch.Root, "objects"), Fixture) > 0, "CommitPack/cancellation");
                Require(IsoEditorAcceptance.VerifyRuntime(bundled.Value, Path.Combine(scratch.Root, "runtime")) > 0, "Retreat ack");
                for (var ruleId = 1; ruleId <= 7; ruleId++) IsoEditorUiAcceptance.Run(ruleId, bundled.Value, Path.Combine(scratch.Root, "ui", ruleId.ToString()));
                break;
            default: throw new ArgumentException("Unknown review rule: " + rule);
        }
    }
    private static void RenderLimits(Scratch scratch)
    {
        foreach (var x in new[] { int.MinValue, 1_000_000 })
        {
            scratch.Change(n => n["sheets"]![0]!["anchor"]!["x"] = x); Rule(importer.Validate(scratch.Manifest), "O007");
            var pack = Pack(); Replace(pack, a => a.Id != "iso/plant" ? a : a with { Variants = a.Variants.Select(v => v with { Parts = v.Parts.Select(p => p with { Anchor = new PixelPoint(x, 0) }).ToArray() }).ToArray() });
            var layout = Blank(pack) with { Furniture = [new("unsafe", "iso/plant", 3, 3)] };
            Rule(new IsoEditor(pack, layout).Validate(), "L019");
            var refused = false;
            try { _ = new IsoSceneBuilder(pack, layout, false); } catch (Exception e) when (e is InvalidDataException or OverflowException) { refused = true; }
            Require(refused, "Unsafe scene bounds reached renderer");
        }
        var combined = Pack();
        Replace(combined, a => a.Id != "iso/plant" ? a : a with { Variants = a.Variants.Select(v => v with
            { Parts = v.Parts.Select(p => p with { Anchor = v.Rotation == 0 ? new PixelPoint(3000, 2000) : new PixelPoint(-3000, -2000) }).ToArray() }).ToArray() });
        var combinedLayout = Blank(combined) with { Furniture = [new("left", "iso/plant", 2, 2), new("right", "iso/plant", 5, 5, 180)] };
        Rule(new IsoEditor(combined, combinedLayout).Validate(), "L019");
        var view = new IsoView(new IsoOffice(Pack(), 42, Blank(Pack())) { PetsEnabled = false }, new PackIsoSheetSource(Pack()));
        var dangerous = new IsoScene(1_000_100, 100, [new("a", IsoLayer.Furniture, "iso-plant", "default/se", 0, 0, 0, 0)], [], 123);
        // Direct static-cache guard is covered with real, valid sheet keys.
        var good = new IsoSceneBuilder(Pack(), Blank(Pack()), false).Build([]).Items.First(i => i.Layer == IsoLayer.Floor);
        dangerous = dangerous with { Items = [good, good with { InstanceId = "far", X = 1_000_000 }] };
        var blocked = false;
        try { view.Present(dangerous); } catch (InvalidDataException) { blocked = true; }
        Require(blocked, "Oversized static bitmap was allocated");
    }
    private static void AuthoredFrames(Scratch scratch)
    {
        var original = ObjectPng.Decode(File.ReadAllBytes(Path.Combine(scratch.Root, "source/crate.png")));
        var rgba = new byte[128 * 80 * 4];
        for (var y = 0; y < 80; y++)
        {
            original.Rgba.AsSpan(y * 64 * 4, 64 * 4).CopyTo(rgba.AsSpan(y * 128 * 4));
            original.Rgba.AsSpan(y * 64 * 4, 64 * 4).CopyTo(rgba.AsSpan((y * 128 + 64) * 4));
        }
        var sample = Enumerable.Range(0, 64 * 80).First(k => original.Rgba[k * 4 + 3] == 255);
        rgba[((sample / 64 * 128) + sample % 64) * 4 + 3] = 0;
        for (var c = 0; c < 3; c++) rgba[((sample / 64 * 128) + sample % 64) * 4 + c] = 0;
        File.WriteAllBytes(Path.Combine(scratch.Root, "source/crate.png"), ObjectPng.Encode(128, 80, rgba));
        scratch.Change(n => { n["sheets"]![0]!["rows"]![0]!["frames"] = 2; n["sheets"]![0]!["rows"]![0]!["fps"] = 2; n["sheets"]![0]!["rows"]![0]!["loop"] = true; Part(n)["frame"] = 1; Part(n)["opacity"] = .1; });
        var shadow = rgba.ToArray();
        for (var k = 0; k < shadow.Length; k += 4) if (shadow[k + 3] != 0)
        { shadow[k] = 0x11; shadow[k + 1] = 0x19; shadow[k + 2] = 0x24; shadow[k + 3] = 48; }
        File.WriteAllBytes(Path.Combine(scratch.Root, "source/shadow.png"), ObjectPng.Encode(128, 80, shadow));
        scratch.Change(n =>
        {
            var sheet = n["sheets"]![0]!.DeepClone(); sheet["kind"] = "shadow"; sheet["id"] = "user/test-crate/shadow"; sheet["file"] = "shadow.png";
            var clip = sheet["rows"]![0]!; clip["name"] = "shadow"; clip["partId"] = "test-crate__shadow__se__00";
            n["sheets"]!.AsArray().Add(sheet);
            var part = Part(n).DeepClone(); part["sheetId"] = "user/test-crate/shadow"; part["animation"] = "shadow"; part["partId"] = "test-crate__shadow__se__00";
            n["furniture"]![0]!["variants"]![0]!["shadow"] = part;
        });
        var plan = importer.PrepareImport(new(scratch.Manifest, new(scratch.Root), new([]), bundled.Value), CancellationToken.None);
        Require(plan.Pack != null, "Valid authored frames refused: " + string.Join(",", plan.Issues.Select(i => i.RuleId)));
        var pack = plan.Pack!; var layout = Blank(pack) with { Furniture = [new("crate", "user/test-crate", 3, 3)] };
        var builder = new IsoSceneBuilder(pack, layout, false); var item = builder.Build([]).Items.Single(i => i.HitTargetId == "crate");
        Require(item.Frame == 1 && item.Alpha == 26, "Static frame/opacity lost");
        var staticShadow = builder.Build([]).Items.Single(i => i.Layer == IsoLayer.Shadow); Require(staticShadow.Frame == 1 && staticShadow.Alpha == 26, "Shadow frame/opacity lost");
        Replace(pack, a => a.Id != "user/test-crate" ? a : a with { Variants = a.Variants.Select(v => v with { Parts = v.Parts.Select(p => p with { Frame = null }).ToArray() }).ToArray() });
        builder = new(pack, layout, false); item = builder.Build([], elapsedSeconds: .6).Items.Single(i => i.HitTargetId == "crate");
        Require(item.Frame == 1 && item.Alpha == 26 && double.IsFinite(builder.NextChangeSeconds), "Authored animation froze");
        Replace(pack, a => a.Id != "user/test-crate" ? a : a with { Variants = a.Variants.Select(v => v with { Parts = v.Parts.Select(p => p with { Frame = 1 }).ToArray(), Shadow = v.Shadow! with { Frame = null } }).ToArray() });
        builder = new(pack, layout, false); var animatedShadow = builder.Build([], elapsedSeconds: .6).Items.Single(i => i.Layer == IsoLayer.Shadow);
        Require(animatedShadow.Frame == 1 && animatedShadow.Alpha == 26 && double.IsFinite(builder.NextChangeSeconds), "Shadow animation froze");
    }
    private static void FixedHosts()
    {
        var pack = Pack(); var layout = Blank(pack); var door = layout.WallSegments!.First(w => w.AssetId.Contains("open-door", StringComparison.Ordinal));
        var plain = layout.WallSegments!.First(w => w.Face == door.Face && w.AssetId.Contains("wall-plain", StringComparison.Ordinal));
        var editor = new IsoEditor(pack, layout with { WallSegments = layout.WallSegments!.Select(w => w.Id == plain.Id ? w with { AssetId = door.AssetId, State = door.State } : w).ToArray() });
        Rule(editor.Validate(), "L016");
        editor = new(pack, layout with { WallSegments = layout.WallSegments!.Select(w => w.Id == plain.Id ? w with { State = "open" } : w).ToArray() }); Rule(editor.Validate(), "L016");
    }
    private static void HiddenPet()
    {
        var pack = Pack(); var layout = Blank(pack); var editor = new IsoEditor(pack, layout); var office = new IsoOffice(pack, 42, layout) { PetsEnabled = true };
        var scene = office.Update([], IsoEditorSmoke.Epoch, 0); var pet = scene.Items.First(i => i.Layer == IsoLayer.Pet);
        var anchor = pack.Clip(pet.SheetId, pet.Clip).Anchor ?? pack.Sheet(pet.SheetId).Anchor;
        var ground = IsoGeometry.Inverse(pet.X - office.Camera.X + anchor.X, pet.Y - office.Camera.Y + anchor.Y);
        office.PetsEnabled = false; office.Update([], IsoEditorSmoke.Epoch, 0); var old = office.Layout;
        Require(editor.Place("iso/plant", new(IsoGeometry.Snap(ground.I), IsoGeometry.Snap(ground.J))).Succeeded, "Pet blocker rejected");
        Require(office.ApplyEditorState(editor.State).IsEmpty && ReferenceEquals(old, office.Layout), "Pet did not retreat");
        for (var k = 0; k < 100 && ReferenceEquals(old, office.Layout); k++)
        {
            office.Update([], IsoEditorSmoke.Epoch.AddSeconds(k * .05), .05);
            if (ReferenceEquals(old, office.Layout)) Require(double.IsFinite(office.NextChangeSeconds), "Hidden retreat stalled");
        }
        Require(!ReferenceEquals(old, office.Layout), "Hidden pet never completed swap");
    }
    private static void StaticRefresh(Scratch scratch)
    {
        var session = IsoEditorAcceptance.Create(bundled.Value, scratch.Root, true); var view = IsoEditorSmoke.View(session);
        view.SetAnimationEnabled(true); Require(!view.AnimationTimerEnabled, "Static room unexpectedly animated");
        var before = view.SceneRenderCount; var result = session.Execute(new IsoPlaceCommand("iso/plant", new(4, 4)));
        Require(result.Succeeded && view.Scene.Items.Any(i => i.InstanceId.StartsWith(result.PrimaryTargetId + "/", StringComparison.Ordinal)), "Accepted edit left stale scene");
        Require(view.SceneRenderCount == before + 1 && !view.AnimationTimerEnabled, "Static edit did not advance exactly once");
        before = view.SceneRenderCount; session.Select(null); Require(view.SceneRenderCount == before, "Selection advanced runtime"); view.DetachEditor();
    }
    private static IEnumerable<ImageDrawing> Images(Drawing drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var item in Images(child)) yield return item;
    }
    private static void WallDraft(Scratch scratch)
    {
        var session = IsoEditorAcceptance.Create(bundled.Value, scratch.Root, true); var view = IsoEditorSmoke.View(session);
        var blocked = session.Execute(new IsoPlaceCommand("iso/plant", session.Editor.State.Layout.Door));
        Require(blocked.Succeeded && session.DraftInvalid, "Invalid draft fixture failed");
        var definition = session.Editor.WallPalette.First(d => d.ModuleIds.Length == 1);
        var host = session.Editor.State.Layout.WallSegments!.First(w => w.HostId == null && w.Face == definition.Face && w.AssetId.Contains("wall-plain", StringComparison.Ordinal));
        var placed = session.Execute(new IsoPlaceWallCommand(definition.AssetId, host.Id)); Require(placed.Succeeded, "Wall draft placement rejected");
        var wall = session.Editor.State.Layout.WallSegments!.Single(w => w.AssemblyId == placed.PrimaryTargetId);
        var module = session.Pack.Manifest.WallModules!.Single(m => m.Id == wall.AssetId); var p = module.Parts[0]; var clip = session.Pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction);
        var anchor = p.Anchor ?? clip.Anchor ?? clip.EdgeAnchor ?? module.EdgeAnchor; var source = session.Pack.Sheet(p.SheetId);
        var point = IsoEditorOverlay.Project(wall.Edge.I, wall.Edge.J, 0, view.EditorCamera);
        var rect = new Rect(point.X - anchor.X, point.Y - anchor.Y, source.CellW, source.CellH);
        Require(Images(view.EditorOverlay!.Visual.Drawing).Any(image => image.Rect == rect), "Committed invalid wall ghost disappeared");
        view.DetachEditor();
    }
    private static void WallHeight(Scratch scratch)
    {
        var session = IsoEditorAcceptance.Create(bundled.Value, scratch.Root, true); var view = IsoEditorSmoke.View(session);
        foreach (var height in new[] { 0, 16, 40, 48 })
        {
            var native = IsoEditorOverlay.Project(-.5, 5, height, view.EditorCamera); var pick = typeof(IsoView).GetMethod("PickHost", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<Point, string?>>(view); var id = pick(view.ScreenPoint(native));
            var wall = session.Editor.State.Layout.WallSegments!.Single(w => w.Id == id);
            Require(wall.Face == "left" && wall.Edge == new IsoGridPoint(-.5, 4.5), "Elevated pointer selected wrong wall edge");
        }
        view.DetachEditor();
    }
    private static void SeatAllocations()
    {
        var pack = Pack(); var editor = new IsoEditor(pack, pack.Layout); var builder = new IsoSceneBuilder(pack, editor.State.Layout, false);
        var id = editor.State.Layout.WorkSeats!.First().DeskId;
        var enabled = typeof(IsoSceneBuilder).GetMethod("EnabledSeat", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<string, bool>>(builder);
        for (var k = 0; k < 1000; k++) Require(enabled(id), "Enabled seat not found");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 10_000; k++) Require(enabled(id), "Enabled seat not found");
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "Closed-editor seat lookup allocated");
        var disabled = new IsoSceneBuilder(pack, editor.State.Layout with { WorkSeats = [] }, false); Require(!typeof(IsoSceneBuilder).GetMethod("EnabledSeat", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<string, bool>>(disabled)(id), "Explicit disabled seats ignored");
    }
    private static void DpiInput(Scratch scratch)
    {
        var session = IsoEditorAcceptance.Create(bundled.Value, scratch.Root, true); var view = IsoEditorSmoke.View(session);
        var id = session.Execute(new IsoPlaceCommand("iso/plant", new(4, 4))).PrimaryTargetId!; session.Select(null);
        var scene = session.Editor.GetPickingScene(view.EditorCamera); var item = scene.Items.First(i => i.InstanceId.StartsWith(id + "/", StringComparison.Ordinal)); var sheet = session.Pack.Sheet(item.SheetId);
        var pixel = Enumerable.Range(0, sheet.CellW * sheet.CellH).First(k => session.Pack.Opaque(item, k % sheet.CellW, k / sheet.CellW));
        var native = new Point(item.X + pixel % sheet.CellW + .5, item.Y + pixel / sheet.CellW + .5);
        var tilePoint = IsoEditorOverlay.Project(2, 3, 0, view.EditorCamera);
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2 }) foreach (var zoom in new[] { 1, 2, 3, 6 }) foreach (var pan in new[] { new Vector(), new Vector(17.25, -9.6) })
        {
            var width = view.Scene.Width; var height = view.Scene.Height;
            var camera = IsoView.PhysicalCamera(width, height, 900, 700, dpi, dpi, zoom, pan);
            typeof(IsoView).GetField("camera", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, camera);
            // Independent physical-pixel oracle, not camera.Transform/inverse round-trip.
            Point Dip(Point p) => new((p.X * zoom + Math.Floor((900 * dpi - width * zoom) / 2 + pan.X + .5)) / dpi,
                (p.Y * zoom + Math.Floor((700 * dpi - height * zoom) / 2 + pan.Y + .5)) / dpi);
            var actual = view.ScenePoint(Dip(tilePoint));
            Require(IsoHitTest.PickTile(session.Editor.State.Layout, view.EditorCamera, actual.X, actual.Y) == new Tile(2, 3), "DPI input selected wrong tile");
            Require(view.EditorHit(Dip(native))?.TargetId == id, "DPI input selected wrong object");
        }
        view.DetachEditor();
    }
    private static void CacheIdentity()
    {
        var pack = IsoPack.LoadDefault(); var builder = new IsoSceneBuilder(pack, pack.Layout); builder.Build([]);
        var desk = pack.Layout.Furniture.First(f => f.AssetId == "iso/desk"); var c = IsoContacts.Desk(desk.X, desk.Y, desk.Rotation);
        builder.Build([new("synthetic", pack.Characters[0].Id, "", new(c.Approach.X, c.Approach.Y), c.Facing, "typing", 0, desk.Id, "seated")]);
        var merged = importer.Merge(pack, new([])).Pack!;
        foreach (var name in new[] { "masks", "images", "clips", "changedFrames", "opaquePixels" })
        {
            var field = typeof(IsoPack).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var source = (IDictionary)field.GetValue(pack)!; var copied = (IDictionary)field.GetValue(merged)!;
            Require(source.Count > 0, "Cache not warmed: " + name);
            foreach (DictionaryEntry pair in source) Require(ReferenceEquals(pair.Value, copied[pair.Key]), "Merged pack lost cache: " + name);
        }
    }
    private static void ExhaustiveEquivalence()
    {
        var pack = Pack(); var editor = new IsoEditor(pack, pack.Layout); var layouts = new List<IsoLayout> { editor.State.Layout };
        foreach (var seat in editor.State.Layout.WorkSeats!.ToArray())
        { editor.ClearSeat(seat.DeskId); layouts.Add(editor.State.Layout); editor.Undo(); }
        var desk = editor.State.Layout.WorkSeats!.First().DeskId;
        for (var y = 0; y < 10; y++) for (var x = 0; x < 10; x++)
        {
            var candidate = editor.Preview(new IsoMoveCommand(desk, new(x, y)));
            if (candidate.CanCommit) layouts.Add(candidate.Candidate);
        }
        foreach (var layout in layouts)
        {
            Require(new IsoEditor(pack, layout).Validate().All(i => i.RuleId != "L019"), "Incremental scene check differs");
            var builder = new IsoSceneBuilder(pack, layout, false); var desks = layout.WorkSeats!.Select(s => layout.Furniture.First(f => f.Id == s.DeskId)).ToArray();
            foreach (var design in pack.Characters) foreach (var phase in new[] { "turn", "lower", "seated", "raise" })
            for (var frame = 0; frame < (phase is "lower" or "raise" ? 4 : 1); frame++)
            {
                var poses = desks.Select(f => { var c = IsoContacts.Desk(f.X, f.Y, f.Rotation); return new IsoActorPose(IsoEditorSceneValidation.ProbePrefix(layout) + f.Id, design.Id, "", new(c.Approach.X, c.Approach.Y), c.Facing, phase == "lower" ? "sit-down" : phase == "raise" ? "stand-up" : "typing", 0, f.Id, phase, frame); }).ToArray();
                Require(builder.Build(poses).Items.Count <= 4096, "Exhaustive count exceeds gate");
            }
        }
        Console.WriteLine($"Exhaustive scene parity: {layouts.Count} layouts x 80 occupied scenes");
    }
}