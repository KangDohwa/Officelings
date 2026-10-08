using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Officelings.Core.Iso;

namespace Officelings.DepthChecks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/depthsort");
        Directory.CreateDirectory(output);
        var packIndex = Array.IndexOf(args, "--pack");
        var pack = packIndex < 0 ? IsoPack.LoadDefault() : IsoPack.Load(args[packIndex + 1]);
        DepthOracle.LegacyOrder = args.Contains("--legacy-order");
        var layouts = new[] { ("default", pack.Layout), ("source-default", IsoLayoutLoader.Default(pack)) };
        var results = new List<object>();
        foreach (var (name, layout) in layouts)
        {
            var scene = DepthOracle.Order(new IsoSceneBuilder(pack, layout).Build([]));
            var audit = DepthOracle.Audit(pack, layout, scene);
            var bitmap = Render(pack, scene);
            Save(bitmap, Path.Combine(output, name + ".png"));
            var crops = audit.Violations;
            var compareIndex = Array.IndexOf(args, "--compare-audit");
            if (compareIndex >= 0)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(args[compareIndex + 1]));
                var prior = document.RootElement.EnumerateArray().First(e => e.GetProperty("name").GetString() == name);
                crops = JsonSerializer.Deserialize<List<DepthOracle.Violation>>(prior.GetProperty("Violations").GetRawText())!;
            }
            foreach (var violation in crops)
            {
                var crop = new CroppedBitmap(bitmap, new Int32Rect(violation.Left, violation.Top,
                    violation.Right - violation.Left, violation.Bottom - violation.Top));
                var scaled = BitmapSource.Create(crop.PixelWidth, crop.PixelHeight, 96, 96, crop.Format, crop.Palette,
                    Pixels(crop), crop.PixelWidth * 4);
                // Integer nearest-neighbor expansion, independent of WPF device DPI.
                var source = Pixels(scaled); var pixels = new byte[crop.PixelWidth * crop.PixelHeight * 4 * 9];
                for (var y = 0; y < crop.PixelHeight * 3; y++) for (var x = 0; x < crop.PixelWidth * 3; x++)
                    source.AsSpan((y / 3 * crop.PixelWidth + x / 3) * 4, 4).CopyTo(pixels.AsSpan((y * crop.PixelWidth * 3 + x) * 4, 4));
                Save(BitmapSource.Create(crop.PixelWidth * 3, crop.PixelHeight * 3, 96, 96, PixelFormats.Pbgra32,
                    null, pixels, crop.PixelWidth * 12), Path.Combine(output, name + "-" + violation.FileId + "-3x.png"));
            }
            results.Add(new { name, audit.OverlappingPairs, mismatches = audit.Violations.Count, audit.Violations });
            Console.WriteLine($"{name}: overlap pairs={audit.OverlappingPairs}; violations={audit.Violations.Count}");
        }
        File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(results,
            new JsonSerializerOptions { WriteIndented = true }));
        if (args.Contains("--audit-only")) return 0;
        return DepthOracle.CheckAll(pack, output);
    }
    private static byte[] Pixels(BitmapSource bitmap)
    { var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels; }
    private static void Save(BitmapSource bitmap, string file)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(file); encoder.Save(stream); }
    private static BitmapSource Render(IsoPack pack, IsoScene scene)
    {
        var sheets = pack.Images.ToDictionary(p => p.Key, p =>
        {
            using var stream = new MemoryStream(p.Value);
            var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            bitmap.Freeze(); return bitmap;
        });
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 41, 43)), null, new Rect(0, 0, scene.Width, scene.Height));
            foreach (var item in scene.Items)
            {
                var sheet = pack.Sheet(item.SheetId); var clip = pack.Clip(item.SheetId, item.Clip);
                var crop = new CroppedBitmap(sheets[item.SheetId], new Int32Rect((clip.Column + item.Frame) * sheet.CellW,
                    clip.Row * sheet.CellH, sheet.CellW, sheet.CellH));
                dc.PushOpacity(item.Alpha / 255d); dc.DrawImage(crop, new Rect(item.X, item.Y, sheet.CellW, sheet.CellH)); dc.Pop();
            }
        }
        var target = new RenderTargetBitmap(scene.Width, scene.Height, 96, 96, PixelFormats.Pbgra32); target.Render(visual); target.Freeze(); return target;
    }
}
