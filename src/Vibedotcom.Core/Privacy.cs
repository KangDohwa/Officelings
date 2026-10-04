using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vibedotcom.Core;

public enum PrivacyMode { Off, Tasks, Full }

public static class PrivacyModes
{
    public static string Value(PrivacyMode mode) => mode.ToString().ToLowerInvariant();
    public static PrivacyMode Parse(string value) => value switch
    {
        "off" => PrivacyMode.Off, "tasks" => PrivacyMode.Tasks, "full" => PrivacyMode.Full,
        _ => throw new ArgumentException("--privacy must be off, tasks or full.")
    };

    public static PrivacyMode Resolve(string? commandLine, string? setting, string? environment) =>
        Parse(commandLine ?? (setting is "off" or "tasks" or "full" ? setting : null) ??
            (environment is "off" or "tasks" or "full" ? environment : "off"));

    // Only read this one key; the tracker does not own the viewer's other preferences.
    public static string? ReadSetting(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 65536) return null;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(file);
            return LogParser.String(json.RootElement, "privacyMode");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static string Diagnostic(Exception error, PrivacyMode mode) => mode == PrivacyMode.Off ? error.Message : error.GetType().Name;
    public static IReadOnlyList<string> Diagnostics(IReadOnlyList<string> warnings, PrivacyMode mode) =>
        mode == PrivacyMode.Off || warnings.Count == 0 ? warnings : ["A local source or office asset is unavailable (details hidden by privacy)."];
}

/// <summary>Redacts at text assignment, with labels stable for this tracker lifetime.</summary>
public sealed class PrivacyRedactor(PrivacyMode mode = PrivacyMode.Off)
{
    private readonly Dictionary<string, string> labels = new(StringComparer.Ordinal);
    private readonly Dictionary<Provider, int> counts = [];
    private bool privateIdentities = mode != PrivacyMode.Off;
    public PrivacyMode Mode { get; private set; } = mode;
    public void SetMode(PrivacyMode value) { privateIdentities |= value != PrivacyMode.Off; Mode = value; }

    public string Label(Provider provider, string id)
    {
        var key = OpaqueId(id);
        if (!labels.TryGetValue(key, out var label))
        {
            var number = counts.GetValueOrDefault(provider) + 1; counts[provider] = number;
            labels[key] = label = provider + (number == 1 ? "" : " #" + number);
        }
        return label;
    }

    internal string Name(Provider provider, string id, string value) =>
        Mode == PrivacyMode.Full || Mode == PrivacyMode.Tasks && provider == Provider.Antigravity ? Label(provider, id) : value;
    internal string Task(string value) => Mode == PrivacyMode.Off ? value : "";
    internal string? Detail(string? value) => Mode == PrivacyMode.Off ? value : null;
    internal string? Model(string? value) => Mode == PrivacyMode.Full ? null : value;
    internal string? Cwd(string? value) => Mode switch
    {
        PrivacyMode.Full => null,
        PrivacyMode.Tasks => Leaf(value),
        _ => value
    };

    public static string? Leaf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.TrimEnd('/', '\\');
        var leaf = trimmed[(Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\')) + 1)..];
        return leaf.Length == 0 || leaf.Contains(':') ? null : leaf;
    }

    // Session IDs can fall back to filenames. Do not export those names in a private snapshot.
    internal static string OpaqueId(string id) => id.StartsWith("private:", StringComparison.Ordinal) ? id :
        "private:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    public string? Id(string? id) => id == null || !privateIdentities ? id : OpaqueId(id);

    public AgentInfo Redact(AgentInfo agent) => Mode == PrivacyMode.Off ? agent : agent with
    {
        Id = Id(agent.Id)!, ParentId = Id(agent.ParentId), Name = Name(agent.Provider, agent.Id, agent.Name),
        Role = null, Model = Model(agent.Model), Cwd = Cwd(agent.Cwd), Task = "", Tool = null, ToolKind = null
    };

    public AgentView Redact(AgentView view)
    {
        if (Mode == PrivacyMode.Off) return view;
        var agent = Redact(view.Agent);
        return new(agent, view.Pose with
        {
            DisplayName = agent.Name,
            ChatPartnerId = Id(view.Pose.ChatPartnerId), ActivityPartnerId = Id(view.Pose.ActivityPartnerId)
        });
    }
}
