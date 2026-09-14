using System.Globalization;
using System.Net;
using System.Text.Json;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class BdoClockTests
{
    [Theory]
    [InlineData("2026-09-13T00:20:00Z", "07:00:00", true, 12000)]
    [InlineData("2026-09-13T00:20:02Z", "07:00:09", true, 11998)]
    [InlineData("2026-09-13T03:39:58Z", "21:59:51", true, 2)]
    [InlineData("2026-09-13T03:40:00Z", "22:00:00", false, 2400)]
    [InlineData("2026-09-13T03:40:02Z", "22:00:27", false, 2398)]
    [InlineData("2026-09-13T03:48:54Z", "00:00:09", false, 1866)]
    [InlineData("2026-09-13T04:19:58Z", "06:59:33", false, 2)]
    [InlineData("2026-09-13T04:20:00Z", "07:00:00", true, 12000)]
    [InlineData("2026-09-13T00:00:00Z", "02:30:00", false, 1200)]
    [InlineData("2018-06-08T07:53:00Z", "00:55:30", false, 1620)]
    [InlineData("1969-12-31T23:40:00Z", "22:00:00", false, 2400)]
    [InlineData("1969-12-31T00:20:00Z", "07:00:00", true, 12000)]
    public void RegularWorldCycleUsesDifferentDayAndNightRatesAndWrapsMidnight(string instant, string gameTime, bool isDay, int remainingSeconds)
    {
        var clock = BdoClock.At(Parse(instant));
        Assert.Equal(TimeSpan.Parse(gameTime, CultureInfo.InvariantCulture), clock.GameTime);
        Assert.Equal(isDay, clock.IsDaytime);
        Assert.Equal(TimeSpan.FromSeconds(remainingSeconds), clock.UntilNextChange);
    }

    [Fact]
    public void FractionalInstantsEitherSideOfSunriseAndSunsetKeepPositiveCountdowns()
    {
        var sunrise = Parse("2026-09-13T04:20:00Z");
        var sunset = Parse("2026-09-13T03:40:00Z");
        Assert.False(BdoClock.At(sunrise.AddTicks(-1)).IsDaytime);
        Assert.Equal(TimeSpan.FromTicks(1), BdoClock.At(sunrise.AddTicks(-1)).UntilNextChange);
        Assert.True(BdoClock.At(sunrise).IsDaytime);
        Assert.True(BdoClock.At(sunset.AddTicks(-1)).IsDaytime);
        Assert.Equal(TimeSpan.FromTicks(1), BdoClock.At(sunset.AddTicks(-1)).UntilNextChange);
        Assert.False(BdoClock.At(sunset).IsDaytime);
    }

    [Fact]
    public void GameTimeDependsOnlyOnUtcInstantAcrossTimezoneAndDstChanges()
    {
        var utc = Parse("2026-10-25T00:20:00Z");
        Assert.Equal(BdoClock.At(utc), BdoClock.At(utc.ToOffset(TimeSpan.FromHours(2))));
        Assert.Equal(BdoClock.At(utc), BdoClock.At(utc.ToOffset(TimeSpan.FromHours(-7))));
        Assert.Equal(BdoClock.At(utc), BdoClock.At(utc.AddHours(24)));
    }

    [Fact]
    public void RegionalCalibrationOffsetsTheCycleWithoutChangingLocalTime()
    {
        var now = Parse("2026-09-13T00:20:00Z");
        var widget = OverlayCatalog.CreateWidget("clock") with { ClockOffsetMinutes = -40 };
        var view = OverlayClockPresentation.Create(widget, now, TimeZoneInfo.Utc);
        Assert.Equal("00:20", view.Rows[0].Value);
        Assert.Equal("22:00", view.Rows[1].Value);
        Assert.Equal("Tag in", view.Rows[2].Label);
        Assert.Equal("00:40", view.Rows[2].Value);
        Assert.Equal(BdoClock.At(now), BdoClock.At(now, 240));
        Assert.Equal(BdoClock.At(now), BdoClock.At(now, -240));
    }

    [Theory]
    [InlineData("2026-09-13T03:38:59Z", "00:02")]
    [InlineData("2026-09-13T03:39:00Z", "00:01")]
    [InlineData("2026-09-13T03:39:59.9999999Z", "00:01")]
    public void CountdownRoundsUpToMinutesWithoutShowingZeroBeforeActualTransition(string instant, string expected)
    {
        var widget = OverlayCatalog.CreateWidget("clock");
        var view = OverlayClockPresentation.Create(widget, Parse(instant), TimeZoneInfo.Utc);
        Assert.Equal("Nacht in", view.Rows[^1].Label);
        Assert.Equal(expected, view.Rows[^1].Value);
    }

    [Fact]
    public void LocalDisplayHonorsTimeZoneDaylightSavingWhileGameTimeDoesNotShift()
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        var widget = OverlayCatalog.CreateWidget("clock");
        var before = OverlayClockPresentation.Create(widget, Parse("2026-10-25T00:20:00Z"), berlin);
        var after = OverlayClockPresentation.Create(widget, Parse("2026-10-25T01:20:00Z"), berlin);
        Assert.Equal("02:20", before.Rows[0].Value);
        Assert.Equal("02:20", after.Rows[0].Value);
        Assert.Equal("07:00", before.Rows[1].Value);
        Assert.Equal("11:30", after.Rows[1].Value);
    }

    [Fact]
    public void DisabledRowsStayHiddenAndInvalidSettingsRetainOneRow()
    {
        var now = Parse("2026-09-13T03:40:00Z");
        var widget = OverlayCatalog.CreateWidget("clock") with { ShowRealTime = false, ShowGameTime = false };
        var row = Assert.Single(OverlayClockPresentation.Create(widget, now).Rows);
        Assert.Equal("Tag in", row.Label);
        Assert.Equal("00:40", row.Value);
        var normalized = Assert.Single(OverlayLayout.Normalize(new OverlaySettings
        {
            Widgets = [widget with { ShowDayNightCountdown = false, ClockOffsetMinutes = int.MaxValue }],
        }).Widgets);
        Assert.True(normalized.ShowRealTime);
        Assert.Equal(240, normalized.ClockOffsetMinutes);
        Assert.Equal("real", Assert.Single(OverlayClockPresentation.Create(normalized, now).Rows).Kind);
    }

    [Fact]
    public async Task SmallPreviewKeepsAllClockRowsAndUsesTheSnapshotInstantWhenIdle()
    {
        var snapshot = new OverlaySnapshot { ClockUtcNow = Parse("2026-09-13T03:40:00Z") };
        var widget = OverlayLayout.ResizeWidget(OverlayCatalog.CreateWidget("clock") with { FontScale = 2 }, 100, 32);
        var markup = await Render(widget, snapshot);
        Assert.Contains("Uhrzeit · Nacht", markup);
        Assert.Contains("Lokal", markup);
        Assert.Contains("BDO", markup);
        Assert.Contains("Tag in", markup);
        Assert.Contains("22:00", markup);
        Assert.Contains("00:40", markup);
        Assert.DoesNotMatch(@"\d{2}:\d{2}:\d{2}", markup);
        Assert.Contains("data-min-content-height=\"208\"", markup);
    }

    [Fact]
    public async Task PreviewOmitsDeselectedClockRows()
    {
        var widget = OverlayCatalog.CreateWidget("clock", 0, 0) with
            { ShowRealTime = false, ShowGameTime = false, ShowIcon = false, ShowLabel = false };
        var snapshot = new OverlaySnapshot { ClockUtcNow = Parse("2026-09-13T03:40:00Z") };
        var markup = await Render(widget, snapshot);
        Assert.DoesNotContain("is-real", markup);
        Assert.DoesNotContain("is-game", markup);
        Assert.Contains("Tag in", markup);
        Assert.Contains("00:40", markup);
        Assert.DoesNotMatch(@"\d{2}:\d{2}:\d{2}", markup);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacySecondsPreferenceCannotRestoreSecondsInAnyClockRow(bool legacySeconds)
    {
        var json = "{\"Widgets\":[{\"Kind\":\"clock\",\"ClockShowSeconds\":" +
            (legacySeconds ? "true" : "false") + "}]}";
        var settings = OverlayLayout.Normalize(JsonSerializer.Deserialize<OverlaySettings>(json));
        var clock = OverlayClockPresentation.Create(Assert.Single(settings.Widgets), Parse("2026-09-13T00:20:02Z"), TimeZoneInfo.Utc);

        Assert.Equal(new[] { "00:20", "07:00", "03:20" }, clock.Rows.Select(row => row.Value));
        Assert.DoesNotContain("ClockShowSeconds", JsonSerializer.Serialize(settings));
    }

    [Fact]
    public async Task ActiveSessionDurationKeepsItsSeconds()
    {
        var snapshot = new OverlayMetrics().Update(new TrackerState
        {
            HasSession = true, IsRunning = true, Elapsed = new TimeSpan(1, 2, 3),
        }, new TrackerPreferences());

        Assert.Equal("01:02:03", snapshot.Metrics["duration"].Value);
        Assert.Contains("01:02:03", await Render(OverlayCatalog.CreateWidget("duration"), snapshot));
    }

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static async Task<string> Render(OverlayWidget widget, OverlaySnapshot snapshot)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<OverlayWidgetPreview>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OverlayWidgetPreview.Widget)] = widget,
                [nameof(OverlayWidgetPreview.Snapshot)] = snapshot,
            }));
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
    }
}
