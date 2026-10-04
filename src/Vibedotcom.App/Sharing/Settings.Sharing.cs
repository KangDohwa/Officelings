using System;
using System.Collections.Generic;
using System.Linq;
using Vibedotcom.Core;
using Vibedotcom.Core.Sharing;

namespace Vibedotcom.App;

internal sealed partial record Settings
{
    public string Sharing { get; init; } = "off";
    public string SharingOfficeId { get; init; } = "";
    public string SharingServer { get; init; } = "";
    public AliasMap SharingAliases { get; init; } = new();

    internal Settings NormalizeSharing() => this with
    {
        Sharing = Sharing == "on" ? "on" : "off", SharingOfficeId = SharingOfficeId ?? "",
        SharingServer = SharingServer ?? "", SharingAliases = SharingAliases ?? new()
    };
    internal SharingConfiguration SharingConfiguration() => new()
    {
        Sharing = Sharing, SharingOfficeId = SharingOfficeId, SharingServer = SharingServer,
        SharingAliases = new(SharingAliases ?? new()), PrivacyMode = PrivacyModes.SharingValue(PrivacyMode)
    };
}

/// <summary>Alias dictionary with value equality, so Settings record comparison stays meaningful.</summary>
internal sealed class AliasMap : Dictionary<string, string>
{
    public AliasMap() : base(StringComparer.Ordinal) { }
    public AliasMap(IEnumerable<KeyValuePair<string, string>> source) : base(StringComparer.Ordinal) { foreach (var pair in source) this[pair.Key] = pair.Value; }
    public override bool Equals(object? obj) => obj is AliasMap other && Count == other.Count
        && this.All(pair => other.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
    public override int GetHashCode() { var hash = new HashCode(); foreach (var pair in this.OrderBy(p => p.Key, StringComparer.Ordinal)) { hash.Add(pair.Key); hash.Add(pair.Value); } return hash.ToHashCode(); }
}
