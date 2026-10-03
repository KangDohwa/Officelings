using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Vibedotcom.Core.Sharing;

namespace Vibedotcom.SharingChecks;

internal static partial class Program
{
    private const string Secret = "synthetic-only-bearer-secret";
    private sealed class ManualTime : TimeProvider
    {
        private long elapsed;
        public override DateTimeOffset GetUtcNow() => Now.AddTicks(elapsed);
        public override long GetTimestamp() => elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(double seconds) => elapsed += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class FakeStore : ISharingTokenStore
    {
        public int Reads, Saves, Deletes;
        public bool Throw;
        public void Save(SharingEndpoint endpoint, string officeId, string token) { Saves++; if (Throw) throw new IOException(Secret); }
        public string? Read(SharingEndpoint endpoint, string officeId) { Reads++; if (Throw) throw new IOException(Secret); return Secret; }
        public void Delete() { Deletes++; if (Throw) throw new IOException(Secret); }
    }
    private sealed class FakeGate : IPresenceAttemptGate
    {
        public TimeSpan Wait;
        public TimeSpan Reserve(string token, TimeSpan interval) => Wait;
        public void Defer(string token, TimeSpan interval) { }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(Uri? Uri, byte[] Bytes, string? Token, string? MediaType)> Requests = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Requests.Add((request.RequestUri, await request.Content!.ReadAsByteArrayAsync(cancellation), request.Headers.Authorization?.Parameter,
                request.Content.Headers.ContentType?.MediaType));
            return await Reply(request, cancellation);
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly ManualTime Time = new();
        public readonly Handler Handler = new();
        public readonly FakeStore Store = new();
        public readonly FakeGate Gate = new();
        public readonly SharingPreview Preview;
        public readonly PresenceSender Sender;
        public readonly HttpClient Client;
        public int Snapshots;
        public Fixture(double jitter = 0)
        {
            Preview = SharingPreview.Create(new() { SharingServer = "https://presence.example", SharingOfficeId = Office,
                SharingAliases = new() { ["codex:1"] = "Nova", ["claude:1"] = "Moon" } }, Time.GetUtcNow());
            Client = new(Handler);
            Sender = new(new PresenceTransport(Client), Store, Gate, (p, now) =>
            { Snapshots++; return PresencePayload.Create(p.OfficeId, PresenceProjection.FromLocal([Local()], p.Aliases), now, now); }, Time, () => jitter);
        }
        public void Enable() => Require(Sender.Enable(Preview, true, Secret, false));
        public async ValueTask DisposeAsync() { await Sender.DisposeAsync(); Client.Dispose(); }
    }
    private static async Task CheckAsync(string name, Func<Task> action)
    {
        try { await action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.GetType().Name); }
    }

