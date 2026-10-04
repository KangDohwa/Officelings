using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Vibedotcom.Core;

namespace Vibedotcom.App;

// Local preferences never modify the pack, exported state or tracked assets.
internal sealed class BrandIcons(string? directory = null)
{
    private readonly string directory = directory ?? Path.Combine(TrackerOptions.OutputDirectory, "brand");
    private readonly Dictionary<(Provider, int), BitmapSource?> cache = new();

    public BitmapSource? Get(Provider provider, int zoom)
    {
        zoom = Math.Clamp(zoom, 1, 3);
        if (cache.TryGetValue((provider, zoom), out var cached)) return cached;
        var name = provider.ToString().ToLowerInvariant();
        // Prefer the user's entire set over an optional embedded overlay. A missing
        // density uses the closest available raster before falling back to generic.
        foreach (var embedded in new[] { false, true })
            foreach (var density in Enumerable.Range(1, 3).OrderBy(density => Math.Abs(density - zoom)))
            {
                var file = name + (density == 1 ? "" : $"@{density}x") + ".png";
                try
                {
                    using var input = embedded
                        ? typeof(BrandIcons).Assembly.GetManifestResourceStream("Vibedotcom.App.Brand." + file)
                        : new MemoryStream(FileBoundary.ReadBytes(FileBoundary.UnderRoot(directory, file), 1024 * 1024));
                    if (input == null) continue;
                    if (input.Length > 1024 * 1024) continue;
                    using var buffer = new MemoryStream(); input.CopyTo(buffer);
                    var bytes = buffer.ToArray();
                    if (AssetPack.PngSize(bytes) != (12 * density, 12 * density)) continue;
                    using var png = new MemoryStream(bytes);
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = png; image.EndInit(); image.Freeze();
                    return cache[(provider, zoom)] = image;
                }
                catch (Exception e) when (e is IOException or InvalidDataException or FileFormatException or
                    UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { /* A broken optional icon must not reject an otherwise usable pack. */ }
            }
        return cache[(provider, zoom)] = null;
    }
}
