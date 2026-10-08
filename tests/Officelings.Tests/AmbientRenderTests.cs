using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Officelings.App;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class AmbientRenderTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var image = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes;
    }

    [Fact]
    public void WindowStatesAndRealClockReachTheOffscreenRenderer() => Sta(() =>
    {
        var assets = AssetPack.LoadDefault(useUserPack: false); Assert.Empty(assets.Warnings);
        var layout = LayoutLoader.Load(assets, null); var local = new DateTimeOffset(2026, 10, 4, 3, 0, 0, TimeSpan.FromHours(9));
        var view = new OfficeView(assets, Path.Combine(Path.GetTempPath(), "officelings-ambient-missing-brand"), localClock: () => local)
            { PetsEnabled = false };
        view.SetZoom(1); var hashes = new HashSet<string>();
        foreach (var (hour, weather, expectedRow) in new[] { (12, "off", 0), (22, "off", 2), (12, "rain", 3), (12, "snow", 4) })
        {
            local = new(2026, 10, 4, hour, 0, 0, TimeSpan.FromHours(9)); view.AmbientSettings = new(weather);
            view.Update(layout, [], local);
            var bitmap = Officelings.App.Program.RenderBitmap(view, layout.WidthTiles * 16 + 32, layout.HeightTiles * 16 + 32);
            var windows = view.ResolvedFurnitureSprites.Where(f => layout.Furniture.Single(p => p.Id == f.Id).Kind == "window").ToArray();
            Assert.NotEmpty(windows); Assert.All(windows, f => Assert.Equal(expectedRow, f.Sprite.Row));
            hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Pixels(bitmap))));
        }
        Assert.Equal(4, hashes.Count);
        view.AmbientSettings = new("rain");
        local = new(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(9)); view.Update(layout, [], local);
        var rain = assets.Manifest.Sheets.Single(s => s.Id == "cozy-window").Rows.Single(r => r.Name == "rain");
        Assert.All(view.ResolvedFurnitureSprites.Where(f => layout.Furniture.Single(p => p.Id == f.Id).Kind == "window"),
            f => Assert.Equal(rain.Column, f.Sprite.Column));
        local = local.AddMilliseconds(1000 / rain.Fps + 1); view.Update(layout, [], local);
        Assert.All(view.ResolvedFurnitureSprites.Where(f => layout.Furniture.Single(p => p.Id == f.Id).Kind == "window"),
            f => Assert.Equal(rain.Column + 1, f.Sprite.Column));
        local = new(2026, 10, 4, 3, 0, 0, TimeSpan.FromHours(9)); view.AmbientSettings = new("off"); view.Update(layout, [], local);
        var image = Officelings.App.Program.RenderBitmap(view, layout.WidthTiles * 16 + 32, layout.HeightTiles * 16 + 32);
        var clock = layout.Furniture.Single(f => f.Kind == "clock"); var frame = view.ResolvedFurnitureSprites.Single(f => f.Id == clock.Id).Sprite;
        var x = 16 + clock.X * 16 + (int)((35 - frame.AnchorX) * frame.Scale + frame.OffsetX);
        var y = 16 + clock.Y * 16 + (int)((50 - frame.AnchorY) * frame.Scale + frame.OffsetY);
        var bytes = Pixels(image); var index = (y * image.PixelWidth + x) * 4;
        Assert.Equal(new byte[] { 0x40, 0x3d, 0x26, 255 }, bytes.AsSpan(index, 4).ToArray());
    });

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 12)]
    [InlineData(3, 12)]
    public void ProviderRolePixelsUseIntegerScalingOnTheTagPlate(int zoom, int size) => Sta(() =>
    {
        var assets = AssetPack.LoadDefault(useUserPack: false); var layout = LayoutLoader.Load(assets, null);
        var now = DateTimeOffset.Parse("2026-10-04T12:00:00+09:00");
        var agent = new AgentInfo("tag-original", Provider.Claude, null, false, "Claude", null, null, null, "", null, null, AgentState.Idle, now, now, false);
        var design = assets.Assign(agent, []); var sprite = assets.CharacterFrame(agent, design, "idle", "down", 0);
        var person = new AgentView(agent, new(4, 8, new(4, 8), "idle", "down", sprite.Row, sprite.Column, true, Sprite: sprite, DisplayName: "Claude"));
        var view = new OfficeView(assets, Path.Combine(Path.GetTempPath(), "officelings-ambient-missing-brand")) { TagMode = "always", PetsEnabled = false };
        view.SetZoom(zoom); view.Update(layout, [person], now);
        var bitmap = Officelings.App.Program.RenderBitmap(view, layout.WidthTiles * 16 * zoom + 32, layout.HeightTiles * 16 * zoom + 32);
        var plate = view.AgentTagRect(agent.Id); var bytes = Pixels(bitmap);
        var sheetId = size == 10 ? "provider-icons-10" : "provider-icons";
        var f = assets.Frame(sheetId, "claude-supervisor", "any", 0);
        using var stream = new MemoryStream(assets.Images[sheetId]); var source = new BitmapImage();
        source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = stream; source.EndInit();
        var expected = Pixels(new CroppedBitmap(source, new(f.X, f.Y, size, size)));
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var from = (y * size + x) * 4; if (expected[from + 3] == 0) continue;
            for (var dy = 0; dy < zoom; dy++) for (var dx = 0; dx < zoom; dx++)
            {
                var px = (int)plate.X + zoom + x * zoom + dx;
                var py = (int)plate.Y + (14 - size) / 2 * zoom + y * zoom + dy;
                Assert.Equal(expected.AsSpan(from, 4).ToArray(), bytes.AsSpan((py * bitmap.PixelWidth + px) * 4, 4).ToArray());
            }
        }
    });
}
