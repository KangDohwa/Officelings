using System.Text.RegularExpressions;

namespace Vibedotcom.Core.Iso.Editing;

internal sealed class ObjectSizeException : IOException;

internal static class ObjectFiles
{
    internal const int JsonLimit = 4 * 1024 * 1024;
    internal const int PngLimit = 8 * 1024 * 1024;
    private static void Component(string part)
    {
        if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
            part.Any(c => c < 32 || "<>\"|?*:".Contains(c)) ||
            Regex.IsMatch(part.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase))
            throw new InvalidDataException("Unsafe local path.");
    }
    internal static string Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidDataException("Expected local path.");
        var root = Path.GetPathRoot(path) ?? "";
        foreach (var part in path[root.Length..].Replace('\\', '/').Split('/')) Component(part);
        return FileBoundary.Check(path);
    }
    internal static string Under(string root, string relative)
    {
        Relative(relative);
        return FileBoundary.UnderRoot(Check(root), relative);
    }
    internal static void Relative(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Expected relative path.");
        foreach (var part in relative.Replace('\\', '/').Split('/')) Component(part);
    }
    internal static byte[] Read(string path, int maximum)
    {
        path = Check(path);
        if (!FileBoundary.Exists(path)) throw new InvalidDataException("Expected regular file.");
        using var ancestors = FileBoundary.LockAncestors(path);
        using var stream = FileBoundary.OpenRead(path, FileShare.Read);
        if (stream.Length > maximum) throw new ObjectSizeException();
        // The shared guard rechecks the actual opened handle and pins ancestors during capture.
        return FileBoundary.ReadBytes(path, maximum);
    }
    internal static string Output(string path) => Check(StateJson.OutputPath(Check(path)));
    internal static void Write(string path, byte[] bytes)
    {
        path = Output(path);
        using var guard = FileBoundary.LockWriteParent(path);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes); output.Flush(true);
    }
    internal static void MoveDirectory(string source, string destination)
    {
        source = Output(source); destination = Output(destination);
        using var sourceLock = FileBoundary.LockWriteParent(source);
        using var targetLock = FileBoundary.LockWriteParent(destination);
        Directory.Move(source, destination);
    }
    internal static void DeleteDirectory(string directory)
    {
        directory = Output(directory);
        using var parent = FileBoundary.LockWriteParent(directory);
        var files = Directory.GetFileSystemEntries(directory);
        if (!CleanupSafe(directory, files))
            throw new InvalidDataException("Unsafe object cleanup.");
        using (FileBoundary.LockWriteParent(Path.Combine(directory, ".cleanup")))
            foreach (var file in files) File.Delete(Output(file));
        Directory.Delete(directory); // No recursive deletion, no link traversal.
    }
    internal static bool CleanupSafe(string directory, string[]? files = null)
    {
        directory = Output(directory);
        if (!FileBoundary.Exists(directory, directory: true)) return true;
        using var parent = FileBoundary.LockAncestors(Path.Combine(directory, ".cleanup-check"));
        files ??= Directory.GetFileSystemEntries(directory);
        return files.Length <= 12 && files.All(f => FileBoundary.Exists(Check(f)) &&
            (Path.GetFileName(f) is "source.iso.json" or "object.iso.json" or "thumbnail.png" ||
                Regex.IsMatch(Path.GetFileName(f), "^sheet-[a-f0-9]{64}\\.png$")));
    }
    internal static FileStream LockWriter(string root)
    {
        var file = Output(Under(root, ".transaction.lock"));
        return FileBoundary.Exists(file) ? FileBoundary.OpenRead(file, FileShare.None) :
            new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }
}
