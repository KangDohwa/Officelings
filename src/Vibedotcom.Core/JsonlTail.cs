using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
    private (uint Volume, ulong Id) identity;
    private byte[] prefix = [], checkpoint = [];
    private long checkpointPosition;
    private readonly MemoryStream pending = new();
    internal int RetainedBytes => pending.Capacity;

    public IReadOnlyList<string> Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var currentIdentity = FileIdentity(file);
        WasReset = initialized && (file.Length < Offset || currentIdentity != identity ||
            !Matches(file, 0, prefix) || !Matches(file, checkpointPosition, checkpoint));
        if (WasReset) { Offset = 0; ClearPending(); initialized = skipping = false; }
        identity = currentIdentity;
        var lines = new List<string>();
        var end = file.Length; // Don't chase a writer indefinitely.
        if (!initialized)
        {
            initialized = true;
            file.Position = Offset;
            ReadUntil(file, Math.Min(end, BootstrapBytes), lines);
            if (end > BootstrapBytes * 2L)
            {
                // Metadata prefix + recent suffix, without reading the intervening history.
                Offset = end - BootstrapBytes;
                file.Position = Offset - 1;
                skipping = file.ReadByte() != '\n';
                file.Position = Offset;
                ClearPending();
            }
        }
        file.Position = Offset;
        ReadUntil(file, Math.Min(end, Offset + 4L * MaxLineBytes), lines);
        // Bounded probes detect overwrites and shrink/regrowth between polls. They cannot
        // detect arbitrary edits outside these windows; BytesRead counts tailed payload only.
        prefix = Probe(file, 0, (int)Math.Min(Offset, 256));
        checkpointPosition = Math.Max(0, Offset - 256);
        checkpoint = Probe(file, checkpointPosition, (int)(Offset - checkpointPosition));
        return lines;
    }

    private void ClearPending()
    {
        pending.SetLength(0);
        if (pending.Capacity > 64 * 1024) pending.Capacity = 64 * 1024;
    }

    private static byte[] Probe(FileStream file, long position, int count)
    {
        file.Position = position;
        var bytes = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = file.Read(bytes, read, count - read);
            if (n == 0) break;
            read += n;
        }
        return bytes[..read];
    }

    private static bool Matches(FileStream file, long position, byte[] bytes) =>
        Probe(file, position, bytes.Length).AsSpan().SequenceEqual(bytes);

    private static (uint, ulong) FileIdentity(FileStream file)
    {
        if (!OperatingSystem.IsWindows()) return (0, (ulong)File.GetCreationTimeUtc(file.Name).Ticks);
        if (!GetFileInformationByHandle(file.SafeFileHandle, out var info))
            throw new IOException("Cannot identify log handle.", new Win32Exception(Marshal.GetLastWin32Error()));
        return (info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);

    private void ReadUntil(FileStream file, long end, List<string> lines)
    {
        if (file.Position >= end) return;
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
                    ClearPending();
                    skipping = false;
                }
                else if (!skipping)
                {
                    if (pending.Length >= MaxLineBytes) { ClearPending(); skipping = true; DroppedLines++; }
                    else pending.WriteByte(b);
                }
            }
        }
    }
}
