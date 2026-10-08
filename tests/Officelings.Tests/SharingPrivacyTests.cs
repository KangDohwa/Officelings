using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Officelings.App;
using Officelings.Core;
using Officelings.Core.Sharing;
using Xunit;

namespace Officelings.Tests;

public sealed class SharingPrivacyTests : IDisposable
{
    private const string Office = "11111111-1111-4111-8111-111111111111";
    private readonly string root = Path.Combine(Path.GetTempPath(), "vdc-sharing-privacy-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    public SharingPrivacyTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private static Settings SettingsFor(string? level) => new()
    {
        PrivacyMode = level, SharingServer = "https://presence.example", SharingOfficeId = Office,
        SharingAliases = new() { ["codex:1"] = "Chosen", ["codex:2"] = "Second", ["claude:1"] = "Moon", ["antigravity:1"] = "Star" }
    };
    private static AgentInfo Local(string id, Provider provider) => new(id, provider, null, false, "private-name", "private-role",
        "private-model", "C:/private-project", "private-prompt", "private-tool", "private-detail", AgentState.Working, Now, Now, true);
    private static readonly AgentInfo[] Agents = [Local("a", Provider.Codex), Local("b", Provider.Codex), Local("c", Provider.Claude), Local("d", Provider.Antigravity)];
    private static string[] Labels(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.GetProperty("agents").EnumerateArray()
            .OrderBy(a => a.GetProperty("platform").GetString(), StringComparer.Ordinal)
            .ThenBy(a => a.GetProperty("alias").GetString(), StringComparer.Ordinal)
            .Select(a => a.GetProperty("alias").GetString()!).ToArray();
    }
    private sealed class ManualTime : TimeProvider
    {
        private long ticks;
        public override DateTimeOffset GetUtcNow() => Now.AddTicks(ticks);
        public override long GetTimestamp() => ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance() => ticks += TimeSpan.FromSeconds(30).Ticks;
    }
    private sealed class Store : ISharingTokenStore
    {
        public int Reads;
        public string? Read(SharingEndpoint endpoint, string office) { Reads++; return "synthetic-token"; }
        public void Save(SharingEndpoint endpoint, string office, string token) { }
        public void Delete() { }
    }
    private sealed class Gate : IPresenceAttemptGate
    {
        public Action? OnReserve;
        public TimeSpan Reserve(string token, TimeSpan interval) { OnReserve?.Invoke(); return TimeSpan.Zero; }
        public void Defer(string token, TimeSpan interval) { }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<byte[]> Bytes = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Bytes.Add(await request.Content!.ReadAsByteArrayAsync(cancellation));
            return new(HttpStatusCode.Accepted);
        }
    }
    private static PresenceSender Sender(HttpClient client, Store store, Gate gate, ManualTime time, Func<Settings> settings) =>
        new(new PresenceTransport(client), store, gate, (p, now) => PresencePayload.Create(p.OfficeId,
            PresenceProjection.FromLocal(Agents, p.Aliases, settings().SharingConfiguration().PrivacyMode), now, now), time, () => 0,
            () => settings().SharingConfiguration().PrivacyMode);

    [Theory]
    [InlineData("off")]
    [InlineData("tasks")]
    [InlineData("full")]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task PreviewMatchesFirstSentBytesAndLiveLabels(string? level)
    {
        var settings = SettingsFor(level); var time = new ManualTime(); var handler = new Handler();
        using var client = new HttpClient(handler);
        await using var sender = Sender(client, new(), new(), time, () => settings);
        var preview = SharingPreview.Create(settings.SharingConfiguration(), time.GetUtcNow());
        Assert.True(sender.Enable(preview, true, "synthetic-token", false));
        await sender.TrySendAsync();
        Assert.Equal(Encoding.UTF8.GetBytes(preview.Payload.Json), Assert.Single(handler.Bytes));
        time.Advance(); await sender.TrySendAsync();
        Assert.Equal(Labels(preview.Payload.CopyBytes()), Labels(handler.Bytes[1]));
        foreach (var bytes in handler.Bytes) Assert.DoesNotContain("private", Encoding.UTF8.GetString(bytes));
        Assert.Equal(level is "off" or "tasks" ? ["Star", "Moon", "Chosen", "Second"] : ["Antigravity", "Claude", "Codex", "Codex #2"], Labels(handler.Bytes[1]));
        using var live = JsonDocument.Parse(handler.Bytes[1]);
        Assert.Equal(new[] { "codex", "codex", "claude", "antigravity" }, live.RootElement.GetProperty("agents").EnumerateArray().Select(a => a.GetProperty("platform").GetString()));
    }

