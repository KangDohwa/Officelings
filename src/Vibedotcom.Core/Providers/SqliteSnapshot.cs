using System.Runtime.InteropServices;

namespace Vibedotcom.Core.Providers;

// SQLite never receives a provider path. Only disposable copies can acquire SQLite locks/SHM.
internal sealed class SqliteSnapshot : IDisposable
{
    private readonly string directory;
    private IntPtr database;
    internal static string Stamp(string path, bool includeShm = false) => string.Join("|",
        (includeShm ? new[] { "", "-wal", "-shm", "-journal" } : new[] { "", "-wal", "-journal" })
        .Select(suffix =>
        {
            var file = new FileInfo(path + suffix);
            return file.Exists ? $"{suffix}:{file.Length}:{file.LastWriteTimeUtc.Ticks}" : suffix + ":missing";
        }));

    internal SqliteSnapshot(string source)
    {
        directory = Path.Combine(Path.GetTempPath(), "Vibedotcom-sqlite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var copy = Path.Combine(directory, Path.GetFileName(source));
            var stable = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var before = Stamp(source, true);
                foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                {
                    if (!File.Exists(source + suffix)) { File.Delete(copy + suffix); continue; }
                    using var input = new FileStream(source + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var output = new FileStream(copy + suffix, FileMode.Create, FileAccess.Write, FileShare.None);
                    var length = input.Length;
                    var buffer = new byte[64 * 1024];
                    while (length > 0)
                    {
                        var read = input.Read(buffer, 0, (int)Math.Min(length, buffer.Length));
                        if (read == 0) throw new IOException("Antigravity snapshot changed while copying.");
                        output.Write(buffer, 0, read); length -= read;
                    }
                }
                if (before == Stamp(source, true)) { stable = true; break; }
                if (attempt < 2) Thread.Sleep(20);
            }
            if (!stable) throw new IOException("Antigravity snapshot remained unstable.");
            Check(Native.sqlite3_open_v2(copy, out database, 1, IntPtr.Zero)); // SQLITE_OPEN_READONLY
            if (Query("PRAGMA quick_check").SingleOrDefault()?[0] != "ok") throw new IOException("Antigravity snapshot failed integrity check.");
            // ponytail: stable sequential copies are best-effort, not an atomic backup; live summaries win.
        }
        catch { Dispose(); throw; }
    }

    internal List<string?[]> Query(string sql)
    {
        IntPtr statement = IntPtr.Zero;
        try
        {
            Check(Native.sqlite3_prepare_v2(database, sql, -1, out statement, IntPtr.Zero));
            var rows = new List<string?[]>();
            int result;
            while ((result = Native.sqlite3_step(statement)) == 100)
            {
                var row = new string?[Native.sqlite3_column_count(statement)];
                for (var i = 0; i < row.Length; i++) row[i] = Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement, i));
                rows.Add(row);
            }
            if (result != 101) Check(result);
            return rows;
        }
        finally { if (statement != IntPtr.Zero) Native.sqlite3_finalize(statement); }
    }

    private static void Check(int code)
    {
        if (code != 0) throw new IOException($"Antigravity SQLite read failed ({code}).");
    }

    public void Dispose()
    {
        if (database != IntPtr.Zero) { Native.sqlite3_close(database); database = IntPtr.Zero; }
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static class Native
    {
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close(IntPtr database);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_count(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    }
}
