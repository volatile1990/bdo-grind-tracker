using BdoGrindTracker.App.Analysis;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffRepeatedTimerTests
{
    [Theory]
    [InlineData("60s 60s 60s", 60, 1)]
    [InlineData("60m 60m 60m", 3600, 60)]
    [InlineData("2h 2h 2h", 7200, 3600)]
    [InlineData("3h 3h 3h", 10800, 3600)]
    [InlineData("4h 4h 4h", 14400, 3600)]
    [InlineData("4 h 4 h 4 h", 14400, 3600)]
    [InlineData("4h 4hr 4Std.", 14400, 3600)]
    [InlineData("  4h\n4h\t4h  ", 14400, 3600)]
    [InlineData("19m 19m 19m", 1140, 60)]
    [InlineData("1h 30m 1h 30m 1h 30m", 5400, 60)]
    [InlineData("1:25 1:25 1:25", 85, 1)]
    public void ThreeMatchingCompleteTimersPreserveTheirDisplayedPrecision(string text, int seconds, int precision)
    {
        var actual = BuffFrameReader.ParseRepeatedTimer(text);

        Assert.NotNull(actual);
        Assert.Equal(TimeSpan.FromSeconds(seconds), actual.Value.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(precision), actual.Value.Precision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("4h")]
    [InlineData("4h 4h")]
    [InlineData("4h 4h 4h 4h")]
    [InlineData("4h4h4h")]
    [InlineData("4h 3h 4h")]
    [InlineData("4h 240m 4h")]
    [InlineData("60m 1h 60m")]
    [InlineData("4h 4 h 4h")]
    [InlineData("4h 4h unreadable")]
    [InlineData("4h 4h 4h nearby text")]
    [InlineData("nearby text 4h 4h 4h")]
    [InlineData("4 4 4")]
    [InlineData("0s 0s 0s")]
    [InlineData("25h 25h 25h")]
    [InlineData("1h 80m 1h 80m 1h 80m")]
    [InlineData("4h 4h 4h 4h 4h 4h")]
    public void MissingExtraConflictingOrAmbiguousCopiesRemainUnknown(string? text) =>
        Assert.Null(BuffFrameReader.ParseRepeatedTimer(text));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FourEmptyNativeReadsRecoverBothRealTentTimersOnlyAfterTwoRepeatedConsensusReads(bool boon)
    {
        var calls = 0;
        using var reader = new BuffFrameReader(() => null, (_, _) =>
            new(++calls <= 4 ? "" : "4h 4h 4h", default));
        using var frame = LoadFrame();

        var timer = reader.ReadTimer(frame, TimerRegion(boon), CancellationToken.None);

        Assert.NotNull(timer);
        Assert.Equal(TimeSpan.FromHours(4), timer.Value.Remaining);
        Assert.Equal(TimeSpan.FromHours(1), timer.Value.Precision);
        Assert.Equal(6, calls);
    }

    [Theory]
    [InlineData("4h 4h 4h", "4h 4h 4h", true)]
    [InlineData("4 h 4 h 4 h", "4h 4h 4h", true)]
    [InlineData("4h 4h 4h", "3h 3h 3h", false)]
    [InlineData("4h 4h 4h", "240m 240m 240m", false)]
    [InlineData("4h 4h 4h", "", false)]
    [InlineData("4h 4h 4h", "4h 4h", false)]
    [InlineData("4h 3h 4h", "4h 4h 4h", false)]
    [InlineData("4h 4h", "4h 4h 4h", false)]
    [InlineData("", "4h 4h 4h", false)]
    public void RepeatedFallbackRequiresAllThreeCopiesAndBothPreparationsToAgree(
        string first, string second, bool expected)
    {
        var calls = 0;
        using var reader = new BuffFrameReader(() => null, (_, _) =>
            new(++calls <= 4 ? "" : calls == 5 ? first : second, default));
        using var frame = LoadFrame();

        var timer = reader.ReadTimer(frame, TimerRegion(), CancellationToken.None);

        Assert.Equal(expected, timer.HasValue);
        Assert.InRange(calls, 5, 6);
        if (expected)
        {
            Assert.Equal(TimeSpan.FromHours(4), timer!.Value.Remaining);
            Assert.Equal(TimeSpan.FromHours(1), timer.Value.Precision);
            Assert.Equal(6, calls);
        }
    }

    [Theory]
    [InlineData("unreadable", "", "", "")]
    [InlineData("", "", "unreadable", "")]
    [InlineData("", "", "4h", "")]
    [InlineData("", "", "4h", "3h")]
    public void NonemptyNativeReadCannotBeOverriddenByRepeatedFallback(
        string first, string second, string third, string fourth)
    {
        var results = new[] { first, second, third, fourth };
        var calls = 0;
        using var reader = new BuffFrameReader(() => null, (_, _) =>
            new(++calls <= 4 ? results[calls - 1] : "4h 4h 4h", default));
        using var frame = LoadFrame();

        Assert.Null(reader.ReadTimer(frame, TimerRegion(), CancellationToken.None));
        Assert.Equal(4, calls);
    }

    [Fact]
    public void ConflictingPrimaryReadsStopBeforeEitherFallback()
    {
        var calls = 0;
        using var reader = new BuffFrameReader(() => null, (_, _) =>
            new(++calls == 1 ? "4h" : calls == 2 ? "3h" : "4h 4h 4h", default));
        using var frame = LoadFrame();

        Assert.Null(reader.ReadTimer(frame, TimerRegion(), CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ExistingSuccessfulNativeTimerDoesNotUseRepeatedFallback()
    {
        var calls = 0;
        using var reader = new BuffFrameReader(() => null, (_, _) => new(++calls == 1 ? "2h" : "", default));
        using var frame = LoadFrame();

        var timer = reader.ReadTimer(frame, TimerRegion(), CancellationToken.None);

        Assert.NotNull(timer);
        Assert.Equal(TimeSpan.FromHours(2), timer.Value.Remaining);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CancellationBetweenRepeatedPreparationsStopsRecognition()
    {
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        using var reader = new BuffFrameReader(() => null, (_, _) =>
        {
            if (++calls <= 4) return new("", default);
            cancellation.Cancel();
            return new("4h 4h 4h", default);
        });
        using var frame = LoadFrame();

        Assert.Throws<OperationCanceledException>(() => reader.ReadTimer(frame, TimerRegion(), cancellation.Token));
        Assert.Equal(5, calls);
    }

    private static Mat LoadFrame() => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "buffs", "bar-tent-refresh-4h.png"), ImreadModes.Color);

    private static Rectangle TimerRegion(bool boon = false) => boon
        ? new Rectangle(299, 135, 55, 21)
        : new Rectangle(250, 134, 54, 21);
}
