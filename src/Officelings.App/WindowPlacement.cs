using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Officelings.App;

internal static class WindowPlacement
{
    internal static Rect Clamp(Rect bounds, IReadOnlyList<Rect> areas)
    {
        if (areas.Count == 0) return bounds;
        if (areas.Any(a => a.Contains(bounds))) return bounds;
        var area = areas.MinBy(a => Math.Pow(Math.Max(a.Left - bounds.Right, Math.Max(bounds.Left - a.Right, 0)), 2) +
            Math.Pow(Math.Max(a.Top - bounds.Bottom, Math.Max(bounds.Top - a.Bottom, 0)), 2));
        var width = Math.Min(bounds.Width, area.Width); var height = Math.Min(bounds.Height, area.Height);
        return new(Math.Clamp(bounds.Left, area.Left, area.Right - width), Math.Clamp(bounds.Top, area.Top, area.Bottom - height), width, height);
    }
}