    private static async Task TransportChecks()
    {
        foreach (var value in new[] { "http://presence.example", "https://user:token@presence.example", "https://presence.example/?token=secret",
            "https://presence.example/#secret", "https://presence.example/api/v1/presence", "file:///tmp", "ftp://localhost" })
            Check("unsafe endpoint rejected", () => Reject(() => SharingEndpoint.Parse(value)));
        foreach (var value in new[] { "https://presence.example", "http://127.0.0.1:5050", "http://localhost:5050", "http://[::1]:5050" })
            Check("secure or loopback origin accepted", () => Require(SharingEndpoint.Parse(value).Ingest.AbsolutePath == "/api/v1/presence"));
        await CheckAsync("off by default and unconfirmed preview sends nothing", async () =>
        {
            await using var f = new Fixture();
            await f.Sender.TrySendAsync(); Require(!f.Sender.Enabled && f.Handler.Requests.Count == 0 && f.Store.Reads == 0);
            Require(!f.Sender.Enable(f.Preview, false, Secret, true));
            await f.Sender.TrySendAsync(); Require(f.Handler.Requests.Count == 0 && f.Store.Saves == 0);
        });
        await CheckAsync("consent bytes are exactly sent with token only in header", async () =>
        {
            await using var f = new Fixture(); f.Enable(); await f.Sender.TrySendAsync();
            var request = f.Handler.Requests.Single();
            Require(request.Bytes.SequenceEqual(Encoding.UTF8.GetBytes(f.Preview.Payload.Json)) && request.Token == Secret);
            Require(request.MediaType == "application/json" && !request.Uri!.AbsoluteUri.Contains(Secret) && !f.Preview.Payload.Json.Contains(Secret));
            Require(f.Snapshots == 0);
            var copy = f.Preview.Payload.CopyBytes(); copy[0] = 0;
            Require(f.Preview.Payload.CopyBytes()[0] == '{');
        });
        foreach (var jitter in new[] { 0d, 1d })
            await CheckAsync("jitter bounds and unchanged complete heartbeat", async () =>
            {
                await using var f = new Fixture(jitter); f.Enable(); await f.Sender.TrySendAsync();
                Require(f.Sender.Remaining.TotalSeconds == 10 + jitter * 20);
                f.Time.Advance(9); await f.Sender.TrySendAsync(); Require(f.Handler.Requests.Count == 1);
                f.Time.Advance(1 + jitter * 20); await f.Sender.TrySendAsync(); Require(f.Handler.Requests.Count == 2 && f.Snapshots == 1);
            });
        await CheckAsync("concurrent calls make one authenticated attempt", async () =>
        {
            await using var f = new Fixture(); f.Enable(); await Task.WhenAll(f.Sender.TrySendAsync(), f.Sender.TrySendAsync());
            Require(f.Handler.Requests.Count == 1);
        });
        foreach (var date in new[] { false, true })
            await CheckAsync("429 respects Retry-After delta and date", async () =>
            {
                await using var f = new Fixture();
                f.Handler.Reply = (_, _) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = date ? new RetryConditionHeaderValue(f.Time.GetUtcNow().AddSeconds(60)) : new(TimeSpan.FromSeconds(60));
                    return Task.FromResult(response);
                };
                f.Enable(); await f.Sender.TrySendAsync(); Require(f.Sender.Status == SharingStatus.Backoff);
                f.Time.Advance(59); await f.Sender.TrySendAsync(); Require(f.Handler.Requests.Count == 1);
                f.Time.Advance(1); await f.Sender.TrySendAsync(); Require(f.Handler.Requests.Count == 2);
            });
        await CheckAsync("short Retry-After still respects 10-second minimum", async () =>
        {
            await using var f = new Fixture();
            f.Handler.Reply = (_, _) => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromSeconds(1)); return Task.FromResult(r); };
            f.Enable(); await f.Sender.TrySendAsync(); Require(f.Sender.Remaining >= TimeSpan.FromSeconds(10));
        });
        await CheckAsync("network errors cannot expose token and use exponential backoff", async () =>
        {
            await using var f = new Fixture(); f.Handler.Reply = (_, _) => throw new HttpRequestException("Authorization: " + Secret);
            f.Enable(); await f.Sender.TrySendAsync(); Require(f.Sender.Remaining.TotalSeconds == 20);
            f.Time.Advance(20); await f.Sender.TrySendAsync(); Require(f.Sender.Remaining.TotalSeconds == 40);
            Require(f.Sender.Status == SharingStatus.Backoff && !f.Sender.Status.ToString().Contains(Secret));
        });
        foreach (var status in new[] { 400, 401, 403, 413, 415, 302 })
            await CheckAsync("rejection stops without echoing server body", async () =>
            {
                await using var f = new Fixture(); f.Handler.Reply = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(Secret) });
                f.Enable(); await f.Sender.TrySendAsync(); Require(!f.Sender.Enabled && f.Sender.Status == SharingStatus.Rejected);
                f.Time.Advance(100); await f.Sender.TrySendAsync(); Require(f.Handler.Requests.Count == 1);
            });
        await CheckAsync("stop cancels in-flight send and optionally deletes token", async () =>
        {
            await using var f = new Fixture(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Handler.Reply = async (_, cancellation) => { started.SetResult(); await Task.Delay(Timeout.Infinite, cancellation); return new(HttpStatusCode.Accepted); };
            f.Enable(); var send = f.Sender.TrySendAsync(); await started.Task;
            await f.Sender.StopAsync(true); await send;
            Require(!f.Sender.Enabled && f.Sender.Status == SharingStatus.Off && f.Store.Deletes == 1);
            f.Time.Advance(100); await f.Sender.TrySendAsync(); Require(f.Handler.Requests.Count == 1);
        });
        await CheckAsync("stop/re-enable cannot erase local cooldown", async () =>
        {
            await using var f = new Fixture(); f.Enable(); await f.Sender.TrySendAsync(); await f.Sender.StopAsync();
            f.Enable(); await f.Sender.TrySendAsync(); Require(f.Handler.Requests.Count == 1);
        });
        await CheckAsync("expired consent and credential exceptions never send or leak", async () =>
        {
            await using var f = new Fixture(); f.Time.Advance(91); Reject(() => f.Sender.Enable(f.Preview, true, Secret, true)); Require(f.Store.Saves == 0);
            await using var g = new Fixture(); g.Store.Throw = true;
            try { g.Sender.Enable(g.Preview, true, null, false); throw new InvalidOperationException(); }
            catch (SharingCredentialException e) { Require(!e.ToString().Contains(Secret) && e.InnerException == null); }
            Require(g.Handler.Requests.Count == 0);
        });
        await CheckAsync("shared gate postpones an unsent preview", async () =>
        {
            await using var f = new Fixture(); f.Gate.Wait = TimeSpan.FromSeconds(60); f.Enable(); await f.Sender.TrySendAsync();
            Require(f.Handler.Requests.Count == 0 && f.Sender.Remaining.TotalSeconds == 60);
            f.Time.Advance(91); await f.Sender.TrySendAsync(); Require(!f.Sender.Enabled && f.Sender.Status == SharingStatus.ConsentExpired);
        });
        Check("DPAPI roundtrip, binding, deletion and corruption use synthetic credentials", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "vibedotcom-sharing-token-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new WindowsSharingTokenStore(directory); var endpoint = SharingEndpoint.Parse("https://presence.example");
                store.Save(endpoint, Office, Secret); Require(store.Read(endpoint, Office) == Secret);
                Require(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "presence.dpapi"))).Contains(Secret));
                try { store.Read(SharingEndpoint.Parse("https://different.example"), Office); throw new InvalidOperationException(); }
                catch (SharingCredentialException e) { Require(!e.ToString().Contains(Secret)); }
                File.WriteAllBytes(Path.Combine(directory, "presence.dpapi"), [1, 2, 3]);
                try { store.Read(endpoint, Office); throw new InvalidOperationException(); }
                catch (SharingCredentialException e) { Require(!e.ToString().Contains(Secret)); }
                store.Delete(); Require(store.Read(endpoint, Office) == null);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        Check("persistent gate preserves Retry-After across sender instances", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "vibedotcom-sharing-gate-" + Guid.NewGuid().ToString("N"));
            try
            {
                var gate = new FilePresenceAttemptGate(directory); Require(gate.Reserve(Secret, TimeSpan.FromSeconds(10)) == TimeSpan.Zero);
                Require(new FilePresenceAttemptGate(directory).Reserve(Secret, TimeSpan.FromSeconds(10)) > TimeSpan.FromSeconds(8));
                gate.Defer(Secret, TimeSpan.FromSeconds(60));
                Require(new FilePresenceAttemptGate(directory).Reserve(Secret, TimeSpan.FromSeconds(10)) > TimeSpan.FromSeconds(58));
                Require(Directory.GetFiles(directory).All(p => !p.Contains(Secret) && !File.ReadAllText(p).Contains(Secret)));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
}
