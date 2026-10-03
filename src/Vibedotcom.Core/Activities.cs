using System.Text.Json;

namespace Vibedotcom.Core;

public sealed record ActivityDefinition(string Id, double Weight = 1, double MinSeconds = 14, double MaxSeconds = 24);

public sealed class ActivityCatalog
{
    private sealed record ActivityFile(int Version, IReadOnlyList<ActivityDefinition> Activities);
    private sealed record ActivityOverride(string Id, double? Weight = null, double? MinSeconds = null, double? MaxSeconds = null);
    private sealed record OverrideFile(int Version, IReadOnlyList<ActivityOverride> Activities);
    public IReadOnlyList<ActivityDefinition> Activities { get; }

    public ActivityCatalog(IEnumerable<ActivityDefinition> activities)
    {
        var definitions = activities.OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
        if (definitions.Length > 64 || definitions.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Length ||
            definitions.Any(a => string.IsNullOrWhiteSpace(a.Id) || !double.IsFinite(a.Weight) || a.Weight is < 0 or > 1000000 ||
                !double.IsFinite(a.MinSeconds) || !double.IsFinite(a.MaxSeconds) || a.MinSeconds <= 0 || a.MaxSeconds < a.MinSeconds || a.MaxSeconds > 3600))
            throw new InvalidDataException("Invalid activity IDs, weights or durations.");
        Activities = Array.AsReadOnly(definitions);
    }

    public static ActivityCatalog Load(string? home = null, IList<string>? warnings = null)
    {
        using var resource = typeof(ActivityCatalog).Assembly.GetManifestResourceStream("Vibedotcom.Core.Data.activities.json")
            ?? throw new InvalidDataException("Bundled activities missing.");
        var defaults = JsonSerializer.Deserialize<ActivityFile>(resource, StateJson.Options)
            ?? throw new InvalidDataException("Empty bundled activities.");
        if (defaults.Version != 1) throw new InvalidDataException("Unsupported activity version.");
        var catalog = new ActivityCatalog(defaults.Activities);
        var file = home == null ? null : Path.Combine(home, "activities.json");
        if (file == null || !File.Exists(file)) return catalog;
        try
        {
            var input = AssetPack.Read<OverrideFile>(file);
            if (input.Version != 1 || input.Activities == null || input.Activities.Count > 64 ||
                input.Activities.Any(a => a == null || !catalog.Activities.Any(d => d.Id == a.Id)) ||
                input.Activities.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != input.Activities.Count)
                throw new InvalidDataException("Unsupported activity version or IDs.");
            return new ActivityCatalog(catalog.Activities.Select(d => input.Activities.FirstOrDefault(a => a.Id == d.Id) is {} a
                ? d with { Weight = a.Weight ?? d.Weight, MinSeconds = a.MinSeconds ?? d.MinSeconds, MaxSeconds = a.MaxSeconds ?? d.MaxSeconds } : d));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            warnings?.Add("Activities rejected; using bundled defaults: " + e.Message);
            return catalog;
        }
    }
}
