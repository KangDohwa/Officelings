using System.Collections.ObjectModel;
using System.Text.Json;

namespace Officelings.Core.Sharing;

public sealed record SharingConfiguration
{
    public string Sharing { get; init; } = "off";
    public string SharingOfficeId { get; init; } = "";
    public string SharingServer { get; init; } = "";
    public Dictionary<string, string> SharingAliases { get; init; } = [];
    public string PrivacyMode { get; init; } = "full";

    public static SharingConfiguration Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 65536) return new();
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(file);
            return (JsonSerializer.Deserialize<SharingConfiguration>(json, StateJson.Options) ?? new()) with
                { PrivacyMode = PrivacyModes.ReadSetting(json.RootElement) ?? "full" };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
}

public sealed class SharingEndpoint
{
    public Uri Origin { get; }
    public Uri Ingest { get; }
    // Wire identifier kept for server compatibility.
    private SharingEndpoint(Uri origin) { Origin = origin; Ingest = new(origin, "/api/v1/presence"); }
    public static SharingEndpoint Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            uri.AbsolutePath != "/" || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException("Sharing server must be an HTTPS origin, or an HTTP loopback origin, without credentials, path, query or fragment.");
        return new(uri);
    }
}

public sealed class SharingPreview
{
    public SharingEndpoint Endpoint { get; }
    public string OfficeId { get; }
    public IReadOnlyDictionary<string, string> Aliases { get; }
    public string PrivacyMode { get; }
    public DateTimeOffset CreatedAt { get; }
    public PresencePayload Payload { get; }
    private SharingPreview(SharingEndpoint endpoint, string office, Dictionary<string, string> aliases, string privacy, DateTimeOffset now)
    {
        Endpoint = endpoint; OfficeId = office; Aliases = new ReadOnlyDictionary<string, string>(aliases);
        PrivacyMode = privacy; CreatedAt = now;
        Payload = PresencePayload.Create(office, PresenceProjection.Invented(Aliases, privacy), now, now);
    }
    public static SharingPreview Create(SharingConfiguration configuration, DateTimeOffset now)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in configuration.SharingAliases ?? [])
        {
            PresenceProjection.ValidateAliasKey(entry.Key);
            if (entry.Value == null) throw new ArgumentException("Presence alias must be a string.");
            aliases.Add(entry.Key, PresencePayload.SanitizeAlias(entry.Value, aliases.Count + 1));
        }
        if (aliases.Count > PresencePayload.MaxAgents) throw new ArgumentException("Sharing supports at most 64 aliases.");
        return new(SharingEndpoint.Parse(configuration.SharingServer), configuration.SharingOfficeId, aliases,
            PrivacyModes.SharingValue(configuration.PrivacyMode), now);
    }
    public void RequirePrivacy(string? current)
    {
        if (PrivacyModes.SharingValue(current) != PrivacyMode)
            throw new ArgumentException("Privacy mode changed. Review a fresh preview.");
    }
    public void RequireFresh(DateTimeOffset now)
    {
        if (now < CreatedAt || now - CreatedAt > TimeSpan.FromSeconds(90))
            throw new ArgumentException("Sharing preview expired. Review a fresh preview before enabling.");
    }
}
