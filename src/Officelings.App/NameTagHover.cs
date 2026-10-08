using System;
using System.Collections.Generic;
using System.Linq;

namespace Officelings.App;

// Renderer-local time and identity; nothing is published to tracker state.
internal sealed class NameTagHover
{
    internal const int FadeInMs = 120, FadeOutMs = 200, LeaveGraceMs = 60, TooltipDelayMs = 420;
    private sealed class Fade
    {
        internal double Opacity;
        internal DateTimeOffset Changed, LeaveAfter;
    }
    private readonly Dictionary<string, Fade> fades = new();
    internal string? Target { get; private set; }
    internal DateTimeOffset Entered { get; private set; }
    internal bool Animating { get; private set; }
    internal static string Normalize(string? mode) => mode is "hover" or "icon" or "always" ? mode : "hover";
    internal double Opacity(string key) => fades.TryGetValue(key, out var fade) ? fade.Opacity : 0;

    internal bool Update(string? target, DateTimeOffset now)
    {
        var changed = Advance(now);
        if (target != Target)
        {
            if (Target != null && fades.TryGetValue(Target, out var old)) old.LeaveAfter = now.AddMilliseconds(LeaveGraceMs);
            Target = target; Entered = now; changed = true;
            if (target != null)
            {
                if (!fades.TryGetValue(target, out var fade)) fades[target] = fade = new();
                fade.Changed = now; fade.LeaveAfter = DateTimeOffset.MaxValue;
            }
        }
        Animating = fades.Any(p => p.Key != Target || p.Value.Opacity < 1);
        return changed;
    }
    internal bool Advance(DateTimeOffset now)
    {
        var changed = false;
        foreach (var (key, fade) in fades.ToArray())
        {
            var previous = fade.Opacity;
            var elapsed = Math.Max(0, (now - fade.Changed).TotalMilliseconds);
            if (key == Target) fade.Opacity = Math.Min(1, previous + elapsed / FadeInMs);
            else fade.Opacity = Math.Max(0, previous - Math.Max(0, (now - (fade.Changed > fade.LeaveAfter ? fade.Changed : fade.LeaveAfter)).TotalMilliseconds) / FadeOutMs);
            fade.Changed = now;
            changed |= previous != fade.Opacity;
            if (key != Target && fade.Opacity == 0) fades.Remove(key);
        }
        Animating = fades.Any(p => p.Key != Target || p.Value.Opacity < 1);
        return changed;
    }
    internal void Clear() { fades.Clear(); Target = null; Animating = false; }
}
