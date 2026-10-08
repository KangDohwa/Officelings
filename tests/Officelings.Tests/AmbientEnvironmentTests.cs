using System.Text.Json;
using Officelings.App;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class AmbientEnvironmentTests
{
    [Theory]
    [InlineData(6, 59, "night", true)]
    [InlineData(7, 0, "day", false)]
    [InlineData(16, 59, "day", false)]
    [InlineData(17, 0, "sunset", true)]
    [InlineData(18, 59, "sunset", true)]
    [InlineData(19, 0, "night", true)]
    [InlineData(23, 59, "night", true)]
    [InlineData(0, 0, "night", true)]
    public void FakeLocalClockMapsBoundaryMinutes(int hour, int minute, string view, bool lights)
    {
        var local = new DateTimeOffset(2026, 10, 4, hour, minute, 42, TimeSpan.FromHours(-7));
        var environment = new AmbientEnvironment(() => local);
        var state = environment.Sample();
        Assert.Equal(view, state.WindowView); Assert.Equal(lights, state.StringLightsOn);
        Assert.Equal(local, state.LocalTime); Assert.False(state.WinterDecor);
    }

    [Theory]
    [InlineData("rain", 10, "rain", false)]
    [InlineData("snow", 10, "snow", false)]
    [InlineData("auto", 12, "snow", true)]
    [InlineData("auto", 1, "night", false)]
    [InlineData("off", 12, "night", true)]
    public void WeatherAndWinterDecorationAreLocalOnly(string weather, int month, string view, bool winter)
    {
        var environment = new AmbientEnvironment(() => new(2026, month, 4, 22, 10, 11, TimeSpan.FromHours(9)))
            { Settings = new(weather), SeasonDecor = "auto" };
        var state = environment.Sample();
        Assert.Equal(view, state.WindowView); Assert.Equal(winter, state.WinterDecor);
    }

    [Fact]
    public void SettingsDefaultsInvalidInputsAndCliChoicesAreExplicit()
    {
        var defaults = new Settings().Normalize();
        Assert.Equal("off", defaults.Ambient.Weather); Assert.Equal("off", defaults.SeasonDecor); Assert.Equal("on", defaults.Pets);
        var input = JsonSerializer.Deserialize<Settings>("{\"ambient\":{\"weather\":\"rain\"},\"seasonDecor\":\"auto\",\"pets\":\"off\"}", StateJson.Options)!.Normalize();
        Assert.Equal("rain", input.Ambient.Weather); Assert.Equal("auto", input.SeasonDecor); Assert.Equal("off", input.Pets);
        var bad = JsonSerializer.Deserialize<Settings>("{\"ambient\":null,\"seasonDecor\":\"bad\",\"pets\":null}", StateJson.Options)!.Normalize();
        Assert.Equal("off", bad.Ambient.Weather); Assert.Equal("off", bad.SeasonDecor); Assert.Equal("on", bad.Pets);
        Assert.Equal("snow", Program.ParseChoice("snow", "--ambient-weather", "off", "rain", "snow", "auto"));
        Assert.Throws<ArgumentException>(() => Program.ParseChoice("storm", "--ambient-weather", "off", "rain", "snow", "auto"));
    }

    [Fact]
    public void TrackerSnapshotOmitsRendererPetsAndOlderInputKeepsOptionalDefaults()
    {
        var snapshot = new StateSnapshot(StateJson.SchemaVersion, DateTimeOffset.UtcNow, 1, SpriteAtlas.Info,
            new(8, 8, 16, new(1, 3), []), [], []);
        var json = JsonSerializer.Serialize(snapshot, StateJson.Options);
        Assert.DoesNotContain("\"pets\"", json);
        Assert.Null(JsonSerializer.Deserialize<StateSnapshot>(json, StateJson.Options)!.Pets);
        Assert.Equal(5, StateJson.SchemaVersion);
    }
}
