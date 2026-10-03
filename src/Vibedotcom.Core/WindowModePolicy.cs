namespace Vibedotcom.Core;

public sealed record DesktopWindow(long Id, long Parent, string ClassName, int ExStyle = 0);
public sealed record DesktopHost(long Parent, long After);
public sealed record WindowModePlan(string Mode, bool ClickThrough, bool Borderless, bool TransparentBackground,
    double Opacity, bool Topmost, bool ShowInTaskbar, bool NoActivate)
{
    public static WindowModePlan Create(string mode, bool through, bool borderless, bool transparent, double opacity, bool qa = false)
    {
        mode = mode is "desktop" or "topmost" ? mode : "normal";
        if (qa) mode = "normal";
        return new(mode, through, borderless || transparent || mode == "desktop", transparent,
            double.IsFinite(opacity) ? Math.Clamp(opacity, .25, 1) : 1, mode == "topmost", mode != "desktop" && !qa,
            mode == "desktop" || through || qa);
    }
    // Input is a fake or a read-only HWND topology, in native top-level z-order.
    public static DesktopHost? FindHost(IReadOnlyList<DesktopWindow> windows, long progman)
    {
        var root = windows.FirstOrDefault(w => w.Id == progman);
        if (root == null) return null;
        var icons = windows.FirstOrDefault(w => w.Parent == progman && w.ClassName == "SHELLDLL_DefView");
        if ((root.ExStyle & 0x00200000) != 0 && icons != null) return new(progman, icons.Id);
        var wallpaper = windows.FirstOrDefault(w => w.Parent == progman && w.ClassName == "WorkerW" &&
            !windows.Any(c => c.Parent == w.Id && c.ClassName == "SHELLDLL_DefView"));
        if (wallpaper != null) return new(wallpaper.Id, 0);
        var top = windows.Where(w => w.Parent == 0).ToArray();
        for (var i = 0; i < top.Length; i++)
            if (windows.Any(w => w.Parent == top[i].Id && w.ClassName == "SHELLDLL_DefView"))
                for (var j = i + 1; j < top.Length; j++)
                    if (top[j].ClassName == "WorkerW" && !windows.Any(w => w.Parent == top[j].Id && w.ClassName == "SHELLDLL_DefView"))
                        return new(top[j].Id, 0);
        return null;
    }
}
