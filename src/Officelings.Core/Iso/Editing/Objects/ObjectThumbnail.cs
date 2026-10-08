namespace Officelings.Core.Iso.Editing;

internal static class ObjectThumbnail
{
    internal static readonly long EncodedBytes = ObjectPng.Encode(128, 128, new byte[128 * 128 * 4]).LongLength;
    internal static byte[] Create(ObjectSource source)
    {
        var v = source.Manifest.Furniture[0].Variants.OrderBy(v => v.State == "default" ? 0 : 1)
            .ThenBy(v => v.State, StringComparer.Ordinal).ThenBy(v => v.Rotation).First();
        var ordered = (v.Shadow == null ? Enumerable.Empty<IsoPart>() : new[] { v.Shadow }).Concat(v.Parts.OrderBy(p => p.IsoZOrder));
        var layers = ordered.Select(p =>
        {
            var sheet = source.Manifest.Sheets.Single(s => s.Id == p.SheetId);
            var clip = sheet.Rows!.First(c => c.Name == p.Animation && (c.Direction == p.Direction || c.Direction == "any"));
            return (Part: p, Sheet: sheet, Clip: clip, Anchor: p.Anchor ?? clip.Anchor ?? sheet.Anchor, Png: source.Pixels[sheet.Id]);
        }).ToArray();
        var left = layers.Min(p => -(long)p.Anchor.X); var top = layers.Min(p => -(long)p.Anchor.Y);
        var width = layers.Max(p => p.Sheet.CellW - (long)p.Anchor.X) - left;
        var height = layers.Max(p => p.Sheet.CellH - (long)p.Anchor.Y) - top;
        var scale = Math.Min(112d / width, 112d / height); var w = Math.Max(1, (int)(width * scale)); var h = Math.Max(1, (int)(height * scale));
        var rgba = new byte[128 * 128 * 4];
        for (var y = 0; y < 128; y++) for (var x = 0; x < 128; x++)
        {
            var dst = (y * 128 + x) * 4; var checker = (byte)(((x / 8 + y / 8) % 2 == 0) ? 224 : 192);
            rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = checker; rgba[dst + 3] = 255;
            var dx = x - (128 - w) / 2; var dy = y - (128 - h) / 2;
            if (dx < 0 || dy < 0 || dx >= w || dy >= h) continue;
            var gx = left + dx * width / w; var gy = top + dy * height / h;
            foreach (var p in layers)
            {
                var sx = gx + (long)p.Anchor.X; var sy = gy + (long)p.Anchor.Y;
                if (sx < 0 || sy < 0 || sx >= p.Sheet.CellW || sy >= p.Sheet.CellH) continue;
                var src = (int)(((p.Clip.Row * p.Sheet.CellH + sy) * p.Png.Width + (p.Clip.Column + (p.Part.Frame ?? 0)) * p.Sheet.CellW + sx) * 4);
                var alpha = p.Png.Rgba[src + 3] * p.Part.Opacity;
                for (var c = 0; c < 3; c++) rgba[dst + c] = (byte)Math.Round((p.Png.Rgba[src + c] * alpha + rgba[dst + c] * (255 - alpha)) / 255);
            }
        }
        return ObjectPng.Encode(128, 128, rgba);
    }
}
