namespace Officelings.Core;

public sealed record AmbientSettings(string Weather = "off")
{
    public AmbientSettings Normalize() => this with { Weather = Weather is "off" or "rain" or "snow" or "auto" ? Weather : "off" };
}

public sealed record AmbientState(string WindowView, string Weather, bool StringLightsOn, bool WinterDecor,
    DateTimeOffset LocalTime);

// The injected clock supplies local civil time; no weather service or network is involved.
public sealed class AmbientEnvironment(Func<DateTimeOffset>? clock = null)
{
    private AmbientState? sampled;
    public AmbientSettings Settings { get; set; } = new();
    public string SeasonDecor { get; set; } = "off";
    public AmbientState Sample(DateTimeOffset? localTime = null)
    {
        var time = localTime ?? clock?.Invoke() ?? DateTimeOffset.Now;
        time = time.AddTicks(-(time.Ticks % TimeSpan.TicksPerSecond));
        var weather = Settings.Weather is "rain" or "snow" or "auto" ? Settings.Weather : "off";
        // Auto is a local seasonal effect: snow in December, otherwise a clear view.
        if (weather == "auto") weather = time.Month == 12 ? "snow" : "off";
        var view = weather == "off" ? TimeOfDay(time.Hour) : weather;
        var lights = time.Hour >= 17 || time.Hour < 7;
        var winter = SeasonDecor == "auto" && time.Month == 12;
        if (sampled is {} old && old.LocalTime.EqualsExact(time) && old.WindowView == view &&
            old.Weather == weather && old.StringLightsOn == lights && old.WinterDecor == winter) return old;
        return sampled = new(view, weather, lights, winter, time);
    }
    public static string TimeOfDay(int hour) => hour is >= 7 and < 17 ? "day" : hour is >= 17 and < 19 ? "sunset" : "night";
}
