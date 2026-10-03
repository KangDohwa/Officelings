using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vibedotcom.Core;

internal static class LogParser
{
    internal static string? String(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() : null;
    private static JsonElement Get(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? item : default;
    private static IEnumerable<JsonElement> Items(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    internal static string Key(Provider provider, string id) => provider.ToString().ToLowerInvariant() + ":" + id;
    private static string Clip(string? text) => Regex.Replace(text ?? "", @"\s+", " ").Trim() is var clean
        ? clean[..Math.Min(clean.Length, 240)] : "";
    private static string Text(JsonElement content) => content.ValueKind == JsonValueKind.String ? content.GetString()! :
        string.Join(" ", Items(content).Where(b => String(b, "type") is "text" or "input_text" or "output_text")
            .Select(b => String(b, "text")));

    internal static string Identity(Provider provider, string path, JsonElement root)
    {
        if (provider == Provider.Codex && String(root, "type") == "session_meta")
            return Key(provider, String(Get(root, "payload"), "id") ?? String(Get(root, "payload"), "session_id") ?? Path.GetFileNameWithoutExtension(path));
        if (provider == Provider.Claude)
        {
            var agentId = String(root, "agentId");
            var filename = Path.GetFileNameWithoutExtension(path);
            if (agentId != null || filename.StartsWith("agent-", StringComparison.Ordinal))
            {
                var parent = ParentFromClaudePath(path) ?? String(root, "sessionId") ?? "unknown";
                return Key(provider, parent + "/agent-" + (agentId ?? filename[6..]));
            }
            return Key(provider, String(root, "sessionId") ?? filename);
        }
        if (provider == Provider.Antigravity)
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (id.StartsWith("transcript", StringComparison.Ordinal))
                id = new DirectoryInfo(Path.GetDirectoryName(path)!).Parent!.Parent!.Name;
            return Key(provider, id);
        }
        // Codex rollout filename ends in a UUID; metadata normally replaces this fallback.
        var match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$", RegexOptions.IgnoreCase);
        return Key(provider, match.Success ? match.Value : Path.GetFileNameWithoutExtension(path));
    }

    private static string? ParentFromClaudePath(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return directory.Name == "subagents" ? directory.Parent?.Name : null;
    }

    internal static void Apply(TrackedAgent agent, JsonElement root, string path, DateTimeOffset fileTime)
    {
        var timestamp = String(root, "timestamp") ?? String(root, "created_at");
        var time = DateTimeOffset.TryParse(timestamp, out var parsed) ? parsed : fileTime;
        if (time > fileTime.AddMinutes(5)) time = fileTime;
        var type = String(root, "type");
        switch (agent.Provider)
        {
            case Provider.Codex: Codex(agent, root, type, time); break;
            case Provider.Claude: Claude(agent, root, type, path, time); break;
            case Provider.Antigravity: Antigravity(agent, root, type, time); break;
        }
    }

    private static void Codex(TrackedAgent a, JsonElement root, string? type, DateTimeOffset time)
    {
        var p = Get(root, "payload");
        if (type == "session_meta")
        {
            var spawn = Get(Get(Get(p, "source"), "subagent"), "thread_spawn");
            var parent = String(p, "parent_thread_id") ?? String(spawn, "parent_thread_id");
            a.ParentId = parent == null ? null : Key(Provider.Codex, parent);
            a.IsSubagent = parent != null || Get(Get(p, "source"), "subagent").ValueKind == JsonValueKind.Object;
            a.Name = String(p, "agent_nickname") ?? String(spawn, "agent_nickname") ?? (a.IsSubagent ? "Codex worker" : "Codex");
            a.Role = String(p, "agent_role") ?? String(spawn, "agent_role");
            a.Cwd = String(p, "cwd");
            a.Model = String(p, "model");
            return;
        }
        if (type == "turn_context") { a.Model = String(p, "model") ?? a.Model; a.Cwd = String(p, "cwd") ?? a.Cwd; return; }
        var kind = String(p, "type");
        if (type == "event_msg")
        {
            switch (kind)
            {
                case "user_message": a.Task = Clip(String(p, "message")); a.State(AgentState.Thinking, time); break;
                case "task_started": a.State(AgentState.Thinking, time); break;
                case "agent_reasoning": a.State(AgentState.Thinking, time); break;
                case "task_complete": case "task_completed":
                    a.State(a.IsSubagent ? AgentState.Reporting : AgentState.Waiting, time); break;
                case "turn_aborted": a.State(AgentState.Waiting, time); break;
                case "session_end": case "session_closed": a.State(AgentState.Gone, time); break;
                case "approval_request": case "request_user_input": a.State(AgentState.Waiting, time); break;
                case "item_completed":
                    var item = Get(p, "item");
                    if (String(item, "type") == "agent_message" && String(item, "phase") == "final_answer")
                        a.State(a.IsSubagent ? AgentState.Reporting : AgentState.Waiting, time);
                    break;
            }
        }
        if (type != "response_item") return;
        switch (kind)
        {
            case "reasoning": a.State(AgentState.Thinking, time); break;
            case "function_call": case "custom_tool_call":
                var name = String(p, "name") ?? "tool";
                if (name == "exec")
                {
                    var nested = Regex.Match(String(p, "input") ?? "", @"tools\.([a-zA-Z0-9_]+)\s*\(");
                    if (nested.Success) name = nested.Groups[1].Value;
                }
                Tool(a, name, time);
                break;
            case "function_call_output": case "custom_tool_call_output": a.State(AgentState.Thinking, time); break;
            case "message":
                if (String(p, "role") == "user") { a.Task = Clip(Text(Get(p, "content"))); a.State(AgentState.Thinking, time); }
                if (String(p, "role") == "assistant")
                    a.State(String(p, "phase") == "final_answer" || String(p, "channel") == "final"
                        ? a.IsSubagent ? AgentState.Reporting : AgentState.Waiting : AgentState.Thinking, time);
                break;
        }
    }

    private static void Claude(TrackedAgent a, JsonElement root, string? type, string path, DateTimeOffset time)
    {
        var agentId = String(root, "agentId");
        var sidechain = Get(root, "isSidechain").ValueKind == JsonValueKind.True;
        var parent = ParentFromClaudePath(path);
        a.IsSubagent |= sidechain || agentId != null || parent != null;
        if (a.IsSubagent)
        {
            var session = parent ?? String(root, "sessionId");
            if (session != null) a.ParentId = Key(Provider.Claude, session);
            a.Name = agentId == null ? "Claude worker" : "Claude " + agentId;
        }
        a.Cwd = String(root, "cwd") ?? a.Cwd;
        if (type == "last-prompt") { a.Task = Clip(String(root, "lastPrompt")); return; }
        if (type is "summary" or "file-history-snapshot" or "progress" or "queue-operation") return;
        if (type == "system")
        {
            if (String(root, "subtype") == "turn_duration") a.State(a.IsSubagent ? AgentState.Reporting : AgentState.Waiting, time);
            return;
        }
        var message = Get(root, "message");
        a.Model = String(message, "model") ?? a.Model;
        var content = Get(message, "content");
        if (type == "user")
        {
            if (Items(content).Any(b => String(b, "type") == "tool_result")) a.State(AgentState.Thinking, time);
            else { a.Task = Clip(Text(content)); a.State(AgentState.Thinking, time); }
        }
        if (type != "assistant") return;
        var tools = Items(content).Where(b => String(b, "type") == "tool_use").ToArray();
        if (tools.Length > 0) Tool(a, String(tools[^1], "name") ?? "tool", time);
        else if (String(message, "stop_reason") == "end_turn") a.State(a.IsSubagent ? AgentState.Reporting : AgentState.Waiting, time);
        else a.State(AgentState.Thinking, time);
    }

    private static void Antigravity(TrackedAgent a, JsonElement root, string? type, DateTimeOffset time)
    {
        var status = String(root, "status");
        if (status is "WAITING_FOR_USER" or "WAITING_FOR_APPROVAL") { a.State(AgentState.Waiting, time); return; }
        switch (type)
        {
            case "USER_INPUT": a.Task = Clip(String(root, "content")); a.State(AgentState.Thinking, time); break;
            case "PLANNER_RESPONSE":
                var tools = Items(Get(root, "tool_calls")).ToArray();
                if (tools.Length > 0) Tool(a, String(tools[^1], "name") ?? "tool", time);
                else a.State(String(root, "content") != null && status == "DONE" ? AgentState.Waiting : AgentState.Thinking, time);
                break;
            case "GENERIC": a.State(AgentState.Thinking, time); break;
            default:
                if (status is "RUNNING" or "IN_PROGRESS") a.State(AgentState.Working, time);
                break;
        }
    }

    private static void Tool(TrackedAgent a, string name, DateTimeOffset time)
    {
        if (time < a.LastEvent) return;
        var lower = name.ToLowerInvariant();
        a.State(lower.Contains("request_user_input") || lower.Contains("askuserquestion") ? AgentState.Waiting : AgentState.Working, time);
        if (a.EventState != AgentState.Working) return;
        a.Tool = name;
        a.ToolKind = lower.Contains("exec") || lower.Contains("bash") || lower.Contains("shell") || lower.Contains("command") ? "shell" :
            lower.Contains("spawn") || lower.Contains("agent") || lower is "task" or "wait" ? "agent" :
            lower.Contains("search") || lower.Contains("web") || lower.Contains("fetch") || lower is "grep" or "glob" ? "search" :
            lower.Contains("read") || lower.Contains("write") || lower.Contains("edit") || lower.Contains("patch") ? "file" : "other";
    }
}
