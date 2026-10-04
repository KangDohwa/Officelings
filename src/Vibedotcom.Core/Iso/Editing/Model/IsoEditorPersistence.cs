namespace Vibedotcom.Core.Iso.Editing;

internal static class IsoEditorPersistence
{
    internal static void Save(string path, byte[] bytes)
    {
        if (bytes.Length > IsoEditorDocument.MaxBytes) throw new InvalidDataException("Iso editor layout limits exceeded.");
        var destination = StateJson.OutputPath(path);
        FileBoundary.Check(destination + ".bak");
        FileBoundary.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var ancestors = FileBoundary.LockWriteParent(destination);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            FileBoundary.Check(destination); FileBoundary.Check(destination + ".bak");
            if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".bak");
            else File.Move(temporary, destination);
        }
        finally { if (FileBoundary.Exists(temporary)) File.Delete(temporary); }
    }
}
