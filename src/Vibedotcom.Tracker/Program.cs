using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vibedotcom.Core;

var activePrivacy = PrivacyMode.Full;
try
{
    var options = new TrackerOptions();
    var output = TrackerOptions.OutputDirectory;
    string? assetDirectory = null, layoutFile = null;
    string? privacyOption = null;
    double seconds = double.PositiveInfinity, interval = .2;
    var sharingRequested = false;
    for (var i = 0; i < args.Length; i++)
    {
        string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + args[i - 1]);
        double Number() => double.TryParse(Value(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n > 0
            ? n : throw new ArgumentException("Expected a positive finite number.");
        switch (args[i])
        {
            case "--help":
                Console.WriteLine("Vibedotcom.Tracker [--once | --seconds N] [--privacy off|tasks|full] [--output DIR] [--home DIR] [--asset-dir DIR] [--layout FILE] [--working-seconds N] [--leave-after-idle-minutes N] [--leave-after-waiting-minutes N] [--gone-minutes N (deprecated waiting alias)] [--scan-seconds N] [--interval-seconds N] [--sharing]\nSharing requires interactive JSON confirmation and the app's stored token. Read-only observation. Default output: " + output);
                return;
            case "--privacy": privacyOption = PrivacyModes.Value(PrivacyModes.Parse(Value())); break;
            case "--output": output = Value(); break;
            case "--home": options = options with { Home = Path.GetFullPath(Value()) }; break;
            case "--asset-dir": assetDirectory = Value(); break;
            case "--layout": layoutFile = Value(); break;
            case "--once": seconds = 0; break;
            case "--seconds": seconds = Number(); break;
            case "--working-seconds": options = options with { WorkingWindow = TimeSpan.FromSeconds(Number()) }; break;
            case "--leave-after-idle-minutes": options = options with { LeaveAfterIdle = TimeSpan.FromMinutes(Number()) }; break;
            case "--leave-after-waiting-minutes": options = options with { LeaveAfterWaiting = TimeSpan.FromMinutes(Number()) }; break;
            case "--gone-minutes": options = options with { GoneAfter = TimeSpan.FromMinutes(Number()) }; break;
            case "--scan-seconds": options = options with { ScanInterval = TimeSpan.FromSeconds(Number()) }; break;
            case "--interval-seconds": interval = Number(); break;
            case "--sharing": sharingRequested = true; break;
                        default: throw new ArgumentException("Unknown tracker option. Use --help.");
        }
    }
    string? privacySetting = null;
    var nextPrivacyRead = DateTimeOffset.MinValue;
    PrivacyMode ReadPrivacy()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < nextPrivacyRead) return PrivacyModes.Resolve(privacyOption,privacySetting,Environment.GetEnvironmentVariable("VIBEDOTCOM_PRIVACY"));
        nextPrivacyRead = now.AddSeconds(1);
        var value=PrivacyModes.ReadSetting(Path.Combine(output,"settings.json"));
        if (value is "off" or "tasks" or "full") privacySetting=value;
        return PrivacyModes.Resolve(privacyOption,privacySetting,Environment.GetEnvironmentVariable("VIBEDOTCOM_PRIVACY"));
    }
    activePrivacy = ReadPrivacy(); options=options with { PrivacyMode=activePrivacy };
    options.Validate();
    output = Path.TrimEndingDirectorySeparator(StateJson.OutputPath(output, options.Home));
    var lockName = "Vibedotcom.Tracker." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToUpperInvariant())))[..24];
    using var mutex = new Mutex(true, lockName, out var owns);
    if (!owns) throw new IOException("Another tracker already writes this output directory.");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    await using var sharing = TrackerSharing.Start(sharingRequested, Path.Combine(output, "settings.json"), cancellation.Token);
    var publicationLogged = false;
    void LogPublication(string message) { if (!publicationLogged) { Console.Error.WriteLine(message); publicationLogged = true; } }
    var personalSources = Path.GetFullPath(options.Home).Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),StringComparison.OrdinalIgnoreCase);
    var assets = AssetPack.LoadDefault(assetDirectory,useUserPack:personalSources);
    AssetManifest? manifest = null;
    await StateJson.PublishWithRetryAsync(() => manifest = assets.Export(output), LogPublication, cancellation.Token,activePrivacy);
    var layout = LayoutLoader.Load(assets,layoutFile ?? Path.Combine(personalSources ? TrackerOptions.OutputDirectory : options.Home,"layout.json"),assets.Warnings);
    if (!File.Exists(Path.Combine(output,"layout.json")))
        await StateJson.PublishWithRetryAsync(() => StateJson.WriteAtomic(Path.Combine(output,"layout.json"),JsonSerializer.SerializeToUtf8Bytes(layout,StateJson.Options)), LogPublication, cancellation.Token,activePrivacy);
    using var tracker = new AgentTracker(options);
    var office = new OfficeAnimator(null,assets,layout,ActivityCatalog.Load(output,assets.Warnings));
    var started = DateTimeOffset.UtcNow;
    var stopAt = seconds < (DateTimeOffset.MaxValue - started).TotalSeconds ? started.AddSeconds(seconds) : DateTimeOffset.MaxValue;
    var previous = started;
    long sequence = 0;
    string lastCounts = "";
    OfficeLayout? previousLayout = null;
    var snapshots = new TrackerSnapshotCache(manifest,activePrivacy);
    IReadOnlyList<AgentInfo> previousAgents = [];
    IReadOnlyList<AgentView> views = [];
    var nextAnimation = started;
    var lastPublished = DateTimeOffset.MinValue;
    var heartbeat = TimeSpan.FromSeconds(3);
    var nextPoll = started;
    IReadOnlyList<AgentInfo> currentAgents = [];
    do
    {
        var now = DateTimeOffset.UtcNow;
        var privacy = ReadPrivacy();
        if (privacy != activePrivacy)
        {
            activePrivacy = privacy; tracker.SetPrivacy(privacy); office.ClearAgentCaches();
            previousAgents=[]; previousLayout=null; snapshots=new(manifest,privacy); nextAnimation=now;
            nextPoll = now;
        }
        if (now >= nextPoll || now < previous)
        {
            currentAgents = tracker.Poll(now);
            nextPoll = now.AddSeconds(currentAgents.Any(a => a.State != AgentState.Gone) ? .5 : 1);
        }
        var agents = currentAgents;
        sharing.Update(agents);
        var hasLiveAgents = agents.Any(a => a.State != AgentState.Gone);
        var agentsChanged = !previousAgents.SequenceEqual(agents);
        if (sequence == 0 || agentsChanged || now < previous || now >= nextAnimation || now >= office.NextUpdateAt)
        {
            views = office.Update(agents, now, (now - previous).TotalSeconds);
            previous = now;
            previousAgents = agents;
            nextAnimation = office.HasMovingCharacters ? now.AddSeconds(interval)
                : office.AnimationInterval > TimeSpan.Zero
                    ? now.AddSeconds(Math.Max(interval, hasLiveAgents ? Math.Clamp(office.AnimationInterval.TotalSeconds, .5, 1) : 1))
                    : DateTimeOffset.MaxValue;
        }
        var changed = snapshots.Update(office.Layout, views, PrivacyModes.Diagnostics(tracker.Warnings.Concat(assets.Warnings).ToArray(),activePrivacy), office.FurnitureSprites);
        if (changed || now < lastPublished || now - lastPublished >= heartbeat)
        {
            await StateJson.PublishWithRetryAsync(() =>
            {
                if (!ReferenceEquals(previousLayout, office.Layout))
                {
                    // Preserve the editable source; reuse the snapshot's resolved layout bytes.
                    StateJson.WriteAtomic(Path.Combine(output, "runtime-layout.json"), snapshots.LayoutBytes);
                    previousLayout = office.Layout;
                }
                var generatedAt = DateTimeOffset.UtcNow;
                StateJson.WriteAtomic(Path.Combine(output, "state.json"), snapshots.Serialize(generatedAt, sequence + 1).Span);
                lastPublished = generatedAt;
            }, LogPublication, cancellation.Token,activePrivacy);
            sequence++;
        }
        if (agentsChanged || sequence == 1 && lastCounts.Length == 0)
        {
            var counts = string.Join(", ", Enum.GetValues<Provider>().Select(p => $"{p}={agents.Count(a => a.Provider == p && a.State != AgentState.Gone)}"));
            if (counts != lastCounts || lastCounts.Length == 0)
            { Console.WriteLine($"{now:O} alive: {counts}; total={agents.Count}; bytesRead={tracker.BytesRead}"); lastCounts = counts; }
        }
        if (seconds == 0 || now >= stopAt) break;
        var wakeAt = now.AddSeconds(office.HasMovingCharacters ? interval : Math.Max(interval, hasLiveAgents ? .5 : 1));
        if (nextAnimation > now && nextAnimation < wakeAt) wakeAt = nextAnimation;
        if (lastPublished + heartbeat < wakeAt) wakeAt = lastPublished + heartbeat;
        if (stopAt < wakeAt) wakeAt = stopAt;
        var delay = wakeAt - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
            try { await Task.Delay(TrackerCadence.Delay(delay), cancellation.Token); } catch (OperationCanceledException) { break; }
    } while (!cancellation.IsCancellationRequested);
    foreach (var warning in PrivacyModes.Diagnostics(tracker.Warnings.Concat(assets.Warnings).ToArray(),activePrivacy)) Console.Error.WriteLine(warning);
    Console.WriteLine($"Wrote {sequence} snapshots{(activePrivacy==PrivacyMode.Off ? " to "+output : "")}; log bytes read={tracker.BytesRead}.");
}
catch (OperationCanceledException) { }
catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
{ Console.Error.WriteLine(PrivacyModes.Diagnostic(e,activePrivacy)); Environment.ExitCode = 1; }

