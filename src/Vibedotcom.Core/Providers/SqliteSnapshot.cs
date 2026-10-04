using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Vibedotcom.Core.Providers;

// SQLite never receives a provider path. Private snapshots stay entirely in memory.
internal sealed class SqliteSnapshot : IDisposable
{
    private readonly string? directory;
    private IntPtr database;
    internal const long MaximumCopyBytes = 64 * 1024 * 1024;
    private readonly Native.Progress progress;
    private long queryDeadline;
    private int progressCalls;
    internal static string Stamp(string path, bool includeShm = false) => string.Join("|",
        (includeShm ? new[] { "", "-wal", "-shm", "-journal" } : new[] { "", "-wal", "-journal" })
        .Select(suffix =>
        {
            FileBoundary.Check(path + suffix);
            using var ancestors = FileBoundary.LockAncestors(path + suffix);
            var file = new FileInfo(path + suffix);
            return file.Exists ? $"{suffix}:{file.Length}:{file.LastWriteTimeUtc.Ticks}" : suffix + ":missing";
        }));

    internal SqliteSnapshot(string source, bool memoryOnly = false)
    {
        progress = _ => Stopwatch.GetTimestamp() >= queryDeadline || ++progressCalls > 2000 ? 1 : 0;
        if (memoryOnly)
        {
            var image = SqliteMemoryImage.Read(source);
            try
            {
                Check(Native.sqlite3_open_v2(":memory:", out database, 6, IntPtr.Zero));
                var buffer = Native.sqlite3_malloc64((ulong)image.Length);
                if (buffer == IntPtr.Zero) throw new IOException("Antigravity memory snapshot allocation failed.");
                var transferred = false;
                try
                {
                    Marshal.Copy(image, 0, buffer, image.Length);
                    // FREEONCLOSE | READONLY. SQLite frees the buffer even when it returns an error.
                    var result=Native.sqlite3_deserialize(database, "main", buffer, image.Length, image.Length, 5);
                    transferred=true; Check(result);
                }
                finally { if (!transferred) Native.sqlite3_free(buffer); }
                // Existing private WAL images contain large stored records even
                // for length-only queries. Returned text still has the 4 KiB cap.
                Configure(256 * 1024);
            }
            catch { Dispose(); throw; }
            finally { Array.Clear(image); }
            return;
        }
        directory = Path.Combine(Path.GetTempPath(), "Vibedotcom-sqlite-" + Guid.NewGuid().ToString("N"));
        FileBoundary.CreateDirectory(directory);
        try
        {
            var copy = Path.Combine(directory, Path.GetFileName(source));
            var stable = false;
            var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var before = Stamp(source, true);
                long total = 0;
                foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                {
                    FileBoundary.Check(source + suffix);
                    using var ancestors = FileBoundary.LockAncestors(source + suffix);
                    if (!FileBoundary.Exists(source + suffix)) { File.Delete(copy + suffix); continue; }
                    using var input = FileBoundary.OpenRead(source + suffix, FileShare.ReadWrite | FileShare.Delete);
                    using var output = new FileStream(copy + suffix, FileMode.Create, FileAccess.Write, FileShare.None);
                    var length = input.Length;
                    total += length;
                    if (total > MaximumCopyBytes) throw new IOException("Antigravity snapshot exceeds 64 MiB copy budget.");
                    var buffer = new byte[64 * 1024];
                    while (length > 0)
                    {
                        if (Stopwatch.GetTimestamp() >= deadline) throw new IOException("Antigravity snapshot copy deadline exceeded.");
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
            Configure();
            // ponytail: stable sequential copies are best-effort, not an atomic backup; live summaries win.
        }
        catch { Dispose(); throw; }
    }

    private void Configure(int recordLimit = 65536)
    {
        Native.sqlite3_limit(database, 0, recordLimit); // SQLITE_LIMIT_LENGTH
        Native.sqlite3_limit(database, 1, 65536); // SQLITE_LIMIT_SQL_LENGTH
        Native.sqlite3_limit(database, 2, 32); // SQLITE_LIMIT_COLUMN
        Native.sqlite3_limit(database, 3, 32); // SQLITE_LIMIT_EXPR_DEPTH
        Native.sqlite3_limit(database, 4, 16); // SQLITE_LIMIT_COMPOUND_SELECT
        Native.sqlite3_progress_handler(database, 1000, progress, IntPtr.Zero);
        Query("PRAGMA trusted_schema=OFF");
        if (Query("PRAGMA quick_check").SingleOrDefault()?[0] != "ok") throw new IOException("Antigravity snapshot failed integrity check.");
    }

    internal List<string?[]> Query(string sql)
    {
        queryDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        progressCalls = 0;
        IntPtr statement = IntPtr.Zero;
        try
        {
            Check(Native.sqlite3_prepare_v2(database, sql, -1, out statement, IntPtr.Zero));
            var rows = new List<string?[]>();
            long total = 0;
            int result;
            while ((result = Native.sqlite3_step(statement)) == 100)
            {
                if (rows.Count >= InputLimits.Agents) throw new IOException("SQLite row budget exceeded.");
                var row = new string?[Native.sqlite3_column_count(statement)];
                for (var i = 0; i < row.Length; i++)
                {
                    var text = Native.sqlite3_column_text(statement, i);
                    var count = Native.sqlite3_column_bytes(statement, i);
                    total += count;
                    if (count > 4096 || total > 8 * 1024 * 1024) throw new IOException("SQLite text budget exceeded.");
                    row[i] = text == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(text, count);
                }
                rows.Add(row);
            }
            if (result != 101) Check(result);
            return rows;
        }
        finally { if (statement != IntPtr.Zero) Native.sqlite3_finalize(statement); }
    }

    internal void RequireTable(string name)
    {
        if (name is not ("steps" or "conversation_summaries") ||
            Query("SELECT type FROM sqlite_master WHERE name='" + name + "'").SingleOrDefault()?[0] != "table")
            throw new IOException("Expected a stored Antigravity table, not a view.");
    }

    private static void Check(int code)
    {
        if (code != 0) throw new IOException($"Antigravity SQLite read failed ({code}).");
    }

    public void Dispose()
    {
        if (database != IntPtr.Zero) { Native.sqlite3_close(database); database = IntPtr.Zero; }
        try { if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int Progress(IntPtr context);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_limit(IntPtr database, int category, int value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_progress_handler(IntPtr database, int instructions, Progress callback, IntPtr context);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_bytes(IntPtr statement, int column);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close(IntPtr database);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_malloc64(ulong size);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sqlite3_free(IntPtr buffer);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_deserialize(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string schema, IntPtr bytes, long size, long capacity, uint flags);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr statement);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr statement);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_count(IntPtr statement);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    }
}
