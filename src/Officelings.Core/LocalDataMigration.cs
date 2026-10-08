namespace Officelings.Core;

public static class LocalDataMigration
{
    public static string? EnvironmentValue(string suffix) => Environment.GetEnvironmentVariable("OFFICELINGS_" + suffix) ??
        Environment.GetEnvironmentVariable("VIBEDOTCOM_" + suffix); // Legacy environment fallback.

    public static string Initialize(string? localAppData = null)
    {
        var root = Path.GetFullPath(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var home = CopyLegacyDirectory(root, "Vibedotcom", "Officelings");
        CredentialsDirectory(root);
        return home;
    }

    public static string CredentialsDirectory(string? localAppData = null) => CopyLegacyDirectory(
        Path.GetFullPath(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        "Vibedotcom.Credentials", "Officelings.Credentials");

    private static string CopyLegacyDirectory(string root, string legacyName, string currentName)
    {
        var source = Path.Combine(root, legacyName);
        var destination = Path.Combine(root, currentName);
        if (Directory.Exists(destination) || !Directory.Exists(source)) return destination;
        if (File.Exists(destination)) throw new IOException("The new local data path is not a directory.");
        // Publish only a complete copy. A failed attempt can be retried on the next startup.
        var staging = Path.Combine(root, "." + currentName + "-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(source, staging);
            try { Directory.Move(staging, destination); }
            catch (IOException) when (Directory.Exists(destination)) { } // Another startup completed first.
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        return destination;
    }

    private static void CopyDirectory(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Local data migration does not follow links or junctions.");
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Local data migration does not follow links or junctions.");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyDirectory(entry, target);
            else File.Copy(entry, target, overwrite: false);
        }
    }
}
