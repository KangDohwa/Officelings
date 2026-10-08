using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Officelings.Core.Providers;

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
                using var homeRequest = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/");
                var html = Body(homeRequest, 256 * 1024).GetAwaiter().GetResult();
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
            using var result = JsonDocument.Parse(Body(request, 4 * 1024 * 1024).GetAwaiter().GetResult());
            return Loaded(result.RootElement);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
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
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in summaries.EnumerateObject())
        {
            if (result.Count >= InputLimits.Agents || !InputLimits.ConversationId(item.Name) ||
                item.Value.ValueKind != JsonValueKind.Object || item.Value.GetRawText().Length > 65536 ||
                !result.TryAdd(item.Name, item.Value.Clone())) throw new JsonException("Invalid or oversized Antigravity summary.");
        }
        return result;
    }

    private async Task<string> Body(HttpRequestMessage request, int maximum)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new IOException("Loopback response byte budget exceeded.");
        using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation.Token)) != 0)
        {
            if (bytes.Length + count > maximum) throw new IOException("Loopback response byte budget exceeded.");
            bytes.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
    }

    public void Dispose() => client.Dispose();
}
