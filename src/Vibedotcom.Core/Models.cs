using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vibedotcom.Core;

public enum Provider { Codex, Claude, Antigravity }
public enum AgentState { Working, Thinking, Waiting, Reporting, Idle, Gone }

public sealed record TrackerOptions
{
    public string Home { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public TimeSpan WorkingWindow { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan GoneAfter { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan GoneRetention { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ReportingWindow { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromSeconds(3);
    public static string OutputDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vibedotcom");

    public void Validate()
    {
        if (WorkingWindow <= TimeSpan.Zero || GoneAfter <= WorkingWindow || GoneRetention <= TimeSpan.Zero ||
            ReportingWindow <= TimeSpan.Zero || ScanInterval <= TimeSpan.Zero)
            throw new ArgumentException("Require 0 < working window < gone timeout, and positive intervals.");
    }
}

public sealed record AgentInfo(string Id, Provider Provider, string? ParentId, bool IsSubagent, string Name,
    string? Role, string? Model, string? Cwd, string Task, string? Tool, string? ToolKind,
    AgentState State, DateTimeOffset LastSeen, DateTimeOffset StateSince, bool BestEffort);

internal sealed class TrackedAgent(string id, Provider provider)
{
    public string Id = id;
    public Provider Provider = provider;
    public string? ParentId, Role, Model, Cwd, Tool, ToolKind;
    public string Name = provider.ToString();
    public string Task = "";
    public bool IsSubagent, Finished;
    public AgentState EventState = AgentState.Idle, DisplayState = AgentState.Idle;
    public DateTimeOffset LastSeen, LastEvent, StateSince;

    public void State(AgentState state, DateTimeOffset time)
    {
        if (time < LastEvent) return;
        LastEvent = time;
        if (EventState != state) StateSince = time;
        EventState = DisplayState = state;
        Finished = state == AgentState.Reporting;
        if (state != AgentState.Working) Tool = ToolKind = null;
    }

    public AgentInfo Snapshot(DateTimeOffset now, TrackerOptions options)
    {
        var next = EventState;
        if (now - LastSeen >= options.GoneAfter || EventState == AgentState.Gone) next = AgentState.Gone;
        else if (next == AgentState.Reporting && now - LastEvent >= options.ReportingWindow) next = AgentState.Idle;
        else if (next is AgentState.Working or AgentState.Thinking && now - LastSeen >= options.WorkingWindow) next = AgentState.Idle;
        if (DisplayState != next) { DisplayState = next; StateSince = now; }
        return new(Id, Provider, ParentId, IsSubagent, Name, Role, Model, Cwd, Task,
            next == AgentState.Working ? Tool : null, next == AgentState.Working ? ToolKind : null,
            next, LastSeen, StateSince, Provider == Provider.Antigravity);
    }
}

public readonly record struct Tile(int X, int Y);
public readonly record struct Position(double X, double Y);
public sealed record Furniture(string Kind, int X, int Y, int Width, int Height, string? AgentId = null,
    string Id = "", string? AssetId = null, int Rotation = 0, string State = "default");
public sealed record Zone(string Name, int X, int Y, int Width, int Height, IReadOnlyList<InteractionPoint>? Points = null);
public sealed record AutoDesks(string AssetId = "desk", int X = 2, int Y = 5, int SpacingX = 5, int SpacingY = 4,
    int MaxColumns = 4, bool AdaptiveColumns = true, bool Expand = true, int MinColumns = 1);
public sealed record OfficeLayout(int WidthTiles, int HeightTiles, int TileSize, Tile Door, IReadOnlyList<Furniture> Furniture,
    int Version = 1, IReadOnlyList<int>? Tiles = null, IReadOnlyList<Zone>? Zones = null, AutoDesks? AutoDesks = null);
public sealed record Pose(double X, double Y, Tile Desk, string Action, string Facing, int SpriteRow, int SpriteColumn, bool Visible,
    double Opacity = 1, bool CarryingPaper = false, bool ReportOnDesk = false, string? Reaction = null, string? ChatPartnerId = null,
    string? CharacterId = null, SpriteFrame? Sprite = null);
public sealed record AgentView(AgentInfo Agent, Pose Pose);
public sealed record FurnitureSprite(string Id, SpriteFrame Sprite);
public sealed record AtlasInfo(string File, int Width, int Height, int CellWidth, int CellHeight, int Columns, int Rows, int Version);
public sealed record StateSnapshot(int SchemaVersion, DateTimeOffset GeneratedAt, long Sequence,
    AtlasInfo Atlas, OfficeLayout Office, IReadOnlyList<AgentView> Agents, IReadOnlyList<string> Warnings, AssetManifest? Assets = null,
    IReadOnlyList<FurnitureSprite>? FurnitureSprites = null);

public static class StateJson
{
    public const int SchemaVersion = 3;
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

    public static void WriteAtomic(string path, byte[] bytes)
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

    public static void WriteAtomic(string path, StateSnapshot snapshot) =>
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(snapshot, Options));
}
