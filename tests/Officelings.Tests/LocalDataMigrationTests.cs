using Officelings.Core;
using Officelings.Core.Sharing;
using Xunit;

namespace Officelings.Tests;

public sealed class LocalDataMigrationTests : IDisposable
{
    private const string LegacyHome = "Vibedotcom", LegacyCredentials = "Vibedotcom.Credentials";
    private readonly string root = Path.Combine(Path.GetTempPath(), "officelings-migration-" + Guid.NewGuid().ToString("N"));

    public LocalDataMigrationTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);
    private void Write(string directory, string file, string contents)
    {
        var path = Path.Combine(root, directory, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    [Fact]
    public void StartupCopiesNestedDataAndKeepsTheLegacyDirectory()
    {
        Write(LegacyHome, "settings.json", "saved settings");
        Write(LegacyHome, "objects/custom/object.json", "custom object");
        Directory.CreateDirectory(Path.Combine(root, LegacyHome, "empty"));

        var home = LocalDataMigration.Initialize(root);

        Assert.Equal(Path.Combine(root, "Officelings"), home);
        Assert.Equal("saved settings", File.ReadAllText(Path.Combine(home, "settings.json")));
        Assert.Equal("custom object", File.ReadAllText(Path.Combine(home, "objects/custom/object.json")));
        Assert.True(Directory.Exists(Path.Combine(home, "empty")));
        Assert.Equal("saved settings", File.ReadAllText(Path.Combine(root, LegacyHome, "settings.json")));
        Assert.Equal("custom object", File.ReadAllText(Path.Combine(root, LegacyHome, "objects/custom/object.json")));
        Assert.Empty(Directory.GetDirectories(root, ".*-migration-*"));
    }

    [Fact]
    public void ExistingDestinationIsNeverOverwrittenOrMerged()
    {
        Write(LegacyHome, "settings.json", "old");
        Write(LegacyHome, "old-only.json", "old only");
        Write("Officelings", "settings.json", "new");

        var home = LocalDataMigration.Initialize(root);

        Assert.Equal("new", File.ReadAllText(Path.Combine(home, "settings.json")));
        Assert.False(File.Exists(Path.Combine(home, "old-only.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, LegacyHome, "settings.json")));
    }

    [Fact]
    public void MissingLegacyDirectoryDoesNotCreateAnEmptyDestination()
    {
        Assert.Equal(Path.Combine(root, "Officelings"), LocalDataMigration.Initialize(root));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void CredentialsAndCooldownsAreCopiedSeparatelyAndDeletionDoesNotRestoreTheOldToken()
    {
        Write(LegacyCredentials, "presence.dpapi", "opaque encrypted bytes");
        Write(LegacyCredentials, "synthetic.cooldown", "saved deadline");

        LocalDataMigration.Initialize(root);
        var credentials = LocalDataMigration.CredentialsDirectory(root);

        Assert.Equal(Path.Combine(root, "Officelings.Credentials"), credentials);
        Assert.Equal(File.ReadAllBytes(Path.Combine(root, LegacyCredentials, "presence.dpapi")),
            File.ReadAllBytes(Path.Combine(credentials, "presence.dpapi")));
        Assert.Equal("saved deadline", File.ReadAllText(Path.Combine(credentials, "synthetic.cooldown")));
        Assert.False(Directory.Exists(Path.Combine(root, "Officelings")));
        new WindowsSharingTokenStore(credentials).Delete();
        LocalDataMigration.Initialize(root);
        Assert.False(File.Exists(Path.Combine(credentials, "presence.dpapi")));
        Assert.True(File.Exists(Path.Combine(root, LegacyCredentials, "presence.dpapi")));
    }

    [Fact]
    public void ConcurrentStartupsPublishACompleteCopy()
    {
        Write(LegacyHome, "settings.json", "settings");
        Write(LegacyHome, "nested/layout.json", "layout");

        Parallel.For(0, 4, _ => LocalDataMigration.Initialize(root));

        Assert.Equal("settings", File.ReadAllText(Path.Combine(root, "Officelings/settings.json")));
        Assert.Equal("layout", File.ReadAllText(Path.Combine(root, "Officelings/nested/layout.json")));
        Assert.Empty(Directory.GetDirectories(root, ".*-migration-*"));
    }

    [Fact]
    public void DestinationFileFailsWithoutChangingTheLegacyData()
    {
        Write(LegacyHome, "settings.json", "settings");
        File.WriteAllText(Path.Combine(root, "Officelings"), "conflicting file");

        Assert.Throws<IOException>(() => LocalDataMigration.Initialize(root));
        Assert.Equal("settings", File.ReadAllText(Path.Combine(root, LegacyHome, "settings.json")));
        Assert.Equal("conflicting file", File.ReadAllText(Path.Combine(root, "Officelings")));
    }
}
