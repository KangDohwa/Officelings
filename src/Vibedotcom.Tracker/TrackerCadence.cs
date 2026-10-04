// Task.Delay(TimeSpan) truncates fractional milliseconds. Waiting until a
// deadline must round up, otherwise a sub-millisecond remainder spins the loop.
internal static class TrackerCadence
{
    internal static TimeSpan Delay(TimeSpan remaining) => remaining <= TimeSpan.Zero
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(remaining.TotalMilliseconds)));
}
