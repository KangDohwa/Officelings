using System.Text.Json;
using Vibedotcom.Core;
using Vibedotcom.Core.Sharing;

namespace Vibedotcom.SharingChecks;

// Dependency-free security checks: dotnet run --project tests/Vibedotcom.SharingChecks
internal static partial class Program
{
    internal const string Office = "11111111-1111-4111-8111-111111111111";
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static int passed, failed;
    private static async Task<int> Main()
    {
        PayloadChecks();
        await TransportChecks();
        Console.WriteLine($"Sharing checks: {passed} passed, {failed} failed. Synthetic data; no network.");
        return failed == 0 ? 0 : 1;
    }
    private static Task TransportChecks() => Task.CompletedTask;
    internal static void Check(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.GetType().Name); }
    }
    internal static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Check failed."); }
    internal static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected validation rejection.");
    }
    internal static PresencePayload Payload(IReadOnlyList<PresenceAgent> agents) => PresencePayload.Create(Office, agents, Now, Now);
    internal static AgentInfo Local(string id = "private-session", Provider platform = Provider.Codex, string? parent = null,
        bool sub = false, AgentState state = AgentState.Thinking) => new(id, platform, parent, sub, "private-agent-name",
            "private-role", "private-model", "C:/private-project", "private-prompt", "private-tool-args", "private-tool-result",
            state, Now.AddDays(-1), Now.AddHours(-1), true);

    private static void PayloadChecks()
    {
        Check("exact allowlist excludes every rich local field", () =>
        {
            var agents = PresenceProjection.FromLocal([Local(), Local("child", Provider.Claude, "private-session", true, AgentState.Waiting)],
                new Dictionary<string, string> { ["codex:1"] = "Nova", ["claude:1"] = "Moon" });
            var payload = Payload(agents);
            using var doc = JsonDocument.Parse(payload.CopyBytes());
            Require(doc.RootElement.EnumerateObject().Select(p => p.Name).SequenceEqual(["v", "officeId", "agents", "ts"]));
            var array = doc.RootElement.GetProperty("agents");
            Require(array[0].EnumerateObject().Select(p => p.Name).SequenceEqual(["platform", "alias", "state", "role"]));
            Require(array[1].EnumerateObject().Select(p => p.Name).SequenceEqual(["platform", "alias", "state", "role", "parentIndex"]));
            Require(!payload.Json.Contains("private") && !payload.Json.Contains("child"));
            Require(array[0].GetProperty("state").GetString() == "working" && array[1].GetProperty("parentIndex").GetInt32() == 0);
            Require(doc.RootElement.GetProperty("ts").GetInt64() == Now.ToUnixTimeMilliseconds());
        });
        foreach (var (alias, expected) in new[] { ("  A  B  ", "A B"), ("<A>\u202e\n😀", "A"), ("😀\u202e\n", "Agent 3"), ("e\u0301", "é"), ("한국어_１２-.", "한국어_１２-.") })
            Check("alias sanitization " + expected, () => Require(PresencePayload.SanitizeAlias(alias, 3) == expected));
        Check("NFC before scalar limit", () => Require(PresencePayload.SanitizeAlias(string.Concat(Enumerable.Repeat("e\u0301", 24)), 1) == new string('é', 24)));
        Check("24 astral scalars accepted", () => Require(PresencePayload.SanitizeAlias(string.Concat(Enumerable.Repeat("😀", 24)), 1) == "Agent 1"));
        Check("25 astral scalars rejected before sanitization", () => Reject(() => PresencePayload.SanitizeAlias(string.Concat(Enumerable.Repeat("😀", 25)), 1)));
        Check("25 ASCII scalars rejected", () => Reject(() => PresencePayload.SanitizeAlias(new string('a', 25), 1)));
        Check("invalid Unicode rejected", () => Reject(() => PresencePayload.SanitizeAlias("\ud800", 1)));
        Check("empty snapshot", () => Require(Payload([]).Json.Contains("\"agents\":[]")));
        Check("64 agents accepted", () => Require(Payload(Enumerable.Repeat(new PresenceAgent("codex", "A", "idle", "supervisor"), 64).ToArray()).CopyBytes().Length <= 8192));
        Check("65 agents rejected", () => Reject(() => Payload(Enumerable.Repeat(new PresenceAgent("codex", "A", "idle", "supervisor"), 65).ToArray())));
        Check("8192-byte cap measures UTF-8", () => Reject(() => Payload(Enumerable.Repeat(new PresenceAgent("antigravity", new string('한', 24), "working", "supervisor"), 64).ToArray())));
        foreach (var agent in new[] { new PresenceAgent("codex", "A", "working", "supervisor", 0), new("codex", "A", "idle", "sub", 0),
            new("codex", "A", "waiting", "sub", -1), new("Codex", "A", "working", "supervisor"), new("codex", "A", "thinking", "supervisor"), new("codex", "A", "idle", "worker") })
            Check("invalid enum or parent rejected", () => Reject(() => Payload([agent])));
        Check("sub parent rejected", () => Reject(() => Payload([new("codex", "A", "idle", "sub"), new("codex", "B", "idle", "sub", 0)])));
        Check("future parent rejected", () => Reject(() => Payload([new("codex", "A", "idle", "sub", 1), new("codex", "B", "idle", "supervisor")])));
        Check("orphan sub accepted", () => Require(Payload([new("codex", "A", "idle", "sub")]).CopyBytes().Length > 0));
        foreach (var office in new[] { "00000000-0000-0000-0000-000000000000", "11111111111141118111111111111111", "not-an-office" })
            Check("canonical nonzero office ID", () => Reject(() => PresencePayload.Create(office, [], Now, Now)));
        Check("120-second skew accepted", () => Require(PresencePayload.Create(Office, [], Now.AddSeconds(120), Now).CopyBytes().Length > 0));
        Check("121-second skew rejected", () => Reject(() => PresencePayload.Create(Office, [], Now.AddSeconds(121), Now)));
        Check("full privacy forces generic aliases", () =>
        {
            var aliases = new Dictionary<string, string> { ["codex:1"] = "Chosen Name" };
            Require(PresenceProjection.FromLocal([Local()], aliases, "full")[0].Alias == "Agent 1");
            Require(PresenceProjection.Invented(aliases, "full")[0].Alias == "Agent 1");
        });
        Check("gone excluded and reporting reduced to idle", () =>
        {
            Require(PresenceProjection.FromLocal([Local(state: AgentState.Gone)], new Dictionary<string, string>()).Count == 0);
            Require(PresenceProjection.FromLocal([Local(state: AgentState.Reporting)], new Dictionary<string, string>())[0].State == "idle");
        });
    }
}
