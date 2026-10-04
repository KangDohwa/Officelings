using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vibedotcom.Core.Iso.Editing;

internal sealed record ObjectSource(byte[] Bytes, IsoCustomObjectManifest Manifest,
    IReadOnlyDictionary<string, byte[]> Images, IReadOnlyDictionary<string, ObjectPng> Pixels, string Hash)
{
    internal long InstalledPngBytes => Images.Values.DistinctBy(SheetFile).Sum(p => (long)p.Length) + ObjectThumbnail.EncodedBytes;
    internal static ObjectSource? Read(string path, ObjectIssues issues, Func<IsoSheet, byte[]>? installedSheet = null,
        byte[]? sourceBytes = null, CancellationToken cancellation = default)
    {
        byte[] bytes;
        try { cancellation.ThrowIfCancellationRequested(); bytes = sourceBytes ?? ObjectFiles.Read(path, ObjectFiles.JsonLimit); }
        catch (OperationCanceledException e) { issues.Add("O009", error: e); return null; }
        catch (ObjectSizeException) { issues.Add("O004"); return null; }
        catch (Exception e) when (Expected(e)) { issues.Add("O003"); return null; }
        IsoCustomObjectManifest m;
        var id = "";
        try
        {
            using var json = ObjectSchema.Parse(bytes);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("id", out var identifier) && identifier.ValueKind == JsonValueKind.String)
                id = identifier.GetString() ?? "";
            ObjectSchema.Validate(json.RootElement);
            m = Freeze(json.RootElement.Deserialize<IsoCustomObjectManifest>(StateJson.Options) ?? throw new JsonException());
        }
        catch (ObjectSizeException) { issues.Add("O004"); return null; }
        catch (ObjectFieldException e) { issues.Add(e.Rule, id); return null; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        { issues.Add("O001"); return null; }
        ValidateIdentity(m, issues);
        var declared = ValidatePalette(m, issues);
        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var pixels = new Dictionary<string, ObjectPng>(StringComparer.Ordinal);
        var sourceFiles = new Dictionary<string, byte[]>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        byte[] ReadImage(IsoSheet s)
        {
            if (installedSheet != null) return installedSheet(s);
            var file = ObjectFiles.Under(Path.GetDirectoryName(ObjectFiles.Check(path))!, s.File);
            if (sourceFiles.TryGetValue(file, out var captured)) return captured;
            return sourceFiles[file] = ObjectFiles.Read(file, ObjectFiles.PngLimit);
        }
        long totalBytes = 0, totalPixels = 0;
        foreach (var s in m.Sheets)
        {
            if (cancellation.IsCancellationRequested) { issues.Add("O009", error: new OperationCanceledException()); return null; }
            if (s.Kind is not ("furniture" or "shadow") || s.Projection != "iso2to1" || s.Scale != 1) issues.Add("O001");
            if (s.CellW is < 1 or > 512 || s.CellH is < 1 or > 512 || s.Rows == null || s.Rows.Count is 0 or > 64) issues.Add("O004");
            if (!Integer(s.Anchor)) issues.Add("O007", s.Id);
            foreach (var c in s.Rows ?? [])
            {
                if (c.Frames is < 1 or > 16 || !double.IsFinite(c.Fps) || c.Fps is <= 0 or > 12) issues.Add("O004");
                if (string.IsNullOrEmpty(c.Name) || c.Name.Length > 96 || c.Direction is not ("se" or "sw" or "ne" or "nw" or "any") ||
                    c.Row < 0 || c.Column < 0 || c.Anchor != null && !Integer(c.Anchor)) issues.Add("O007", s.Id);
            }
            try
            {
                ObjectFiles.Relative(s.File); // Runtime validates the syntax but never resolves source filenames.
                var png = ReadImage(s);
                totalBytes += png.Length;
                if (png.Length > ObjectFiles.PngLimit || totalBytes > 16 * 1024 * 1024) { issues.Add("O004"); continue; }
                images.TryAdd(s.Id, png);
                var (w, h) = ObjectPng.Size(png); totalPixels += (long)w * h;
                if (totalPixels > 8_388_608) { issues.Add("O004"); continue; }
                if (s.CellW <= 0 || s.CellH <= 0 || w % s.CellW != 0 || h % s.CellH != 0 || s.Rows?.Any(c =>
                    ((long)c.Row + 1) * s.CellH > h || ((long)c.Column + c.Frames) * s.CellW > w) == true) issues.Add("O004");
                var decoded = ObjectPng.Decode(png, cancellation); pixels.TryAdd(s.Id, decoded);
                ValidateColors(declared, s, decoded, issues);
            }
            catch (ObjectSizeException) { issues.Add("O004"); }
            catch (OperationCanceledException e) { issues.Add("O009", error: e); return null; }
            catch (Exception e) when (Expected(e))
            { issues.Add(images.ContainsKey(s.Id) ? "O005" : "O003", s.Id); }
        }
        try { ObjectGeometry.Validate(m, pixels, issues, cancellation); }
        catch (OperationCanceledException e) { issues.Add("O009", error: e); return null; }
        return new(bytes, m, images, pixels, ContentHash(bytes, images));
    }
    internal static bool Expected(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    private static IsoCustomObjectManifest Freeze(IsoCustomObjectManifest m)
    {
        IsoPart Part(IsoPart p) => p with { AnchorChecks = p.AnchorChecks?.ToImmutableArray() };
        return m with
        {
            Sheets = m.Sheets.Select(s => s with { Rows = s.Rows?.Select(c => c with { AnchorChecks = c.AnchorChecks?.ToImmutableArray() }).ToImmutableArray() }).ToImmutableArray(),
            Furniture = m.Furniture.Select(f => f with { Variants = f.Variants.Select(v => v with
            {
                Parts = v.Parts.Select(Part).ToImmutableArray(), Shadow = v.Shadow == null ? null : Part(v.Shadow),
                Points = v.Points.IsDefault ? [] : v.Points
            }).ToImmutableArray() }).ToImmutableArray()
        };
    }
    internal static bool Integer(PixelPoint? p) => p != null && double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X == Math.Floor(p.X) && p.Y == Math.Floor(p.Y);
    private static void ValidateIdentity(IsoCustomObjectManifest m, ObjectIssues issues)
    {
        if (m.Version != 1 || m.CustomObjectVersion != 1 || m.Projection != "iso2to1" || m.Furniture.Length != 1 || m.Sheets.Length == 0) issues.Add("O001");
        if (!Regex.IsMatch(m.Id ?? "", "^user/[a-z0-9][a-z0-9-]{0,47}$") || string.IsNullOrEmpty(m.DisplayName) ||
            m.DisplayName.Length > 80 || m.DisplayName.EnumerateRunes().Any(c => Rune.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Control or
                System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator) ||
            m.Furniture.Any(f => f.Id != m.Id) || m.Sheets.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != m.Sheets.Length)
            issues.Add("O002", m.Id ?? "");
        var slug = m.Id?.StartsWith("user/", StringComparison.Ordinal) == true ? m.Id[5..] : "";
        var partPattern = "^" + Regex.Escape(slug) + "__[a-z0-9]+(?:-[a-z0-9]+)*__(se|sw|ne|nw|any)__[0-9]{2}$";
        var partIds = new Dictionary<string, (string Sheet, string Direction)>(StringComparer.Ordinal);
        foreach (var s in m.Sheets)
        {
            if (!s.Id.StartsWith(m.Id + "/", StringComparison.Ordinal) || !Regex.IsMatch(s.Id, "^user/[a-z0-9-]+/[a-z0-9/_-]{1,96}$") ||
                s.Rows?.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != s.Rows?.Count) issues.Add("O002", m.Id ?? "");
            foreach (var c in s.Rows ?? [])
            {
                if (!Regex.IsMatch(c.PartId ?? "", partPattern)) issues.Add("O002", m.Id ?? "");
                else if (partIds.TryGetValue(c.PartId!, out var owner) && owner != (s.Id, c.Direction)) issues.Add("O002", m.Id ?? "");
                else partIds[c.PartId!] = (s.Id, c.Direction);
            }
        }
        foreach (var v in m.Furniture.SelectMany(f => f.Variants))
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in v.Parts.Concat(v.Shadow == null ? [] : new[] { v.Shadow }))
                if (!Regex.IsMatch(p.PartId ?? "", partPattern) || !ids.Add(p.PartId!)) issues.Add("O002", m.Id ?? "");
        }
    }
    private static readonly HashSet<int> Frozen = ("4C2A23 804A2F B96537 C78251 ECA662 111924 122031 325571 48728F 6492A3 " +
        "10282C 235E5C 20746C 3C9F96 5DB1A5 3F1F18 61282B 9F2937 BF6554 191E1E 262D38 565958 808985 89918F " +
        "605E5C 898D7B D0B999 E7D1AC F2EBD3 186D7A 338F88 4BAFA3 0E1E1C 1D3A39 264F4B 537B78")
        .Split(' ').Select(s => Convert.ToInt32(s, 16)).ToHashSet();
    private static HashSet<int> ValidatePalette(IsoCustomObjectManifest m, ObjectIssues issues)
    {
        var palette = new HashSet<int>();
        if (m.Palette.Length is < 1 or > 256) issues.Add("O008");
        foreach (var color in m.Palette)
            if (!Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") || !palette.Add(Convert.ToInt32(color[1..], 16))) issues.Add("O008");
        return palette;
    }
    private static void ValidateColors(HashSet<int> palette, IsoSheet s, ObjectPng png, ObjectIssues issues)
    {
        var rgba = png.Rgba;
        var invalidAlpha = false; var outsideDeclared = false; var outsideFrozen = false;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var rgb = rgba[i] << 16 | rgba[i + 1] << 8 | rgba[i + 2]; var a = rgba[i + 3];
            if (a == 0 && rgb != 0 || s.Kind == "shadow" && a is not (0 or 48 or 96) || s.Kind != "shadow" && a is not (0 or 255)) invalidAlpha = true;
            if (a == 0) continue;
            if (s.Kind == "shadow") { if (rgb != 0x111924) outsideDeclared = true; }
            else
            {
                if (!palette.Contains(rgb)) outsideDeclared = true;
                if (!Frozen.Contains(rgb)) outsideFrozen = true;
            }
        }
        if (invalidAlpha) issues.Add("O005", s.Id);
        if (outsideDeclared) issues.Add("O008");
        if (outsideFrozen) issues.Add("O008", severity: IsoIssueSeverity.Warning);
    }
    internal static string ContentHash(byte[] manifest, IReadOnlyDictionary<string, byte[]> images)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(length, (ulong)manifest.Length); hash.AppendData(length); hash.AppendData(manifest);
        foreach (var (id, png) in images.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(id); var idLength = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(idLength, (uint)name.Length);
            hash.AppendData(idLength); hash.AppendData(name); BinaryPrimitives.WriteUInt64LittleEndian(length, (ulong)png.Length); hash.AppendData(length); hash.AppendData(png);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    internal static string SheetFile(byte[] png) => "sheet-" + Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant() + ".png";
    internal byte[] InstalledManifest()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Bytes)!;
        foreach (var sheet in node["sheets"]!.AsArray()) sheet!["file"] = SheetFile(Images[sheet["id"]!.GetValue<string>()]);
        return JsonSerializer.SerializeToUtf8Bytes(node, StateJson.Options);
    }
}