// Cache only serialized content. Publication still uses StateJson's atomic replace and retry.
internal sealed class TrackerSnapshotCache(AssetManifest? manifest, PrivacyMode privacy)
{
    public TrackerSnapshotCache(AssetManifest? manifest) : this(manifest,PrivacyMode.Off) { }
    private static readonly JsonSerializerOptions ContentOptions = new(StateJson.Options) { WriteIndented = false };
    private readonly byte[] atlasBytes = JsonSerializer.SerializeToUtf8Bytes(SpriteAtlas.Info, ContentOptions);
    private readonly byte[] assetBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ContentOptions);
    private readonly ArrayBufferWriter<byte> buffer = new();
    private OfficeLayout? layout;
    private AgentView[] agents = [];
    private string[] warnings = [];
    private FurnitureSprite[] furniture = [];
    private byte[] agentBytes = "[]"u8.ToArray(), warningBytes = "[]"u8.ToArray(), furnitureBytes = "[]"u8.ToArray();
    public byte[] LayoutBytes { get; private set; } = [];

    public bool Update(OfficeLayout office, IReadOnlyList<AgentView> views, IReadOnlyList<string> diagnostics, IReadOnlyList<FurnitureSprite> sprites)
    {
        var changed = false;
        if (!ReferenceEquals(layout, office))
        {
            LayoutBytes = JsonSerializer.SerializeToUtf8Bytes(office, ContentOptions);
            layout = office; changed = true;
        }
        if (!agents.SequenceEqual(views))
        {
            agents = views.ToArray(); agentBytes = JsonSerializer.SerializeToUtf8Bytes(agents, ContentOptions); changed = true;
        }
        if (!warnings.SequenceEqual(diagnostics))
        {
            warnings = diagnostics.ToArray(); warningBytes = JsonSerializer.SerializeToUtf8Bytes(warnings, ContentOptions); changed = true;
        }
        if (!furniture.SequenceEqual(sprites))
        {
            furniture = sprites.ToArray(); furnitureBytes = JsonSerializer.SerializeToUtf8Bytes(furniture, ContentOptions); changed = true;
        }
        return changed;
    }

    public ReadOnlyMemory<byte> Serialize(DateTimeOffset generatedAt, long sequence)
    {
        buffer.Clear();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = StateJson.Options.WriteIndented }))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", StateJson.SchemaVersion);
            json.WriteString("generatedAt", generatedAt);
            json.WriteNumber("sequence", sequence);
            json.WriteString("privacy", PrivacyModes.Value(privacy));
            void Raw(string name, byte[] bytes) { json.WritePropertyName(name); json.WriteRawValue(bytes, skipInputValidation: true); }
            Raw("atlas", atlasBytes); Raw("office", LayoutBytes); Raw("agents", agentBytes);
            Raw("warnings", warningBytes); Raw("assets", assetBytes); Raw("furnitureSprites", furnitureBytes);
            json.WriteEndObject();
        }
        // The caller finishes its synchronous atomic write before the next serialization.
        if (buffer.WrittenCount > 4 * 1024 * 1024) throw new InvalidDataException("State exceeds 4 MiB.");
        return buffer.WrittenMemory;
    }
}
