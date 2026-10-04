using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vibedotcom.Core;

public enum Provider { Codex, Claude, Antigravity }
public enum AgentState { Working, Thinking, Waiting, Reporting, Idle, Gone }

public sealed record TrackerOptions
{
    public PrivacyMode PrivacyMode { get; init; }
    public string Home { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public TimeSpan WorkingWindow { get; init; } = TimeSpan.FromMinutes(2);
    private TimeSpan? waiting;
    private TimeSpan legacyGoneAfter = TimeSpan.FromMinutes(30);
    public TimeSpan LeaveAfterIdle { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan LeaveAfterWaiting { get => waiting ?? legacyGoneAfter; init => waiting = value; }
    // Deprecated alias. An explicit waiting threshold wins, regardless of initializer order.
    public TimeSpan GoneAfter { get => LeaveAfterWaiting; init => legacyGoneAfter = value; }
    internal TimeSpan DiscoveryWindow => LeaveAfterIdle > LeaveAfterWaiting ? LeaveAfterIdle : LeaveAfterWaiting;
    public TimeSpan GoneRetention { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ReportingWindow { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromSeconds(3);
    public static string OutputDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vibedotcom");

    public void Validate()
    {
        if (WorkingWindow <= TimeSpan.Zero || LeaveAfterIdle <= WorkingWindow || LeaveAfterWaiting <= WorkingWindow || GoneRetention <= TimeSpan.Zero ||
            ReportingWindow <= TimeSpan.Zero || ScanInterval <= TimeSpan.Zero)
            throw new ArgumentException("Require 0 < working window < both leave timeouts, and positive intervals.");
    }
}

public sealed record AgentInfo(string Id, Provider Provider, string? ParentId, bool IsSubagent, string Name,
    string? Role, string? Model, string? Cwd, string Task, string? Tool, string? ToolKind,
    AgentState State, DateTimeOffset LastSeen, DateTimeOffset StateSince, bool BestEffort);

internal sealed class TrackedAgent(string id, Provider provider, PrivacyRedactor? redactor = null)
{
    private readonly PrivacyRedactor privacy = redactor ?? new();
    public string Id = id;
    public Provider Provider = provider;
    public string? ParentId;
    private string? role, model, cwd, tool, toolKind;
    private string name = (redactor ?? new()).Name(provider, id, provider.ToString()), task = "";
    public string? Role { get => role; set => role = privacy.Detail(value); }
    public string? Model { get => model; set => model = privacy.Model(value); }
    public string? Cwd { get => cwd; set => cwd = privacy.Cwd(value); }
    public string? Tool { get => tool; set => tool = privacy.Detail(value); }
    public string? ToolKind { get => toolKind; set => toolKind = privacy.Detail(value); }
    public string Name { get => name; set => name = privacy.Name(Provider, Id, value); }
    public string Task { get => task; set => task = privacy.Task(value); }

    internal void Scrub()
    {
        Name = name; Role = role; Model = model; Cwd = cwd; Task = task; Tool = tool; ToolKind = toolKind;
    }
    public bool IsSubagent, Finished;
    public AgentState EventState = AgentState.Idle, DisplayState = AgentState.Idle;
    public DateTimeOffset LastSeen, LastEvent, StateSince;
    public DateTimeOffset? ClosedAt;

    public void State(AgentState state, DateTimeOffset time)
    {
        if (time < LastEvent) return;
        LastEvent = time;
        if (EventState != state) StateSince = time;
        EventState = DisplayState = state;
        Finished = state == AgentState.Reporting;
        if (state != AgentState.Working) Tool = ToolKind = null;
    }

    public AgentInfo Snapshot(DateTimeOffset now, TrackerOptions options, bool hasActiveChildren = false)
    {
        var next = StateAt(now, options, hasActiveChildren);
        if (DisplayState != next) { DisplayState = next; StateSince = now; }
        return new(privacy.Id(Id)!, Provider, privacy.Id(ParentId), IsSubagent, Name, Role, Model, Cwd, Task,
            next == AgentState.Working ? Tool : null, next == AgentState.Working ? ToolKind : null,
            next, LastSeen, StateSince, Provider == Provider.Antigravity);
    }

    internal AgentState StateAt(DateTimeOffset now, TrackerOptions options, bool hasActiveChildren = false)
    {
        var next = EventState;
        var activity = LastEvent == default ? LastSeen : LastEvent;
        var timeout = EventState == AgentState.Waiting ? options.LeaveAfterWaiting : options.LeaveAfterIdle;
        if (ClosedAt is { } closed && now >= closed || EventState == AgentState.Gone) next = AgentState.Gone;
        else if (!hasActiveChildren && now - activity >= timeout) next = AgentState.Gone;
        else if (next == AgentState.Reporting && now - LastEvent >= options.ReportingWindow) next = AgentState.Idle;
        else if (next is AgentState.Working or AgentState.Thinking && now - activity >= options.WorkingWindow) next = AgentState.Idle;
        return next;
    }
}

public readonly record struct Tile(int X, int Y);
public readonly record struct Position(double X, double Y);
public sealed record Furniture(string Kind, int X, int Y, int Width, int Height, string? AgentId = null,
    string Id = "", string? AssetId = null, int Rotation = 0, string State = "default",
    string? DeskId = null, InteractionPoint? Seat = null, string? MonitorState = null);
public sealed record Zone(string Name, int X, int Y, int Width, int Height, IReadOnlyList<InteractionPoint>? Points = null);
public sealed record AutoDesks(string AssetId = "desk", int X = 2, int Y = 5, int SpacingX = 5, int SpacingY = 4,
    int MaxColumns = 4, bool AdaptiveColumns = true, bool Expand = true, int MinColumns = 1, int Capacity = 12);
public sealed record OverflowSeat(string Id, int X, int Y, string Facing, string? FurnitureId = null);
public sealed record OfficeLayout(int WidthTiles, int HeightTiles, int TileSize, Tile Door, IReadOnlyList<Furniture> Furniture,
    int Version = 1, IReadOnlyList<int>? Tiles = null, IReadOnlyList<Zone>? Zones = null, AutoDesks? AutoDesks = null,
    IReadOnlyList<OverflowSeat>? OverflowSeats = null);
public sealed record Pose(double X, double Y, Tile Desk, string Action, string Facing, int SpriteRow, int SpriteColumn, bool Visible,
    double Opacity = 1, bool CarryingPaper = false, bool ReportOnDesk = false, string? Reaction = null, string? ChatPartnerId = null,
    string? CharacterId = null, SpriteFrame? Sprite = null, string? DeskId = null, string? OverflowSeatId = null,
    string WorkLocation = "desk", bool Queued = false, string? DisplayName = null, string? ProviderIcon = null,
    string? SeatedVariant = null, PixelPoint? SeatAnchor = null, string? ChairBackId = null,
    string? ChairFrontId = null, string? SeatPhase = null, int SeatPhaseFrame = 0,
    string? ActivitySpotId = null, string? ActivityPartnerId = null);
public sealed record AgentView(AgentInfo Agent, Pose Pose);
public sealed record FurnitureSprite(string Id, SpriteFrame Sprite, string? MonitorState = null);
public sealed record PetView(string Id, string SheetId, double X, double Y, string Action, string Facing,
    SpriteFrame Sprite, string? FollowingId = null, string? FurnitureId = null);
public sealed record AtlasInfo(string File, int Width, int Height, int CellWidth, int CellHeight, int Columns, int Rows, int Version);
public sealed record StateSnapshot(int SchemaVersion, DateTimeOffset GeneratedAt, long Sequence,
    AtlasInfo Atlas, OfficeLayout Office, IReadOnlyList<AgentView> Agents, IReadOnlyList<string> Warnings, AssetManifest? Assets = null,
    IReadOnlyList<FurnitureSprite>? FurnitureSprites = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] AmbientState? Ambient = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PetView>? Pets = null,
    PrivacyMode Privacy = PrivacyMode.Off);

public static class StateJson
{
    public const int SchemaVersion = 5;
    public static string OutputPath(string path, string? sourceHome = null)
    {
        var output = Path.GetFullPath(path);
        var realHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var home in new[] { sourceHome ?? realHome, realHome }.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var name in new[] { ".codex", ".claude", ".gemini" })
            {
                var protectedRoot = Path.GetFullPath(Path.Combine(home, name));
                if (output.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase) || output.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Output must be outside provider directories.");
            }
        return output;
    }

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void WriteAtomic(string path, byte[] bytes) => WriteAtomic(path, bytes.AsSpan());

    public static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task PublishWithRetryAsync(Action publish, Action<string> log, CancellationToken cancellation = default,
        PrivacyMode privacy = PrivacyMode.Off)
    {
        var delay = 100; var logged = false;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { publish(); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (!logged) { log("Publication unavailable; retrying: " + PrivacyModes.Diagnostic(e, privacy)); logged = true; }
                await Task.Delay(delay, cancellation); delay = Math.Min(delay * 2, 2000);
            }
        }
    }

    public static void WriteAtomic(string path, StateSnapshot snapshot) =>
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(snapshot, Options));
}
