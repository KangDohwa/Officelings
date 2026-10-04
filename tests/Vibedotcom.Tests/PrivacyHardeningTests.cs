using System.Diagnostics;
using Vibedotcom.Core;
using Vibedotcom.Core.Providers;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class PrivacyHardeningTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vdc-private-hardening-" + Guid.NewGuid().ToString("N"));
    public PrivacyHardeningTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData("-wal")]
    [InlineData("-journal")]
    public void PrivateSqliteRejectsAggregateSourceSizeBeforeReadingSidecar(string suffix)
    {
        var path = Path.Combine(root, "source.db");
        File.WriteAllBytes(path, new byte[4096]);
        using (var sidecar = File.Create(path + suffix)) sidecar.SetLength(SqliteSnapshot.MaximumCopyBytes - 4095);
        var error = Assert.Throws<IOException>(() => new SqliteSnapshot(path, memoryOnly: true));
        Assert.Contains("64 MiB", error.Message);
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-journal")]
    public void PrivateSqliteRejectsLinkedSidecars(string suffix)
    {
        var path = Path.Combine(root, "source.db");
        File.WriteAllBytes(path, new byte[4096]);
        var target = Path.Combine(root, "target"); Directory.CreateDirectory(target);
        var link = path + suffix;
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        try { Assert.Throws<InvalidDataException>(() => new SqliteSnapshot(path, memoryOnly: true)); }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void WalRecoveryHonorsTheSharedDeadline() =>
        Assert.Throws<IOException>(() => SqliteMemoryImage.Merge(new byte[4096], [], Stopwatch.GetTimestamp() - 1));

    [Theory]
    [InlineData(PrivacyMode.Off)]
    [InlineData(PrivacyMode.Tasks)]
    [InlineData(PrivacyMode.Full)]
    public void SnapshotKeepsPrivacyAndMetadataBoundsTogether(PrivacyMode mode)
    {
        var privacy = new PrivacyRedactor(mode);
        var agent = new TrackedAgent("codex:synthetic", Provider.Codex, privacy)
        {
            ParentId = new string('p', 1000), Name = new string('n', 1000), Task = new string('t', 1000),
            Role = new string('r', 1000), Model = new string('m', 1000), Cwd = new string('c', 2000),
            Tool = new string('u', 1000), ToolKind = new string('k', 1000), EventState = AgentState.Working,
            LastSeen = DateTimeOffset.UtcNow, LastEvent = DateTimeOffset.UtcNow
        };
        var snapshot = agent.Snapshot(DateTimeOffset.UtcNow, new());
        Assert.Equal(privacy.Id(agent.Id), snapshot.Id);
        Assert.True(snapshot.ParentId!.Length <= 512);
        Assert.True(snapshot.Name.Length <= 128 && snapshot.Task.Length <= 240);
        Assert.True(snapshot.Cwd == null || snapshot.Cwd.Length <= 1024);
        Assert.True(snapshot.Model == null || snapshot.Model.Length <= 128);
        Assert.True(snapshot.ToolKind == null || snapshot.ToolKind.Length <= 128);
        if (mode != PrivacyMode.Off)
        { Assert.Empty(snapshot.Task); Assert.Null(snapshot.Role); Assert.Null(snapshot.Tool); Assert.Null(snapshot.ToolKind); }
    }
}
