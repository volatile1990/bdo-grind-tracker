using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class LootScrollMonitorTests
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task TwoIdenticalSecondPrecisionSamplesWarnAfterThirtySeconds()
    {
        var detector = new StubDetector((_, _) => Timed(LootScrollStatus.Active, 20_416, level: 2));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);

        await Observe(monitor, frame, 0);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));
        await Observe(monitor, frame, 30);

        Assert.Equal(new LootScrollState(LootScrollStatus.Inactive, null, StartedAt.AddSeconds(30)),
            monitor.Snapshot(StartedAt.AddSeconds(30)));
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(30)).ShouldWarn);
    }

    [Theory]
    [InlineData(1, LootScrollStatus.Inactive, 2)]
    [InlineData(1, LootScrollStatus.Active, 2)]
    [InlineData(1, LootScrollStatus.Unknown, null)]
    [InlineData(1, LootScrollStatus.Active, 0)]
    [InlineData(1, LootScrollStatus.Active, 3)]
    [InlineData(2, LootScrollStatus.Inactive, 1)]
    [InlineData(2, LootScrollStatus.Active, 1)]
    [InlineData(2, LootScrollStatus.Unknown, null)]
    public async Task OneCountdownIntervalDeterminesTheLevelWithoutUsingGlyphStatusOrLevel(
        int speed, LootScrollStatus glyph, int? glyphLevel)
    {
        var remaining = 3_600;
        var detector = new StubDetector((_, _) => Timed(glyph, remaining, level: glyphLevel));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);

        await Observe(monitor, frame, 0);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt));
        remaining -= 30 * speed;
        await Observe(monitor, frame, 30);

        var state = monitor.Snapshot(StartedAt.AddSeconds(30));
        Assert.Equal(new LootScrollState(LootScrollStatus.Active, speed, StartedAt.AddSeconds(30)), state);
        Assert.False(state.ShouldWarn);
    }

    [Theory]
    [InlineData(27, 1)]
    [InlineData(30, 1)]
    [InlineData(33, 1)]
    [InlineData(57, 2)]
    [InlineData(60, 2)]
    [InlineData(63, 2)]
    public async Task SmallSecondsRoundingErrorsKeepTheTwoConsumptionLevelsSeparate(int consumed, int expected)
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining -= consumed;
        await Observe(monitor, frame, 30);

        Assert.Equal(expected, monitor.Snapshot(StartedAt.AddSeconds(30)).Level);
    }

    [Theory]
    [InlineData(1)] // OCR jitter on a stopped clock
    [InlineData(15)] // half speed
    [InlineData(45)] // mixed levels
    [InlineData(90)] // three times wall time
    [InlineData(300)] // implausible OCR digit change
    public async Task MixedOrImplausibleRatesNeverGuessALevel(int consumptionPerThirtySeconds)
    {
        var remaining = 20_416;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, level: 2)));
        using var frame = new Bitmap(2, 2);
        for (var sample = 0; sample < 5; sample++)
        {
            remaining = 20_416 - sample * consumptionPerThirtySeconds;
            await Observe(monitor, frame, sample * 30);
            Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(sample * 30)));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CountdownUsesCaptureTimeRatherThanTheNominalSamplingInterval(int speed)
    {
        var remaining = 21_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining -= 65 * speed;
        await Observe(monitor, frame, 65);
        Assert.Equal(speed, monitor.Snapshot(StartedAt.AddSeconds(65)).Level);

        remaining -= 75 * speed;
        await Observe(monitor, frame, 140);
        Assert.Equal(new LootScrollState(LootScrollStatus.Active, speed, StartedAt.AddSeconds(140)),
            monitor.Snapshot(StartedAt.AddSeconds(140)));
    }

    [Fact]
    public async Task EveryFreshUnambiguousIntervalCanImmediatelyChangeTheConfirmedStatus()
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, level: 2)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 30);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(30)).ShouldWarn);

        remaining -= 30;
        await Observe(monitor, frame, 60);
        Assert.Equal(1, monitor.Snapshot(StartedAt.AddSeconds(60)).Level);
        remaining -= 60;
        await Observe(monitor, frame, 90);
        Assert.Equal(2, monitor.Snapshot(StartedAt.AddSeconds(90)).Level);
        await Observe(monitor, frame, 120);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(120)).ShouldWarn);
        Assert.Null(monitor.Snapshot(StartedAt.AddSeconds(120)).Level);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    public async Task AMidIntervalSwitchResolvesOnTheNextPureInterval(int originalSpeed, int newSpeed)
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining -= 30 * originalSpeed;
        await Observe(monitor, frame, 30);

        remaining -= 15 * originalSpeed + 15 * newSpeed;
        await Observe(monitor, frame, 60);
        remaining -= 30 * newSpeed;
        await Observe(monitor, frame, 90);

        var expected = newSpeed == 0 ? new LootScrollState(LootScrollStatus.Inactive, null, StartedAt.AddSeconds(90))
            : new LootScrollState(LootScrollStatus.Active, newSpeed, StartedAt.AddSeconds(90));
        Assert.Equal(expected, monitor.Snapshot(StartedAt.AddSeconds(90)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingOcrPreservesTheConfirmedStatusWithoutRenewingItsTwoMinuteLifetime(int speed)
    {
        var reading = Timed(LootScrollStatus.Unknown, 3_600);
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => reading));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        reading = Timed(LootScrollStatus.Unknown, 3_600 - 30 * speed);
        await Observe(monitor, frame, 30);
        var confirmed = monitor.Snapshot(StartedAt.AddSeconds(30));

        reading = LootScrollReading.Unknown;
        foreach (var at in new[] { 60, 90, 120 })
        {
            await Observe(monitor, frame, at);
            Assert.Equal(confirmed, monitor.Snapshot(StartedAt.AddSeconds(at)));
        }
        Assert.Equal(confirmed, monitor.Snapshot(StartedAt.AddSeconds(149)));
        await Observe(monitor, frame, 150);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(150)));

        reading = Timed(LootScrollStatus.Unknown, 3_000);
        await Observe(monitor, frame, 180);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(180)));
        reading = Timed(LootScrollStatus.Unknown, 3_000 - 30 * speed);
        await Observe(monitor, frame, 210);
        Assert.Equal(speed == 0 ? LootScrollStatus.Inactive : LootScrollStatus.Active,
            monitor.Snapshot(StartedAt.AddSeconds(210)).Status);
    }

    [Fact]
    public async Task OneUnreadableSampleKeepsTheUsefulTimerBaseline()
    {
        var reading = Timed(LootScrollStatus.Unknown, 3_600);
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => reading));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        reading = LootScrollReading.Unknown;
        await Observe(monitor, frame, 30);
        reading = Timed(LootScrollStatus.Unknown, 3_540);
        await Observe(monitor, frame, 60);

        Assert.Equal(new LootScrollState(LootScrollStatus.Active, 1, StartedAt.AddSeconds(60)),
            monitor.Snapshot(StartedAt.AddSeconds(60)));
    }

    [Theory]
    [InlineData(100)] // a large single OCR error must not become the next baseline
    [InlineData(3_525)] // an ambiguous 1.5x rate also must not replace it
    [InlineData(3_569)] // a one-second OCR fluctuation
    public async Task OneBadReadableSampleKeepsStatusAndAllowsRecoveryFromTheLastUsefulBaseline(int badRemaining)
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining = 3_570;
        await Observe(monitor, frame, 30);
        var confirmed = monitor.Snapshot(StartedAt.AddSeconds(30));

        remaining = badRemaining;
        await Observe(monitor, frame, 60);
        Assert.Equal(confirmed, monitor.Snapshot(StartedAt.AddSeconds(60)));
        remaining = 3_510;
        await Observe(monitor, frame, 90);

        Assert.Equal(new LootScrollState(LootScrollStatus.Active, 1, StartedAt.AddSeconds(90)),
            monitor.Snapshot(StartedAt.AddSeconds(90)));
    }

    [Fact]
    public async Task RepeatedAmbiguousReadingsCannotKeepAConfirmedStateAlive()
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining = 3_570;
        await Observe(monitor, frame, 30);
        for (var at = 60; at <= 150; at += 30)
        {
            remaining = 3_570 - (at - 30) * 3 / 2;
            await Observe(monitor, frame, at);
        }

        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(150)));
        remaining -= 45;
        await Observe(monitor, frame, 180); // the old comparison window has expired
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(180)));
        remaining -= 30;
        await Observe(monitor, frame, 210);
        Assert.Equal(1, monitor.Snapshot(StartedAt.AddSeconds(210)).Level);
    }

    [Theory]
    [InlineData(LootScrollStatus.Inactive)]
    [InlineData(LootScrollStatus.Active)]
    [InlineData(LootScrollStatus.Unknown)]
    public async Task UnreadableTimersNeverEstablishAStatusFromGlyphs(LootScrollStatus glyph)
    {
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => new(glyph, 2)));
        using var frame = new Bitmap(2, 2);
        for (var at = 0; at <= 180; at += 30)
        {
            await Observe(monitor, frame, at);
            Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(at)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RapidSamplesStillNeedSixSecondsOfEvidence(int speed)
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining)),
            TimeSpan.FromSeconds(1));
        using var frame = new Bitmap(2, 2);
        foreach (var at in new[] { 0, 1, 5 })
        {
            remaining = 3_600 - at * speed;
            await Observe(monitor, frame, at);
            Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(at)));
        }
        remaining = 3_600 - 6 * speed;
        await Observe(monitor, frame, 6);

        Assert.Equal(speed == 0 ? LootScrollStatus.Inactive : LootScrollStatus.Active,
            monitor.Snapshot(StartedAt.AddSeconds(6)).Status);
        Assert.Equal(speed == 0 ? (int?)null : speed, monitor.Snapshot(StartedAt.AddSeconds(6)).Level);
    }

    [Fact]
    public async Task MinutePrecisionEqualityRequiresMoreThanAMinuteBeforeWarning()
    {
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Active, 3_600, 60, 2)));
        using var frame = new Bitmap(2, 2);
        foreach (var at in new[] { 0, 30, 60 })
        {
            await Observe(monitor, frame, at);
            Assert.False(monitor.Snapshot(StartedAt.AddSeconds(at)).ShouldWarn);
        }
        await Observe(monitor, frame, 90);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(90)).ShouldWarn);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MinutePrecisionWaitsForAnUnambiguousLongerConsumptionWindow(int speed)
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining, 60)));
        using var frame = new Bitmap(2, 2);
        for (var at = 0; at <= 120; at += 30)
        {
            remaining = 3_600 - at * speed / 60 * 60;
            await Observe(monitor, frame, at);
            var state = monitor.Snapshot(StartedAt.AddSeconds(at));
            if (at < 120) Assert.Equal(LootScrollState.Unknown, state);
            else Assert.Equal(speed, state.Level);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MinutePrecisionComparisonAllowsNormalSamplingJitterWithoutExtendingStatusFreshness(int speed)
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining, 60)));
        using var frame = new Bitmap(2, 2);
        for (var sample = 0; sample < 5; sample++)
        {
            var at = sample * 30.1;
            remaining = 3_600 - (int)Math.Floor(at * speed / 60) * 60;
            await Observe(monitor, frame, at);
            var state = monitor.Snapshot(StartedAt.AddSeconds(at));
            if (sample < 4) Assert.Equal(LootScrollState.Unknown, state);
            else Assert.Equal(speed, state.Level);
        }

        Assert.Equal(speed, monitor.Snapshot(StartedAt.AddSeconds(240.3)).Level);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(240.4)));
    }

    [Theory]
    [InlineData(10_800, 3_600, 3_600)]
    [InlineData(3_600, 180, 60)]
    public async Task CoarsePrecisionCannotHideAnImpossibleConsumptionRate(int initial, int step, int resolution)
    {
        var remaining = initial;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, resolution, 2)));
        using var frame = new Bitmap(2, 2);
        for (var sample = 0; sample < 3; sample++)
        {
            remaining = initial - sample * step;
            await Observe(monitor, frame, sample * 60);
            Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(sample * 60)));
        }
    }

    [Fact]
    public async Task ChangingVisiblePrecisionStartsANewComparisonInsteadOfInventingConsumption()
    {
        var reading = Timed(LootScrollStatus.Unknown, 3_665);
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => reading));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        reading = Timed(LootScrollStatus.Unknown, 3_660, 60);
        foreach (var at in new[] { 30, 60, 90 })
        {
            await Observe(monitor, frame, at);
            Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(at)));
        }
        await Observe(monitor, frame, 120);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(120)).ShouldWarn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RechargeClearsStatusAndTheNextSampleUsesOnlyTheNewBaseline(int speed)
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining -= 30 * speed;
        await Observe(monitor, frame, 30);
        Assert.NotEqual(LootScrollStatus.Unknown, monitor.Snapshot(StartedAt.AddSeconds(30)).Status);

        remaining = 7_200;
        await Observe(monitor, frame, 60);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(60)));
        remaining -= 30 * speed;
        await Observe(monitor, frame, 90);
        Assert.Equal(speed == 0 ? LootScrollStatus.Inactive : LootScrollStatus.Active,
            monitor.Snapshot(StartedAt.AddSeconds(90)).Status);
        Assert.Equal(speed == 0 ? (int?)null : speed, monitor.Snapshot(StartedAt.AddSeconds(90)).Level);
        remaining -= 30 * speed;
        await Observe(monitor, frame, 120);

        Assert.Equal(speed == 0 ? LootScrollStatus.Inactive : LootScrollStatus.Active,
            monitor.Snapshot(StartedAt.AddSeconds(120)).Status);
        Assert.Equal(speed == 0 ? (int?)null : speed, monitor.Snapshot(StartedAt.AddSeconds(120)).Level);
    }

    [Fact]
    public async Task ReachingZeroCannotLeaveAnActiveStatusBehind()
    {
        var remaining = 60;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Active, remaining, level: 2)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining = 30;
        await Observe(monitor, frame, 30);
        Assert.Equal(1, monitor.Snapshot(StartedAt.AddSeconds(30)).Level);
        remaining = 0;
        await Observe(monitor, frame, 60);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(60)));
        await Observe(monitor, frame, 90);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(90)).ShouldWarn);
    }

    [Fact]
    public async Task ResetImmediatelyDiscardsStatusAndThePreviousSessionsBaseline()
    {
        var remaining = 3_600;
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, remaining)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        remaining -= 30;
        await Observe(monitor, frame, 30);
        Assert.Equal(1, monitor.Snapshot(StartedAt.AddSeconds(30)).Level);

        monitor.Reset();
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(30)));
        remaining -= 30;
        await Observe(monitor, frame, 60);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(60)));
        await Observe(monitor, frame, 90);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(90)).ShouldWarn);
    }

    [Fact]
    public async Task StaleBaselinesCannotEstablishAComparisonAndKnownStatesExpireWithoutNewFrames()
    {
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, 3_600)));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 30);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(149)).ShouldWarn);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(150)));

        await Observe(monitor, frame, 151);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(151)));
        await Observe(monitor, frame, 181);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(181)).ShouldWarn);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(3_600, 0)]
    [InlineData(3_600, -1)]
    [InlineData(3_600, null)]
    public async Task InvalidTimersNeverEstablishActivity(int remaining, int? precision)
    {
        var reading = new LootScrollReading(LootScrollStatus.Active, 2)
        {
            RemainingTime = TimeSpan.FromSeconds(remaining),
            TimerResolution = precision.HasValue ? TimeSpan.FromSeconds(precision.Value) : null,
        };
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => reading));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 30);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(30)));
    }

    [Fact]
    public async Task LongerSamplingIntervalsKeepAThirtySecondFreshnessMargin()
    {
        using var monitor = new LootScrollMonitor(new StubDetector((_, _) => Timed(LootScrollStatus.Unknown, 3_600)),
            TimeSpan.FromMinutes(2));
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 120);

        Assert.Equal(LootScrollStatus.Inactive, monitor.Snapshot(StartedAt.AddSeconds(269)).Status);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(270)));
    }

    [Fact]
    public async Task RepeatedAndOldFramesDoNotIncreaseTheThirtySecondDetectionLoad()
    {
        var detector = new StubDetector((_, _) => LootScrollReading.Unknown);
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        foreach (var at in new[] { 0, 0, -1, 1, 29 })
            await Observe(monitor, frame, at);
        Assert.Equal(1, detector.Calls);
        await Observe(monitor, frame, 30);
        await Observe(monitor, frame, 59);
        Assert.Equal(2, detector.Calls);
        await Observe(monitor, frame, 60);
        Assert.Equal(3, detector.Calls);
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
    public async Task DetectorErrorsPreserveWarningsOnlyUntilTheOriginalObservationExpires()
    {
        var fail = false;
        var detector = new StubDetector((_, _) => fail
            ? throw new InvalidOperationException("HUD unavailable")
            : Timed(LootScrollStatus.Inactive, 3_600));
        using var monitor = new LootScrollMonitor(detector);
        using var frame = new Bitmap(2, 2);
        await Observe(monitor, frame, 0);
        await Observe(monitor, frame, 30);
        var confirmed = monitor.Snapshot(StartedAt.AddSeconds(30));
        Assert.True(confirmed.ShouldWarn);
        fail = true;
        await Observe(monitor, frame, 60);
        Assert.Equal(confirmed, monitor.Snapshot(StartedAt.AddSeconds(60)));
        await Observe(monitor, frame, 150);
        Assert.Equal(LootScrollState.Unknown, monitor.Snapshot(StartedAt.AddSeconds(150)));
        fail = false;
        await Observe(monitor, frame, 180);
        Assert.False(monitor.Snapshot(StartedAt.AddSeconds(180)).ShouldWarn);
        await Observe(monitor, frame, 210);
        Assert.True(monitor.Snapshot(StartedAt.AddSeconds(210)).ShouldWarn);
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
