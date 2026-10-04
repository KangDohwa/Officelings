using Vibedotcom.Core;
using Vibedotcom.Core.Sharing;

internal sealed class TrackerSharing : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation;
    private readonly CancellationTokenRegistration stopRegistration;
    private IReadOnlyList<AgentInfo> agents = [];
    private PresenceSender? sender;
    private HttpClient? client;
    private Task? loop;

    private TrackerSharing(CancellationToken stop)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop);
        stopRegistration = cancellation.Token.Register(() => sender?.Stop());
    }
    public static TrackerSharing Start(bool requested, string settingsPath, CancellationToken stop = default, Func<PrivacyMode>? currentPrivacy = null)
    {
        var sharing = new TrackerSharing(stop);
        if (!requested) return sharing;
        if (Console.IsInputRedirected)
        { Console.WriteLine("Sharing: off. --sharing requires interactive JSON confirmation."); return sharing; }
        var config = SharingConfiguration.Read(settingsPath);
        string Privacy()
        {
            var saved = PrivacyModes.Parse(SharingConfiguration.Read(settingsPath).PrivacyMode);
            return PrivacyModes.Value((PrivacyMode)Math.Max((int)saved, (int)(currentPrivacy?.Invoke() ?? saved)));
        }
        if (config.Sharing != "on")
        { Console.WriteLine("Sharing: off. Configure sharing and store a token in the app first."); return sharing; }
        try
        {
            var preview = SharingPreview.Create(config with { PrivacyMode = Privacy() }, DateTimeOffset.UtcNow);
            Console.WriteLine("Sharing is off. This complete JSON is an invented example using your aliases and will be the first request body. Later whole snapshots include only live presence.");
            Console.WriteLine("Public visitors can see aliases, platforms, states, hierarchy and approximate activity times, and can save or redistribute them. Sharing does not make an office public.");
            Console.WriteLine("Ctrl+C stops transmission; presence expires within 90 seconds. Revocation/unpublishing clears it immediately, but downloaded data cannot be recalled. Use the app to optionally delete the stored token.");
            Console.WriteLine(preview.Payload.Json);
            Console.WriteLine("Type ENABLE to confirm this JSON and start sharing:");
            if (Console.ReadLine() != "ENABLE") return sharing;
            sharing.cancellation.Token.ThrowIfCancellationRequested();
            sharing.client = PresenceTransport.CreateClient();
            sharing.sender = new(new PresenceTransport(sharing.client), new WindowsSharingTokenStore(WindowsSharingTokenStore.DefaultDirectory),
                new FilePresenceAttemptGate(WindowsSharingTokenStore.DefaultDirectory), (p, now) =>
                    PresencePayload.Create(p.OfficeId, PresenceProjection.FromLocal(Volatile.Read(ref sharing.agents), p.Aliases,
                        Privacy()), now, now), privacyMode: Privacy);
            sharing.sender.Enable(preview, true, null, false);
            if (sharing.cancellation.IsCancellationRequested) sharing.sender.Stop();
            sharing.loop = sharing.RunAsync(settingsPath);
        }
        catch (Exception) { Console.WriteLine("Sharing: off. Configuration, consent or stored credential unavailable."); sharing.sender?.Stop(); }
        return sharing;
    }
    public void Update(IReadOnlyList<AgentInfo> current)
    {
        if (sender?.Enabled == true) Volatile.Write(ref agents, current.ToArray());
    }
    private async Task RunAsync(string settingsPath)
    {
        var previous = SharingStatus.Off;
        try
        {
            while (!cancellation.IsCancellationRequested && sender?.Enabled == true)
            {
                var config = SharingConfiguration.Read(settingsPath);
                if (config.Sharing != "on")
                { sender.Stop(); Console.WriteLine("Sharing: off. Settings changed; confirm a fresh preview to resume."); break; }
                await sender.TrySendAsync();
                if (sender.Status != previous) { previous = sender.Status; Console.WriteLine("Sharing: " + previous); }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) { sender?.Stop(); Console.WriteLine("Sharing: off. Presence transmission unavailable."); }
    }
    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel(); sender?.Stop();
        if (loop != null) await loop;
        if (sender != null) await sender.DisposeAsync();
        client?.Dispose(); stopRegistration.Dispose(); cancellation.Dispose();
    }
}
