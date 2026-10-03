using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vibedotcom.Core;

public sealed record PixelPoint(double X, double Y);
public sealed record AnimationRow(string Name, string Direction, int Row, int Frames, double Fps,
    int Column = 0, bool Loop = true, PixelPoint? FootOffset = null, PixelPoint? SeatAnchor = null);
public sealed record Sheet(string Id, string File, int CellW, int CellH, IReadOnlyList<AnimationRow> Rows,
    PixelPoint Anchor, double Scale = 1, int Width = 0, int Height = 0, string? Sha256 = null);
public sealed record InteractionPoint(string Id, int X, int Y, string Action, string Facing = "up");
public sealed record FurnitureVariant(int Rotation, string State, int FootprintW, int FootprintH,
    IReadOnlyList<InteractionPoint> Points, IReadOnlyList<Tile>? BlockedCells = null,
    string? SheetId = null, string? Animation = null, int Layer = 1, double ZOrder = 0,
    string? ChairBackId = null, string? ChairFrontId = null, PixelPoint? ChairOffset = null,
    PixelPoint? ChairSeatAnchor = null);
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
    private readonly Dictionary<string, Sheet> sheets = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, int Rotation, string State, InteractionPoint? Seat), FurnitureVariant> variants = [];
    private readonly Dictionary<(string Sheet, string Action, string Direction), AnimationRow> rows = [];
    private readonly Dictionary<(string Sheet, string Action, string Direction), AnimationRow> exactRows = [];
    private readonly Dictionary<(string Sheet, AnimationRow Row), SpriteFrame[]> frames = [];
    private readonly Dictionary<(int Row, int Column, bool Napping), SpriteFrame> primitiveFrames = [];
    private readonly Dictionary<(string Sheet, int Row, int Column), AnimationRow?> blinkRows = [];
    private readonly Dictionary<(string Sheet, string Action, string Direction), AnimationRow?> closedRows = [];
    private readonly Dictionary<(string Sheet, string Facing), string?> seatedVariants = [];
    private static readonly int[] ThinkingColumns = [0, 1, 0, 3];
    public AssetManifest Manifest { get; private set; }
    public Dictionary<string, byte[]> Images { get; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; } = [];
    public static string DefaultDirectory => Path.Combine(TrackerOptions.OutputDirectory, "assets");

    public static AssetPack LoadDefault(string? directory = null, bool useUserPack = true) =>
        directory != null ? new(directory) : useUserPack && ManifestFile(DefaultDirectory) != null
            ? new(DefaultDirectory) : new(null, bundled: true);

    public static AssetPack LoadExported(string directory) => new(directory, exported: true);

    private static string? ManifestFile(string directory) => new[] { "assets.json", "manifest.json" }
        .Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);

    public AssetPack(string? directory = null, bool bundled = false, bool exported = false)
    {
        using var resource = typeof(AssetPack).Assembly.GetManifestResourceStream("Vibedotcom.Core.Data.assets.json")!;
        Manifest = JsonSerializer.Deserialize<AssetManifest>(resource, StateJson.Options)!;
        // A nonblocking foreground chair also gives the primitive fallback fixed, editable chair instances.
        Manifest = Manifest with { Furniture = Manifest.Furniture.Append(new FurnitureAsset("chair", "chair",
            [new(0, "default", 3, 2, [], [])])).ToArray() };
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
                Validate(custom, exported);
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
                    var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (exported && (width != sheet.Width || height != sheet.Height || !sha.Equals(sheet.Sha256,StringComparison.OrdinalIgnoreCase) ||
                        sheet.Id == "fallback" && !bytes.AsSpan().SequenceEqual(Images["fallback"])))
                        throw new InvalidDataException("Exported sheet dimensions/hash mismatch: " + sheet.Id);
                    if (width % sheet.CellW != 0 || height % sheet.CellH != 0 || sheet.Rows.Any(r =>
                        ((long)r.Row + 1) * sheet.CellH > height || ((long)r.Column + r.Frames) * sheet.CellW > width))
                        throw new InvalidDataException("Clip exceeds sheet: " + sheet.Id);
                    images.Add(sheet.Id, bytes);
                    sheets.Add(sheet with { Width = width, Height = height, Sha256 = sha });
                }
                foreach (var (id, bytes) in images) Images[id] = bytes;
                // The reserved fallback remains available for missing animations and bubbles.
                Manifest = custom with { Sheets = sheets.Concat(Manifest.Sheets.Where(s => !sheets.Any(c => c.Id == s.Id))).ToArray(),
                    Furniture = custom.Furniture.Concat(Manifest.Furniture.Where(f => !custom.Furniture.Any(c => c.Id == f.Id))).ToArray(),
                    TileTypes = custom.TileTypes.Concat(Manifest.TileTypes.Where(t => !custom.TileTypes.Any(c => c.Id == t.Id))).ToArray() };
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
            { Warnings.Add("Asset pack rejected; using fallback: " + e.Message); }
        }
        Manifest = Manifest with { Sheets = Manifest.Sheets.Select(s => s.Sha256 != null ? s : s with
            { Sha256 = Convert.ToHexString(SHA256.HashData(Images[s.Id])).ToLowerInvariant() }).ToArray() };
        ResetCaches();
    }

    internal void UseSafeFallback()
    {
        var safe = new AssetPack(); Manifest = safe.Manifest;
        Images.Clear(); foreach (var (id, bytes) in safe.Images) Images[id] = bytes;
        ResetCaches();
    }

    private void ResetCaches()
    {
        sheets.Clear(); variants.Clear(); rows.Clear(); exactRows.Clear(); frames.Clear(); primitiveFrames.Clear();
        blinkRows.Clear(); closedRows.Clear(); seatedVariants.Clear();
        foreach (var sheet in Manifest.Sheets)
            if (sheets.TryAdd(sheet.Id, sheet))
                foreach (var row in sheet.Rows) exactRows.TryAdd((sheet.Id, row.Name, row.Direction), row);
        foreach (var asset in Manifest.Furniture)
            foreach (var variant in asset.Variants) variants.TryAdd((asset.Id, variant.Rotation, variant.State, null), variant);
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
    private static void Validate(AssetManifest m, bool exported = false)
    {
        if (m == null || m.Sheets == null || m.Characters == null || m.Furniture == null || m.TileTypes == null ||
            m.Version != 1 || m.Sheets.Count == 0 || m.Characters.Count == 0 || m.Sheets.Count > 128 || m.Characters.Count > 512 ||
            m.Furniture.Count > 512 || m.TileTypes.Count > 512 ||
            m.Sheets.Any(s => s == null) || m.Characters.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id)) ||
            m.Furniture.Any(f => f == null || string.IsNullOrWhiteSpace(f.Id)) || m.TileTypes.Any(t => t == null))
            throw new InvalidDataException("Unsupported/empty asset manifest.");
        if (m.Sheets.Select(s => s.Id).Distinct().Count() != m.Sheets.Count || m.Characters.Select(c => c.Id).Distinct().Count() != m.Characters.Count ||
            m.Furniture.Select(f => f.Id).Distinct().Count() != m.Furniture.Count || m.TileTypes.Select(t => t.Id).Distinct().Count() != m.TileTypes.Count)
            throw new InvalidDataException("Duplicate asset ID.");
        foreach (var s in m.Sheets)
            if (string.IsNullOrWhiteSpace(s.Id) || s.Id == "fallback" && !exported || s.CellW is < 1 or > 512 || s.CellH is < 1 or > 512 ||
                !double.IsFinite(s.Scale) || s.Scale is < .125 or > 4 || !double.IsFinite(s.Anchor.X) || !double.IsFinite(s.Anchor.Y) ||
                s.Rows == null || s.Rows.Count is 0 or > 512 || s.Rows.Any(r => r == null) ||
                s.Rows.Select(r => (r.Name, r.Direction)).Distinct().Count() != s.Rows.Count ||
                s.Rows.Any(r => string.IsNullOrWhiteSpace(r.Name) || r.Row < 0 || r.Column < 0 || r.Frames is < 1 or > 256 || !double.IsFinite(r.Fps) || r.Fps is <= 0 or > 60 ||
                    r.Direction is not ("any" or "up" or "down" or "left" or "right") || r.FootOffset is {} p && (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) ||
                    r.SeatAnchor is {} seat && (!double.IsFinite(seat.X) || !double.IsFinite(seat.Y) || seat.X < 0 || seat.Y < 0 || seat.X > s.CellW || seat.Y > s.CellH)))
                throw new InvalidDataException("Invalid sheet: " + s.Id);
        bool HasSheet(string? id) => id == null || m.Sheets.Any(s => s.Id == id);
        if (m.Characters.Any(c => string.IsNullOrWhiteSpace(c.SheetId) || !HasSheet(c.SheetId) || c.Provider is not (null or "codex" or "claude" or "antigravity") || c.Role is not (null or "supervisor" or "worker")))
            throw new InvalidDataException("Invalid character reference/filter.");
        foreach (var f in m.Furniture)
            if (f.Variants == null || f.Variants.Count is 0 or > 64 || f.Variants.Any(v => v == null) ||
                f.Variants.Select(v => (v.Rotation, v.State)).Distinct().Count() != f.Variants.Count || f.Variants.Any(v =>
                v.Rotation is not (0 or 90 or 180 or 270) || v.FootprintW is < 1 or > 64 || v.FootprintH is < 1 or > 64 ||
                v.Layer is < 0 or > 2 || !double.IsFinite(v.ZOrder) || !HasSheet(v.SheetId) ||
                new[] { v.ChairBackId, v.ChairFrontId }.Any(id => id != null && !m.Furniture.Any(a => a.Id == id)) ||
                new[] { v.ChairOffset, v.ChairSeatAnchor }.Any(p => p != null && (!double.IsFinite(p.X) || !double.IsFinite(p.Y))) ||
                v.Points == null || v.Points.Count > 128 || v.BlockedCells?.Count > 4096 ||
                v.Points.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || p.X is < -128 or > 128 || p.Y is < -128 or > 128) ||
                v.Points.Select(p => p.Id).Distinct().Count() != v.Points.Count ||
                v.Points.Any(p => p.Facing is not ("up" or "down" or "left" or "right")) ||
                v.BlockedCells?.Any(t => t.X < 0 || t.Y < 0 || t.X >= v.FootprintW || t.Y >= v.FootprintH) == true))
                throw new InvalidDataException("Invalid furniture: " + f.Id);
        if (m.TileTypes.Any(t => t.Kind is not ("floor" or "wall" or "void") || !HasSheet(t.SheetId) ||
            t.Color.Length != 7 || t.Color[0] != '#' || !uint.TryParse(t.Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _)))
            throw new InvalidDataException("Invalid tile type.");
    }

    public FurnitureVariant Variant(Furniture f)
    {
        var id = f.AssetId ?? f.Kind;
        if (variants.TryGetValue((id, f.Rotation, f.State, f.Seat), out var cached)) return cached;
        if (!variants.TryGetValue((id, f.Rotation, f.State, null), out var variant))
            throw new InvalidDataException("Unknown furniture variant: " + id + "/" + f.Rotation + "/" + f.State);
        if (f.Seat == null) return variant;
        return CacheSeatVariant(f, id, variant);
    }

    private FurnitureVariant CacheSeatVariant(Furniture f, string id, FurnitureVariant variant)
    {
        var authored = variant.Points.FirstOrDefault(p => p.Action == "typing");
        var dx = (f.Seat!.X - (authored?.X ?? f.Seat.X)) * 16;
        var dy = (f.Seat.Y - (authored?.Y ?? f.Seat.Y)) * 16;
        return variants[(id, f.Rotation, f.State, f.Seat)] = variant with { Points = variant.Points.Where(p => p.Action != "typing").Append(f.Seat).ToArray(),
            ChairSeatAnchor = variant.ChairSeatAnchor is {} seat ? new(seat.X + dx, seat.Y + dy) : null,
            ChairOffset = variant.ChairOffset is {} offset ? new(offset.X + dx, offset.Y + dy) : null };
    }
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
        var sheet = sheets[sheetId];
        var row = Row(sheetId, action, direction);
        var tick = (long)Math.Floor(milliseconds / 1000d * row.Fps) + (row.Loop ? phase : 0);
        var column = (int)(row.Loop ? (tick % row.Frames + row.Frames) % row.Frames : Math.Clamp(tick, 0, row.Frames - 1));
        if (!frames.TryGetValue((sheetId, row), out var clip))
        {
            clip = new SpriteFrame[row.Frames];
            for (var i = 0; i < clip.Length; i++)
            {
                var cell = row.Column + i;
                clip[i] = new(sheet.Id, row.Row, cell, cell * sheet.CellW, row.Row * sheet.CellH, sheet.CellW, sheet.CellH,
                    sheet.Anchor.X, sheet.Anchor.Y, sheet.Scale, row.FootOffset?.X ?? 0, row.FootOffset?.Y ?? 0);
            }
            frames[(sheetId, row)] = clip;
        }
        return clip[column];
    }

    private AnimationRow Row(string sheetId, string action, string direction)
    {
        if (rows.TryGetValue((sheetId, action, direction), out var row)) return row;
        return rows[(sheetId, action, direction)] = SelectRow(sheetId, action, direction);
    }

    private AnimationRow SelectRow(string sheetId, string action, string direction)
    {
        var sheet = sheets[sheetId];
        return sheet.Rows.FirstOrDefault(r => r.Name == action && r.Direction == direction) ??
            sheet.Rows.FirstOrDefault(r => r.Name == action && r.Direction == "any") ??
            sheet.Rows.FirstOrDefault(r => r.Name == "idle" && r.Direction == direction) ??
            sheet.Rows.FirstOrDefault(r => r.Name == "idle") ?? sheet.Rows[0];
    }

    private AnimationRow? BlinkRow(SpriteFrame frame)
    {
        var key = (frame.SheetId, frame.Row, frame.Column);
        if (blinkRows.TryGetValue(key, out var closed)) return closed;
        var sheet = sheets[frame.SheetId];
        var normal = sheet.Rows.First(r => r.Row == frame.Row && frame.Column >= r.Column && frame.Column < r.Column + r.Frames);
        return blinkRows[key] = ClosedRow(sheet.Id, normal.Name, normal.Direction);
    }

    private AnimationRow? ClosedRow(string sheetId, string action, string direction)
    {
        var key = (sheetId, action, direction);
        if (closedRows.TryGetValue(key, out var row)) return row;
        exactRows.TryGetValue((sheetId, "blink-" + action, direction), out row);
        return closedRows[key] = row;
    }

    internal double AnimationFps(string sheetId, string action, string direction, long milliseconds)
    {
        var row = Row(sheetId, action, direction);
        return row.Frames > 1 && (row.Loop || Math.Floor(milliseconds / 1000d * row.Fps) < row.Frames - 1) ? row.Fps : 0;
    }
    public SpriteFrame CharacterFrame(AgentInfo a, CharacterDesign design, string action, string facing, long elapsedMilliseconds, bool blink = false)
    {
        if (design.SheetId != "fallback")
        {
            var animation = CharacterAnimation(action);
            if (facing == "up" && action is "thinking" or "waiting") animation = "idle";
            var phase = SpriteAtlas.Hash(a.Id) % 4;
            var frame = Frame(design.SheetId, animation, facing, elapsedMilliseconds, phase);
            if (!blink || animation == "walk") return frame;
            var closed = BlinkRow(frame);
            return closed == null ? frame : Frame(design.SheetId, closed.Name, closed.Direction, elapsedMilliseconds, phase);
        }
        var identity = int.TryParse(design.Id.AsSpan("fallback-".Length), out var value) ? value : SpriteAtlas.Variant(a.Id);
        var row = SpriteAtlas.Row(a.Provider, a.IsSubagent, a.Id, identity); var col = SpriteAtlas.Frame(action == "beanbag" ? "napping" : action, facing, elapsedMilliseconds, identity);
        // The primitive atlas also has immutable frames, independent of the caller's identity.
        var sleeping = action is "napping" or "beanbag";
        var key = (row, col, sleeping);
        if (!primitiveFrames.TryGetValue(key, out var primitive))
            primitiveFrames[key] = primitive = new("fallback", row, col, col * 16, row * 24, 16, 24, 8, 24, 1, 0, sleeping ? -10 : 0);
        return primitive;
    }

    public string? SeatedVariant(CharacterDesign design, string facing)
    {
        if (seatedVariants.TryGetValue((design.SheetId, facing), out var cached)) return cached;
        return seatedVariants[(design.SheetId, facing)] = SelectSeatedVariant(design.SheetId, facing);
    }

    private string? SelectSeatedVariant(string sheetId, string facing)
    {
        var name = facing == "up" ? "seated_back" : "seated_" + facing;
        return sheets[sheetId].Rows.Any(r => r.Name == name && r.Direction == facing && r.SeatAnchor != null) ? name : null;
    }

    public (SpriteFrame Frame, PixelPoint Anchor) SeatedFrame(AgentInfo agent, CharacterDesign design,
        string variant, string facing, long elapsed, bool blink)
    {
        var sheet = sheets[design.SheetId];
        var row = exactRows[(sheet.Id, variant, facing)];
        var phase = agent.State == AgentState.Working ? SpriteAtlas.Hash(agent.Id) % (uint)row.Frames : 0;
        if (agent.State == AgentState.Waiting) elapsed = 0;
        else if (agent.State == AgentState.Thinking)
        {
            var cycle = elapsed % 8000;
            var column = cycle is >= 6000 and < 6600 ? 2 : ThinkingColumns[(cycle / 1000) % 4];
            elapsed = (long)Math.Ceiling(column * 1000 / row.Fps);
        }
        var closed = blink ? ClosedRow(sheet.Id, variant, facing) : null;
        return (Frame(sheet.Id, closed?.Name ?? variant, facing, elapsed, phase), row.SeatAnchor!);
    }

    internal double CharacterAnimationFps(AgentInfo agent, CharacterDesign design, string action, string facing, long elapsed, bool blink)
    {
        if (design.SheetId == "fallback") return action switch
        {
            "entering" or "walking" or "exiting" or "typing" or "pickup" or "reporting" => 6.25,
            "coffee" or "water" => 1.5625, "tv" => 1.25, "phone" or "chatting" => 6.25 / 3, "napping" => 6.25 / 6, _ => 0
        };
        var animation = CharacterAnimation(action);
        if (facing == "up" && action is "thinking" or "waiting") animation = "idle";
        var direction = facing;
        if (blink && animation != "walk" && BlinkRow(Frame(design.SheetId, animation, facing, elapsed, SpriteAtlas.Hash(agent.Id) % 4)) is {} closed)
        { animation = closed.Name; direction = closed.Direction; }
        return AnimationFps(design.SheetId, animation, direction, elapsed);
    }

    private static string CharacterAnimation(string action) => action switch
    {
        "walking" or "entering" or "exiting" => "walk", "beanbag" => "napping", "reading" => "phone",
        "arcade" => "typing", "vending" or "fridge" => "coffee", "darts" or "pingpong" => "chatting",
        "aquarium" or "window" => "idle", _ => action
    };

    internal double SeatedAnimationFps(AgentInfo agent, CharacterDesign design, string variant, string facing, long elapsed, bool blink)
    {
        if (agent.State == AgentState.Waiting) return 0;
        if (agent.State == AgentState.Thinking) return exactRows[(design.SheetId, variant, facing)].Frames > 1 ? 1 : 0;
        var animation = blink ? ClosedRow(design.SheetId, variant, facing)?.Name ?? variant : variant;
        return AnimationFps(design.SheetId, animation, facing, elapsed);
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
