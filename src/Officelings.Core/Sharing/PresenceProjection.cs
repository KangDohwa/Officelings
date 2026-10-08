namespace Officelings.Core.Sharing;

public static class PresenceProjection
{
    public static string Platform(Provider provider) => provider switch
    { Provider.Codex => "codex", Provider.Claude => "claude", Provider.Antigravity => "antigravity", _ => "other" };

    // Alias keys are platform:slot, not session identifiers. Slots are numbered within each platform in this snapshot.
    public static IReadOnlyList<PresenceAgent> FromLocal(IReadOnlyList<AgentInfo> source,
        IReadOnlyDictionary<string, string> aliases, string? privacyMode = null)
    {
        var live = source.Where(a => a.State != AgentState.Gone).OrderBy(a => a.IsSubagent)
            .ThenBy(a => a.Provider).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray();
        if (live.Length > PresencePayload.MaxAgents) throw new ArgumentException("Sharing supports at most 64 agents.");
        var parents = new Dictionary<string, int>(StringComparer.Ordinal);
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<PresenceAgent>();
        for (var i = 0; i < live.Length; i++)
        {
            var local = live[i];
            var platform = Platform(local.Provider);
            var slot = slots.GetValueOrDefault(platform) + 1; slots[platform] = slot;
            var alias = PrivacyModes.SharingValue(privacyMode) == "full" ? GenericLabel(platform, slot)
                : aliases.GetValueOrDefault($"{platform}:{slot}") ?? $"Agent {i + 1}";
            int? parent = local.IsSubagent && local.ParentId != null && parents.TryGetValue(local.ParentId, out var p) ? p : null;
            result.Add(new(platform, alias, local.State switch
            { AgentState.Working or AgentState.Thinking => "working", AgentState.Waiting => "waiting", _ => "idle" },
                local.IsSubagent ? "sub" : "supervisor", parent));
            if (!local.IsSubagent) parents.TryAdd(local.Id, i);
        }
        return result;
    }

    public static IReadOnlyList<PresenceAgent> Invented(IReadOnlyDictionary<string, string> aliases, string? privacyMode = null)
    {
        var entries = aliases.Count == 0
            ? new Dictionary<string, string> { ["codex:1"] = "Agent 1", ["claude:1"] = "Agent 2" }
            : aliases;
        var result = new List<PresenceAgent>();
        foreach (var entry in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var platform = ValidateAliasKey(entry.Key);
            var slot = int.Parse(entry.Key[(entry.Key.IndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            result.Add(new(platform, PrivacyModes.SharingValue(privacyMode) == "full" ? GenericLabel(platform, slot) : entry.Value,
                result.Count == 0 ? "working" : "waiting", result.Count == 0 ? "supervisor" : "sub", result.Count == 0 ? null : 0));
        }
        return result;
    }

    private static string GenericLabel(string platform, int slot) => (platform switch
    { "codex" => "Codex", "claude" => "Claude", "antigravity" => "Antigravity", _ => "Other" }) + (slot == 1 ? "" : $" #{slot}");

    internal static bool IsGenericLabel(string platform, string alias)
    {
        var prefix = GenericLabel(platform, 1) + " #";
        return alias.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(alias[prefix.Length..], out var slot)
            && slot is >= 2 and <= 64 && alias == GenericLabel(platform, slot);
    }

    public static string ValidateAliasKey(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 2 || parts[0] is not ("codex" or "claude" or "antigravity" or "other") ||
            !int.TryParse(parts[1], out var slot) || slot is < 1 or > 64 || parts[1] != slot.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new ArgumentException("Alias keys must be platform:slot (codex, claude, antigravity or other; slots 1–64).");
        return parts[0];
    }
}
