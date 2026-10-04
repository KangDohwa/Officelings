using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;
using Xunit;

namespace Vibedotcom.Tests;

[Collection("Iso assets")]
public sealed class IsoEditorObjectTests
{
    private static readonly Lazy<IsoPack> BuiltIn = new(IsoPack.LoadDefault);
    private static readonly LocalIsoCustomObjectImporter Importer = new();
    private static readonly IsoCustomObjectCatalog Empty = new([]);
    private const string GoldenHash = "88d16cbb12cbb96f98521eba4389d19d66789cca5395002227cb9d0dd91b1034";
    private static string Repository => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string Fixtures => Path.Combine(Repository, "tests/fixtures/iso-editor");
    private static string Valid => Path.Combine(Fixtures, "objects/valid/test-crate/test-crate.iso.json");
    private static void Error(IEnumerable<IsoValidationIssue> issues, string rule) =>
        Assert.Contains(issues, i => i.RuleId == rule && i.Severity == IsoIssueSeverity.Error);

    private sealed class Scratch : IDisposable
    {
        internal string Root { get; } = Path.Combine(Repository, "artifacts/objects-temp", Guid.NewGuid().ToString("N"));
        internal string Manifest => Path.Combine(Root, "input/test-crate.iso.json");
        internal IsoEditorPaths Paths => new(Path.Combine(Root, "home"));
        internal Scratch()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Manifest)!);
            File.Copy(Valid, Manifest); File.Copy(Path.Combine(Path.GetDirectoryName(Valid)!, "crate.png"), Path.Combine(Root, "input/crate.png"));
        }
        internal void Change(Action<JsonNode> change)
        {
            var node = JsonNode.Parse(File.ReadAllBytes(Manifest))!; change(node);
            File.WriteAllBytes(Manifest, JsonSerializer.SerializeToUtf8Bytes(node));
        }
        internal IsoCustomObjectResult Import(IsoCustomObjectCatalog? catalog = null) =>
            Importer.Import(new(Manifest, Paths, catalog ?? Empty, BuiltIn.Value));
        public void Dispose()
        {
            var allowed = Path.GetFullPath(Path.Combine(Repository, "artifacts/objects-temp")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(allowed, StringComparison.Ordinal)) throw new InvalidOperationException("Scratch escaped the worktree.");
            Directory.Delete(Root, recursive: true);
        }
    }
    private static JsonNode Variant(JsonNode n) => n["furniture"]![0]!["variants"]![0]!;
    private static JsonNode Clip(JsonNode n) => n["sheets"]![0]!["rows"]![0]!;
    private static JsonNode Part(JsonNode n) => Variant(n)["parts"]![0]!;

    [Fact]
    public void O001_ClosedManifestSchema()
    {
        using var s = new Scratch(); s.Change(n => n["script"] = "file:///private/name");
        var issues = Importer.Validate(s.Manifest); Error(issues, "O001");
        Assert.All(issues, i => Assert.DoesNotContain("private/name", i.Message));
        File.WriteAllText(s.Manifest, "{\"version\":1,\"version\":1}"); Error(Importer.Validate(s.Manifest), "O001");
        File.WriteAllText(s.Manifest, new string('[', 65) + "0" + new string(']', 65)); Error(Importer.Validate(s.Manifest), "O001");
        Assert.Equal("Unsupported custom object manifest.", issues.Single(i => i.RuleId == "O001").Message);
    }
    [Theory]
    [InlineData("aliases")]
    [InlineData("blink")]
    [InlineData("characters")]
    [InlineData("phaseContacts")]
    [InlineData("seatGroupId")]
    [InlineData("hitTargetId")]
    public void O001_RejectsBehavioralFieldsAtEveryBoundary(string field)
    {
        using var s = new Scratch();
        s.Change(n => { if (field == "characters") n[field] = new JsonArray(); else if (field is "aliases" or "blink") n["sheets"]![0]![field] = new JsonArray(); else Part(n)[field] = "owner"; });
        Error(Importer.Validate(s.Manifest), "O001");
    }
    [Fact]
    public void O002_UserNamespaceAndIdConflict()
    {
        using var s = new Scratch(); var installed = s.Import(); Assert.True(installed.Succeeded);
        s.Change(n => n["displayName"] = "Changed source");
        var rejected = s.Import(installed.Catalog); Assert.False(rejected.Succeeded); Error(rejected.Issues, "O002"); Assert.Equal(installed.Catalog, rejected.Catalog);
        s.Change(n => n["id"] = "iso/desk"); Error(Importer.Validate(s.Manifest), "O002");
    }
    [Theory]
    [InlineData("../crate.png")]
    [InlineData("./crate.png")]
    [InlineData("sub//crate.png")]
    [InlineData("C:/crate.png")]
    [InlineData("//server/share.png")]
    [InlineData("crate.png:stream")]
    [InlineData("https://example.invalid/a.png")]
    [InlineData("NUL.png")]
    [InlineData("aux/file.png")]
    [InlineData("COM1.png")]
    [InlineData("crate.png.")]
    [InlineData("crate.png ")]
    public void O003_RegularContainedPathsAndRaceChecks(string file)
    {
        using var s = new Scratch(); s.Change(n => n["sheets"]![0]!["file"] = file);
        Error(Importer.Validate(s.Manifest), "O003"); Assert.False(Directory.Exists(s.Paths.RootDirectory));
    }
    [Fact]
    public void O003_OpenWriterCannotReplaceCapturedBytes()
    {
        using var s = new Scratch(); using var writer = new FileStream(s.Manifest, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        Error(Importer.Validate(s.Manifest), "O003");
    }
    [Fact]
    public void O003_ReparseAncestorIsRejectedForInputAndOutput()
    {
        using var s = new Scratch();
        var link = Path.Combine(s.Root, "junction"); var target = Path.Combine(s.Root, "input");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"", CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        try
        {
            Error(Importer.Validate(Path.Combine(link, "test-crate.iso.json")), "O003");
            var result = Importer.Import(new(s.Manifest, new IsoEditorPaths(link), Empty, BuiltIn.Value));
            Assert.False(result.Succeeded); Error(result.Issues, "O003"); Assert.False(Directory.Exists(Path.Combine(target, "iso-objects")));
        }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public void O004_InputCountDimensionAndByteLimits()
    {
        using var s = new Scratch(); s.Change(n => Clip(n)["frames"] = 17); Error(Importer.Validate(s.Manifest), "O004");
        File.WriteAllBytes(s.Manifest, new byte[4 * 1024 * 1024 + 1]); Error(Importer.Validate(s.Manifest), "O004");
        Error(Importer.Validate(Path.Combine(Fixtures, "objects/invalid/pixel-bomb/test-crate.iso.json")), "O004");
    }
    [Fact]
    public void O005_BoundedPngDecodeAndAlpha()
    {
        using var s = new Scratch(); var path = Path.Combine(s.Root, "input/crate.png"); var png = File.ReadAllBytes(path);
        png[^1] ^= 1; File.WriteAllBytes(path, png); Error(Importer.Validate(s.Manifest), "O005");
        var decoded = ObjectPng.Decode(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Valid)!, "crate.png")));
        var at = Enumerable.Range(0, decoded.Width * decoded.Height).First(i => decoded.Rgba[i * 4 + 3] == 255);
        decoded.Rgba[at * 4 + 3] = 128; File.WriteAllBytes(path, ObjectPng.Encode(decoded.Width, decoded.Height, decoded.Rgba)); Error(Importer.Validate(s.Manifest), "O005");
        decoded.Rgba[at * 4 + 3] = 0; decoded.Rgba[at * 4] = 99;
        File.WriteAllBytes(path, ObjectPng.Encode(decoded.Width, decoded.Height, decoded.Rgba)); Error(Importer.Validate(s.Manifest), "O005");
    }
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void O005_RgbRgbaAndIndexedResolveLosslessly(int color)
    {
        var image = ObjectPng.Decode(Png(color, color == 3 ? [0] : color == 2 ? [76, 42, 35] : [76, 42, 35, 255]));
        Assert.Equal(new byte[] { 76, 42, 35, 255 }, image.Rgba);
    }
    [Fact]
    public void O005_ShadowAlphaAndPaletteStaySeparateFromObjectMasks()
    {
        using var s = new Scratch();
        s.Change(n =>
        {
            var sheet = n["sheets"]![0]!.DeepClone(); sheet["id"] = "user/test-crate/shadow"; sheet["kind"] = "shadow";
            sheet["file"] = "shadow.png"; sheet["rows"]![0]!["partId"] = "test-crate__shadow__se__00";
            n["sheets"]!.AsArray().Add(sheet);
            var shadow = Part(n).DeepClone(); shadow["sheetId"] = "user/test-crate/shadow"; shadow["partId"] = "test-crate__shadow__se__00";
            Variant(n)["shadow"] = shadow;
        });
        var rgba = new byte[64 * 80 * 4]; var at = (32 * 64 + 32) * 4;
        rgba[at] = 17; rgba[at + 1] = 25; rgba[at + 2] = 36; rgba[at + 3] = 48;
        var file = Path.Combine(s.Root, "input/shadow.png"); File.WriteAllBytes(file, ObjectPng.Encode(64, 80, rgba));
        Assert.DoesNotContain(Importer.Validate(s.Manifest), i => i.Severity == IsoIssueSeverity.Error);
        var installed = s.Import(); Assert.True(installed.Succeeded);
        Assert.NotNull(Importer.Merge(BuiltIn.Value, installed.Catalog).Pack!.Variant("user/test-crate", 0).Shadow);
        rgba[at + 3] = 255; File.WriteAllBytes(file, ObjectPng.Encode(64, 80, rgba)); Error(Importer.Validate(s.Manifest), "O005");
        rgba[at + 3] = 96; rgba[at] = 99; File.WriteAllBytes(file, ObjectPng.Encode(64, 80, rgba)); Error(Importer.Validate(s.Manifest), "O008");
    }
    [Fact]
    public void O005_RejectsCompressedExcessUnknownChunksAndChunkBounds()
    {
        Assert.Throws<InvalidDataException>(() => ObjectPng.Decode(Png(6, [76, 42, 35, 255, 0])));
        Assert.Throws<InvalidDataException>(() => ObjectPng.Decode(Png(6, [76, 42, 35, 255], "acTL")));
        Assert.Throws<InvalidDataException>(() => ObjectPng.Decode(Png(6, [76, 42, 35, 255], "tEXt")));
        var png = Png(6, [76, 42, 35, 255]); BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(33), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => ObjectPng.Decode(png));
        Assert.Throws<InvalidDataException>(() => ObjectPng.Decode(Png(6, [76, 42, 35, 255], trailing: true)));
    }
    private static byte[] Png(int color, byte[] row, string? extra = null, bool trailing = false)
    {
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            var buffer = new byte[data.Length + 12]; BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
            Encoding.ASCII.GetBytes(name).CopyTo(buffer, 4); data.CopyTo(buffer, 8);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(buffer.Length - 4), ObjectPng.Crc(buffer.AsSpan(4, data.Length + 4))); output.Write(buffer);
        }
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, 1); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8; header[9] = (byte)color; Chunk("IHDR", header); if (color == 3) Chunk("PLTE", [76, 42, 35]); if (extra != null) Chunk(extra, new byte[8]);
        using var compressed = new MemoryStream(); using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) { z.WriteByte(0); z.Write(row); }
        var data = compressed.ToArray(); if (trailing) data = [.. data, .. data[^4..]];
        Chunk("IDAT", data); Chunk("IEND", []); return output.ToArray();
    }
    [Fact]
    public void O006_AuthoredGeometryAndNoSeatRig()
    {
        using var s = new Scratch(); s.Change(n => Variant(n)["height"] = 97); Error(Importer.Validate(s.Manifest), "O006");
        s.Change(n => { Variant(n)["height"] = 16; Variant(n)["layer"] = 0; }); Error(Importer.Validate(s.Manifest), "O006");
        s.Change(n => { Variant(n)["layer"] = 1; Variant(n)["points"] = JsonNode.Parse("[{\"id\":\"seat\",\"x\":0,\"y\":0,\"action\":\"typing\"}]"); }); Error(Importer.Validate(s.Manifest), "O006");
        s.Change(n => { Variant(n)["points"] = new JsonArray(); Variant(n)["seatProfiles"] = new JsonArray(); }); Error(Importer.Validate(s.Manifest), "O001");
    }
    [Theory]
    [InlineData("height", "1e999")]
    [InlineData("footprintW", "1.5")]
    [InlineData("rotation", "0.5")]
    public void O006_FractionalAndNonfiniteGeometryReportsGeometryRule(string field, string value)
    {
        using var s = new Scratch(); s.Change(n => Variant(n)[field] = JsonNode.Parse(value)); Error(Importer.Validate(s.Manifest), "O006");
    }
    [Fact]
    public void O007_StablePartIdsAcrossStatesAndLargeTransparentAnchorsArePreserved()
    {
        using var s = new Scratch();
        s.Change(n =>
        {
            var c = Clip(n).DeepClone(); c["name"] = "body-off"; n["sheets"]![0]!["rows"]!.AsArray().Add(c);
            var v = Variant(n).DeepClone(); v["state"] = "off"; v["parts"]![0]!["animation"] = "body-off";
            n["furniture"]![0]!["variants"]!.AsArray().Add(v);
            n["sheets"]![0]!["anchor"]!["x"] = int.MinValue;
        });
        Assert.DoesNotContain(Importer.Validate(s.Manifest), i => i.Severity == IsoIssueSeverity.Error);
        var result = s.Import(); Assert.True(result.Succeeded);
        var manifest = result.Object!.Manifest;
        Assert.IsType<ImmutableArray<IsoClip>>(manifest.Sheets[0].Rows);
        Assert.IsType<ImmutableArray<IsoAnchorCheck>>(manifest.Sheets[0].Rows![0].AnchorChecks);
        Assert.Equal(int.MinValue, manifest.Sheets[0].Anchor.X);
        Assert.Equal(2, manifest.Furniture[0].Variants.Length);
        var pack = Importer.Merge(BuiltIn.Value, result.Catalog).Pack!;
        Assert.Equal(16, pack.Variant("user/test-crate", 0, "off").HeightPx);
    }
    [Fact]
    public void O007_PartsAnchorsDepthAndDisjointMasks()
    {
        using var s = new Scratch(); s.Change(n => Part(n)["isoZOrder"] = 1); Error(Importer.Validate(s.Manifest), "O007");
        s.Change(n => { Part(n)["isoZOrder"] = 0; Clip(n)["anchorChecks"]![0]!["x"] = 0; Clip(n)["anchorChecks"]![0]!["y"] = 0; }); Error(Importer.Validate(s.Manifest), "O007");
        File.Copy(Valid, s.Manifest, true);
        s.Change(n =>
        {
            var c = Clip(n).DeepClone(); c["name"] = "duplicate"; c["partId"] = "test-crate__front__se__01"; n["sheets"]![0]!["rows"]!.AsArray().Add(c);
            var p = Part(n).DeepClone(); p["animation"] = "duplicate"; p["partId"] = "test-crate__front__se__01"; Variant(n)["parts"]!.AsArray().Add(p);
        }); Error(Importer.Validate(s.Manifest), "O007");
    }
    [Fact]
    public void O008_DeclaredPaletteErrorAndFrozenPaletteWarning()
    {
        using var s = new Scratch(); var file = Path.Combine(s.Root, "input/crate.png"); var png = ObjectPng.Decode(File.ReadAllBytes(file));
        var at = Enumerable.Range(0, png.Width * png.Height).First(i => png.Rgba[i * 4 + 3] == 255) * 4;
        png.Rgba[at] = 1; png.Rgba[at + 1] = 2; png.Rgba[at + 2] = 3; File.WriteAllBytes(file, ObjectPng.Encode(png.Width, png.Height, png.Rgba)); Error(Importer.Validate(s.Manifest), "O008");
        s.Change(n => n["palette"]!.AsArray().Add("#010203")); var issues = Importer.Validate(s.Manifest);
        Assert.DoesNotContain(issues, i => i.Severity == IsoIssueSeverity.Error);
        Assert.Equal("Custom colors differ from the built-in frozen palette.", issues.Single(i => i.RuleId == "O008").Message);
        var installed = s.Import(); Assert.True(installed.Succeeded); Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Directory.GetFiles(installed.Object!.InstalledDirectory, "sheet-*.png").Single()));
    }
    [Fact]
    public void O009_AtomicInstallAndContentHash()
    {
        using var s = new Scratch(); File.Copy(Valid, s.Manifest, true); var result = s.Import(); Assert.True(result.Succeeded); Assert.Equal(GoldenHash, result.Object!.ContentHash);
        Assert.Equal(File.ReadAllBytes(Valid), File.ReadAllBytes(Path.Combine(result.Object.InstalledDirectory, "source.iso.json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Valid)!, "crate.png")), File.ReadAllBytes(Directory.GetFiles(result.Object.InstalledDirectory, "sheet-*.png").Single()));
        var thumbnail = ObjectPng.Decode(File.ReadAllBytes(result.Object.ThumbnailFile)); Assert.Equal((128, 128), (thumbnail.Width, thumbnail.Height));
        var before = Directory.GetFiles(s.Paths.RootDirectory, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => (File.GetLastWriteTimeUtc(f), File.ReadAllBytes(f)));
        Assert.True(s.Import(result.Catalog).Succeeded);
        foreach (var (path, value) in before) { Assert.Equal(value.Item1, File.GetLastWriteTimeUtc(path)); Assert.Equal(value.Item2, File.ReadAllBytes(path)); }
        var loaded = Importer.LoadCatalog(s.Paths); Assert.True(loaded.Succeeded); Assert.Equal(GoldenHash, Assert.Single(loaded.Catalog.Objects).ContentHash);
        Assert.DoesNotContain(s.Root, File.ReadAllText(Path.Combine(s.Paths.RootDirectory, "iso-objects/catalog.json")));
    }
    [Fact]
    public void O009_FailedPublicationRetainsAuthoritativeCatalogAndCleansStage()
    {
        using var s = new Scratch(); Directory.CreateDirectory(Path.Combine(s.Paths.RootDirectory, "iso-objects/catalog.json"));
        var result = s.Import(); Assert.False(result.Succeeded); Error(result.Issues, "O009"); Assert.Empty(result.Catalog.Objects);
        Assert.Empty(Directory.GetDirectories(Path.Combine(s.Paths.RootDirectory, "iso-objects"), "stage-*"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(s.Paths.RootDirectory, "iso-objects/objects")));
    }
    [Fact]
    public void O009_CancellationBeforeCommitLeavesNoOutput()
    {
        using var s = new Scratch(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var importer = new LocalIsoCustomObjectImporter(cancellation.Token);
        var result = importer.Import(new(s.Manifest, s.Paths, Empty, BuiltIn.Value));
        Assert.False(result.Succeeded); Error(result.Issues, "O009"); Assert.False(Directory.Exists(s.Paths.RootDirectory));
        Error(importer.Validate(s.Manifest), "O009");
    }
    [Fact]
    public void O009_FailedCatalogReplacePreservesPreviousBytesAndRemovesUnreferencedDirectory()
    {
        using var s = new Scratch(); var first = s.Import(); Assert.True(first.Succeeded);
        var catalogFile = Path.Combine(s.Paths.RootDirectory, "iso-objects/catalog.json"); var before = File.ReadAllBytes(catalogFile);
        Directory.CreateDirectory(catalogFile + ".bak");
        s.Change(n =>
        {
            n["id"] = "user/test-second"; n["furniture"]![0]!["id"] = "user/test-second";
            n["sheets"]![0]!["id"] = "user/test-second/body";
            Clip(n)["partId"] = "test-second__ground__se__00"; Part(n)["partId"] = "test-second__ground__se__00";
            Part(n)["sheetId"] = "user/test-second/body";
        });
        var rejected = s.Import(first.Catalog); Assert.False(rejected.Succeeded); Error(rejected.Issues, "O009");
        Assert.Equal(before, File.ReadAllBytes(catalogFile));
        Assert.Single(Directory.GetDirectories(Path.Combine(s.Paths.RootDirectory, "iso-objects/objects")));
        Assert.Equal(first.Object!.ContentHash, Assert.Single(Importer.LoadCatalog(s.Paths).Catalog.Objects).ContentHash);
    }
    [Fact]
    public void O010_WholePackLimitsAndBuiltInPreservation()
    {
        using var s = new Scratch(); var imported = s.Import(); Assert.True(imported.Succeeded);
        var original = BuiltIn.Value; var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(original.Manifest, StateJson.Options));
        var imageHashes = original.Images.ToDictionary(p => p.Key, p => SHA256.HashData(p.Value), StringComparer.Ordinal);
        var result = Importer.Merge(original, imported.Catalog); Assert.True(result.Succeeded); var merged = result.Pack!;
        Assert.Equal(hash, SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(original.Manifest, StateJson.Options)));
        Assert.Equal(original.Characters, merged.Characters); Assert.Same(original.Manifest.WallModules, merged.Manifest.WallModules);
        foreach (var sheet in original.Manifest.Sheets)
        {
            Assert.Same(sheet, merged.Sheet(sheet.Id)); Assert.Equal(original.Images[sheet.Id], merged.Images[sheet.Id]);
            Assert.Equal(imageHashes[sheet.Id], SHA256.HashData(original.Images[sheet.Id]));
            Assert.Equal(imageHashes[sheet.Id], SHA256.HashData(merged.Images[sheet.Id]));
            Assert.Equal(SHA256.HashData(original.Images[sheet.Id]), SHA256.HashData(merged.Images[sheet.Id]));
            foreach (var clip in sheet.Rows ?? []) Assert.Equal(original.Clip(sheet.Id, clip.Key), merged.Clip(sheet.Id, clip.Key));
            foreach (var clip in sheet.Aliases ?? []) Assert.Equal(original.Clip(sheet.Id, clip.Name, clip.Direction), merged.Clip(sheet.Id, clip.Name, clip.Direction));
        }
        Assert.Equal(original.Images.Count + 1, merged.Images.Count); Assert.Equal(original.Manifest.Furniture!.Count + 2, merged.Manifest.Furniture!.Count);
        var limited = original.WithCustomObjects([]);
        typeof(IsoPack).GetProperty(nameof(IsoPack.Manifest))!.SetValue(limited, original.Manifest with { Sheets = Enumerable.Repeat(original.Manifest.Sheets[0], 129).ToArray() }); Error(Importer.Merge(limited, Empty).Issues, "O010");
        typeof(IsoPack).GetProperty(nameof(IsoPack.Manifest))!.SetValue(limited, original.Manifest with { Sheets = [original.Manifest.Sheets[0] with { Width = 8192, Height = 8192 }] }); Error(Importer.Merge(limited, Empty).Issues, "O010");
        Assert.False(Importer.Merge(merged, imported.Catalog).Succeeded);
        Error(Importer.Merge(original, new(Enumerable.Repeat(imported.Object!, 65).ToImmutableArray())).Issues, "O010");
    }
    [Fact]
    public void O011_ReferencedRemoval()
    {
        using var s = new Scratch(); var result = s.Import(); Assert.True(result.Succeeded);
        var rejected = Importer.Remove(new("user/test-crate", s.Paths, result.Catalog, ["user/test-crate"])); Assert.False(rejected.Succeeded); Error(rejected.Issues, "O011"); Assert.True(Directory.Exists(result.Object!.InstalledDirectory));
        var removed = Importer.Remove(new("user/test-crate", s.Paths, result.Catalog, [])); Assert.True(removed.Succeeded); Assert.Empty(Importer.LoadCatalog(s.Paths).Catalog.Objects); Assert.False(Directory.Exists(result.Object.InstalledDirectory));
        Assert.True(File.Exists(Path.Combine(s.Paths.RootDirectory, "iso-objects/catalog.json.bak"))); Assert.True(File.Exists(s.Manifest)); Error(Importer.Remove(new("iso/desk", s.Paths, Empty, [])).Issues, "O011");
    }
    [Fact]
    public void O011_UnsafeCleanupDoesNotDropCatalogEntry()
    {
        using var s = new Scratch(); var installed = s.Import(); Assert.True(installed.Succeeded);
        Directory.CreateDirectory(Path.Combine(installed.Object!.InstalledDirectory, "unexpected"));
        var removed = Importer.Remove(new("user/test-crate", s.Paths, installed.Catalog, []));
        Assert.False(removed.Succeeded); Error(removed.Issues, "O011"); Assert.Single(Importer.LoadCatalog(s.Paths).Catalog.Objects);
    }
    [Fact]
    public void Adapter_ComputerMatchesDeskPartIdsAndSuppressesOnlyMonitor()
    {
        var pack = Importer.Merge(BuiltIn.Value, Empty).Pack!; var desk = pack.Manifest.Furniture!.Single(f => f.Id == "iso/desk");
        foreach (var v in desk.Variants)
        {
            var computer = pack.Variant("iso/computer", v.Rotation, v.State);
            Assert.Equal((v.FootprintW, v.FootprintH, v.Orientation), (computer.FootprintW, computer.FootprintH, computer.Orientation));
            Assert.Equal(40, computer.HeightPx); Assert.Equal(1, computer.Layer); Assert.Empty(computer.BlockedCells!); Assert.Empty(computer.Points!); Assert.Null(computer.SeatProfiles);
            var expected = v.Parts.Where(p => p.Animation!.StartsWith("monitor-", StringComparison.Ordinal) || p.Animation is "rear-15" or "rear-16").ToArray();
            Assert.Equal(expected, computer.Parts); Assert.NotEmpty(computer.Parts); Assert.Equal(v.Screen, computer.Screen); Assert.Equal(v.Glow, computer.Glow); Assert.DoesNotContain(computer.Parts, p => p.Animation == "rear-14");
            if (computer.Screen is {} screen)
            {
                var originalScreen = v.Screen!;
                Assert.Equal(originalScreen.PartId ?? pack.Clip(originalScreen.SheetId, originalScreen.Animation!, originalScreen.Direction).PartId,
                    screen.PartId ?? pack.Clip(screen.SheetId, screen.Animation!, screen.Direction).PartId);
                Assert.NotNull(pack.Clip(screen.SheetId, screen.Animation!, screen.Direction).PartId);
            }
        }
    }
    [Fact]
    public void Catalog_BadInstalledObjectAndMergeFailureRetainLastGoodPack()
    {
        using var s = new Scratch(); var result = s.Import(); Assert.True(result.Succeeded); File.WriteAllText(Path.Combine(result.Object!.InstalledDirectory, "source.iso.json"), "{}");
        var loaded = Importer.LoadCatalog(s.Paths); Assert.False(loaded.Succeeded); Assert.Empty(loaded.Catalog.Objects); Error(loaded.Issues, "O001");
        var merged = Importer.Merge(BuiltIn.Value, result.Catalog); Assert.False(merged.Succeeded); Assert.Null(merged.Pack); Assert.Equal(12, BuiltIn.Value.Manifest.Furniture!.Count);
    }
    [Fact]
    public void Catalog_RejectsInstalledMetadataChangedBeyondDerivedFilenames()
    {
        using var s = new Scratch(); var result = s.Import(); Assert.True(result.Succeeded);
        var file = Path.Combine(result.Object!.InstalledDirectory, "object.iso.json"); var n = JsonNode.Parse(File.ReadAllBytes(file))!; n["displayName"] = "tampered"; File.WriteAllText(file, n.ToJsonString());
        var loaded = Importer.LoadCatalog(s.Paths); Assert.False(loaded.Succeeded); Error(loaded.Issues, "O009"); Assert.Empty(loaded.Catalog.Objects);
    }
    [Fact]
    public void Fixtures_ValidAndInvalidCustomManifestGoldenCases()
    {
        using var index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "cases.json")));
        foreach (var c in index.RootElement.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "object"))
        {
            var issues = Importer.Validate(Path.Combine(Fixtures, c.GetProperty("path").GetString()!));
            if (c.GetProperty("valid").GetBoolean()) Assert.DoesNotContain(issues, i => i.Severity == IsoIssueSeverity.Error);
            else foreach (var rule in c.GetProperty("expectedRules").EnumerateArray()) Error(issues, rule.GetString()!);
        }
        Assert.Equal(GoldenHash, ObjectSource.Read(Valid, new ObjectIssues())!.Hash);
    }
}
