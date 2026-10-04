using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vibedotcom.Core.Sharing;

namespace Vibedotcom.App;

internal sealed class SharingDialog : Window
{
    internal const string Disclosure = "Sharing is off until you review this exact JSON and confirm. The first request sends this invented example; later requests send complete live presence every 10–30 seconds.\n\nVisitors to a public office can see aliases, platforms, working/idle/waiting states, hierarchy and approximate activity times. Anyone with its public URL can save or redistribute this information. Enabling transmission does not make an office public.\n\nStop sharing cancels transmission. Presence expires within 90 seconds; revoking the token or unpublishing clears it immediately on the server. Previously downloaded data cannot be recalled.";
    private readonly TextBox server = new(), office = new(), aliases = new(), json = new();
    private readonly PasswordBox token = new();
    private readonly CheckBox consent = new() { Content = "I reviewed this JSON and agree to share presence" };
    private readonly CheckBox remember = new() { Content = "Store token with Windows DPAPI (CurrentUser)", IsChecked = true };
    private readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly Button enable = new() { Content = "Enable sharing", IsEnabled = false, Padding = new(12, 5, 12, 5) };
    private SharingPreview? preview;

    internal SharingDialog(SharingConfiguration configuration, bool active,
        Func<SharingPreview, string?, bool, Task> start, Func<bool, Task> stop)
    {
        Title = "Sharing"; Width = 720; Height = 800; MinWidth = 540; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var body = new StackPanel { Margin = new(18) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = Disclosure, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = active ? "Sharing is on. Stop before changing its configuration." : "Sharing is off.", FontWeight = FontWeights.SemiBold });
        var delete = new CheckBox { Content = "Also delete the stored token", Margin = new(0, 8, 0, 5) }; body.Children.Add(delete);
        var stopButton = new Button { Content = "Stop sharing", HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 5, 12, 5) };
        body.Children.Add(stopButton);
        stopButton.Click += async (_, _) =>
        {
            try { await stop(delete.IsChecked == true); Close(); }
            catch (Exception) { error.Text = "Sharing stopped. The stored token could not be deleted."; }
        };
        server.Text = configuration.SharingServer; office.Text = configuration.SharingOfficeId;
        aliases.Text = string.Join(Environment.NewLine, (configuration.SharingAliases ?? new()).OrderBy(e => e.Key).Select(e => e.Key + "=" + e.Value));
        aliases.AcceptsReturn = true; aliases.MinHeight = 65; aliases.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Field(body, "Server origin (HTTPS; loopback HTTP allowed for development)", server);
        Field(body, "Office ID (UUID from the server)", office);
        Field(body, "Aliases: one platform:slot=alias per line, e.g. codex:1=Nova. Platforms: codex, claude, antigravity, other. Unlisted slots use Agent N.", aliases);
        if (configuration.Privacy == "full") body.Children.Add(new TextBlock { Text = "Full privacy mode forces Agent N aliases, including this preview.", TextWrapping = TextWrapping.Wrap });
        Field(body, "Bearer token (leave blank to use the stored token bound to this server and office)", token);
        body.Children.Add(remember);
        var review = new Button { Content = "Preview exact JSON", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 10, 0, 5), Padding = new(12, 5, 12, 5) };
        body.Children.Add(review);
        json.IsReadOnly = true; json.AcceptsReturn = true; json.TextWrapping = TextWrapping.Wrap;
        json.FontFamily = new("Consolas"); json.Height = 155; json.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Field(body, "Complete UTF-8 request body (bearer token is sent only in the Authorization header)", json);
        body.Children.Add(consent); body.Children.Add(enable); body.Children.Add(error);
        void Invalidate() { preview = null; json.Clear(); consent.IsChecked = false; enable.IsEnabled = false; }
        server.TextChanged += (_, _) => Invalidate(); office.TextChanged += (_, _) => Invalidate(); aliases.TextChanged += (_, _) => Invalidate();
        consent.Checked += (_, _) => enable.IsEnabled = preview != null;
        consent.Unchecked += (_, _) => enable.IsEnabled = false;
        review.Click += (_, _) =>
        {
            Invalidate(); error.Text = "";
            try
            {
                preview = SharingPreview.Create(new()
                {
                    SharingServer = server.Text.Trim(), SharingOfficeId = office.Text.Trim(),
                    SharingAliases = ParseAliases(aliases.Text), Privacy = configuration.Privacy
                }, DateTimeOffset.UtcNow);
                json.Text = preview.Payload.Json;
            }
            catch (ArgumentException e) { error.Text = e.Message; }
        };
        enable.Click += async (_, _) =>
        {
            if (preview == null || consent.IsChecked != true) return;
            enable.IsEnabled = false; error.Text = "";
            try { await start(preview, token.Password.Length == 0 ? null : token.Password, remember.IsChecked == true); token.Clear(); Close(); }
            catch (ArgumentException e) { error.Text = e.Message; }
            catch (Exception) { error.Text = "Sharing could not start. Check the stored token, server and office, then review again."; }
            finally { enable.IsEnabled = preview != null && consent.IsChecked == true; }
        };
        if (active) { server.IsEnabled = office.IsEnabled = aliases.IsEnabled = token.IsEnabled = review.IsEnabled = consent.IsEnabled = false; }
        Closed += (_, _) => token.Clear();
    }
    private static void Field(Panel panel, string title, FrameworkElement input)
    {
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new(0, 9, 0, 3) });
        panel.Children.Add(input);
    }
    internal static Dictionary<string, string> ParseAliases(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) throw new ArgumentException("Use one platform:slot=alias per line.");
            var key = line[..separator].Trim(); PresenceProjection.ValidateAliasKey(key);
            if (!result.TryAdd(key, line[(separator + 1)..].TrimEnd('\r'))) throw new ArgumentException("Alias slots cannot be repeated.");
        }
        return result;
    }
}
