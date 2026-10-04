using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Vibedotcom.Core;

internal static class LogParser
{
    private sealed class Context
    {
        public DateTimeOffset MetadataTime, ContextTime;
        public readonly HashSet<string> Questions = [];
    }
    private static readonly ConditionalWeakTable<TrackedAgent, Context> contexts = new();
    internal static string? String(JsonElement value, string key)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.String) return null;
        var text = item.GetString()!;
        var maximum = key switch { "id" or "session_id" or "sessionId" or "agentId" or "parent_thread_id" or "parentConversationId" => 128,
            "agent_nickname" or "agent_role" or "agent_type" or "model" or "name" => 128, "cwd" => 1024, _ => 4096 };
        return text[..Math.Min(text.Length, maximum)];
    }
    private static JsonElement Get(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? item : default;
    private static IEnumerable<JsonElement> Items(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    internal static string Key(Provider provider, string id) => provider.ToString().ToLowerInvariant() + ":" + id;
    internal static string Clip(string? text) => Regex.Replace(text == null ? "" : text[..Math.Min(text.Length, 4096)], @"\s+", " ").Trim() is var clean
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
            {
                var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
                while (directory.Parent != null && directory.Parent.Name != "brain") directory = directory.Parent;
                id = directory.Name;
            }
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
        var time = ParseTime(timestamp) ?? fileTime;
        if (time > fileTime) time = fileTime;
        var type = String(root, "type");
        switch (agent.Provider)
        {
            case Provider.Codex: Codex(agent, root, type, time); break;
            case Provider.Claude: Claude(agent, root, type, path, time); break;
            case Provider.Antigravity: Antigravity(agent, root, type, time); break;
        }
    }

    // Offset-free provider timestamps are treated as UTC, independent of OS culture/timezone.
    internal static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            && parsed.Year > 1 ? parsed : null;

    internal static void SetState(TrackedAgent a, AgentState state, DateTimeOffset time)
    {
        if (time < a.LastEvent) return;
        if (a.DisplayState != state) a.StateSince = time;
        a.State(state, time);
    }

    private static void ContextFields(TrackedAgent a, string? model, string? cwd, DateTimeOffset time)
    {
        var context = contexts.GetOrCreateValue(a);
        var latest = time >= context.ContextTime && time >= a.LastEvent;
        if (model != null && (latest || a.Model == null)) a.Model = model;
        if (cwd != null && (latest || a.Cwd == null)) a.Cwd = cwd;
        if (latest && (model != null || cwd != null)) context.ContextTime = time;
    }

    private static void Codex(TrackedAgent a, JsonElement root, string? type, DateTimeOffset time)
    {
        var p = Get(root, "payload");
        if (type == "session_meta")
        {
            var spawn = Get(Get(Get(p, "source"), "subagent"), "thread_spawn");
            var parent = String(p, "parent_thread_id") ?? String(spawn, "parent_thread_id");
            var context = contexts.GetOrCreateValue(a);
            var latest = time >= context.MetadataTime;
            if (parent != null && Key(Provider.Codex, parent) != a.Id && (latest || a.ParentId == null))
                a.ParentId = Key(Provider.Codex, parent);
            a.IsSubagent |= parent != null || Get(Get(p, "source"), "subagent").ValueKind is JsonValueKind.Object or JsonValueKind.String;
            var name = String(p, "agent_nickname") ?? String(spawn, "agent_nickname");
            var role = String(p, "agent_role") ?? String(p, "agent_type") ?? String(spawn, "agent_role") ?? String(spawn, "agent_type");
            if (name != null && (latest || a.Name is "Codex" or "Codex worker")) a.Name = name;
            else if (a.IsSubagent && a.Name == "Codex") a.Name = "Codex worker";
            if (role != null && (latest || a.Role == null)) a.Role = role;
            if (latest) context.MetadataTime = time;
            ContextFields(a, String(p, "model"), String(p, "cwd"), time);
            return;
        }
        if (type == "turn_context") { ContextFields(a, String(p, "model"), String(p, "cwd"), time); return; }
        if (time < a.LastEvent) return;
        var kind = String(p, "type");
        if (type == "event_msg")
        {
            switch (kind)
            {
                case "user_message": a.Task = Clip(String(p, "message")); SetState(a, AgentState.Thinking, time); break;
                case "task_started": SetState(a, AgentState.Thinking, time); break;
                case "agent_reasoning": SetState(a, AgentState.Thinking, time); break;
                case "task_complete": case "task_completed":
                    Complete(a, time); break;
                case "turn_aborted": SetState(a, AgentState.Waiting, time); break;
                case "session_end": case "session_closed": case "shutdown": case "shutdown_complete":
                    SetState(a, AgentState.Gone, time); break;
                case "approval_request": case "request_user_input": SetState(a, AgentState.Waiting, time); break;
                case "item_completed":
                    var item = Get(p, "item");
                    if (String(item, "type") == "agent_message" && String(item, "phase") == "final_answer")
                        Complete(a, time);
                    break;
            }
        }
        if (type != "response_item") return;
        switch (kind)
        {
            case "reasoning": SetState(a, AgentState.Thinking, time); break;
            case "function_call": case "custom_tool_call":
                var name = String(p, "name") ?? "tool";
                if (name == "exec")
                {
                    var nested = Regex.Match(String(p, "input") ?? "", @"tools\.([a-zA-Z0-9_]+)\s*\(");
                    if (nested.Success) name = nested.Groups[1].Value;
                }
                Tool(a, name, time);
                break;
            case "function_call_output": case "custom_tool_call_output": SetState(a, AgentState.Thinking, time); break;
            case "message":
                if (String(p, "role") == "user") { a.Task = Clip(Text(Get(p, "content"))); SetState(a, AgentState.Thinking, time); }
                if (String(p, "role") == "assistant")
                {
                    if (String(p, "phase") == "final_answer" || String(p, "channel") == "final") Complete(a, time);
                    else SetState(a, AgentState.Thinking, time);
                }
                break;
        }
    }

    private static void Claude(TrackedAgent a, JsonElement root, string? type, string path, DateTimeOffset time)
    {
        var agentId = String(root, "agentId");
        var parent = ParentFromClaudePath(path);
        a.IsSubagent |= agentId != null || parent != null || Path.GetFileName(path).StartsWith("agent-", StringComparison.Ordinal);
        if (a.IsSubagent)
        {
            var session = parent ?? String(root, "sessionId");
            if (session != null && Key(Provider.Claude, session) != a.Id) a.ParentId ??= Key(Provider.Claude, session);
            if (a.Name == "Claude" || agentId != null) a.Name = agentId == null ? "Claude worker" : "Claude " + agentId;
        }
        var message = Get(root, "message");
        ContextFields(a, String(message, "model"), String(root, "cwd"), time);
        if (time < a.LastEvent) return;
        if (type == "last-prompt") { a.Task = Clip(String(root, "lastPrompt")); return; }
        if (type is "summary" or "file-history-snapshot" or "progress" or "queue-operation") return;
        if (type == "system")
        {
            if (String(root, "subtype") == "turn_duration" && contexts.GetOrCreateValue(a).Questions.Count == 0) Complete(a, time);
            return;
        }
        var content = Get(message, "content");
        var questions = contexts.GetOrCreateValue(a).Questions;
        if (type == "user")
        {
            var results = Items(content).Where(b => String(b, "type") == "tool_result").ToArray();
            foreach (var result in results) if (String(result, "tool_use_id") is { } id) questions.Remove(id);
            if (results.Length > 0) SetState(a, questions.Count > 0 ? AgentState.Waiting : AgentState.Thinking, time);
            else { a.Task = Clip(Text(content)); SetState(a, AgentState.Thinking, time); }
        }
        if (type != "assistant") return;
        var tools = Items(content).Where(b => String(b, "type") == "tool_use").ToArray();
        foreach (var tool in tools)
            if ((String(tool, "name") ?? "").Contains("AskUserQuestion", StringComparison.OrdinalIgnoreCase))
                { if (questions.Count < 64) questions.Add(String(tool, "id") ?? "unknown-question"); }
        if (questions.Count > 0) SetState(a, AgentState.Waiting, time);
        else if (tools.Length > 0) Tool(a, String(tools[^1], "name") ?? "tool", time);
        else if (String(message, "stop_reason") == "end_turn") Complete(a, time);
        else SetState(a, AgentState.Thinking, time);
    }

    private static void Antigravity(TrackedAgent a, JsonElement root, string? type, DateTimeOffset time)
    {
        if (time < a.LastEvent) return;
        var status = String(root, "status");
        if (status is "WAITING_FOR_USER" or "WAITING_FOR_APPROVAL") { SetState(a, AgentState.Waiting, time); return; }
        switch (type)
        {
            case "USER_INPUT": a.Task = Clip(String(root, "content")); SetState(a, AgentState.Thinking, time); break;
            case "PLANNER_RESPONSE":
                var tools = Items(Get(root, "tool_calls")).ToArray();
                if (tools.Length > 0) Tool(a, String(tools[^1], "name") ?? "tool", time);
                else if (String(root, "content") != null && status == "DONE") Complete(a, time);
                else SetState(a, AgentState.Thinking, time);
                break;
            case "GENERIC": SetState(a, AgentState.Thinking, time); break;
            default:
                if (status is "RUNNING" or "IN_PROGRESS") SetState(a, AgentState.Working, time);
                break;
        }
    }

    private static void Complete(TrackedAgent a, DateTimeOffset time)
    {
        if (time < a.LastEvent) return;
        SetState(a, a.IsSubagent ? AgentState.Reporting : AgentState.Idle, time);
        a.Finished = true;
    }

    private static void Tool(TrackedAgent a, string name, DateTimeOffset time)
    {
        if (time < a.LastEvent) return;
        var lower = name.ToLowerInvariant();
        SetState(a, lower.Contains("request_user_input") || lower.Contains("askuserquestion") ? AgentState.Waiting : AgentState.Working, time);
        if (a.EventState != AgentState.Working) return;
        a.Tool = name;
        a.ToolKind = lower.Contains("exec") || lower.Contains("bash") || lower.Contains("shell") || lower.Contains("command") ? "shell" :
            lower.Contains("spawn") || lower.Contains("agent") || lower is "task" or "wait" ? "agent" :
            lower.Contains("search") || lower.Contains("web") || lower.Contains("fetch") || lower is "grep" or "glob" ? "search" :
            lower.Contains("read") || lower.Contains("write") || lower.Contains("edit") || lower.Contains("patch") ? "file" : "other";
    }
}
