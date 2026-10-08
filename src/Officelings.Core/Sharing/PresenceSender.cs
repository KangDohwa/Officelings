namespace Officelings.Core.Sharing;

public enum SharingStatus { Off, Ready, Sending, Active, Backoff, ConsentExpired, CredentialError, Rejected, PayloadError }

public sealed class PresenceSender : IAsyncDisposable
{
    private readonly IPresenceTransport transport;
    private readonly ISharingTokenStore store;
    private readonly IPresenceAttemptGate attempts;
    private readonly Func<SharingPreview, DateTimeOffset, PresencePayload> snapshot;
    private readonly TimeProvider time;
    private readonly Func<double> random;
    private readonly Func<string?>? privacyMode;
    private readonly SemaphoreSlim sending = new(1, 1);
    private CancellationTokenSource cancellation = new();
    private SharingPreview? preview;
    private string? token;
    private bool first;
    private int failures;
    private long nextAttempt;
    public bool Enabled { get; private set; }
    public SharingStatus Status { get; private set; } = SharingStatus.Off;
    public TimeSpan Remaining => TimeSpan.FromSeconds(Math.Max(0, (nextAttempt - time.GetTimestamp()) / (double)time.TimestampFrequency));

    public PresenceSender(IPresenceTransport transport, ISharingTokenStore store, IPresenceAttemptGate attempts,
        Func<SharingPreview, DateTimeOffset, PresencePayload> snapshot, TimeProvider? time = null, Func<double>? random = null,
        Func<string?>? privacyMode = null)
    {
        this.transport = transport; this.store = store; this.attempts = attempts; this.snapshot = snapshot;
        this.time = time ?? TimeProvider.System; this.random = random ?? Random.Shared.NextDouble;
        this.privacyMode = privacyMode;
    }
    public bool Enable(SharingPreview consent, bool confirmed, string? suppliedToken, bool rememberToken)
    {
        if (!confirmed || Enabled || sending.CurrentCount == 0) return false;
        consent.RequireFresh(time.GetUtcNow());
        consent.RequirePrivacy(privacyMode == null ? consent.PrivacyMode : privacyMode());
        string? value;
        try { value = string.IsNullOrEmpty(suppliedToken) ? store.Read(consent.Endpoint, consent.OfficeId) : suppliedToken; }
        catch (Exception) { throw new SharingCredentialException(); }
        if (value == null) throw new SharingCredentialException();
        WindowsSharingTokenStore.ValidateToken(value);
        if (rememberToken)
            try { store.Save(consent.Endpoint, consent.OfficeId, value); }
            catch (Exception) { throw new SharingCredentialException(); }
        cancellation.Dispose(); cancellation = new();
        preview = consent; token = value; first = true; failures = 0;
        Enabled = true; Status = SharingStatus.Ready;
        // nextAttempt is retained when this sender is stopped and enabled again.
        return true;
    }
    public async Task TrySendAsync()
    {
        if (!Enabled || Remaining > TimeSpan.Zero || !await sending.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (!Enabled || token == null || preview == null || Remaining > TimeSpan.Zero) return;
            var activeToken = token;
            PresencePayload payload;
            var now = time.GetUtcNow();
            try { preview.RequirePrivacy(privacyMode == null ? preview.PrivacyMode : privacyMode()); }
            catch (Exception) { Halt(SharingStatus.ConsentExpired); return; }
            try
            {
                if (first) preview.RequireFresh(now);
                payload = first ? preview.Payload : snapshot(preview, now);
            }
            catch (Exception) { Halt(first ? SharingStatus.ConsentExpired : SharingStatus.PayloadError); return; }
            TimeSpan wait;
            try { wait = attempts.Reserve(activeToken, TimeSpan.FromSeconds(10)); }
            catch (Exception) { Halt(SharingStatus.CredentialError); return; }
            if (wait > TimeSpan.Zero) { Schedule(wait); Status = SharingStatus.Backoff; return; }
            Schedule(TimeSpan.FromSeconds(10)); // Every attempted request, including failures, consumes a slot.
            if (!Enabled || cancellation.IsCancellationRequested) return;
            try { preview.RequirePrivacy(privacyMode == null ? preview.PrivacyMode : privacyMode()); }
            catch (Exception) { Halt(SharingStatus.ConsentExpired); return; }
            var attemptedAt = time.GetTimestamp();
            first = false; Status = SharingStatus.Sending;
            PresenceResponse response;
            try { response = await transport.SendAsync(preview.Endpoint, activeToken, payload, now, cancellation.Token).ConfigureAwait(false); }
            catch (Exception) { response = new(0); }
            if (!Enabled) return;
            var jitter = Math.Clamp(random(), 0, 1);
            TimeSpan delay;
            if (response.StatusCode == 202)
            { failures = 0; Status = SharingStatus.Active; delay = TimeSpan.FromSeconds(10 + jitter * 20); }
            else if (response.StatusCode == 429)
            { Status = SharingStatus.Backoff; delay = response.RetryAfter is { } retry ? Max(TimeSpan.FromSeconds(10), retry) : TimeSpan.FromSeconds(20 + jitter * 10); }
            else if (response.StatusCode == 0 || response.StatusCode >= 500)
            { failures = Math.Min(failures + 1, 5); Status = SharingStatus.Backoff; delay = TimeSpan.FromSeconds(Math.Min(300, 10 * Math.Pow(2, failures) + jitter * 10)); }
            else { Halt(SharingStatus.Rejected); return; }
            // Successful heartbeat cadence is measured from the attempt, not from a slow response.
            Schedule(delay, response.StatusCode == 202 ? attemptedAt : null);
            try { attempts.Defer(activeToken, Remaining); }
            catch (Exception) { Halt(SharingStatus.CredentialError); }
        }
        finally { sending.Release(); }
    }
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    private void Schedule(TimeSpan delay, long? origin = null)
    {
        var timestamp = origin ?? time.GetTimestamp();
        var ticks = Math.Min(delay.TotalSeconds * time.TimestampFrequency, long.MaxValue - (double)timestamp);
        nextAttempt = Math.Max(nextAttempt, timestamp + (long)ticks);
    }
    private void Halt(SharingStatus status) { Enabled = false; Status = status; token = null; }
    public void Stop() { Enabled = false; Status = SharingStatus.Off; cancellation.Cancel(); token = null; }
    public async Task StopAsync(bool deleteToken = false)
    {
        Stop();
        await sending.WaitAsync().ConfigureAwait(false);
        try
        {
            if (deleteToken)
                try { store.Delete(); } catch (Exception) { throw new SharingCredentialException(); }
        }
        finally { sending.Release(); }
    }
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); cancellation.Dispose(); }
}
