using System.Collections.Generic;
using System.Text.Json;
using Vibedotcom.Core;
using Vibedotcom.Core.Sharing;

namespace Vibedotcom.App;

internal sealed partial record Settings
{
    public string Sharing { get; init; } = "off";
    public string SharingOfficeId { get; init; } = "";
    public string SharingServer { get; init; } = "";
    public Dictionary<string, string> SharingAliases { get; init; } = new();

    internal Settings NormalizeSharing() => this with
    {
        Sharing = Sharing == "on" ? "on" : "off", SharingOfficeId = SharingOfficeId ?? "",
        SharingServer = SharingServer ?? "", SharingAliases = SharingAliases ?? new()
    };
    // Read the shared settings contract, including a privacy key when that feature is merged.
    // No local session/state object or credential is involved in this conversion.
    internal SharingConfiguration SharingConfiguration() =>
        JsonSerializer.Deserialize<SharingConfiguration>(JsonSerializer.SerializeToUtf8Bytes(this, StateJson.Options), StateJson.Options) ?? new();
}
