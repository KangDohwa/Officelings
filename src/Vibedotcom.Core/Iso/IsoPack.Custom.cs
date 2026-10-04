using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.Core.Iso;

public sealed partial class IsoPack
{
    internal static readonly JsonSerializerOptions CustomJson = new(StateJson.Options)
        { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    // Clone resolved playback/mask caches, including compiled chair-frame alias overrides.
    // Build is intentionally not involved: it would append shared UI and regenerate rigs.
    internal IsoPack WithCustomObjects(IReadOnlyList<ObjectSource> objects)
    {
        var result = new IsoPack { Layout = Layout };
        foreach (var pair in sheets) result.sheets.Add(pair.Key, pair.Value);
        foreach (var pair in clips) result.clips.Add(pair.Key, pair.Value);
        foreach (var pair in masks) result.masks.Add(pair.Key, pair.Value);
        foreach (var pair in images) result.images.Add(pair.Key, pair.Value);
        var furniture = (Manifest.Furniture ?? []).ToList();
        if (!furniture.Any(f => f.Id == "iso/computer"))
        {
            var desk = furniture.Single(f => f.Id == "iso/desk");
            furniture.Add(new("iso/computer", desk.Variants.Select(v => new IsoFurnitureVariant(v.Rotation, v.Orientation, v.State,
                v.FootprintW, v.FootprintH, v.Parts.Where(p => p.Animation?.StartsWith("monitor-", StringComparison.Ordinal) == true ||
                    p.Animation is "rear-15" or "rear-16").ToImmutableArray(), Points: [], BlockedCells: [], Layer: 1,
                Screen: v.Screen, Glow: v.Glow, HeightPx: 40)).ToImmutableArray(), "computer"));
        }
        foreach (var source in objects)
        {
            foreach (var raw in source.Manifest.Sheets)
            {
                var png = source.Pixels[raw.Id]; var bytes = source.Images[raw.Id];
                var sheet = raw with { File = ObjectSource.SheetFile(bytes), Width = png.Width, Height = png.Height,
                    Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() };
                result.sheets.Add(sheet.Id, sheet); result.images.Add(sheet.Id, bytes);
                result.masks.Add(sheet.Id, new IsoPngMask(png.Width, png.Height, png.Rgba));
                foreach (var clip in sheet.Rows!) result.clips.Add((sheet.Id, clip.Key), clip);
            }
            foreach (var asset in source.Manifest.Furniture)
                furniture.Add(new(asset.Id, asset.Variants.Select(v => new IsoFurnitureVariant(v.Rotation, v.Orientation, v.State,
                    v.FootprintW, v.FootprintH, v.Parts, v.Points.IsDefault ? [] : v.Points, v.BlockedCells, Layer: v.Layer,
                    Shadow: v.Shadow, HeightPx: v.Height)).ToImmutableArray(), asset.Kind));
        }
        result.Manifest = Manifest with { Sheets = result.sheets.Values.ToImmutableArray(), Furniture = furniture.ToImmutableArray() };
        return result;
    }
    internal static bool CustomLimits(IsoPack builtIn, IReadOnlyList<ObjectSource> objects)
    {
        if (objects.Count > 64 || objects.Sum(o => o.InstalledPngBytes) > 64 * 1024 * 1024) return false;
        var sheets = builtIn.Manifest.Sheets.Concat(objects.SelectMany(o => o.Manifest.Sheets)).ToArray();
        var families = (builtIn.Manifest.Furniture?.Count ?? 0) + objects.Count + (builtIn.Manifest.Furniture?.Any(f => f.Id == "iso/computer") == true ? 0 : 1);
        if (sheets.Length > 128 || families > 512 || sheets.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != sheets.Length ||
            (builtIn.Manifest.Furniture ?? []).Select(f => f.Id).Concat(objects.Select(o => o.Manifest.Id)).Distinct(StringComparer.Ordinal).Count() != families - (builtIn.Manifest.Furniture?.Any(f => f.Id == "iso/computer") == true ? 0 : 1)) return false;
        var decoded = builtIn.Manifest.Sheets.Sum(s => (long)s.Width * s.Height * 4) + objects.SelectMany(o => o.Pixels.Values).Sum(p => (long)p.Rgba.Length);
        return decoded <= 128 * 1024 * 1024;
    }
    internal bool CustomJsonFits() => JsonSerializer.SerializeToUtf8Bytes(Manifest, CustomJson).Length <= ObjectFiles.JsonLimit &&
        JsonSerializer.SerializeToUtf8Bytes(Layout, CustomJson).Length <= ObjectFiles.JsonLimit;
}
