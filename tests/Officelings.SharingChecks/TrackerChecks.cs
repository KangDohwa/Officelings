using System.IO;
using System.Reflection;
using System.Text.Json;
using Officelings.Core;
using Officelings.Core.Sharing;

namespace Officelings.SharingChecks;

internal static partial class Program
{
    private static async Task TrackerChecks()
    {
        var type = Assembly.Load("Officelings.Tracker").GetType("TrackerSharing", true)!;
        var start = type.GetMethod("Start", BindingFlags.Static | BindingFlags.Public)!;
        await CheckAsync("headless tracker off by default creates no transport or credential store", async () =>
        {
            var sharing = start.Invoke(null, [false, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"), CancellationToken.None, null])!;
            Require(type.GetField("sender", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sharing) == null);
            Require(type.GetField("client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sharing) == null);
            await ((IAsyncDisposable)sharing).DisposeAsync();
        });
        if (Console.IsInputRedirected)
            await CheckAsync("redirected --sharing cannot enable even with saved on configuration", async () =>
            {
                var directory = Path.Combine(Path.GetTempPath(), "officelings-sharing-tracker-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var path = Path.Combine(directory, "settings.json");
                    StateJson.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(new SharingConfiguration
                    { Sharing = "on", SharingServer = "https://presence.example", SharingOfficeId = Office }, StateJson.Options));
                    var sharing = start.Invoke(null, [true, path, CancellationToken.None, null])!;
                    Require(type.GetField("sender", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sharing) == null);
                    Require(type.GetField("client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sharing) == null);
                    await ((IAsyncDisposable)sharing).DisposeAsync();
                }
                finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            });
    }
}
