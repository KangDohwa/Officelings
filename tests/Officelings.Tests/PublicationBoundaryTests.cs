using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class PublicationBoundaryTests
{
    [Fact]
    public void SettingsCanPublishWhileDirectoryMetadataIsBeingUpdated()
    {
        var root = Path.Combine(Path.GetTempPath(), "vdc-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Atomic renames need FILE_ADD_FILE on the parent directory. Hold
            // that access deterministically instead of racing two processes.
            using var updating = CreateFileW(root, 0x82, 3, 0, 3, 0x02200000, 0);
            Assert.False(updating.IsInvalid);
            var settings = Path.Combine(root, "settings.json");
            StateJson.WriteAtomic(settings, "{\"privacyMode\":\"full\"}"u8.ToArray());
            Assert.Equal("{\"privacyMode\":\"full\"}", File.ReadAllText(settings));
        }
        finally { Directory.Delete(root, true); }
    }

    private static IDisposable PinWriter(string path)
    {
        var method = typeof(FileBoundary).GetMethod("LockWriteParent", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return (IDisposable)method.Invoke(null, [path])!; }
        catch (TargetInvocationException e) when (e.InnerException != null)
        { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }

    [Fact]
    public void SettingsCanPublishWhileTrackerPinsTheSameDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "vdc-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (PinWriter(Path.Combine(root, "state.json")))
            {
                var settings = Path.Combine(root, "settings.json");
                StateJson.WriteAtomic(settings, "{\"privacyMode\":\"full\"}"u8.ToArray());
                Assert.Equal("{\"privacyMode\":\"full\"}", File.ReadAllText(settings));
                Assert.Throws<IOException>(() => Directory.Move(root, root + "-moved"));
            }
            Assert.Empty(Directory.GetFiles(root, ".boundary-*.lock"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        nint security, uint creation, uint flags, nint template);
}
