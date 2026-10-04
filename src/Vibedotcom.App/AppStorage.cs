using System;
using System.IO;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed class AppStorage : IDisposable
{
    internal bool Isolated { get; }
    internal string Home { get; }
    internal string SettingsPath => Path.Combine(Home, "settings.json");
    internal string LayoutPath => Path.Combine(Home, "layout.json");
    internal string IsoLayoutPath => Path.Combine(Home, "iso-layout.json");
    private readonly string personalHome;

    internal AppStorage(bool isolated, string? personalHome = null)
    {
        Isolated = isolated; this.personalHome = Path.GetFullPath(personalHome ?? TrackerOptions.OutputDirectory);
        Home = isolated ? Path.Combine(Path.GetTempPath(), "vibedotcom-demo-" + Guid.NewGuid().ToString("N")) : this.personalHome;
    }
    internal void CheckInput(string? path)
    {
        if (!Isolated || path == null) return;
        var full = Path.GetFullPath(path);
        if (full.Equals(personalHome, StringComparison.OrdinalIgnoreCase) || full.StartsWith(personalHome + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Demo/QA input must be outside the personal app home.");
    }
    public void Dispose()
    {
        if (Isolated && Directory.Exists(Home))
            try { Directory.Delete(Home, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
