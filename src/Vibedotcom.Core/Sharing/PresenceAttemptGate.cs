using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vibedotcom.Core.Sharing;

public interface IPresenceAttemptGate
{
    TimeSpan Reserve(string token, TimeSpan interval);
    void Defer(string token, TimeSpan interval);
}

// Shared by App and Tracker and retained across stop/re-enable/restart. Contains timing only, never a token.
public sealed class FilePresenceAttemptGate(string directory) : IPresenceAttemptGate
{
    private sealed record Deadline(long RecordedUptime, long Uptime, long Utc);
    public TimeSpan Reserve(string token, TimeSpan interval) => Access(token, interval, reserve: true);
    public void Defer(string token, TimeSpan interval) => Access(token, interval, reserve: false);
    private TimeSpan Access(string token, TimeSpan interval, bool reserve)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        using var mutex = new Mutex(false, "Local\\Vibedotcom.Sharing." + key);
        var owned = false;
        try
        {
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) return TimeSpan.FromSeconds(10);
            var path = Path.Combine(directory, key + ".cooldown");
            var uptime = Environment.TickCount64;
            var utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Deadline? saved = null;
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 1024) throw new SharingCredentialException();
                saved = JsonSerializer.Deserialize<Deadline>(File.ReadAllBytes(path));
                if (saved == null) throw new SharingCredentialException();
            }
            var remaining = saved == null ? 0 : Math.Max(saved.Utc - utc, uptime >= saved.RecordedUptime ? saved.Uptime - uptime : 0);
            if (reserve && remaining > 0) return TimeSpan.FromMilliseconds(remaining);
            var milliseconds = (long)Math.Ceiling(Math.Max(interval.TotalMilliseconds, remaining));
            StateJson.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(new Deadline(uptime, checked(uptime + milliseconds), checked(utc + milliseconds))));
            return TimeSpan.Zero;
        }
        catch (Exception) { throw new SharingCredentialException(); }
        finally { if (owned) mutex.ReleaseMutex(); }
    }
}