    [Theory]
    [InlineData("off", "tasks")]
    [InlineData("tasks", "off")]
    [InlineData("off", "full")]
    [InlineData("full", "off")]
    [InlineData("tasks", "full")]
    [InlineData("full", "tasks")]
    [InlineData("off", null)]
    [InlineData("tasks", "bad")]
    public async Task LevelChangeStopsBeforeNextSnapshotAndFreshConsentUsesCurrentLevel(string before, string? after)
    {
        var settings = SettingsFor(before); var time = new ManualTime(); var handler = new Handler(); var store = new Store();
        using var client = new HttpClient(handler);
        await using var sender = Sender(client, store, new(), time, () => settings);
        var old = SharingPreview.Create(settings.SharingConfiguration(), time.GetUtcNow());
        Assert.True(sender.Enable(old, true, "synthetic-token", false)); await sender.TrySendAsync();
        settings = settings with { PrivacyMode = after };
        time.Advance(); await sender.TrySendAsync();
        Assert.False(sender.Enabled); Assert.Equal(SharingStatus.ConsentExpired, sender.Status); Assert.Single(handler.Bytes);
        Assert.Throws<ArgumentException>(() => sender.Enable(old, true, null, false)); Assert.Equal(0, store.Reads);
        var fresh = SharingPreview.Create(settings.SharingConfiguration(), time.GetUtcNow());
        Assert.True(sender.Enable(fresh, true, "synthetic-token", false)); await sender.TrySendAsync();
        Assert.Equal(fresh.Payload.CopyBytes(), handler.Bytes[1]);
        time.Advance(); await sender.TrySendAsync();
        Assert.Equal(Labels(fresh.Payload.CopyBytes()), Labels(handler.Bytes[2]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LevelChangeBeforeFirstPostOrDuringReservationNeverSendsOldPreview(bool duringReservation)
    {
        var settings = SettingsFor("off"); var time = new ManualTime(); var handler = new Handler(); var gate = new Gate();
        using var client = new HttpClient(handler);
        await using var sender = Sender(client, new(), gate, time, () => settings);
        var preview = SharingPreview.Create(settings.SharingConfiguration(), time.GetUtcNow());
        Assert.True(sender.Enable(preview, true, "synthetic-token", false));
        if (duringReservation) gate.OnReserve = () => settings = settings with { PrivacyMode = "full" };
        else settings = settings with { PrivacyMode = "tasks" };
        await sender.TrySendAsync();
        Assert.Empty(handler.Bytes); Assert.Equal(SharingStatus.ConsentExpired, sender.Status);
    }

    [Theory]
    [InlineData("{\"privacy\":\"off\"}", "off")]
    [InlineData("{\"privacy\":\"tasks\"}", "tasks")]
    [InlineData("{\"privacy\":\"full\"}", "full")]
    [InlineData("{\"privacyMode\":\"off\",\"privacy\":\"full\"}", "full")]
    [InlineData("{\"privacy\":\"tasks\",\"privacyMode\":\"off\"}", "tasks")]
    [InlineData("{\"privacyMode\":\"full\",\"privacy\":\"off\"}", "full")]
    [InlineData("{\"privacyMode\":\"tasks\",\"privacy\":\"full\"}", "full")]
    [InlineData("{\"privacyMode\":\"off\",\"privacy\":null}", "full")]
    [InlineData("{\"privacy\":{},\"privacyMode\":\"off\"}", "full")]
    [InlineData("{\"privacyMode\":\"bad\",\"privacy\":\"off\"}", "full")]
    [InlineData("{\"privacyMode\":null}", "full")]
    [InlineData("{\"privacyMode\":42}", "full")]
    [InlineData("{\"privacyMode\":true}", "full")]
    [InlineData("{\"privacyMode\":{}}", "full")]
    [InlineData("{\"privacyMode\":[]}", "full")]
    [InlineData("{\"privacyMode\":\"FULL\"}", "full")]
    [InlineData("{\"privacyMode\":\"full\",\"privacyMode\":\"off\"}", "full")]
    [InlineData("{}", "full")]
    public void SavedPrivacyMigratesWithoutLoweringPrivacy(string input, string expected)
    {
        var path = Path.Combine(root, "settings.json"); File.WriteAllText(path, input);
        var settings = Settings.Read(path);
        Assert.Equal(expected, settings.PrivacyMode);
        Assert.Equal(expected, PrivacyModes.ReadSetting(path));
        Assert.Equal(expected, SharingConfiguration.Read(path).PrivacyMode);
        Assert.Equal(expected, settings.SharingConfiguration().PrivacyMode);
        settings.Write(path);
        using var saved = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(expected, saved.RootElement.GetProperty("privacyMode").GetString());
        Assert.False(saved.RootElement.TryGetProperty("privacy", out _));
        Assert.Equal(expected, Settings.Read(path).PrivacyMode);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public void MalformedSettingsCannotExposeChosenAliases(string input)
    {
        var path = Path.Combine(root, "settings.json"); File.WriteAllText(path, input);
        Assert.Equal("full", Settings.Read(path).SharingConfiguration().PrivacyMode);
        Assert.Equal("full", SharingConfiguration.Read(path).PrivacyMode);
    }

    [Fact]
    public void GenericProviderSlotsAreIndependentOfInputOrderAndOtherPlatforms()
    {
        var aliases = SettingsFor("full").SharingAliases;
        var codex = Agents.Take(2).ToArray();
        Assert.Equal(new[] { "Codex", "Codex #2" }, PresenceProjection.FromLocal(codex, aliases, "full").Select(a => a.Alias));
        Assert.Equal(PresenceProjection.FromLocal(Agents, aliases, "full"), PresenceProjection.FromLocal(Agents.Reverse().ToArray(), aliases, "full"));
        Assert.Equal(new[] { "Codex", "Codex #2" }, PresenceProjection.FromLocal(Agents, aliases, "full").Where(a => a.Platform == "codex").Select(a => a.Alias));
    }

    private static void Sta(Action action)
    {
        Exception? error = null; var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Theory]
    [InlineData("off", "tasks")]
    [InlineData("tasks", "off")]
    [InlineData("off", "full")]
    [InlineData("full", "off")]
    [InlineData("tasks", "full")]
    [InlineData("full", "tasks")]
    public void MenuChangeImmediatelyInvalidatesSenderAndOpenDialog(string before, string after) => Sta(() =>
    {
        using var storage = new AppStorage(false, Path.Combine(root, "app"));
        var window = new OfficeWindow(initial: SettingsFor(before), assets: new AssetPack(), liveAgents: Agents, storage: storage, trackerHome: root);
        var time = new ManualTime(); using var client = new HttpClient(new Handler());
        var settingsField = typeof(OfficeWindow).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Settings Current() => (Settings)settingsField.GetValue(window)!;
        var sender = Sender(client, new(), new(), time, Current);
        SharingPreview? reviewed = null;
        var dialog = new SharingDialog(Current().SharingConfiguration(), false,
            (p, _, _) => { reviewed = p; return Task.CompletedTask; }, _ => Task.CompletedTask, () => Current().SharingConfiguration().PrivacyMode);
        try
        {
            typeof(OfficeWindow).GetField("sharingSender", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, sender);
            typeof(OfficeWindow).GetField("sharingDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, dialog);
            var preview = SharingPreview.Create(Current().SharingConfiguration(), time.GetUtcNow());
            Assert.True(sender.Enable(preview, true, "synthetic-token", false));
            var children = ((StackPanel)((ScrollViewer)dialog.Content).Content).Children.Cast<UIElement>().ToArray();
            var review = children.OfType<Button>().Single(b => Equals(b.Content, "Preview exact JSON"));
            var enable = children.OfType<Button>().Single(b => Equals(b.Content, "Enable sharing"));
            var consent = children.OfType<CheckBox>().Single(b => ((string)b.Content).StartsWith("I reviewed"));
            var json = children.OfType<TextBox>().Single(b => b.IsReadOnly);
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); consent.IsChecked = true;
            Assert.True(enable.IsEnabled); Assert.NotEmpty(json.Text);
            window.SetPrivacy(PrivacyModes.Parse(after));
            Assert.False(sender.Enabled); Assert.Equal("off", Current().Sharing);
            Assert.False(enable.IsEnabled); Assert.False(consent.IsChecked); Assert.Empty(json.Text);
            // Returning to the former level does not restore the old consent.
            window.SetPrivacy(PrivacyModes.Parse(before)); Assert.False(enable.IsEnabled);
            window.SetPrivacy(PrivacyModes.Parse(after));
            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var displayed = json.Text; consent.IsChecked = true; enable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(reviewed); Assert.Equal(after, reviewed.PrivacyMode); Assert.Equal(displayed, reviewed.Payload.Json);
        }
        finally { dialog.Close(); window.Close(); }
    });
}
