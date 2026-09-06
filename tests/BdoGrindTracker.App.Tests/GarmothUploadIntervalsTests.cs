using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothUploadIntervalsTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 9, 6, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly GarmothUploadResult Success = new(GarmothUploadStatus.Succeeded, "ok");
    private static readonly GarmothUploadResult Rejected = new(GarmothUploadStatus.Rejected, "rejected");

    [Fact]
    public void AFullActiveHourIsRequiredAndBoundaryFrameIsIncluded()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 59.99, 99);
        Assert.Null(intervals.PrepareAutomatic());
        Observe(intervals, 60, 100);

        var hour = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());

        Assert.Equal(TimeSpan.FromHours(1), hour.ActiveDuration);
        Assert.Equal(100, hour.Totals["Trash"]);
        Assert.Equal(StartedAt, hour.StartedAt);
        Assert.NotEqual(Guid.Empty, hour.Id);
    }

    [Fact]
    public void FrameAfterBoundaryBelongsToNextHour()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 59, 100);
        Observe(intervals, 61, 110);
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        Assert.Equal(100, first.Totals["Trash"]);
        intervals.Complete(first, Success);
        Observe(intervals, 120, 210);

        var second = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());

        Assert.Equal(110, second.Totals["Trash"]);
        Assert.Equal(TimeSpan.FromHours(1), second.ActiveDuration);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(StartedAt.AddHours(1), second.StartedAt);
    }

    [Fact]
    public void BacklogKeepsDistinctHourDeltasInsteadOfReuploadingCumulativeTotals()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        Observe(intervals, 120, 230);
        Observe(intervals, 180, 400);
        var sent = new List<GarmothUploadInterval>();

        for (var index = 0; index < 3; index++)
        {
            var hour = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
            sent.Add(hour);
            intervals.Complete(hour, Success);
        }

        Assert.Equal([100L, 130L, 170L], sent.Select(static hour => hour.Totals["Trash"]));
        Assert.All(sent, hour => Assert.Equal(TimeSpan.FromHours(1), hour.ActiveDuration));
        Assert.Equal(3, sent.Select(static hour => hour.Id).Distinct().Count());
        Assert.Null(intervals.PrepareAutomatic());
    }

    [Fact]
    public void ObservationsAndPreparedRequestsAreImmutableWhileUploadIsPending()
    {
        var intervals = new GarmothUploadIntervals();
        var totals = Totals(100);
        intervals.Observe(TimeSpan.FromHours(1), totals, StartedAt.AddHours(1));
        totals["Trash"] = 999;
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        Observe(intervals, 120, 220);
        Assert.Null(intervals.PrepareAutomatic());
        Assert.Null(intervals.PrepareManual(TimeSpan.FromHours(2), Totals(220), StartedAt));
        Assert.Equal(100, first.Totals["Trash"]);

        intervals.Complete(first with { Totals = Totals(999) }, Success);
        var second = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());

        Assert.Equal(120, second.Totals["Trash"]);
    }

    [Fact]
    public void CorrectionsRetainDebtInsteadOfLoweringAlreadyUploadedWatermarks()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        intervals.Complete(first, Success);
        Observe(intervals, 120, 80);
        Assert.Null(intervals.PrepareAutomatic());
        Observe(intervals, 180, 90);
        Assert.Null(intervals.PrepareAutomatic());
        Observe(intervals, 240, 130);

        var corrected = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());

        Assert.Equal(30, corrected.Totals["Trash"]);
        Assert.Equal(TimeSpan.FromHours(1), corrected.ActiveDuration);
    }

    [Fact]
    public void CorrectionsOfOneItemDoNotConsumeNewLootForAnotherItem()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        intervals.Complete(first, Success);
        intervals.Observe(TimeSpan.FromHours(2), new Dictionary<string, long>
        {
            ["Trash"] = 80, ["Rare"] = 2,
        }, StartedAt.AddHours(2));
        var second = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        Assert.Equal(2, Assert.Single(second.Totals).Value);
        intervals.Complete(second, Success);
        intervals.Observe(TimeSpan.FromHours(3), new Dictionary<string, long>
        {
            ["Trash"] = 110, ["Rare"] = 3,
        }, StartedAt.AddHours(3));

        var third = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());

        Assert.Equal(10, third.Totals["Trash"]);
        Assert.Equal(1, third.Totals["Rare"]);
    }

    [Fact]
    public void RejectionSuspendsAutomaticRetryAndExplicitResumeKeepsFrozenRequest()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        intervals.Complete(first, Rejected);
        Observe(intervals, 120, 250);
        Assert.True(intervals.AutomaticSuspended);
        Assert.False(intervals.IsBlocked);
        Assert.Null(intervals.PrepareAutomatic());

        intervals.ResumeAutomatic();
        var retry = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());

        Assert.Same(first, retry);
        intervals.Complete(retry, Success);
        var second = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        Assert.Equal(150, second.Totals["Trash"]);
    }

    [Theory]
    [InlineData(GarmothUploadStatus.OutcomeUnknown)]
    [InlineData(GarmothUploadStatus.AlreadySubmitted)]
    public void UnknownOutcomeBlocksBothAutomaticAndManualEvenAfterExplicitResume(object status)
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        intervals.Complete(first, new((GarmothUploadStatus)status, "uncertain"));
        Observe(intervals, 120, 250);
        intervals.ResumeAutomatic();

        Assert.True(intervals.IsBlocked);
        Assert.True(intervals.AutomaticSuspended);
        Assert.Null(intervals.PrepareAutomatic());
        Assert.Null(intervals.PrepareManual(TimeSpan.FromHours(2), Totals(250), StartedAt));
    }

    [Fact]
    public void ManualAfterSuccessfulHoursSendsOnlyRemainderIncludingQueuedHours()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        intervals.Complete(first, Success);
        Observe(intervals, 120, 230);
        Observe(intervals, 150, 300);

        var remainder = Assert.IsType<GarmothUploadInterval>(intervals.PrepareManual(
            TimeSpan.FromMinutes(150), Totals(300), StartedAt));

        Assert.Equal(TimeSpan.FromMinutes(90), remainder.ActiveDuration);
        Assert.Equal(200, remainder.Totals["Trash"]);
        Assert.Equal(StartedAt.AddHours(1), remainder.StartedAt);
        intervals.Complete(remainder, Success);
        Assert.Null(intervals.PrepareAutomatic());
        Assert.Null(intervals.PrepareManual(TimeSpan.FromMinutes(150), Totals(300), StartedAt));
    }

    [Fact]
    public void ManualAfterDefiniteRejectionIncludesAllUnsentLootWithoutWaitingForHourRetries()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        intervals.Complete(first, Rejected);
        Observe(intervals, 120, 220);

        var manual = Assert.IsType<GarmothUploadInterval>(intervals.PrepareManual(
            TimeSpan.FromMinutes(130), Totals(250), StartedAt));

        Assert.Equal(TimeSpan.FromMinutes(130), manual.ActiveDuration);
        Assert.Equal(250, manual.Totals["Trash"]);
        Assert.NotEqual(first.Id, manual.Id);
        intervals.Complete(manual, Success);
        intervals.ResumeAutomatic();
        Assert.Null(intervals.PrepareAutomatic());
    }

    [Fact]
    public void EnablingAutomaticAfterRejectedManualWaitsForAndSendsAFullHour()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 30, 100);
        var manual = Assert.IsType<GarmothUploadInterval>(intervals.PrepareManual(
            TimeSpan.FromMinutes(30), Totals(100), StartedAt));
        intervals.Complete(manual, Rejected);
        intervals.ResumeAutomatic();
        Assert.Null(intervals.PrepareAutomatic());

        Observe(intervals, 60, 220);
        var hour = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        Assert.Equal(TimeSpan.FromHours(1), hour.ActiveDuration);
        Assert.Equal(220, hour.Totals["Trash"]);
        Assert.NotEqual(manual.Id, hour.Id);
    }

    [Fact]
    public void ClockSamplingJitterDoesNotDiscardQuantityCorrectionsBeforeBoundary()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 59, 100);
        intervals.Observe(TimeSpan.FromMinutes(59) - TimeSpan.FromTicks(1), Totals(80), StartedAt.AddMinutes(59));
        Observe(intervals, 61, 90);

        var hour = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        Assert.Equal(80, hour.Totals["Trash"]);
    }

    [Fact]
    public void RemovedAndReaddedItemWithDifferentCasingKeepsItsUploadWatermark()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        intervals.Complete(Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic()), Success);
        intervals.Observe(TimeSpan.FromMinutes(61), new Dictionary<string, long>(), StartedAt.AddMinutes(61));
        intervals.Observe(TimeSpan.FromHours(2), new Dictionary<string, long> { ["trash"] = 120 }, StartedAt.AddHours(2));

        var hour = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        Assert.Equal(20, Assert.Single(hour.Totals).Value);
    }

    [Fact]
    public void ManualWithoutAutomaticUploadsPreservesExistingWholeSessionBehavior()
    {
        var intervals = new GarmothUploadIntervals();
        var manual = Assert.IsType<GarmothUploadInterval>(intervals.PrepareManual(
            TimeSpan.FromMinutes(30), Totals(123), StartedAt));

        Assert.Equal(TimeSpan.FromMinutes(30), manual.ActiveDuration);
        Assert.Equal(123, manual.Totals["Trash"]);
        Assert.Equal(StartedAt, manual.StartedAt);
    }

    [Fact]
    public void ALongObservationGapDoesNotSpreadOrAssignLaterLootToEarlierHours()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 30, 50);
        Observe(intervals, 190, 500);
        var first = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        Assert.Equal(50, first.Totals["Trash"]);
        Assert.Equal(TimeSpan.FromHours(1), first.ActiveDuration);
        intervals.Complete(first, Success);
        Assert.Null(intervals.PrepareAutomatic());
        Observe(intervals, 240, 550);

        var fourth = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());

        Assert.Equal(500, fourth.Totals["Trash"]);
        Assert.Equal(TimeSpan.FromHours(1), fourth.ActiveDuration);
    }

    [Fact]
    public void ConfirmedActiveTimeDoesNotAdvanceDuringIdleWallTime()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 59, 100);
        intervals.Observe(TimeSpan.FromMinutes(59), Totals(100), StartedAt.AddHours(3));
        Assert.Null(intervals.PrepareAutomatic());
        intervals.Observe(TimeSpan.FromHours(1), Totals(110), StartedAt.AddHours(3).AddMinutes(1));

        Assert.Equal(110, Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic()).Totals["Trash"]);
    }

    [Fact]
    public void ResetDiscardsOldCutoffsAndIgnoresLateHttpCompletion()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);
        var old = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        intervals.Reset();
        Observe(intervals, 60, 25);
        var current = Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic());
        intervals.Complete(old, new(GarmothUploadStatus.OutcomeUnknown, "old"));
        Assert.False(intervals.IsBlocked);
        Assert.NotEqual(old.Id, current.Id);
        intervals.Complete(current, Success);
        Observe(intervals, 120, 40);

        Assert.Equal(15, Assert.IsType<GarmothUploadInterval>(intervals.PrepareAutomatic()).Totals["Trash"]);
    }

    [Fact]
    public async Task ConcurrentPreparationReservesOnlyOneRequest()
    {
        var intervals = new GarmothUploadIntervals();
        Observe(intervals, 60, 100);

        var prepared = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(intervals.PrepareAutomatic)));

        Assert.Single(prepared.OfType<GarmothUploadInterval>());
    }

    private static Dictionary<string, long> Totals(long quantity) => new() { ["Trash"] = quantity };
    private static void Observe(GarmothUploadIntervals intervals, double minutes, long quantity) =>
        intervals.Observe(TimeSpan.FromMinutes(minutes), Totals(quantity), StartedAt.AddMinutes(minutes));
}
