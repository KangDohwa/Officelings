using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Officelings.Core.Sharing;

// This is deliberately separate from AgentInfo and StateSnapshot. Never serialize either locally rich type here.
public sealed record PresenceAgent(string Platform, string Alias, string State, string Role, int? ParentIndex = null);

public sealed class PresencePayload
{
    public const int MaxAgents = 64, MaxBytes = 8192;
    private readonly byte[] bytes;
    private PresencePayload(byte[] bytes) => this.bytes = bytes;
    public string Json => Encoding.UTF8.GetString(bytes);
    public byte[] CopyBytes() => bytes.ToArray();

    public static PresencePayload Create(string officeId, IReadOnlyList<PresenceAgent> agents, DateTimeOffset timestamp, DateTimeOffset now)
    {
        if (!Guid.TryParseExact(officeId, "D", out var office) || office == Guid.Empty)
            throw new ArgumentException("Sharing office ID must be a nonzero UUID.");
        if (agents.Count > MaxAgents) throw new ArgumentException("Sharing supports at most 64 agents.");
        if ((timestamp - now).Duration() > TimeSpan.FromSeconds(120))
            throw new ArgumentException("Sharing timestamp must be within 120 seconds of UTC now.");
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            // Wire identifier kept for server compatibility.
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("officeId", office.ToString("D"));
            json.WriteStartArray("agents");
            for (var i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent.Platform is not ("codex" or "claude" or "antigravity" or "other") ||
                    agent.State is not ("working" or "idle" or "waiting") || agent.Role is not ("supervisor" or "sub"))
                    throw new ArgumentException("Invalid presence platform, state or role.");
                if (agent.ParentIndex is { } parent && (agent.Role != "sub" || parent < 0 || parent >= i || agents[parent].Role != "supervisor"))
                    throw new ArgumentException("Presence parents must be earlier supervisors.");
                if (agent.Alias == null) throw new ArgumentException("Presence alias must be a string.");
                var alias = PresenceProjection.IsGenericLabel(agent.Platform, agent.Alias) ? agent.Alias : SanitizeAlias(agent.Alias, i + 1);
                json.WriteStartObject();
                json.WriteString("platform", agent.Platform);
                json.WriteString("alias", alias);
                json.WriteString("state", agent.State);
                json.WriteString("role", agent.Role);
                if (agent.ParentIndex is { } index) json.WriteNumber("parentIndex", index);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteNumber("ts", timestamp.ToUnixTimeMilliseconds());
            json.WriteEndObject();
        }
        if (buffer.WrittenCount > MaxBytes) throw new ArgumentException("Presence payload exceeds 8192 UTF-8 bytes.");
        return new(buffer.WrittenSpan.ToArray());
    }

    public static string SanitizeAlias(string alias, int position)
    {
        string normalized;
        try { normalized = alias.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { throw new ArgumentException("Alias must contain valid Unicode."); }
        if (normalized.EnumerateRunes().Count() > 24)
            throw new ArgumentException("Alias must contain at most 24 Unicode scalar values after NFC normalization.");
        var result = new StringBuilder();
        var pendingSpace = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (rune.Value == ' ') { pendingSpace = result.Length > 0; continue; }
            if (!Rune.IsLetterOrDigit(rune) && rune.Value is not ('_' or '-' or '.')) continue;
            if (pendingSpace) result.Append(' ');
            pendingSpace = false;
            result.Append(rune.ToString());
        }
        return result.Length == 0 ? $"Agent {position}" : result.ToString();
    }
}
