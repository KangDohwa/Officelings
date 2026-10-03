using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Vibedotcom.Core;

try
{
    var options = new TrackerOptions();
    var output = TrackerOptions.OutputDirectory;
    double seconds = double.PositiveInfinity, interval = .2;
    for (var i = 0; i < args.Length; i++)
    {
        string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + args[i - 1]);
        double Number() => double.TryParse(Value(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n > 0
            ? n : throw new ArgumentException("Expected a positive finite number.");
        switch (args[i])
        {
            case "--help":
                Console.WriteLine("Vibedotcom.Tracker [--once | --seconds N] [--output DIR] [--home DIR] [--working-seconds N] [--gone-minutes N] [--scan-seconds N] [--interval-seconds N]\nRead-only observation. Default output: " + output);
                return;
            case "--output": output = Value(); break;
            case "--home": options = options with { Home = Path.GetFullPath(Value()) }; break;
            case "--once": seconds = 0; break;
            case "--seconds": seconds = Number(); break;
            case "--working-seconds": options = options with { WorkingWindow = TimeSpan.FromSeconds(Number()) }; break;
            case "--gone-minutes": options = options with { GoneAfter = TimeSpan.FromMinutes(Number()) }; break;
            case "--scan-seconds": options = options with { ScanInterval = TimeSpan.FromSeconds(Number()) }; break;
            case "--interval-seconds": interval = Number(); break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    options.Validate();
    output = StateJson.OutputPath(output, options.Home);
    var lockName = "Vibedotcom.Tracker." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToUpperInvariant())))[..24];
    using var mutex = new Mutex(true, lockName, out var owns);
    if (!owns) throw new IOException("Another tracker already writes this output directory.");
    StateJson.WriteAtomic(Path.Combine(output, "atlas.png"), SpriteAtlas.CreatePng());
    var tracker = new AgentTracker(options);
    var office = new OfficeAnimator();
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var started = DateTimeOffset.UtcNow;
    var previous = started;
    long sequence = 0;
    string lastCounts = "";
    do
    {
        var now = DateTimeOffset.UtcNow;
        var agents = tracker.Poll(now);
        var views = office.Update(agents, now, (now - previous).TotalSeconds);
        previous = now;
        StateJson.WriteAtomic(Path.Combine(output, "state.json"), new StateSnapshot(1, now, ++sequence, SpriteAtlas.Info, office.Layout, views, tracker.Warnings));
        var counts = string.Join(", ", Enum.GetValues<Provider>().Select(p => $"{p}={agents.Count(a => a.Provider == p && a.State != AgentState.Gone)}"));
        if (counts != lastCounts || sequence == 1)
        { Console.WriteLine($"{now:O} alive: {counts}; total={agents.Count}; bytesRead={tracker.BytesRead}"); lastCounts = counts; }
        if (seconds == 0 || (now - started).TotalSeconds >= seconds) break;
        try { await Task.Delay(TimeSpan.FromSeconds(interval), cancellation.Token); } catch (OperationCanceledException) { break; }
    } while (!cancellation.IsCancellationRequested);
    foreach (var warning in tracker.Warnings) Console.Error.WriteLine(warning);
    Console.WriteLine($"Wrote {sequence} snapshots to {output}; log bytes read={tracker.BytesRead}.");
}
catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
{ Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
