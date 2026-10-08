using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Officelings.Core;
using Officelings.Core.Providers;
using Xunit;

namespace Officelings.Tests;

public sealed class HardeningTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vdc-hardening-" + Guid.NewGuid().ToString("N"));
    public HardeningTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string Junction(string name, string target)
    {
        var path = Path.GetFullPath(Path.Combine(root, name));
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return path;
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("nested/../../outside.png")]
    [InlineData("sheet.png:stream")]
    [InlineData("C:/outside.png")]
    [InlineData("nested./sheet.png")]
    public void RelativeFilesRejectTraversalAndAmbiguousNames(string name) =>
        Assert.Throws<InvalidDataException>(() => FileBoundary.UnderRoot(root, name));

    [Fact]
    public void JunctionInputsAndOutputsAreRejectedBeforeAccess()
    {
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
        var marker = Path.Combine(outside, "marker"); File.WriteAllText(marker, "keep");
        var link = Junction("alias", outside);
        Assert.Throws<InvalidDataException>(() => FileBoundary.ReadBytes(Path.Combine(link, "marker"), 100));
        Assert.Throws<InvalidDataException>(() => FileBoundary.Exists(Path.Combine(link, "marker")));
        Assert.Throws<InvalidDataException>(() => StateJson.OutputPath(link, Path.Combine(root, "fake-home")));
        Assert.Throws<InvalidDataException>(() => StateJson.WriteAtomic(Path.Combine(link, "marker"), new byte[] { 1 }));
        Assert.Throws<InvalidDataException>(() => FileBoundary.CreateDirectory(Path.Combine(link, "new", "nested")));
        Assert.False(Directory.Exists(Path.Combine(outside, "new")));
        Assert.Equal("keep", File.ReadAllText(marker));
        Directory.Delete(link);
    }

    [Fact]
    public void ExcessivePathDepthIsRejectedBeforeDirectoryCreation()
    {
        var path = Path.Combine(root, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("a", 129)));
        Assert.Throws<InvalidDataException>(() => FileBoundary.CreateDirectory(path));
        Assert.False(Directory.Exists(Path.Combine(root, "a")));
    }

    [Fact]
    public void PackSheetAndManifestJunctionsUseFallback()
    {
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "sheet.png"), SpriteAtlas.CreatePng());
        var pack = Path.Combine(root, "pack"); Directory.CreateDirectory(pack);
        var link = Junction("pack/linked", outside);
        var manifest = new AssetPack().Manifest with
        {
            Sheets = [new("custom", "linked/sheet.png", 16, 24, [new("idle", "any", 0, 1, 1)], new(8, 24))],
            Characters = [new("custom", "custom")]
        };
        File.WriteAllBytes(Path.Combine(pack, "assets.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, StateJson.Options));
        var result = new AssetPack(pack);
        Assert.NotEmpty(result.Warnings); Assert.Single(result.Images);
        Directory.Delete(link);
        var rootLink = Junction("root-link", pack);
        Assert.NotEmpty(new AssetPack(rootLink).Warnings);
        Directory.Delete(rootLink);
    }

    [Fact]
    public void OpenedFileByteBudgetRejectsOversizedInput()
    {
        var path = Path.Combine(root, "large.json");
        using (var stream = File.Create(path)) stream.SetLength(4 * 1024 * 1024 + 1);
        Assert.Throws<InvalidDataException>(() => AssetPack.Read<JsonElement>(path));
    }

    [Fact]
    public void PackAggregatePixelBudgetRejectsBeforeInflation()
    {
        var bytes = SpriteAtlas.CreatePng();
        // A hostile IHDR must not cause the loader to start unbounded inflation.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), 8192);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), 8192);
        File.WriteAllBytes(Path.Combine(root, "sheet.png"), bytes);
        var manifest = new AssetPack().Manifest with
        {
            Sheets = [new("custom", "sheet.png", 16, 24, [new("idle", "any", 0, 1, 1)], new(8, 24))],
            Characters = [new("custom", "custom")]
        };
        File.WriteAllBytes(Path.Combine(root, "assets.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, StateJson.Options));
        Assert.Contains(new AssetPack(root).Warnings, w => w.Contains("pixel budget"));
    }

    [Fact]
    public void IndividuallyValidSheetsCannotExceedAggregatePixelBudget()
    {
        // 128 tiny compressed grayscale sheets: about 34M decoded pixels total.
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            Span<byte> number = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length); png.Write(number);
            var kind = System.Text.Encoding.ASCII.GetBytes(name); png.Write(kind); png.Write(data);
            uint crc = uint.MaxValue;
            foreach (var b in kind.Concat(data)) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); png.Write(number);
        }
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(), 256);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1056); header[8] = 8;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true))
            for (var y = 0; y < 1056; y++) z.Write(new byte[257]);
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        var bytes = png.ToArray(); Assert.Equal((256, 1056), AssetPack.PngSize(bytes));
        File.WriteAllBytes(Path.Combine(root, "valid.png"), bytes);
        var manifest = new AssetPack().Manifest with
        {
            Sheets = Enumerable.Range(0, 128).Select(i => new Sheet("s-" + i, "valid.png", 16, 24, [new("idle", "any", 0, 1, 1)], new(8, 24))).ToArray(),
            Characters = [new("custom", "s-0")]
        };
        File.WriteAllBytes(Path.Combine(root, "assets.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, StateJson.Options));
        var result = new AssetPack(root); Assert.Single(result.Images);
        Assert.Contains(result.Warnings, w => w.Contains("pixel budget"));
    }

    [Fact]
    public void LinkedProviderLogsDoNotEscapeOrCrashPoll()
    {
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "rollout-link.jsonl"), "{\"type\":\"session_meta\",\"payload\":{\"id\":\"external\"}}\n");
        var provider = Path.Combine(root, ".codex"); Directory.CreateDirectory(provider);
        var link = Junction(".codex/sessions", outside);
        using var tracker = new AgentTracker(new() { Home = root });
        Assert.Empty(tracker.Poll(DateTimeOffset.UtcNow)); Assert.NotEmpty(tracker.Warnings);
        Directory.Delete(link);
    }

    [Theory]
    [InlineData(".gemini/antigravity")]
    [InlineData(".claude/sessions")]
    public void LinkedProviderCatalogsDoNotEscapeOrCrashPoll(string name)
    {
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "conversation_summaries.db"), "synthetic malformed database");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, name))!);
        var link = Junction(name, outside);
        using var tracker = new AgentTracker(new() { Home = root });
        Assert.Empty(tracker.Poll(DateTimeOffset.UtcNow)); Assert.NotEmpty(tracker.Warnings);
        Directory.Delete(link);
    }

    [Fact]
    public void JsonlFanoutAndMetadataAreBoundedAcrossPolls()
    {
        var folder = Path.Combine(root, ".codex", "sessions"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "rollout-synthetic.jsonl");
        var now = DateTimeOffset.UtcNow;
        string Line(int i) => JsonSerializer.Serialize(new { type = "session_meta", timestamp = now, payload = new { id = "synthetic-" + i, agent_nickname = new string('n', 2000), cwd = new string('c', 3000), model = new string('m', 2000) } }) + "\n";
        File.WriteAllText(path, string.Concat(Enumerable.Range(0, 400).Select(Line)));
        using var tracker = new AgentTracker(new() { Home = root, ScanInterval = TimeSpan.FromMilliseconds(1) });
        var agents = tracker.Poll(now.AddSeconds(1));
        Assert.InRange(agents.Count, 1, InputLimits.AgentsPerSource);
        Assert.All(agents, a => { Assert.True(a.Name.Length <= 128); Assert.True(a.Cwd!.Length <= 1024); Assert.True(a.Model!.Length <= 128); });
        File.AppendAllText(path, string.Concat(Enumerable.Range(400, 100).Select(Line)));
        Assert.True(tracker.Poll(now.AddSeconds(2)).Count <= InputLimits.AgentsPerSource);
        Assert.True(tracker.BytesRead <= InputLimits.PollBytes * 2L);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:/outside")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void LoopbackMapRejectsPathIds(string id)
    {
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { trajectorySummaries = new Dictionary<string, object> { [id] = new { } } }));
        Assert.Throws<JsonException>(() => AntigravityRuntime.Loaded(input.RootElement));
    }

    [Fact]
    public void LoopbackMapCountIsBounded()
    {
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { trajectorySummaries = Enumerable.Range(0, 2049).ToDictionary(i => "id-" + i, _ => new { }) }));
        Assert.Throws<JsonException>(() => AntigravityRuntime.Loaded(input.RootElement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SqliteOversizedCopiesAndLinksAreRejected(bool memoryOnly)
    {
        var path = Path.Combine(root, "oversized.db");
        using (var stream = File.Create(path)) stream.SetLength(SqliteSnapshot.MaximumCopyBytes + 1);
        Assert.Throws<IOException>(() => new SqliteSnapshot(path, memoryOnly));
        var link = Junction("db-link", root);
        Assert.Throws<InvalidDataException>(() => new SqliteSnapshot(Path.Combine(link, "oversized.db"), memoryOnly));
        Directory.Delete(link);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SqliteViewsTextAndRecursiveWorkAreRejected(bool memoryOnly)
    {
        var path = Path.Combine(root, "hostile.db");
        Assert.Equal(0, SqliteSnapshot.Native.sqlite3_open_v2(path, out var db, 6, IntPtr.Zero));
        try { Assert.Equal(0, sqlite3_exec(db, "CREATE VIEW conversation_summaries AS SELECT hex(zeroblob(32768));", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)); }
        finally { SqliteSnapshot.Native.sqlite3_close(db); }
        using var snapshot = new SqliteSnapshot(path, memoryOnly);
        Assert.Throws<IOException>(() => snapshot.RequireTable("conversation_summaries"));
        Assert.Throws<IOException>(() => snapshot.Query("SELECT hex(zeroblob(8192))"));
        Assert.Throws<IOException>(() => snapshot.Query("SELECT zeroblob(524288)"));
        Assert.Throws<IOException>(() => snapshot.Query("WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<10000000) SELECT sum(x) FROM n"));
        Assert.Equal("1", snapshot.Query("SELECT 1").Single()[0]);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, IntPtr error);
}
