using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class LootScrollMonitorTests
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task InactiveWarnsOnlyAfterTwoMinuteChecks()
    {
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Inactive, 20_416, level: 2));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);

        await Observe(monitor, frame, 0);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));
        await Observe(monitor, frame, 60);

        var confirmed = monitor.Snapshot(StartedAt.AddSeconds(60));
        Assert.True(confirmed.ShouldWarn);
        Assert.Equal(LootScrollStatus.Inactive, confirmed.Status);
        Assert.Null(confirmed.Level);
        Assert.Equal(StartedAt.AddSeconds(60), confirmed.ObservedAt);
    }

    [Fact]
    public async Task RapidInactiveReadingsStillRequireSixSecondsOfEvidence()
    {
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Inactive, 3_600));
        using var monitor = new LootScrollMonitor(detector, TimeSpan.FromSeconds(1));
        using var frame = new Bitmap(2, 2);

        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 1);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(1)).ShouldWarn);
        await Observe(monitor, frame, 6);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(6)).ShouldWarn);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(3, null)]
    public async Task OnlyRepeatedCountdownConfirmsActivityAndOnlyKnownLevelsAreExposed(int? level, int? expected)
    {
        var remaining = 3_600;
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, level: level));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);

        await Observe(monitor, frame, 0);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));
        remaining -= 60;
        await Observe(monitor, frame, 60);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(60)));
        remaining -= 60;
        await Observe(monitor, frame, 120);

        var state = monitor.Snapshot(StartedAt.AddSeconds(120));
        Assert.Equal(LootScrollStatus.Active, state.Status);
        Assert.Equal(expected, state.Level);
        Assert.False(state.ShouldWarn);
    }

    [Fact]
    public async Task MissingHudBreaksAnInactiveStreakAndClearsAnExistingWarning()
    {
        var reading = Timed(LootScrollStatus.Inactive, 3_600);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);

        await Observe(monitor, frame, 0);
        reading = LootScrollReading.Unknown;
        await Observe(monitor, frame, 60);
        reading = Timed(LootScrollStatus.Inactive, 3_600);
        await Observe(monitor, frame, 120);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(120)).ShouldWarn);
        await Observe(monitor, frame, 180);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(180)).ShouldWarn);

        reading = LootScrollReading.Unknown;
        await Observe(monitor, frame, 240);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(240)));
    }

    [Fact]
    public async Task CountdownClearsAWarningAndConfirmsActivityAfterRepeatedConsumption()
    {
        var reading = Timed(LootScrollStatus.Inactive, 3_600);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 60);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);

        reading = Timed(LootScrollStatus.Active, 3_540, level: 1);
        await Observe(monitor, frame, 120);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));

        reading = Timed(LootScrollStatus.Active, 3_420, level: 2);
        await Observe(monitor, frame, 180);
        Assert.Equal(new LootScrollState(LootScrollStatus.Active, 2, StartedAt.AddSeconds(180)),
            monitor.Snapshot(StartedAt.AddSeconds(180)));
    }

    [Theory]
    [InlineData(LootScrollStatus.Inactive, null, null)]
    [InlineData(LootScrollStatus.Unknown, null, null)]
    [InlineData(LootScrollStatus.Active, 1, 1)]
    [InlineData(LootScrollStatus.Active, 2, 2)]
    [InlineData(LootScrollStatus.Active, 3, null)]
    public async Task FallingTimerOverridesTheGlyphWithoutInferringAConsumptionLevel(
        LootScrollStatus glyph, int? level, int? expectedLevel)
    {
        var reading = Timed(LootScrollStatus.Inactive, 3_600);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 60);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);

        reading = Timed(glyph, 3_480, level: level);
        await Observe(monitor, frame, 120);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));
        reading = Timed(glyph, 3_360, level: level);
        await Observe(monitor, frame, 180);

        var state = monitor.Snapshot(StartedAt.AddSeconds(180));
        Assert.Equal(LootScrollStatus.Active, state.Status);
        Assert.Equal(expectedLevel, state.Level);
        Assert.False(state.ShouldWarn);
    }

    [Fact]
    public async Task StationaryTimerOverridesAnActiveGlyphAfterTheSecondMinuteSample()
    {
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, 3_600, level: 2));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));

        await Observe(monitor, frame, 60);

        var state = monitor.Snapshot(StartedAt.AddSeconds(60));
        Assert.True(state.ShouldWarn);
        Assert.Null(state.Level);
    }

    [Fact]
    public async Task StationarySecondPrecisionTimerStillNeedsSixSeconds()
    {
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, 3_600));
        using var monitor = new LootScrollMonitor(detector, TimeSpan.FromSeconds(1));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 1);
        await Observe(monitor, frame, 5);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(5)).ShouldWarn);

        await Observe(monitor, frame, 6);

        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(6)).ShouldWarn);
    }

    [Theory]
    [InlineData(20_416, 20_415, 20_414)] // one-second OCR jitter over two minutes
    [InlineData(20_416, 16_816, 13_216)] // misread hour digits
    [InlineData(20_416, 20_116, 19_816)] // consumption much faster than either level
    [InlineData(20_416, 20_356, 20_356)] // a single plausible error followed by a stopped clock
    [InlineData(20_416, 20_356, 20_416)] // OCR recovers its original value
    public async Task InconsistentTimerReadsNeverConfirmActivity(int first, int second, int third)
    {
        var remaining = first;
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, level: 2));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        var samples = new[] { first, second, third };
        for (var index = 0; index < samples.Length; index++)
        {
            remaining = samples[index];
            await Observe(monitor, frame, index * 60);
            Assert.NotEqual(LootScrollStatus.Active, monitor.Snapshot(StartedAt.AddSeconds(index * 60)).Status);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 60)]
    [InlineData(2, 60)]
    public async Task CountdownUsesCaptureTimesAndStopsEvenWhenTheActiveGlyphRemains(int speed, int resolution)
    {
        var remaining = 21_600;
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, resolution, level: 2));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining -= (resolution == 1 ? 65 : 60) * speed;
        await Observe(monitor, frame, 65);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(65)));
        remaining -= (resolution == 1 ? 75 : 60) * speed;
        await Observe(monitor, frame, 140);
        Assert.Equal(LootScrollStatus.Active, monitor.Snapshot(StartedAt.AddSeconds(140)).Status);

        await Observe(monitor, frame, 205);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(205)).ShouldWarn);
        await Observe(monitor, frame, 270);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(270)).ShouldWarn);
    }

    [Fact]
    public async Task ActiveCountdownCannotSurviveMissingTimerOrRechargeDespiteAnActiveGlyph()
    {
        var reading = Timed(LootScrollStatus.Active, 3_600, level: 1);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        reading = reading with { RemainingTime = TimeSpan.FromSeconds(3_540) };
        await Observe(monitor, frame, 60);
        reading = reading with { RemainingTime = TimeSpan.FromSeconds(3_480) };
        await Observe(monitor, frame, 120);
        Assert.Equal(LootScrollStatus.Active, monitor.Snapshot(StartedAt.AddSeconds(120)).Status);

        reading = new(LootScrollStatus.Active, 1);
        await Observe(monitor, frame, 180);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(180)));
        reading = Timed(LootScrollStatus.Active, 3_360, level: 1);
        await Observe(monitor, frame, 240);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(240)));
        reading = reading with { RemainingTime = TimeSpan.FromSeconds(7_200) };
        await Observe(monitor, frame, 300);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(300)));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(180)]
    public async Task CountdownReachingZeroCannotReportAnActiveScroll(int startRemaining)
    {
        var remaining = startRemaining;
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, level: 1));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        for (var at = 0; at <= startRemaining; at += 60)
        {
            remaining = startRemaining - at;
            await Observe(monitor, frame, at);
        }
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(startRemaining)));
        await Observe(monitor, frame, startRemaining + 60);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(startRemaining + 60)).ShouldWarn);
    }

    [Theory]
    [InlineData(10_800, 3_600, 3_600)] // hours cannot resolve changes within one minute
    [InlineData(3_600, 180, 60)] // rounding tolerance must not accumulate over samples
    public async Task CoarsePrecisionCannotHideAnImpossibleConsumptionRate(int startRemaining, int step, int resolution)
    {
        var remaining = startRemaining;
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, resolution, level: 2));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        for (var sample = 0; sample < 3; sample++)
        {
            remaining = startRemaining - sample * step;
            await Observe(monitor, frame, sample * 60);
            Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(sample * 60)));
        }
    }

    [Theory]
    [InlineData(LootScrollStatus.Active)]
    [InlineData(LootScrollStatus.Inactive)]
    public async Task MinutePrecisionTimerAccumulatesEnoughStableTimeBeforeWarning(LootScrollStatus glyph)
    {
        var detector = new StubDetector((_, _) => Timed(glyph, 3_600, resolutionSeconds: 60));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 60);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);

        await Observe(monitor, frame, 120);

        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(120)).ShouldWarn);
        Assert.Equal(3, detector.Calls);
    }

    [Fact]
    public async Task RechargingClearsAWarningAndStartsANewTimerBaseline()
    {
        var reading = Timed(LootScrollStatus.Inactive, 3_600);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 60);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);

        reading = Timed(LootScrollStatus.Inactive, 7_200);
        await Observe(monitor, frame, 120);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));

        await Observe(monitor, frame, 180);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(180)).ShouldWarn);
        reading = Timed(LootScrollStatus.Inactive, 7_140);
        await Observe(monitor, frame, 240);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(240)));
        reading = Timed(LootScrollStatus.Inactive, 7_080);
        await Observe(monitor, frame, 300);
        Assert.Equal(LootScrollStatus.Active, monitor.Snapshot(StartedAt.AddSeconds(300)).Status);
    }

    [Fact]
    public async Task ChangingVisiblePrecisionCannotLookLikeADecliningTimer()
    {
        var reading = Timed(LootScrollStatus.Inactive, 3_665);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector, TimeSpan.FromSeconds(1));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);

        reading = Timed(LootScrollStatus.Inactive, 3_660, resolutionSeconds: 60);
        await Observe(monitor, frame, 60);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(60)));
        await Observe(monitor, frame, 120);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(120)).ShouldWarn);

        await Observe(monitor, frame, 122);

        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(122)).ShouldWarn);
    }

    [Fact]
    public async Task MissingTimerIgnoresAnActiveGlyphAndBreaksTheOldTimerComparison()
    {
        var reading = Timed(LootScrollStatus.Inactive, 3_600);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 60);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);

        reading = new(LootScrollStatus.Active, 1);
        await Observe(monitor, frame, 120);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));

        reading = Timed(LootScrollStatus.Inactive, 3_540);
        await Observe(monitor, frame, 180);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(180)));
        await Observe(monitor, frame, 240);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(240)).ShouldWarn);
    }

    [Theory]
    [InlineData(LootScrollStatus.Inactive)]
    [InlineData(LootScrollStatus.Active)]
    public async Task UnreadableTimerNeverEstablishesActivityFromGlyphs(LootScrollStatus glyph)
    {
        var reading = Timed(LootScrollStatus.Active, 3_600, level: 2);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);

        reading = new(glyph, 2);
        await Observe(monitor, frame, 60);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);
        await Observe(monitor, frame, 120);

        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));
    }

    [Fact]
    public async Task HiddenHudCannotBridgeAComparisonBetweenTwoReadableTimers()
    {
        var reading = Timed(LootScrollStatus.Inactive, 3_600);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        reading = LootScrollReading.Unknown;
        await Observe(monitor, frame, 60);

        reading = Timed(LootScrollStatus.Inactive, 3_540);
        await Observe(monitor, frame, 120);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));

        reading = Timed(LootScrollStatus.Inactive, 3_480);
        await Observe(monitor, frame, 180);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(180)));
        reading = Timed(LootScrollStatus.Inactive, 3_420);
        await Observe(monitor, frame, 240);
        Assert.Equal(LootScrollStatus.Active, monitor.Snapshot(StartedAt.AddSeconds(240)).Status);
    }

    [Fact]
    public async Task StaleTimersCannotEstablishACountdownOrAStoppedClock()
    {
        var reading = Timed(LootScrollStatus.Inactive, 3_600);
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);

        reading = Timed(LootScrollStatus.Inactive, 3_540);
        await Observe(monitor, frame, 90);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(90)));

        await Observe(monitor, frame, 180);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(180)));
        await Observe(monitor, frame, 240);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(240)).ShouldWarn);
    }

    [Fact]
    public async Task ResetDiscardsThePreviousSessionsTimerBaseline()
    {
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Inactive, 3_600));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 60);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);

        monitor.Reset();
        await Observe(monitor, frame, 120);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));
        await Observe(monitor, frame, 180);

        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(180)).ShouldWarn);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(3_600, 0)]
    [InlineData(3_600, -1)]
    [InlineData(3_600, null)]
    public async Task InvalidTimersNeverEstablishActivity(int remaining, int? precision)
    {
        var reading = new LootScrollReading(LootScrollStatus.Inactive)
        {
            RemainingTime = TimeSpan.FromSeconds(remaining),
            TimerResolution = precision.HasValue ? TimeSpan.FromSeconds(precision.Value) : null,
        };
        var detector = new StubDetector((_, _) => reading);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);

        reading = reading with { RemainingTime = reading.RemainingTime - TimeSpan.FromSeconds(1) };
        await Observe(monitor, frame, 60);

        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(60)));
    }

    [Fact]
    public async Task KnownObservationsExpireAndAStaleStreakCannotConfirmInactivity()
    {
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Inactive, 3_600));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 60);

        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(149)).ShouldWarn);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(150)));
        await Observe(monitor, frame, 150);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(150)));
        await Observe(monitor, frame, 210);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(210)).ShouldWarn);
    }

    [Fact]
    public async Task LongerSamplingIntervalsKeepAThirtySecondFreshnessMargin()
    {
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, 3_600));
        using var monitor = new LootScrollMonitor(detector, TimeSpan.FromMinutes(2));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 120);

        Assert.Equal(LootScrollStatus.Inactive, monitor.Snapshot(StartedAt.AddSeconds(269)).Status);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(270)));
    }

    [Fact]
    public async Task RepeatedAndOldFramesDoNotIncreaseDetectionLoad()
    {
        var detector = new StubDetector((_, _) => new(LootScrollStatus.Active));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, -1);
        await Observe(monitor, frame, 30);
        await Observe(monitor, frame, 59);
        Assert.Equal(1, detector.Calls);
        await Observe(monitor, frame, 60);
        Assert.Equal(2, detector.Calls);
    }

    [Fact]
    public async Task SlowDetectorNeverQueuesFramesOrRetainsTheCaptureOwnedBitmap()
    {
        var entered = Completion();
        using var release = new ManualResetEventSlim();
        var observedColor = Color.Empty;
        var detector = new StubDetector((copy, _) =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            observedColor = copy.GetPixel(0, 0);
            return new(LootScrollStatus.Active, 1);
        });
        using var monitor = new LootScrollMonitor(detector);
        var frame = new Bitmap(2, 2);
        frame.SetPixel(0, 0, Color.Gold);
        try
        {
            monitor.Observe(frame, StartedAt);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            frame.Dispose();
            // The source is deliberately disposed: a busy monitor must not even clone it.
            monitor.Observe(frame, StartedAt.AddSeconds(60));
            monitor.Observe(frame, StartedAt.AddSeconds(120));
            Assert.Equal(1, detector.Calls);
        }
        finally
        {
            frame.Dispose();
            release.Set();
        }
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Color.Gold.ToArgb(), observedColor.ToArgb());
        Assert.Equal(1, detector.Calls);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));
    }

    [Fact]
    public async Task ResetCancelsAndRejectsPendingWorkWithoutStartingAConcurrentWorker()
    {
        var entered = Completion();
        using var release = new ManualResetEventSlim();
        var observedToken = CancellationToken.None;
        var reading = new LootScrollReading(LootScrollStatus.Active, 2);
        var detector = new StubDetector((_, token) =>
        {
            observedToken = token;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return reading;
        });
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        try
        {
            monitor.Observe(frame, StartedAt);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            monitor.Reset();
            Assert.True(observedToken.IsCancellationRequested);
            monitor.Observe(frame, StartedAt.AddSeconds(60));
            Assert.Equal(1, detector.Calls);
        }
        finally { release.Set(); }
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(60)));

        reading = Timed(LootScrollStatus.Inactive, 3_600);
        await Observe(monitor, frame, 60);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);
        await Observe(monitor, frame, 120);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(120)).ShouldWarn);
    }

    [Fact]
    public async Task DetectorErrorsClearWarningsAndDoNotFaultTheBackgroundTask()
    {
        var fail = false;
        var detector = new StubDetector((_, _) => fail
            ? throw new InvalidOperationException("HUD unavailable")
            : Timed(LootScrollStatus.Inactive, 3_600));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 60);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(60)).ShouldWarn);
        fail = true;
        await Observe(monitor, frame, 120);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(120)));
        fail = false;
        await Observe(monitor, frame, 180);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(180)).ShouldWarn);
        await Observe(monitor, frame, 240);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(240)).ShouldWarn);
    }

    [Fact]
    public async Task ABitmapCopyFailureCannotEscapeIntoCapture()
    {
        var detector = new StubDetector((_, _) => new(LootScrollStatus.Active));
        using var monitor = new LootScrollMonitor(detector);
        var frame = new Bitmap(2, 2);
        frame.Dispose();

        await Observe(monitor, frame, 0);

        Assert.Equal(0, detector.Calls);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));
    }

    [Fact]
    public async Task DisposeDoesNotWaitForDetectionAndDisposesItsEngineOnlyAfterItReturns()
    {
        var entered = Completion();
        using var release = new ManualResetEventSlim();
        var detector = new StubDetector((_, _) =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return new(LootScrollStatus.Active, 2);
        }) { ThrowOnDispose = true };
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        try
        {
            monitor.Observe(frame, StartedAt);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            monitor.Dispose();
            Assert.Equal(0, detector.Disposals);
            Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));
            monitor.Observe(frame, StartedAt.AddSeconds(60));
            monitor.Reset();
            Assert.Equal(1, detector.Calls);
        }
        finally { release.Set(); }
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, detector.Disposals);
        monitor.Dispose();
        Assert.Equal(1, detector.Disposals);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));
    }

    [Fact]
    public async Task FaultyCancellationCallbackCannotEscapeReset()
    {
        var entered = Completion();
        using var release = new ManualResetEventSlim();
        var detector = new StubDetector((_, token) =>
        {
            using var registration = token.Register(() => throw new InvalidOperationException("cancel failed"));
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return new(LootScrollStatus.Active);
        });
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        try
        {
            monitor.Observe(frame, StartedAt);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            monitor.Reset();
        }
        finally { release.Set(); }
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));
    }

    private static LootScrollReading Timed(LootScrollStatus status, double remainingSeconds,
        double resolutionSeconds = 1, int? level = null) => new(status, level)
    {
        RemainingTime = TimeSpan.FromSeconds(remainingSeconds),
        TimerResolution = TimeSpan.FromSeconds(resolutionSeconds),
    };

    private static async Task Observe(LootScrollMonitor monitor, Bitmap frame, double seconds)
    {
        monitor.Observe(frame, StartedAt.AddSeconds(seconds));
        await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class StubDetector(Func<Bitmap, CancellationToken, LootScrollReading> read)
        : ILootScrollFrameDetector
    {
        private int _calls;
        private int _disposals;
        public int Calls => Volatile.Read(ref _calls);
        public int Disposals => Volatile.Read(ref _disposals);
        public bool ThrowOnDispose { get; init; }
        public LootScrollReading Analyze(Bitmap frame, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return read(frame, cancellationToken);
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            if (ThrowOnDispose) throw new InvalidOperationException("dispose failed");
        }
    }
}
