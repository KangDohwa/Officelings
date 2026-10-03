using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vibedotcom.Core;

public sealed record PixelPoint(double X, double Y);
public sealed record AnimationRow(string Name, string Direction, int Row, int Frames, double Fps,
    int Column = 0, bool Loop = true, PixelPoint? FootOffset = null);
public sealed record Sheet(string Id, string File, int CellW, int CellH, IReadOnlyList<AnimationRow> Rows,
    PixelPoint Anchor, double Scale = 1, int Width = 0, int Height = 0, string? Sha256 = null);
public sealed record InteractionPoint(string Id, int X, int Y, string Action, string Facing = "up");
public sealed record FurnitureVariant(int Rotation, string State, int FootprintW, int FootprintH,
    IReadOnlyList<InteractionPoint> Points, IReadOnlyList<Tile>? BlockedCells = null,
    string? SheetId = null, string? Animation = null, int Layer = 1, double ZOrder = 0);
public sealed record FurnitureAsset(string Id, string Kind, IReadOnlyList<FurnitureVariant> Variants);
public sealed record CharacterDesign(string Id, string SheetId, string? Provider = null, string? Role = null);
public sealed record TileType(int Id, string Kind, string Color, string? SheetId = null, string? Animation = null);
public sealed record AssetManifest(int Version, IReadOnlyList<Sheet> Sheets, IReadOnlyList<FurnitureAsset> Furniture,
    IReadOnlyList<CharacterDesign> Characters, IReadOnlyList<TileType> TileTypes);
public sealed record SpriteFrame(string SheetId, int Row, int Column, int X, int Y, int Width, int Height,
    double AnchorX, double AnchorY, double Scale, double OffsetX = 0, double OffsetY = 0);

// Geometry/animation selection is shared with the headless tracker. Only WPF decodes pixels.
public sealed class AssetPack
{
    public AssetManifest Manifest { get; private set; }
    public Dictionary<string, byte[]> Images { get; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; } = [];
    public static string DefaultDirectory => Path.Combine(TrackerOptions.OutputDirectory, "assets");

    public static AssetPack LoadDefault(string? directory = null, bool useUserPack = true) =>
        directory != null ? new(directory) : useUserPack && ManifestFile(DefaultDirectory) != null
            ? new(DefaultDirectory) : new(null, bundled: true);

