using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vibedotcom.App;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class RenderingTests
{
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "vibedotcom-fix21");
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static AgentInfo Agent(Provider provider = Provider.Codex) => new("render:" + provider, provider, null, false,
        provider.ToString(), null, null, null, "", null, null, AgentState.Working, Epoch, Epoch, false);
    private static BitmapSource Render(OfficeView view, int width, int height, string? name = null)
    {
        var bitmap = Vibedotcom.App.Program.RenderBitmap(view, width, height);
        if (name != null) Save(bitmap, name);
        return bitmap;
    }
    private static void Save(BitmapSource bitmap, string name)
    {
        Directory.CreateDirectory(Output);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Output, name)); encoder.Save(file);
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bgra.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }
    private static byte[] Pixel(BitmapSource bitmap, int x, int y)
    {
        var bytes = new byte[4]; new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0)
            .CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0); return bytes;
    }
    private static OfficeLayout Small(AssetPack pack, IReadOnlyList<Furniture>? furniture = null, IReadOnlyList<OverflowSeat>? overflow = null)
    {
        var floor = pack.Manifest.TileTypes.First(t => t.Kind == "floor").Id;
        var wall = pack.Manifest.TileTypes.First(t => t.Kind == "wall").Id;
        return new(16, 12, 16, new(1, 10), furniture ?? [], Tiles: Enumerable.Range(0, 192)
            .Select(i => i / 16 < 3 ? wall : floor).ToArray(), OverflowSeats: overflow ?? []);
    }
    private static OfficeView View(AssetPack pack, OfficeLayout layout, IReadOnlyList<AgentView> agents, string? brand = null)
    {
        var view = new OfficeView(pack, brand ?? Path.Combine(Output, "missing-brand")) { TransparentBackground = true };
        view.SetZoom(1); view.Update(layout, agents, Epoch); return view;
    }

    [Fact]
    public void TransparentMarginsPreserveOpaqueTilesAndSpritesAndUniformOpacity() => Sta(() =>
    {
        var pack = AssetPack.LoadDefault(useUserPack: false); var office = new OfficeAnimator(42, pack);
        var agents = Enumerable.Range(0, 20).Select(i => Agent((Provider)(i % 3)) with { Id = $"render:{i:00}" }).ToArray();
        IReadOnlyList<AgentView> poses = [];
        for (var i = 0; i < 1200; i++) poses = office.Update(agents, Epoch.AddMilliseconds(i * 100), .1);
        var layout = office.Layout; var view = View(pack, layout, poses, Path.Combine(Output, "missing-brand"));
        var width = layout.WidthTiles * 16 + 32; var height = layout.HeightTiles * 16 + 32;
        var transparent = Render(view, width, height, "transparent.png");
        Assert.Equal(0, Pixel(transparent, 0, 0)[3]);
        var inside = Pixels(transparent);
        // Sample tile surfaces without decorative overlays (text/relationship
        // antialiasing has its own alpha). Their expected alpha comes from the
        // transparency contract, independently of the scene's sort order.
        var tiles = Render(View(pack, layout with { Furniture = [] }, []), width, height);
        foreach (var tile in Enumerable.Range(0, layout.WidthTiles * layout.HeightTiles))
        {
            var type = pack.Manifest.TileTypes.Single(t => t.Id == layout.Tiles![tile]);
            if (type.Kind == "void") continue;
            Assert.Equal(255, Pixel(tiles, 16 + tile % layout.WidthTiles * 16 + 8,
                16 + tile / layout.WidthTiles * 16 + 8)[3]);
        }
        var person = poses.First(a => a.Pose.Visible && a.Pose.SeatPhase == "seated");
        var point = view.AgentPoint(person);
        Assert.Equal(255, Pixel(transparent, (int)point.X, (int)point.Y)[3]);
        Assert.NotNull(view.HitAgent(point)); Assert.Null(view.HitAgent(new(0, 0)));
        view.TransparentBackground = false;
        var opaque = Render(view, width, height);
        Assert.Equal(255, Pixel(opaque, 0, 0)[3]);
        // The toggle affects only the backdrop. Inside colours must be identical.
        var normal = Pixels(opaque);
        for (var y = 16; y < height - 16; y++) for (var x = 16; x < width - 16; x++)
        {
            var i = (y * width + x) * 4;
            if (inside[i + 3] == 255) Assert.Equal(normal.AsSpan(i, 4).ToArray(), inside.AsSpan(i, 4).ToArray());
        }
        view.TransparentBackground = true; view.Opacity = .5;
        var faded = Render(view, width, height, "transparent-opacity50.png");
        Assert.Equal(0, Pixel(faded, 0, 0)[3]);
        var half = Pixels(faded);
        for (var i = 3; i < half.Length; i += 4)
            if (inside[i] == 255) Assert.InRange(half[i], (byte)127, (byte)128);
    });

    [Fact]
    public void BrandLoaderPrefersLocalDensityAndFallsBackForMissingCorruptOrWrongSizeIcons() => Sta(() =>
    {
        var folder = Path.Combine(Output, "loader-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            void Icon(string file, int size, Color color)
            {
                var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null,
                    Enumerable.Range(0, size * size).SelectMany(_ => new byte[] { color.B, color.G, color.R, 255 }).ToArray(), size * 4);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, file)); encoder.Save(stream);
            }
            Icon("codex.png", 12, Colors.Lime); Icon("codex@2x.png", 24, Colors.Blue);
            var loader = new BrandIcons(folder);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(loader.Get(Provider.Codex, 1)!, 5, 5));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(loader.Get(Provider.Codex, 2)!, 5, 5));
            Assert.Equal(24, loader.Get(Provider.Codex, 3)!.PixelWidth);
            Assert.Null(loader.Get(Provider.Claude, 1));
            File.WriteAllText(Path.Combine(folder, "codex@2x.png"), "corrupt PNG");
            Assert.Equal(12, new BrandIcons(folder).Get(Provider.Codex, 2)!.PixelWidth);
            Icon("claude.png", 13, Colors.Red); Assert.Null(new BrandIcons(folder).Get(Provider.Claude, 1));
            File.WriteAllText(Path.Combine(folder, "codex.png"), "broken");
            Assert.Null(new BrandIcons(folder).Get(Provider.Codex, 1));
            Assert.DoesNotContain(typeof(OfficeView).Assembly.GetManifestResourceNames(), n => n.Contains(".Brand."));
            // A corrupt override still draws the packaged generic icon on the actual tag.
            Tags("generic", folder);
        }
        finally { Directory.Delete(folder, true); }
    });

    private static void Tags(string prefix, string directory)
    {
        var pack = AssetPack.LoadDefault(useUserPack: false); var layout = Small(pack);
        var people = Enum.GetValues<Provider>().Select((provider, i) =>
        {
            var a = Agent(provider); var design = pack.Manifest.Characters[0];
            var sprite = pack.Frame(design.SheetId, "idle", "down", 0);
            return new AgentView(a, new(3 + i * 5, 6, new(3 + i * 5, 6), "idle", "down", sprite.Row, sprite.Column, true,
                Sprite: sprite, DisplayName: provider.ToString()));
        }).ToArray();
        var view = View(pack, layout, people, directory);
        view.TagMode = "always";
        for (var zoom = 1; zoom <= 3; zoom++)
        {
            view.SetZoom(zoom);
            var image = Render(view, layout.WidthTiles * 16 * zoom + 32, layout.HeightTiles * 16 * zoom + 32);
            Save(new CroppedBitmap(image, new(16, 16 + 64 * zoom, 256 * zoom, 60 * zoom)), $"tags-{prefix}-{zoom}x.png");
        }
    }

    [Fact]
    public void DefaultBuildIsGenericAndExplicitOverlayBuildContainsOnlyNamedPngs()
    {
        Assert.DoesNotContain(typeof(OfficeView).Assembly.GetManifestResourceNames(), n => n.Contains(".Brand."));
        var file = Environment.GetEnvironmentVariable("VIBEDOTCOM_EMBEDDED_QA");
        if (file == null) return;
        var context = new AssemblyLoadContext("brand-overlay-verification", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(file);
            var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Vibedotcom.App.Brand.")).ToArray();
            Assert.Equal(9, names.Length);
            foreach (var provider in new[] { "codex", "claude", "antigravity" }) foreach (var density in new[] { 1, 2, 3 })
            {
                var name = $"Vibedotcom.App.Brand.{provider}" + (density == 1 ? "" : $"@{density}x") + ".png";
                using var input = assembly.GetManifestResourceStream(name)!;
                using var buffer = new MemoryStream(); input.CopyTo(buffer);
                Assert.Equal((12 * density, 12 * density), AssetPack.PngSize(buffer.ToArray()));
            }
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void OfficialLocalOverlayCanBeRenderedWithoutChangingPack()
    {
        // Optional QA only: ordinary test runs require no network or logos.
        var directory = Environment.GetEnvironmentVariable("VIBEDOTCOM_BRAND_QA");
        if (directory == null) return;
        Sta(() =>
        {
            var icons = new BrandIcons(directory);
            foreach (var provider in Enum.GetValues<Provider>()) foreach (var zoom in new[] { 1, 2, 3 })
                Assert.Equal(12 * zoom, icons.Get(provider, zoom)!.PixelWidth);
            Tags("official", directory);
        });
    }

    [Fact]
    public void ChairFrontAndDeskEdgeOccludeRealSeatedPosesAcrossFacingsColoursAndOverflowSeats() => Sta(() =>
    {
        var pack = AssetPack.LoadDefault(useUserPack: false); var points = new List<object>();
        var design = pack.Manifest.Characters.First(c => new[] { "up", "left", "right" }.All(d => pack.SeatedVariant(c, d) != null));
        var states = pack.Manifest.Furniture.Single(f => f.Id == "cozy-chair-front").Variants.Where(v => v.Rotation == 0).Select(v => v.State);
        foreach (var state in states) Check("cozy-desk", 0, state);
        foreach (var id in pack.Manifest.Furniture.Where(f => f.Id.StartsWith("cozy-desk-side-") || f.Id == "cozy-laptop-table-side").Select(f => f.Id))
            foreach (var rotation in new[] { 90, 270 }) Check(id, rotation, "default");
        foreach (var id in new[] { "cozy-sofa", "cozy-armchair", "cozy-counter-stool" }) Check(id, 0, "default");
        File.WriteAllText(Path.Combine(Output, "chair-pixels.json"), JsonSerializer.Serialize(points, StateJson.Options));

        void Check(string id, int rotation, string chairState)
        {
            var host = new Furniture("desk", 6, 5, 3, 2, Id: "host", AssetId: id, Rotation: rotation, DeskId: "host");
            var v = pack.Variant(host); host = host with { Width = v.FootprintW, Height = v.FootprintH };
            var facing = v.Points.FirstOrDefault(p => p.Action == "typing")?.Facing ?? (rotation == 90 ? "right" : rotation == 270 ? "left" : "up");
            var authored = v.Points.FirstOrDefault(p => p.Action == "typing");
            var source = LayoutLoader.Load(pack, null);
            var originalHost = source.Furniture.FirstOrDefault(f => f.AssetId == id && f.Rotation == rotation);
            var extra = source.OverflowSeats?.FirstOrDefault(e => e.FurnitureId == originalHost?.Id);
            var work = authored ?? (extra != null ? new InteractionPoint("seat", extra.X - originalHost!.X, extra.Y - originalHost.Y, "typing", extra.Facing) :
                new("seat", 1, v.FootprintH, "typing", facing));
            var contact = v.ChairSeatAnchor ?? new PixelPoint((work.X + .5) * 16, (work.Y + 1) * 16 - 8);
            var seat = new PixelPoint(host.X * 16 + contact.X, host.Y * 16 + contact.Y);
            var a = Agent(); var variant = pack.SeatedVariant(design, facing)!;
            var (s, pelvis) = pack.SeatedFrame(a with { State = AgentState.Waiting }, design, variant, facing, 0, false);
            var tile = new Tile(host.X + work.X, host.Y + work.Y);
            s = s with { OffsetX = seat.X - (tile.X + .5) * 16 + (s.AnchorX - pelvis.X) * s.Scale,
                OffsetY = seat.Y - (tile.Y + 1) * 16 + (s.AnchorY - pelvis.Y) * s.Scale };
            var overflow = v.Points.All(p => p.Action != "typing");
            // Isolate the real seated clip from laptop and bubble overlays;
            // those deliberately draw above the scene and are not chair pixels.
            var person = new AgentView(a, new(tile.X, tile.Y, tile, "idle", facing, s.Row, s.Column, true, Sprite: s,
                DeskId: overflow ? null : "host", OverflowSeatId: overflow ? "extra" : null, WorkLocation: overflow ? "overflow" : "desk",
                SeatedVariant: variant, SeatAnchor: seat, ChairBackId: v.ChairBackId, ChairFrontId: v.ChairFrontId, SeatPhase: "seated"));
            var furniture = new List<Furniture> { host };
            foreach (var (asset, suffix) in new[] { (v.ChairBackId, "-chair-back"), (v.ChairFrontId, "-chair-front") })
                if (asset != null) furniture.Add(new("chair", host.X, host.Y, host.Width, host.Height,
                    Id: "host" + suffix, AssetId: asset, Rotation: rotation, State: chairState, DeskId: "host"));
            var extras = overflow ? new[] { new OverflowSeat("extra", tile.X, tile.Y, facing, "host") } : [];
            var layout = Small(pack, furniture, extras); const int width = 288, height = 224;
            var empty = Pixels(Render(View(pack, layout with { Furniture = [] }, []), width, height));
            var body = Pixels(Render(View(pack, layout with { Furniture = [] }, [person]), width, height));
            var scene = Render(View(pack, layout, [person]), width, height);
            var final = Pixels(scene);
            // Independently render the foreground asset alone to identify an actual
            // opaque chair pixel intersecting the authored character silhouette.
            var voidId = pack.Manifest.TileTypes.First(t => t.Kind == "void").Id;
            var foregrounds = new List<Furniture> { v.ChairFrontId == null ? host : furniture.Single(f => f.AssetId == v.ChairFrontId) };
            if (v.ChairFrontId != null && authored != null && facing is "left" or "right") foregrounds.Add(host);
            foreach (var foreground in foregrounds)
            {
                var front = Pixels(Render(View(pack, layout with { Furniture = [foreground] }, []), width, height));
                var mask = Pixels(Render(View(pack, layout with { Furniture = [foreground], Tiles = Enumerable.Repeat(voidId, 192).ToArray() }, []), width, height));
                var overlap = Enumerable.Range(0, width * height).Where(pixel =>
                {
                    var index = pixel * 4; var worldY = pixel / width - 16;
                    return worldY <= seat.Y && worldY >= seat.Y - 18 && mask[index + 3] == 255 &&
                        !body.AsSpan(index, 3).SequenceEqual(empty.AsSpan(index, 3)) &&
                        !front.AsSpan(index, 3).SequenceEqual(empty.AsSpan(index, 3));
                }).ToArray();
                Assert.True(overlap.Length > 0, $"No foreground/body overlap exercised: {id}/{rotation}/{chairState}/{foreground.Id}");
                foreach (var pixel in overlap)
                    Assert.True(final.AsSpan(pixel * 4, 4).SequenceEqual(front.AsSpan(pixel * 4, 4)), $"Foreground overwritten at {pixel % width},{pixel / width}: {id}/{rotation}/{chairState}/{foreground.Id}");
                points.Add(new { id, rotation, chairState, foreground = foreground.Id, x = overlap[0] % width, y = overlap[0] / width, pixels = overlap.Length });
            }
            Save(scene, $"chair-{id}-{rotation}-{chairState}.png");
        }
    });
}
