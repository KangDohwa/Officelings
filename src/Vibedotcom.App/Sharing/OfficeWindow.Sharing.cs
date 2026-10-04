using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Vibedotcom.Core.Sharing;

namespace Vibedotcom.App;

internal sealed partial class OfficeWindow
{
    private PresenceSender? sharingSender;
    private HttpClient? sharingClient;
    private DispatcherTimer? sharingTimer;
    private string sharingPrivacy = "off";
    private readonly TextBlock sharingIndicator = new() { Margin = new(16, 3, 16, 5), Foreground = Brushes.LightGray, Text = "Sharing: off" };

    private void InitializeSharing()
    {
        if (Content is DockPanel root) { DockPanel.SetDock(sharingIndicator, Dock.Bottom); root.Children.Insert(0, sharingIndicator); }
        if (settings.Sharing == "on") sharingIndicator.Text = "Sharing: off · review JSON to resume";
        if (demo || qa || storage.Isolated) { sharingIndicator.Text = "Sharing: off (demo / QA)"; return; }
        sharingTimer = new(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        sharingTimer.Tick += async (_, _) =>
        {
            if (closed || sharingSender == null) return;
            if (sharingSender.Enabled && settings.SharingConfiguration().Privacy != sharingPrivacy)
            { sharingSender.Stop(); settings = settings with { Sharing = "off" }; Save(); }
            await sharingSender.TrySendAsync();
            RefreshSharingIndicator();
            if (!sharingSender.Enabled && sharingSender.Status is not SharingStatus.Off && settings.Sharing != "off")
            { settings = settings with { Sharing = "off" }; Save(); }
        };
        Loaded += (_, _) => sharingTimer.Start();
        Closed += (_, _) => { sharingTimer.Stop(); sharingSender?.Stop(); _ = DisposeSharingAsync(); };
    }
    private async Task DisposeSharingAsync()
    {
        if (sharingSender != null) await sharingSender.DisposeAsync();
        sharingClient?.Dispose();
    }
    private void EnsureSharingSender()
    {
        if (sharingSender != null) return;
        sharingClient = PresenceTransport.CreateClient();
        sharingSender = new(new PresenceTransport(sharingClient), new WindowsSharingTokenStore(WindowsSharingTokenStore.DefaultDirectory),
            new FilePresenceAttemptGate(WindowsSharingTokenStore.DefaultDirectory), (preview, now) =>
                PresencePayload.Create(preview.OfficeId, PresenceProjection.FromLocal(agents, preview.Aliases, settings.SharingConfiguration().Privacy), now, now));
    }
    private void ShowSharing()
    {
        if (demo || qa || storage.Isolated)
        { MessageBox.Show(this, "Sharing is disabled in demo and QA runs.", "Sharing"); return; }
        var dialog = new SharingDialog(settings.SharingConfiguration(), sharingSender?.Enabled == true, EnableSharingAsync, StopSharingAsync);
        if (settings.Mode != "desktop") dialog.Owner = this;
        dialog.ShowDialog();
    }
    private async Task EnableSharingAsync(SharingPreview preview, string? token, bool remember)
    {
        if (demo || qa || storage.Isolated || closed) throw new ArgumentException("Sharing is unavailable in this window.");
        if (settings.SharingConfiguration().Privacy != preview.PrivacyMode &&
            (settings.SharingConfiguration().Privacy == "full" || preview.PrivacyMode == "full"))
            throw new ArgumentException("Privacy mode changed. Review a fresh preview.");
        EnsureSharingSender();
        if (!sharingSender!.Enable(preview, confirmed: true, token, remember)) throw new ArgumentException("Stop sharing before enabling it again.");
        sharingPrivacy = settings.SharingConfiguration().Privacy;
        settings = settings with { Sharing = "on", SharingServer = preview.Endpoint.Origin.AbsoluteUri, SharingOfficeId = preview.OfficeId,
            SharingAliases = new Dictionary<string, string>(preview.Aliases) };
        Save(); await sharingSender.TrySendAsync(); RefreshSharingIndicator();
        if (!sharingSender.Enabled) { settings = settings with { Sharing = "off" }; Save(); throw new ArgumentException("Sharing stopped. Check the server, office and token, then review again."); }
    }
    private async Task StopSharingAsync(bool deleteToken)
    {
        sharingSender?.Stop(); settings = settings with { Sharing = "off" }; Save(); RefreshSharingIndicator();
        if (sharingSender != null) await sharingSender.StopAsync(deleteToken);
        else if (deleteToken) new WindowsSharingTokenStore(WindowsSharingTokenStore.DefaultDirectory).Delete();
    }
    private void RefreshSharingIndicator()
    {
        sharingIndicator.Text = sharingSender?.Status switch
        {
            SharingStatus.Ready => "Sharing: on · waiting to send", SharingStatus.Sending => "Sharing: on · sending",
            SharingStatus.Active => "Sharing: on · live presence", SharingStatus.Backoff => "Sharing: on · waiting before retry",
            SharingStatus.ConsentExpired => "Sharing: stopped · review a fresh JSON preview",
            SharingStatus.CredentialError => "Sharing: stopped · stored credential unavailable",
            SharingStatus.Rejected => "Sharing: stopped · server rejected the request",
            SharingStatus.PayloadError => "Sharing: stopped · snapshot exceeds protocol limits",
            _ => "Sharing: off"
        };
    }
}
