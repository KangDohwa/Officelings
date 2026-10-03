using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vibedotcom.Core.Providers;

internal sealed class AntigravityRuntime : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(1.5) };
    private readonly string logs;
    private readonly JsonlTail logTail = new();
    private int discoveredPort;
    private int port;
    private string? csrf;
    internal AntigravityRuntime(string logs) => this.logs = logs;

    // null = unavailable, empty map = a successful observation of no loaded conversations.
    internal Dictionary<string, JsonElement>? Read()
    {
        try
        {
            if (port == 0 || csrf == null)
            {
                var processes = Process.GetProcessesByName("language_server");
                var running = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                if (!running) return null;
                var text = string.Join('\n', logTail.Read(Path.Combine(logs, "language_server.log")));
                var matches = Regex.Matches(text, @"random port at (\d+) for HTTP\b");
                if (logTail.WasReset) discoveredPort = 0;
                if (matches.Count > 0) int.TryParse(matches[^1].Groups[1].Value, out discoveredPort);
                port = discoveredPort;
                if (port is < 1 or > 65535)
                { port = 0; return null; }
                var html = client.GetStringAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult();
                var config = Regex.Match(html, @"window\.__APP_CONFIG__\s*=\s*(\{[^;\r\n]+\})\s*;");
                if (!config.Success) { port = 0; return null; }
                using var doc = JsonDocument.Parse(config.Groups[1].Value);
                if (!(LogParser.String(doc.RootElement, "productName") ?? "").Contains("antigravity", StringComparison.OrdinalIgnoreCase))
                { port = 0; return null; }
                csrf = LogParser.String(doc.RootElement, "csrfToken");
                if (csrf == null) { port = 0; return null; }
            }
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"http://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetAllCascadeTrajectories");
            request.Headers.Add("x-codeium-csrf-token", csrf);
            request.Headers.Add("Connect-Protocol-Version", "1");
            request.Content = new StringContent("{\"excludeSubtrajectories\":false}", Encoding.UTF8, "application/json");
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return Loaded(result.RootElement);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { port = 0; csrf = null; return null; } // Tokens and response bodies never enter diagnostics.
    }

    internal static Dictionary<string, JsonElement> Loaded(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid Antigravity loaded-list root.");
        if (!root.TryGetProperty("trajectorySummaries", out var summaries))
        {
            if (root.ValueKind == JsonValueKind.Object && !root.EnumerateObject().Any()) return [];
            throw new JsonException("Unknown Antigravity loaded-list schema.");
        }
        if (summaries.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid Antigravity loaded-list map.");
        return summaries.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }

    public void Dispose() => client.Dispose();
}
