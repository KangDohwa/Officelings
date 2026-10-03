using System.Text;

namespace Vibedotcom.Core;

// Byte offsets are independent of UTF-8 characters and StreamReader read-ahead.
public sealed class JsonlTail
{
    public const int BootstrapBytes = 512 * 1024;
    public const int MaxLineBytes = 2 * 1024 * 1024;
    public long Offset { get; private set; }
    public long BytesRead { get; private set; }
    public int DroppedLines { get; private set; }
    public bool WasReset { get; private set; }
    private bool initialized, skipping;
    private DateTime creationTime;
    private readonly MemoryStream pending = new();

    public IReadOnlyList<string> Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var created = File.GetCreationTimeUtc(path);
        WasReset = initialized && (file.Length < Offset || created != creationTime);
        if (WasReset) { Offset = 0; pending.SetLength(0); initialized = skipping = false; }
        creationTime = created;
        var lines = new List<string>();
        var end = file.Length; // Don't chase a writer indefinitely.
        if (!initialized)
        {
            initialized = true;
            ReadUntil(file, Math.Min(end, BootstrapBytes), lines);
            if (end > BootstrapBytes * 2L)
            {
                // Metadata prefix + recent suffix, without reading the intervening history.
                Offset = end - BootstrapBytes;
                file.Position = Offset;
                pending.SetLength(0);
                skipping = true; // suffix can start inside a line or multibyte character
            }
        }
        file.Position = Offset;
        ReadUntil(file, Math.Min(end, Offset + 4L * MaxLineBytes), lines);
        return lines;
    }

    private void ReadUntil(FileStream file, long end, List<string> lines)
    {
        var buffer = new byte[16384];
        while (file.Position < end)
        {
            var count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, end - file.Position));
            if (count == 0) break;
            BytesRead += count;
            Offset += count;
            for (var i = 0; i < count; i++)
            {
                var b = buffer[i];
                if (b == '\n')
                {
                    if (!skipping && pending.Length > 0)
                        lines.Add(Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length).TrimEnd('\r').TrimStart('\uFEFF'));
                    pending.SetLength(0);
                    skipping = false;
                }
                else if (!skipping)
                {
                    if (pending.Length >= MaxLineBytes) { pending.SetLength(0); skipping = true; DroppedLines++; }
                    else pending.WriteByte(b);
                }
            }
        }
    }
}
