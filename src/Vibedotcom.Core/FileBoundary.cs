using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.IO.Enumeration;

namespace Vibedotcom.Core;

// File inputs are local, bounded and never follow links across a trust boundary.
public static class FileBoundary
{
    public static string Check(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 4096 || full.Count(c => c == Path.DirectorySeparatorChar) > 128)
            throw new InvalidDataException("File path length/depth budget exceeded.");
        if (OperatingSystem.IsWindows() && (full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.Length < 3 || full[1] != ':' || full.AsSpan(2).Contains(':')))
            throw new InvalidDataException("A local drive path without alternate streams is required.");
        using var ancestors = LockAncestors(full, writing: true);
        // Walk from the drive down, before querying anything below a possible link.
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse points are not allowed at file boundaries.");
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
        return full;
    }

    public static string UnderRoot(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Replace('\\', '/').Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')))
            throw new InvalidDataException("A safe relative file path is required.");
        var folder = Path.TrimEndingDirectorySeparator(Check(root));
        var path = Check(Path.Combine(folder, relative));
        if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("File escapes its allowed root.");
        return path;
    }

    public static bool Exists(string path, bool directory = false)
    {
        path = Check(path);
        using var ancestors = LockAncestors(path, writing: true);
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points are not allowed at file boundaries.");
            return ((attributes & FileAttributes.Directory) != 0) == directory;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public static void CreateDirectory(string path)
    {
        path = Check(path);
        if (Exists(path, directory: true)) return;
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))
            ?? throw new IOException("Directory has no local parent.");
        CreateDirectory(parent);
        using var ancestors = LockAncestors(path);
        if (OperatingSystem.IsWindows())
        {
            if (!CreateDirectoryW(path, IntPtr.Zero) && Marshal.GetLastWin32Error() != 183)
                throw new IOException("Cannot create local boundary directory.");
        }
        else Directory.CreateDirectory(path);
        if (!Exists(path, directory: true)) throw new IOException("Expected a local directory.");
    }

    internal static IEnumerable<FileInfo> Files(string root, string pattern, bool recursive)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((Check(root), 0));
        var visited = 0;
        while (pending.Count > 0 && visited < 4096)
        {
            var (directory, depth) = pending.Pop();
            if (!Exists(directory, directory: true)) continue;
            using var ancestors = LockAncestors(Path.Combine(directory, ".boundary"));
            foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
                { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                if (++visited > 4096) yield break;
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    // Re-open each child without following links before descending.
                    if (recursive && depth < 16) pending.Push((path, depth + 1));
                }
                else if (FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: true))
                    yield return new FileInfo(path);
            }
        }
    }

    public static byte[] ReadBytes(string path, int maximum)
    {
        path = Check(path);
        using var ancestors = LockAncestors(path);
        // Deny replacement/writes for the lifetime of this read. Validate the opened
        // handle too, so a swapped ancestor cannot silently redirect the read.
        using var input = OpenRead(path, FileShare.Read);
        if (OperatingSystem.IsWindows())
        {
            var final = new StringBuilder(32768);
            var count = GetFinalPathNameByHandleW(input.SafeFileHandle, final, final.Capacity, 0);
            if (count == 0 || count >= final.Capacity || !final.ToString().Equals(@"\\?\" + path, StringComparison.Ordinal))
                throw new InvalidDataException("Opened file differs from the validated local path.");
        }
        if (input.Length > maximum) throw new InvalidDataException("File exceeds its byte budget.");
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("File grew beyond its byte budget.");
        return bytes;
    }

    internal static IDisposable LockAncestors(string path, bool writing = false)
    {
        var handles = new List<SafeFileHandle>();
        if (!OperatingSystem.IsWindows()) return new DirectoryLocks(handles);
        path = Path.GetFullPath(path);
        var root = Path.GetPathRoot(path)!;
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || GetDriveTypeW(root) is not (2 or 3))
            throw new InvalidDataException("A fixed/removable local drive is required.");
        try
        {
            var directory = root;
            var parent = Path.GetDirectoryName(path) ?? root;
            var parts = parent[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in new[] { "" }.Concat(parts))
            {
                if (part.Length > 0) directory = Path.Combine(directory, part);
                var share = writing || !directory.Equals(parent, StringComparison.Ordinal) ? 3u : 1u;
                var handle = CreateFileW(directory, 0x81, share, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error(); handle.Dispose();
                    if (error is 2 or 3) break;
                    throw new IOException("Cannot lock a boundary ancestor.");
                }
                handles.Add(handle);
                if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0)
                    throw new InvalidDataException("Reparse ancestor rejected.");
            }
            return new DirectoryLocks(handles);
        }
        catch { foreach (var handle in handles) handle.Dispose(); throw; }
    }

    internal static FileStream OpenRead(string path, FileShare share)
    {
        if (!OperatingSystem.IsWindows()) return new FileStream(path, FileMode.Open, FileAccess.Read, share);
        var handle = CreateFileW(path, 0x80000000, (uint)share, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid) throw new IOException("Cannot open local boundary file.");
            if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0)
                throw new InvalidDataException("Reparse input file rejected.");
            return new FileStream(handle, FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static IDisposable LockWriteParent(string path)
    {
        if (!OperatingSystem.IsWindows()) return LockAncestors(path, writing: true);
        using var initial = LockAncestors(path);
        var guardPath = Path.Combine(Path.GetDirectoryName(path)!, ".boundary-" + Guid.NewGuid().ToString("N") + ".lock");
        // Pin a child so the directory cannot become empty and acquire a
        // junction while normal child-file renames remain allowed.
        var guard = CreateFileW(guardPath, 0x40010000, 0, IntPtr.Zero, 1, 0x04200000, IntPtr.Zero);
        try
        {
            if (guard.IsInvalid) throw new IOException("Cannot pin publication directory.");
            return new WriteLocks(LockAncestors(path, writing: true), guard);
        }
        catch { guard.Dispose(); throw; }
    }

    private sealed class WriteLocks(IDisposable ancestors, SafeFileHandle guard) : IDisposable
    {
        public void Dispose() { guard.Dispose(); ancestors.Dispose(); }
    }

    private sealed class DirectoryLocks(List<SafeFileHandle> handles) : IDisposable
    {
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string root);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, int length, uint flags);
}