    private static string? ManifestFile(string directory) => new[] { "assets.json", "manifest.json" }
        .Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);

    public AssetPack(string? directory = null, bool bundled = false)
    {
        using var resource = typeof(AssetPack).Assembly.GetManifestResourceStream("Vibedotcom.Core.Data.assets.json")!;
        Manifest = JsonSerializer.Deserialize<AssetManifest>(resource, StateJson.Options)!;
        Images["fallback"] = SpriteAtlas.CreatePng();
        var file = directory == null ? null : ManifestFile(directory);
        if (bundled || file != null)
        {
            try
            {
                using var input = bundled ? typeof(AssetPack).Assembly.GetManifestResourceStream("Vibedotcom.Core.Pack.assets.json")
                    ?? throw new InvalidDataException("Bundled art pack missing; run tools/build_pack.py before building.") : null;
                var custom = bundled ? JsonSerializer.Deserialize<AssetManifest>(input!, StateJson.Options)!
                    : Read<AssetManifest>(file!);
                Validate(custom);
                var images = new Dictionary<string, byte[]>();
                var sheets = new List<Sheet>();
                foreach (var sheet in custom.Sheets)
                {
                    byte[] bytes;
                    if (bundled)
                    {
                        using var png = typeof(AssetPack).Assembly.GetManifestResourceStream("Vibedotcom.Core.Pack." + Path.GetFileName(sheet.File))
                            ?? throw new InvalidDataException("Bundled sheet missing: " + sheet.File);
                        using var buffer = new MemoryStream(); png.CopyTo(buffer); bytes = buffer.ToArray();
                    }
                    else bytes = File.ReadAllBytes(LocalFile(directory!, sheet.File));
                    var (width, height) = PngSize(bytes);
                    if (width % sheet.CellW != 0 || height % sheet.CellH != 0 || sheet.Rows.Any(r =>
                        (r.Row + 1) * sheet.CellH > height || (r.Column + r.Frames) * sheet.CellW > width))
                        throw new InvalidDataException("Clip exceeds sheet: " + sheet.Id);
                    images.Add(sheet.Id, bytes);
                    sheets.Add(sheet with { Width = width, Height = height, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
                }
                foreach (var (id, bytes) in images) Images[id] = bytes;
                // The reserved fallback remains available for missing animations and bubbles.
                Manifest = custom with { Sheets = sheets.Concat(Manifest.Sheets).ToArray(),
                    Furniture = custom.Furniture.Concat(Manifest.Furniture.Where(f => !custom.Furniture.Any(c => c.Id == f.Id))).ToArray(),
                    TileTypes = custom.TileTypes.Concat(Manifest.TileTypes.Where(t => !custom.TileTypes.Any(c => c.Id == t.Id))).ToArray() };
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
            { Warnings.Add("Asset pack rejected; using fallback: " + e.Message); }
        }
        Manifest = Manifest with { Sheets = Manifest.Sheets.Select(s => s.Sha256 != null ? s : s with
            { Sha256 = Convert.ToHexString(SHA256.HashData(Images[s.Id])).ToLowerInvariant() }).ToArray() };
    }

    internal static T Read<T>(string file)
    {
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("JSON exceeds 4 MiB.");
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<T>(input, StateJson.Options) ?? throw new InvalidDataException("Empty JSON.");
    }
    internal static string LocalFile(string root, string file)
    {
        var folder = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, file));
        if (Path.IsPathRooted(file) || !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Sheet must be a local relative file.");
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Sheet exceeds 32 MiB.");
        return path;
    }
    public static (int Width, int Height) PngSize(byte[] bytes)
    {
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("Expected PNG.");
        var w = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)); var h = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || (long)w * h > 16_777_216) throw new InvalidDataException("Invalid PNG dimensions.");
        var channels = bytes[25] switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        if (bytes[24] != 8 || bytes[26] != 0 || bytes[27] != 0 || bytes[28] != 0 || channels == 0)
            throw new InvalidDataException("Use an 8-bit, non-interlaced PNG sheet.");
        using var compressed = new MemoryStream(); var ended = false; var palette = false;
        for (var pos = 8; pos < bytes.Length;)
        {
            if (pos + 12 > bytes.Length) throw new InvalidDataException("Truncated PNG chunk.");
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos));
            if (length < 0 || (long)pos + length + 12 > bytes.Length) throw new InvalidDataException("Invalid PNG chunk length.");
            var name = bytes.AsSpan(pos + 4, 4);
            if (name.SequenceEqual("IDAT"u8)) compressed.Write(bytes,pos + 8,length);
            if (name.SequenceEqual("PLTE"u8)) palette = length is > 0 and <= 768 && length % 3 == 0;
            if (name.SequenceEqual("IEND"u8)) { ended = length == 0; break; }
            pos += length + 12;
        }
        if (!ended || bytes[25] == 3 && !palette || compressed.Length == 0) throw new InvalidDataException("Incomplete PNG.");
        compressed.Position = 0; using var z = new ZLibStream(compressed,CompressionMode.Decompress);
        var row = new byte[w * channels + 1];
        for (var y = 0; y < h; y++) { z.ReadExactly(row); if (row[0] > 4) throw new InvalidDataException("Invalid PNG filter."); }
        if (z.ReadByte() != -1) throw new InvalidDataException("PNG pixel data exceeds dimensions.");
        return (w, h);
    }
    private static void Validate(AssetManifest m)
    {
        if (m.Version != 1 || m.Sheets.Count == 0 || m.Characters.Count == 0 || m.Sheets.Count > 128 || m.Characters.Count > 512)
            throw new InvalidDataException("Unsupported/empty asset manifest.");
        if (m.Sheets.Select(s => s.Id).Distinct().Count() != m.Sheets.Count || m.Characters.Select(c => c.Id).Distinct().Count() != m.Characters.Count ||
            m.Furniture.Select(f => f.Id).Distinct().Count() != m.Furniture.Count || m.TileTypes.Select(t => t.Id).Distinct().Count() != m.TileTypes.Count)
            throw new InvalidDataException("Duplicate asset ID.");
        foreach (var s in m.Sheets)
            if (string.IsNullOrWhiteSpace(s.Id) || s.Id == "fallback" || s.CellW is < 1 or > 512 || s.CellH is < 1 or > 512 ||
                !double.IsFinite(s.Scale) || s.Scale is < .125 or > 4 || !double.IsFinite(s.Anchor.X) || !double.IsFinite(s.Anchor.Y) ||
                s.Rows.Count == 0 || s.Rows.Select(r => (r.Name, r.Direction)).Distinct().Count() != s.Rows.Count ||
                s.Rows.Any(r => r.Row < 0 || r.Column < 0 || r.Frames is < 1 or > 256 || !double.IsFinite(r.Fps) || r.Fps is <= 0 or > 60 ||
                    r.Direction is not ("any" or "up" or "down" or "left" or "right") || r.FootOffset is {} p && (!double.IsFinite(p.X) || !double.IsFinite(p.Y))))
                throw new InvalidDataException("Invalid sheet: " + s.Id);
        bool HasSheet(string? id) => id == null || m.Sheets.Any(s => s.Id == id);
        if (m.Characters.Any(c => !HasSheet(c.SheetId) || c.Provider is not (null or "codex" or "claude" or "antigravity") || c.Role is not (null or "supervisor" or "worker")))
            throw new InvalidDataException("Invalid character reference/filter.");
        foreach (var f in m.Furniture)
            if (f.Variants.Count == 0 || f.Variants.Select(v => (v.Rotation, v.State)).Distinct().Count() != f.Variants.Count || f.Variants.Any(v =>
                v.Rotation is not (0 or 90 or 180 or 270) || v.FootprintW is < 1 or > 64 || v.FootprintH is < 1 or > 64 ||
                v.Layer is < 0 or > 2 || !double.IsFinite(v.ZOrder) || !HasSheet(v.SheetId) ||
                v.Points.Select(p => p.Id).Distinct().Count() != v.Points.Count ||
                v.Points.Any(p => p.Facing is not ("up" or "down" or "left" or "right")) ||
                v.BlockedCells?.Any(t => t.X < 0 || t.Y < 0 || t.X >= v.FootprintW || t.Y >= v.FootprintH) == true))
                throw new InvalidDataException("Invalid furniture: " + f.Id);
        if (m.TileTypes.Any(t => t.Kind is not ("floor" or "wall" or "void") || !HasSheet(t.SheetId) ||
            t.Color.Length != 7 || t.Color[0] != '#' || !uint.TryParse(t.Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _)))
            throw new InvalidDataException("Invalid tile type.");
    }

    public FurnitureVariant Variant(Furniture f) => Manifest.Furniture.FirstOrDefault(a => a.Id == (f.AssetId ?? f.Kind))?.Variants
        .FirstOrDefault(v => v.Rotation == f.Rotation && v.State == f.State)
        ?? throw new InvalidDataException("Unknown furniture variant: " + (f.AssetId ?? f.Kind) + "/" + f.Rotation + "/" + f.State);
    public IEnumerable<(Tile Tile, InteractionPoint Point)> Points(OfficeLayout layout) => layout.Furniture.SelectMany(f =>
        Variant(f).Points.Select(p => (new Tile(f.X + p.X, f.Y + p.Y), p))).Concat((layout.Zones ?? []).SelectMany(z =>
        (z.Points ?? []).Select(p => (new Tile(z.X + p.X, z.Y + p.Y), p))));
    public CharacterDesign Assign(AgentInfo agent, IEnumerable<string> used)
    {
        var eligible = Manifest.Characters.Where(c => (c.Provider == null || c.Provider == agent.Provider.ToString().ToLowerInvariant()) &&
            (c.Role == null || c.Role == (agent.IsSubagent ? "worker" : "supervisor"))).ToArray();
        if (eligible.Length == 0) return new("fallback-" + SpriteAtlas.Variant(agent.Id), "fallback");
        var start = (int)(SpriteAtlas.Hash(agent.Id) % eligible.Length);
        var assigned = used.ToHashSet();
        for (var i = 0; i < eligible.Length; i++) if (!assigned.Contains(eligible[(start + i) % eligible.Length].Id)) return eligible[(start + i) % eligible.Length];
        return eligible[start];
    }
    public SpriteFrame Frame(string sheetId, string action, string direction, long milliseconds, uint phase = 0)
    {
        var sheet = Manifest.Sheets.First(s => s.Id == sheetId);
        var row = sheet.Rows.FirstOrDefault(r => r.Name == action && r.Direction == direction) ??
            sheet.Rows.FirstOrDefault(r => r.Name == action && r.Direction == "any") ??
            sheet.Rows.FirstOrDefault(r => r.Name == "idle" && r.Direction == direction) ??
            sheet.Rows.FirstOrDefault(r => r.Name == "idle") ?? sheet.Rows[0];
        var tick = (long)Math.Floor(milliseconds / 1000d * row.Fps) + (row.Loop ? phase : 0);
        var column = row.Column + (int)(row.Loop ? (tick % row.Frames + row.Frames) % row.Frames : Math.Clamp(tick, 0, row.Frames - 1));
        return new(sheet.Id, row.Row, column, column * sheet.CellW, row.Row * sheet.CellH, sheet.CellW, sheet.CellH,
            sheet.Anchor.X, sheet.Anchor.Y, sheet.Scale, row.FootOffset?.X ?? 0, row.FootOffset?.Y ?? 0);
    }
    public SpriteFrame CharacterFrame(AgentInfo a, CharacterDesign design, string action, string facing, long elapsedMilliseconds, bool blink = false)
    {
        if (design.SheetId != "fallback")
        {
            var animation = action is "walking" or "entering" or "exiting" ? "walk" : action;
            if (facing == "up" && action is "thinking" or "waiting") animation = "idle";
            var phase = SpriteAtlas.Hash(a.Id) % 4;
            var frame = Frame(design.SheetId, animation, facing, elapsedMilliseconds, phase);
            if (!blink || animation == "walk") return frame;
            var rows = Manifest.Sheets.First(s => s.Id == design.SheetId).Rows;
            var normal = rows.First(r => r.Row == frame.Row && frame.Column >= r.Column && frame.Column < r.Column + r.Frames);
            var closed = rows.FirstOrDefault(r => r.Name == "blink-" + normal.Name && r.Direction == normal.Direction);
            return closed == null ? frame : Frame(design.SheetId, closed.Name, closed.Direction, elapsedMilliseconds, phase);
        }
        var identity = int.TryParse(design.Id.AsSpan("fallback-".Length), out var value) ? value : SpriteAtlas.Variant(a.Id);
        var row = SpriteAtlas.Row(a.Provider, a.IsSubagent, a.Id, identity); var col = SpriteAtlas.Frame(action, facing, elapsedMilliseconds, identity);
        return new("fallback", row, col, col * 16, row * 24, 16, 24, 8, 24, 1, 0, action == "napping" ? -10 : 0);
    }
    // Hashed sheet names keep old snapshots valid while another tracker publishes a new pack.
    public AssetManifest Export(string output)
    {
        var sheets = Manifest.Sheets.Select(s =>
        {
            var file = "sheets/" + s.Sha256 + ".png";
            var target = Path.Combine(output, file);
            if (!File.Exists(target)) StateJson.WriteAtomic(target, Images[s.Id]);
            return s with { File = file };
        }).ToArray();
        StateJson.WriteAtomic(Path.Combine(output, "atlas.png"), Images["fallback"]);
        var exported = Manifest with { Sheets = sheets };
        StateJson.WriteAtomic(Path.Combine(output, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(exported, StateJson.Options));
        return exported;
    }
}
