using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vibedotcom.Core.Iso;

/// The only boundary between production manifests and the compiled runtime pack.
/// No user profile, installed flat pack, generator, or network access is used.
public sealed partial class IsoPack
{
    private readonly Dictionary<string, IsoSheet> sheets = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Sheet, string Key), IsoClip> clips = [];
    private readonly Dictionary<string, IsoPngMask> masks = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Sheet, string Key), bool[]> changedFrames = [];
    private readonly Dictionary<(string Sheet, string Key), IsoBounds> opaqueBounds = [];
    private readonly Dictionary<(string Sheet, string Key), bool[]> opaquePixels = [];
    public IsoManifest Manifest { get; private set; } = null!;
    public IReadOnlyDictionary<string, byte[]> Images => images;
    private readonly Dictionary<string, byte[]> images = new(StringComparer.Ordinal);
    public IsoLayout Layout { get; private set; } = null!;
    public IReadOnlyList<CharacterDesign> Characters => Manifest.Characters ?? [];
    public static IsoPack Load(string dir)
    {
        dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        var manifest = Path.Combine(dir, "assets.json");
        if (File.Exists(manifest))
        {
            var result = Build([Read(manifest)], (_, path) => File.ReadAllBytes(AssetPack.LocalFile(dir, path)));
            var layoutFile = Path.Combine(dir, "layout.json");
            if (File.Exists(layoutFile)) result.Layout = IsoLayoutLoader.Load(layoutFile, result);
            return result;
        }
        // Temporary source adapter: enumerate only the approved final exports, never intermediate art.
        var files = new List<string> { "env/floors/floors.iso.json", "env/walls/walls.iso.json" };
        files.AddRange(Directory.EnumerateFiles(Path.Combine(dir, "furniture-v2"), "*.iso.json", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(dir, p)).Order(StringComparer.Ordinal));
        files.AddRange(["characters/dev-hoodie-v2/dev-hoodie.iso.json", "characters/punk-girl/punk-girl.iso.json",
            "characters/professor/professor.iso.json", "pets/cat.iso.json", "props/carried-props.iso.json"]);
        var inputs = files.Select(p => (Path.Combine(dir, p), Read(Path.Combine(dir, p)))).ToArray();
        return Build(inputs.Select(p => p.Item2).ToArray(), (index, path) =>
            File.ReadAllBytes(AssetPack.LocalFile(Path.GetDirectoryName(inputs[index].Item1)!, path)));
    }
    public static IsoPack LoadDefault()
    {
        var assembly = typeof(IsoPack).Assembly;
        using var manifest = assembly.GetManifestResourceStream("Vibedotcom.Core.IsoPack.assets.json")
            ?? throw new InvalidDataException("Embedded iso pack is unavailable; merge/build the isopack lane first.");
        if (manifest.Length > 4 * 1024 * 1024) throw new InvalidDataException("JSON exceeds 4 MiB.");
        var m = JsonSerializer.Deserialize<IsoManifest>(manifest, StateJson.Options) ?? throw new InvalidDataException("Empty iso manifest.");
        var result = Build([m], (_, path) =>
        {
            // Validate paths even though resources cannot escape the assembly.
            ValidateRelativePath(path);
            using var png = assembly.GetManifestResourceStream("Vibedotcom.Core.IsoPack." + Path.GetFileName(path))
                ?? throw new InvalidDataException("Embedded iso sheet missing: " + path);
            if (png.Length > 32 * 1024 * 1024) throw new InvalidDataException("Sheet exceeds 32 MiB.");
            using var buffer = new MemoryStream(); png.CopyTo(buffer); return buffer.ToArray();
        });
        using var layout = assembly.GetManifestResourceStream("Vibedotcom.Core.IsoPack.layout.json");
        if (layout != null) result.Layout = IsoLayoutLoader.Read(layout, result);
        return result;
    }
    private static IsoManifest Read(string file) => AssetPack.Read<IsoManifest>(file);
    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') ||
            path.Split('/', '\\').Any(p => p is ".." or ".")) throw new InvalidDataException("Expected local relative path.");
    }
    private static IsoPack Build(IReadOnlyList<IsoManifest> inputs, Func<int, string, byte[]> readImage)
    {
        var pack = new IsoPack(); var loaded = new List<IsoSheet>();
        var furniture = new List<IsoFurnitureAsset>(); var characters = new List<CharacterDesign>();
        var walls = new List<IsoWallModule>(); var pets = new List<IsoPet>(); var props = new List<IsoCarriedProp>();
        var tiles = new List<TileType>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var m = inputs[index];
            if (m.Version != 1 || m.Projection != "iso2to1" || m.Sheets == null ||
                m.RequiredCapabilities?.Any(c => c is not ("iso2to1" or "renderItemsV1")) == true)
                throw new InvalidDataException("Unsupported iso manifest/capability.");
            if (m.Tile is {} tile && (tile.Width != 64 || tile.Height != 32 || tile.Anchor != null && tile.Anchor != new PixelPoint(32, 16)) ||
                m.WallRise is not (null or 16) || m.WallHeight is not (null or 48) ||
                m.CharacterCell is {} cell && (cell.Width != 32 || cell.Height != 64) ||
                m.FootAnchor != null && m.FootAnchor != new PixelPoint(16, 60) || m.SeatAnchor != null && m.SeatAnchor != new PixelPoint(16, 44) ||
                m.StandingHeight is not (null or 40) || m.DeskHeight is not (null or 18) || m.SeatHeight is not (null or 12) ||
                m.Facings != null && !m.Facings.SequenceEqual(new[] { "se", "sw", "ne", "nw" }))
                throw new InvalidDataException("Manifest geometry differs from strict 2:1 S0.");
            foreach (var raw in m.Sheets)
            {
                var s = raw with { Rows = raw.Rows ?? raw.Clips ?? [] };
                ValidateSheet(s); ValidateRelativePath(s.File);
                var bytes = readImage(index, s.File);
                if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("Sheet exceeds 32 MiB.");
                var (width, height) = AssetPack.PngSize(bytes); var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (s.Sha256 != null && !sha.Equals(s.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    s.Width != 0 && s.Width != width || s.Height != 0 && s.Height != height ||
                    width % s.CellW != 0 || height % s.CellH != 0 || s.Rows!.Any(c =>
                        ((long)c.Row + 1) * s.CellH > height || ((long)c.Column + c.Frames) * s.CellW > width))
                    throw new InvalidDataException("Sheet bounds/hash mismatch: " + s.Id);
                s = s with { Width = width, Height = height, Sha256 = sha };
                if (!pack.sheets.TryAdd(s.Id, s)) throw new InvalidDataException("Duplicate iso sheet: " + s.Id);
                pack.images.Add(s.Id, bytes); loaded.Add(s);
                // Managed alpha/pixel decoding supports the existing PNG formats; no WPF dependency.
                pack.masks.Add(s.Id, IsoPngMask.Read(bytes));
                foreach (var c in s.Rows!)
                {
                    if (!pack.clips.TryAdd((s.Id, c.Key), c)) throw new InvalidDataException("Duplicate clip: " + c.Key);
                    pack.ValidateAnchorChecks(s, c, c.AnchorChecks);
                }
            }
            furniture.AddRange(m.Furniture ?? []); characters.AddRange(m.Characters ?? []);
            walls.AddRange(m.WallModules ?? []); pets.AddRange(m.Pets ?? []); props.AddRange(m.CarriedProps ?? []);
            tiles.AddRange(m.TileTypes ?? []);
        }
        if (loaded.Count is 0 or > 128 || furniture.Count > 512 || characters.Count is 0 or > 512 || tiles.Count > 512)
            throw new InvalidDataException("Iso asset limits exceeded.");
        foreach (var s in loaded) pack.ExpandAliases(s);
        // Compiled assembly bindings augment resolved source clips; unrelated
        // pack-only actions/effects retain the existing runtime adapter.
        foreach (var s in loaded.Where(s => s.Kind == "character")) foreach (var c in s.Clips ?? [])
            if (c.ChairFrames != null)
            {
                var original = pack.Clip(s.Id, c.Key);
                if (original.Row != c.Row || original.Column != c.Column || original.Frames != c.Frames)
                    throw new InvalidDataException("Chair binding differs from source playback.");
                pack.clips[(s.Id, c.Key)] = original with { ChairFrames = c.ChairFrames };
            }
        foreach (var s in loaded) pack.ExpandBlinkAliases(s);
        RequireUnique(furniture.Select(f => f.Id)); RequireUnique(characters.Select(c => c.Id));
        foreach (var c in characters)
            if (c.Provider is not (null or "codex" or "claude" or "antigravity") || c.Role is not (null or "worker" or "supervisor") || !pack.sheets.TryGetValue(c.SheetId, out var s) || s.Kind != "character") throw new InvalidDataException("Invalid character sheet.");
        foreach (var asset in furniture) pack.ValidateFurniture(asset);
        foreach (var wall in walls) foreach (var p in wall.Parts) pack.ValidatePart(p);
        foreach (var p in props) pack.Clip(p.SheetId, p.Animation, "se");
        foreach (var pet in pets) pack.Sheet(pet.SheetId);
        // Runtime-only ground art is kept in the same sheet API; it is never written into the I1 pack.
        var shadowBytes = IsoActorShadow.Png();
        var shadow = new IsoSheet(IsoActorShadow.SheetId, "shadow", "core-contact-shadow.png", 128, 128, new(64, 64),
            [new("contact", "any", 0, 1, 1)], Width: 128, Height: 128, Sha256: Convert.ToHexString(SHA256.HashData(shadowBytes)).ToLowerInvariant());
        pack.sheets.Add(shadow.Id, shadow); pack.images.Add(shadow.Id, shadowBytes); pack.masks.Add(shadow.Id, IsoPngMask.Read(shadowBytes));
        pack.clips.Add((shadow.Id, "contact/any"), shadow.Rows![0]); loaded.Add(shadow);
        foreach (var (ui, uiImage) in new[] { IsoSharedUi.Load(), IsoSharedUi.LoadSpeech() })
        {
            pack.sheets.Add(ui.Id, ui); pack.images.Add(ui.Id, uiImage); pack.masks.Add(ui.Id, IsoPngMask.Read(uiImage));
            foreach (var c in ui.Rows!) pack.clips.Add((ui.Id, c.Key), c);
            loaded.Add(ui);
        }
        if (loaded.Count > 128) throw new InvalidDataException("Iso sheets including Core/shared UI exceed 128.");
        pack.Manifest = new(1, "iso2to1", loaded.AsReadOnly(), furniture.AsReadOnly(), characters.AsReadOnly(),
            tiles.AsReadOnly(), walls.AsReadOnly(), pets.AsReadOnly(), props.AsReadOnly(), ["iso2to1", "renderItemsV1"]);
        pack.Layout = IsoLayoutLoader.Default(pack);
        return pack;
    }
    private static void RequireUnique(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids) if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) throw new InvalidDataException("Duplicate/empty iso ID.");
    }
    private static bool Integer(PixelPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X == Math.Floor(p.X) && p.Y == Math.Floor(p.Y);
    private static void ValidateSheet(IsoSheet s)
    {
        if (string.IsNullOrWhiteSpace(s.Id) || s.Kind is not ("character" or "blink" or "floor" or "wall" or "furniture" or "pet" or "prop" or "shadow" or "overlay") ||
            s.Projection is not ("iso2to1" or "any") || s.Scale != 1 || s.CellW is < 1 or > 512 || s.CellH is < 1 or > 512 ||
            s.Anchor == null || !Integer(s.Anchor) || s.Rows == null || s.Rows.Count is 0 or > 512)
            throw new InvalidDataException("Invalid iso sheet: " + s.Id);
        if (s.Kind is "character" or "blink" && (s.CellW != 32 || s.CellH != 64 || s.Anchor != new PixelPoint(16, 60) ||
            s.FootAnchor != new PixelPoint(16, 60))) throw new InvalidDataException("Iso rig must retain 32x64/(16,60).");
        // identityPalette is provenance for characters/blink/pets. Environment identity colors are ignored.
        foreach (var c in s.Rows)
        {
            if (string.IsNullOrWhiteSpace(c.Name) || c.Direction is not ("se" or "sw" or "ne" or "nw" or "any") ||
                !IsoSharedUi.Supports(c.Effect) || c.Row < 0 || c.Column < 0 || c.Frames is < 1 or > 256 || !double.IsFinite(c.Fps) || c.Fps is <= 0 or > 60 ||
                c.SeatProfile is not (null or "front") || c.IsoZOrder is < -512 or > 511 ||
                new[] { c.Anchor, c.FootOffset, c.SeatAnchor, c.EdgeAnchor }.Any(p => p != null && !Integer(p)) ||
                c.DepthAnchor is {} d && (!double.IsFinite(d.I) || !double.IsFinite(d.J)))
                throw new InvalidDataException("Invalid iso clip: " + c.Key);
            if (s.Kind == "character" && c.Name is "sit-down" or "stand-up")
            {
                if (c.PhaseContacts == null || c.PhaseContacts.Count != 4) throw new InvalidDataException("Transitions require four phaseContacts.");
                for (var k = 0; k < 4; k++)
                {
                    var expected = IsoContacts.Lowering(c.Name == "sit-down" ? k : 3 - k); var actual = c.PhaseContacts[k];
                    if (actual.SeatAnchor != new PixelPoint(expected.Anchor.X, expected.Anchor.Y) ||
                        actual.ContactOffset != new PixelPoint(expected.Offset.X, expected.Offset.Y) || actual.FootY != expected.FootY)
                        throw new InvalidDataException("Transition differs from the frozen planted-sole schedule.");
                }
            }
            if (s.Kind == "character" && c.Name == "seated-typing-front")
            {
                var expected = IsoContacts.Desk(0, 0, IsoGeometry.Rotation(c.DeskOrientation ?? throw new InvalidDataException("Typing needs deskOrientation.")));
                var ground = expected.Pelvis; var left = new PixelPoint(expected.HandLeft.X - ground.X + 16, expected.HandLeft.Y - ground.Y + 44);
                var right = new PixelPoint(expected.HandRight.X - ground.X + 16, expected.HandRight.Y - ground.Y + 44);
                if (c.Direction != expected.Facing || c.SeatAnchor != new PixelPoint(16, 44) || c.HandAnchors == null || !c.HandAnchors.SequenceEqual(new[] { left, right }))
                    throw new InvalidDataException("Typing contact differs from frozen S0.");
            }
            if (c.PhaseContacts is {} phases && (phases.Count != 4 || phases.Any(p => !Integer(p.SeatAnchor) || !Integer(p.ContactOffset))))
                throw new InvalidDataException("Invalid transition contacts.");
        }
    }
    private void ExpandAliases(IsoSheet sheet)
    {
        if ((sheet.Aliases ?? []).Any(a => a == null || a.Target == null || string.IsNullOrWhiteSpace(a.Name) || clips.ContainsKey((sheet.Id, a.Name + "/" + a.Direction))))
            throw new InvalidDataException("Alias must have a distinct name and a valid target.");
        var aliases = (sheet.Aliases ?? []).ToDictionary(a => a.Name + "/" + a.Direction, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        IsoClip Resolve(string key)
        {
            if (clips.TryGetValue((sheet.Id, key), out var c)) return c;
            if (!aliases.TryGetValue(key, out var a) || !visiting.Add(key) || a.Target.Direction != a.Direction || a.SeatProfile is not (null or "front") || !IsoSharedUi.Supports(a.Effect))
                throw new InvalidDataException("Invalid/cyclic iso alias: " + key);
            var target = Resolve(a.Target.Name + "/" + a.Target.Direction);
            if (a.Frame is {} f && (f < 0 || f >= target.Frames)) throw new InvalidDataException("Alias frame outside target.");
            c = target with { Name = a.Name, Direction = a.Direction, Column = target.Column + (a.Frame ?? 0),
                Frames = a.Frame != null ? 1 : target.Frames, Loop = a.Frame != null || target.Loop,
                Effect = a.Effect ?? target.Effect, Prop = a.Prop ?? target.Prop, SeatProfile = a.SeatProfile ?? target.SeatProfile };
            visiting.Remove(key); clips.Add((sheet.Id, key), c); return c;
        }
        foreach (var a in aliases.Keys) Resolve(a);
    }
    private void ExpandBlinkAliases(IsoSheet normal)
    {
        if (normal.Blink is not {} blink) return;
        var closed = Sheet(blink.SheetId);
        if (closed.Kind != "blink" || blink.DurationMs != 100 || blink.NormalRows.Count != closed.Rows!.Count ||
            normal.IdentityPalette?.SequenceEqual(closed.IdentityPalette ?? []) != true)
            throw new InvalidDataException("Invalid phase-matched blink sheet.");
        foreach (var pair in clips.Where(p => p.Key.Sheet == normal.Id).ToArray())
        {
            var c = pair.Value; if (c.Direction is not ("se" or "sw")) continue;
            var row = -1;
            for (var k = 0; k < blink.NormalRows.Count; k++) if (blink.NormalRows[k] == c.Row) { row = k; break; }
            if (row < 0) continue;
            clips.TryAdd((closed.Id, c.Key), c with { Row = row });
        }
    }
    private void ValidateFurniture(IsoFurnitureAsset asset)
    {
        if (asset.Variants == null || asset.Variants.Count is 0 or > 64 ||
            asset.Variants.Select(v => (v.Rotation, v.State)).Distinct().Count() != asset.Variants.Count)
            throw new InvalidDataException("Invalid furniture variants.");
        foreach (var v in asset.Variants)
        {
            if (v.Orientation != IsoGeometry.Orientation(v.Rotation) || v.FootprintW is < 1 or > 64 || v.FootprintH is < 1 or > 64 ||
                v.Parts == null || v.Parts.Count == 0 || v.BlockedCells?.Any(t => t.X < 0 || t.Y < 0 || t.X >= v.FootprintW || t.Y >= v.FootprintH) == true ||
                v.SeatProfiles?.Any(s => s.Id == "end" || asset.Id == "iso/desk" && s.Id != "front") == true)
                throw new InvalidDataException("Invalid furniture geometry/profile: " + asset.Id);
            if (v.Points?.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != v.Points?.Count ||
                v.Points?.Any(p => p.Facing is not ("right" or "down" or "up" or "left") ||
                    p.IsoFacing != null && p.IsoFacing != IsoGeometry.Facing(p.Facing) || p.SeatProfile is not (null or "front")) == true)
                throw new InvalidDataException("Invalid iso interaction facing/profile.");
            foreach (var p in v.Parts) ValidatePart(p);
            if (v.Shadow != null) ValidatePart(v.Shadow);
            if (v.Screen != null) ValidatePart(v.Screen);
            if (v.Glow != null) ValidatePart(v.Glow);
            foreach (var s in v.SeatProfiles ?? [])
            {
                if (!Integer(s.ContactOffset) || !double.IsFinite(s.SeatGround.I) || !double.IsFinite(s.SeatGround.J)) throw new InvalidDataException("Invalid seat contact.");
                if (asset.Id == "iso/desk")
                {
                    var expected = IsoContacts.Desk(0, 0, v.Rotation);
                    if (s.Approach != expected.Approach || s.IsoFacing != expected.Facing || s.SeatGround != expected.SeatGround ||
                        s.ContactOffset != new PixelPoint(expected.Pelvis.X, expected.Pelvis.Y) || s.HandContacts == null ||
                        !s.HandContacts.SequenceEqual(new[] { new PixelPoint(expected.HandLeft.X, expected.HandLeft.Y), new PixelPoint(expected.HandRight.X, expected.HandRight.Y) }))
                        throw new InvalidDataException("Desk contact differs from frozen S0.");
                }
            }
        }
    }
    private void ValidatePart(IsoPart p)
    {
        var c = Clip(p.SheetId, p.Animation ?? p.Clip ?? throw new InvalidDataException("Missing part clip."), p.Direction);
        if (p.DepthAnchor is {} d && (!double.IsFinite(d.I) || !double.IsFinite(d.J)) || p.IsoZOrder is < -512 or > 511 ||
            p.Anchor != null && !Integer(p.Anchor) || !double.IsFinite(p.Opacity) || p.Opacity is < 0 or > 1 ||
            p.Frame is {} frame && (frame < 0 || frame >= c.Frames)) throw new InvalidDataException("Invalid part depth/anchor/opacity/frame.");
        ValidateAnchorChecks(Sheet(p.SheetId), c, p.AnchorChecks);
    }
    private void ValidateAnchorChecks(IsoSheet s, IsoClip c, IReadOnlyList<IsoAnchorCheck>? checks)
    {
        foreach (var a in checks ?? [])
        {
            if (a.Radius is < 0 or > 1 || a.X < 0 || a.X >= s.CellW || a.Y < 0 || a.Y >= s.CellH) throw new InvalidDataException("Invalid anchor check.");
            for (var frame = 0; frame < c.Frames; frame++)
            {
                var found = false;
                for (var dy = -a.Radius; dy <= a.Radius; dy++) for (var dx = -a.Radius; dx <= a.Radius; dx++)
                    if (a.X + dx >= 0 && a.X + dx < s.CellW && a.Y + dy >= 0 && a.Y + dy < s.CellH &&
                        masks[s.Id].Alpha((c.Column + frame) * s.CellW + a.X + dx, c.Row * s.CellH + a.Y + dy) > 0) found = true;
                if (!found) throw new InvalidDataException("Unresolved physical anchor: " + s.Id + "/" + c.Key);
            }
        }
    }
    internal CharacterDesign Assign(AgentInfo agent, IEnumerable<string> used)
    {
        var eligible = Characters.Where(c => (c.Provider == null || c.Provider == agent.Provider.ToString().ToLowerInvariant()) &&
            (c.Role == null || c.Role == (agent.IsSubagent ? "worker" : "supervisor"))).ToArray();
        if (eligible.Length == 0) throw new InvalidDataException("No iso character is eligible for this agent.");
        var start = (int)(SpriteAtlas.Hash(agent.Id) % eligible.Length); var assigned = used.ToHashSet(StringComparer.Ordinal);
        for (var k = 0; k < eligible.Length; k++) if (!assigned.Contains(eligible[(start + k) % eligible.Length].Id)) return eligible[(start + k) % eligible.Length];
        return eligible[start];
    }
    public IsoSheet Sheet(string id) => sheets.TryGetValue(id, out var s) ? s : throw new InvalidDataException("Unknown iso sheet: " + id);
    public IsoClip Clip(string sheetId, string key) => clips.TryGetValue((sheetId, key), out var c) ? c :
        clips.TryGetValue((sheetId, key + "/any"), out c) ? c : throw new InvalidDataException("Unknown iso clip: " + sheetId + "/" + key);
    public IsoClip Clip(string sheetId, string name, string direction) => clips.TryGetValue((sheetId, name + "/" + direction), out var c) ? c : Clip(sheetId, name + "/any");
    public IsoFurnitureVariant Variant(string assetId, int rotation, string state = "default") =>
        Manifest.Furniture?.FirstOrDefault(a => a.Id == assetId)?.Variants.FirstOrDefault(v => v.Rotation == rotation && v.State == state)
        ?? throw new InvalidDataException("Unknown iso furniture variant: " + assetId + "/" + rotation + "/" + state);
    public SpriteFrame Frame(string sheetId, string clip, int frame)
    {
        var s = Sheet(sheetId); var c = Clip(sheetId, clip); frame = Math.Clamp(frame, 0, c.Frames - 1);
        var a = c.Anchor ?? c.EdgeAnchor ?? s.Anchor; var column = c.Column + frame;
        return new(s.Id, c.Row, column, column * s.CellW, c.Row * s.CellH, s.CellW, s.CellH, a.X, a.Y, 1,
            c.FootOffset?.X ?? 0, c.FootOffset?.Y ?? 0);
    }
    public SpriteFrame Frame(IsoDrawItem item) => Frame(item.SheetId, item.Clip, item.Frame);
    public bool Opaque(IsoDrawItem item, int x, int y)
    {
        var s = Sheet(item.SheetId); if (x < 0 || x >= s.CellW || y < 0 || y >= s.CellH || item.Alpha < 32) return false;
        var c = Clip(item.SheetId, item.Clip);
        return masks[s.Id].Alpha((c.Column + item.Frame) * s.CellW + x, c.Row * s.CellH + y) * item.Alpha >= 32 * 255;
    }
    internal IsoBounds OpaqueBounds(string sheetId, string clipKey)
    {
        lock (opaqueBounds)
        {
            if (opaqueBounds.TryGetValue((sheetId, clipKey), out var bounds)) return bounds;
            var s = Sheet(sheetId); var c = Clip(sheetId, clipKey); var mask = masks[s.Id];
            var pixels = new bool[s.CellW * s.CellH];
            var left = s.CellW; var top = s.CellH; var right = 0; var bottom = 0;
            for (var frame = 0; frame < c.Frames; frame++)
                for (var y = 0; y < s.CellH; y++) for (var x = 0; x < s.CellW; x++)
                    if (mask.Alpha((c.Column + frame) * s.CellW + x, c.Row * s.CellH + y) >= 32)
                    { pixels[y * s.CellW + x] = true; left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
            opaquePixels[(sheetId, clipKey)] = pixels;
            return opaqueBounds[(sheetId, clipKey)] = new(left, top, right, bottom);
        }
    }
    internal bool AlphaOverlaps(IsoDrawItem a, IsoDrawItem b)
    {
        var ba = OpaqueBounds(a.SheetId, a.Clip); var bb = OpaqueBounds(b.SheetId, b.Clip);
        var l = Math.Max(a.X + ba.Left, b.X + bb.Left); var t = Math.Max(a.Y + ba.Top, b.Y + bb.Top);
        var r = Math.Min(a.X + ba.Right, b.X + bb.Right); var bottom = Math.Min(a.Y + ba.Bottom, b.Y + bb.Bottom);
        var wa = Sheet(a.SheetId).CellW; var wb = Sheet(b.SheetId).CellW;
        bool[] pa, pb;
        lock (opaqueBounds) { pa = opaquePixels[(a.SheetId, a.Clip)]; pb = opaquePixels[(b.SheetId, b.Clip)]; }
        for (var y = t; y < bottom; y++) for (var x = l; x < r; x++)
            if (pa[(y - a.Y) * wa + x - a.X] && pb[(y - b.Y) * wb + x - b.X]) return true;
        return false;
    }
    /// Pixel-based scheduling avoids redraws for repeated still phases without moving any anchors.
    internal double NextFrameChange(string sheetId, IsoClip clip, double elapsed)
    {
        if (clip.Frames == 1) return double.PositiveInfinity;
        var key = (sheetId, clip.Key);
        if (!changedFrames.TryGetValue(key, out var changes))
        {
            var s = Sheet(sheetId); changes = new bool[clip.Frames];
            for (var k = 0; k < clip.Frames; k++) changes[k] = !masks[s.Id].EqualCells(s.CellW, s.CellH, clip.Row, clip.Column + k,
                clip.Column + (k + 1) % clip.Frames);
            changedFrames.Add(key, changes);
        }
        var tick = Math.Max(0, (long)Math.Floor(elapsed * clip.Fps + 1e-9));
        var current = clip.Loop ? (int)(tick % clip.Frames) : (int)Math.Min(tick, clip.Frames - 1);
        for (var k = 0; k < clip.Frames; k++)
        {
            if (!clip.Loop && current + k >= clip.Frames - 1) break;
            if (changes[(current + k) % clip.Frames]) return Math.Max(0, (tick + k + 1) / clip.Fps - elapsed);
        }
        return double.PositiveInfinity;
    }
    internal bool SamePixels(IsoDrawItem a, IsoDrawItem b)
    {
        if (a.SheetId != b.SheetId) return false;
        var s = Sheet(a.SheetId); var ca = Clip(a.SheetId, a.Clip); var cb = Clip(b.SheetId, b.Clip);
        return masks[s.Id].EqualCells(s.CellW, s.CellH, ca.Row, ca.Column + a.Frame, cb.Row, cb.Column + b.Frame);
    }
    internal int HeadTop(string sheetId, IsoClip c, int frame)
    {
        var s = Sheet(sheetId);
        for (var y = 0; y < s.CellH; y++) for (var x = 0; x < s.CellW; x++)
            if (masks[s.Id].Alpha((c.Column + frame) * s.CellW + x, c.Row * s.CellH + y) > 0) return y;
        return 21;
    }
}
