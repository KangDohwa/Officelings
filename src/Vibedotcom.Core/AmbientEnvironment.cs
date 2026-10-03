namespace Vibedotcom.Core;

public sealed record AmbientSettings(string Weather = "off")
{
    public AmbientSettings Normalize() => this with { Weather = Weather is "off" or "rain" or "snow" or "auto" ? Weather : "off" };
}

public sealed record AmbientState(string WindowView, string Weather, bool StringLightsOn, bool WinterDecor,
    DateTimeOffset LocalTime);

// The injected clock supplies local civil time; no weather service or network is involved.
public sealed class AmbientEnvironment(Func<DateTimeOffset>? clock = null)
{
    public AmbientSettings Settings { get; set; } = new();
    public string SeasonDecor { get; set; } = "off";
    public AmbientState Sample(DateTimeOffset? localTime = null)
    {
        var time = localTime ?? (clock ?? (() => DateTimeOffset.Now))();
        time = time.AddTicks(-(time.Ticks % TimeSpan.TicksPerSecond));
        var weather = Settings.Normalize().Weather;
        // Auto is a local seasonal effect: snow in December, otherwise a clear view.
        if (weather == "auto") weather = time.Month == 12 ? "snow" : "off";
        return new(weather == "off" ? TimeOfDay(time.Hour) : weather, weather,
            time.Hour >= 17 || time.Hour < 7, SeasonDecor == "auto" && time.Month == 12, time);
    }
    public static string TimeOfDay(int hour) => hour is >= 7 and < 17 ? "day" : hour is >= 17 and < 19 ? "sunset" : "night";
}
