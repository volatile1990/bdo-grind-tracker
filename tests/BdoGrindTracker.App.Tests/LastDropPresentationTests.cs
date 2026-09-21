using BdoGrindTracker.App.Components;

namespace BdoGrindTracker.App.Tests;

public sealed class LastDropPresentationTests
{
    [Theory]
    [InlineData(0, "Gerade eben", "Just now")]
    [InlineData(4.999, "Gerade eben", "Just now")]
    [InlineData(5, "vor 5 s", "5s ago")]
    [InlineData(59.999, "vor 59 s", "59s ago")]
    [InlineData(60, "vor 1 Min.", "1m ago")]
    [InlineData(3599.999, "vor 59 Min.", "59m ago")]
    [InlineData(3600, "vor 1 Std. 0 Min.", "1h 0m ago")]
    [InlineData(3660, "vor 1 Std. 1 Min.", "1h 1m ago")]
    [InlineData(86399, "vor 23 Std. 59 Min.", "23h 59m ago")]
    [InlineData(86400, "vor 24 Std. 0 Min.", "24h 0m ago")]
    public void FormatsActiveTimeSinceDropAtUnitBoundaries(double ageSeconds, string german, string english)
    {
        var drop = TimeSpan.FromMinutes(10);
        var elapsed = drop + TimeSpan.FromSeconds(ageSeconds);

        Assert.Equal(german, LastDropPresentation.Create(drop, elapsed, "de").Label);
        Assert.Equal(english, LastDropPresentation.Create(drop, elapsed, "en").Label);
    }

    [Theory]
    [InlineData(null, 100d)]
    [InlineData(-1d, 100d)]
    [InlineData(101d, 100d)]
    [InlineData(0d, -1d)]
    public void UnknownOrInvalidObservationDoesNotInventAnAge(double? observedSeconds, double elapsedSeconds)
    {
        TimeSpan? drop = observedSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
        var elapsed = TimeSpan.FromSeconds(elapsedSeconds);

        var german = LastDropPresentation.Create(drop, elapsed, "de");
        var english = LastDropPresentation.Create(drop, elapsed, "en");

        Assert.Equal("—", german.Label);
        Assert.Equal("Für dieses Item wurde noch kein Zeitpunkt des letzten Drops erfasst.", german.Description);
        Assert.Equal("—", english.Label);
        Assert.Equal("No last drop time has been recorded for this item yet.", english.Description);
    }

    [Theory]
    [InlineData("de", "Letzter Drop: vor 2 Min. · aktive Grindzeit, ohne Pausen.")]
    [InlineData("en", "Last drop: 2m ago · active grind time, excluding pauses.")]
    public void DescriptionExplainsTheClockBasis(string language, string expected)
    {
        var presentation = LastDropPresentation.Create(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5), language);

        Assert.Equal(expected, presentation.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unsupported")]
    public void UnspecifiedLanguageUsesTheAppDefault(string? language)
    {
        var presentation = LastDropPresentation.Create(TimeSpan.Zero, TimeSpan.Zero, language);

        Assert.Equal("Just now", presentation.Label);
    }
}
