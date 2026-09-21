using System.Text.Json;
using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothUploadCorrectionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly GarmothUploadResult Success = new(GarmothUploadStatus.Succeeded, "ok");

    [Fact]
    public void EqualNetTotalsStillPauseAQueuedHourWhenDropRevisionEvidenceIsAmbiguous()
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        ledger.Observe(TimeSpan.FromMinutes(61), Totals(100), Start.AddMinutes(61),
            isCorrection: true, includesNewDrops: true, requiresReview: true);
        Assert.True(ledger.CorrectionReviewRequired);
        Assert.Null(ledger.PrepareAutomatic());
    }

    [Fact]
    public void ManualCompletionCannotClearReviewOfCorrectionsArrivingAfterDispatch()
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        ledger.Observe(TimeSpan.FromMinutes(119.99), Totals(200), Start.AddMinutes(119.99));
        var manual = Assert.IsType<GarmothUploadInterval>(ledger.PrepareManual(TimeSpan.FromMinutes(119.99), Totals(200), Start));
        Observe(ledger, 120, 210);
        Observe(ledger, 120, 3, correction: true);
        Assert.True(ledger.CorrectionReviewRequired);
        ledger.Complete(manual, Success);
        ledger.ResumeAutomatic();

        Assert.True(ledger.CorrectionReviewRequired);
        Assert.True(ledger.AutomaticSuspended);
        Assert.Null(ledger.PrepareAutomatic());
        Assert.Empty(Assert.IsType<GarmothUploadInterval>(ledger.PreviewManual(TimeSpan.FromHours(2), Totals(3), Start)).Totals);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(130)]
    [InlineData(0)]
    public void CorrectedUnsentHourUsesTheNewQuantity(long quantity)
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        Observe(ledger, 60, quantity, correction: true);

        Assert.False(ledger.AutomaticSuspended);
        var request = ledger.PrepareAutomatic();
        if (quantity == 0) Assert.Null(request);
        else Assert.Equal(quantity, Assert.IsType<GarmothUploadInterval>(request).Totals["Trash"]);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(130)]
    public void CorrectionOnFirstObservationPastBoundaryCannotFreezeOldCounters(long quantity)
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 59, 100);
        Observe(ledger, 61, quantity, correction: true);

        Assert.Equal(quantity, Assert.IsType<GarmothUploadInterval>(ledger.PrepareAutomatic()).Totals["Trash"]);
    }

    [Fact]
    public void DownwardRevisionIsRecognizedWithoutAnExplicitCorrectionFlag()
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        Observe(ledger, 61, 3);
        Assert.Equal(3, Assert.IsType<GarmothUploadInterval>(ledger.PrepareAutomatic()).Totals["Trash"]);
    }

    [Fact]
    public void MixedRevisionAndNewDropsCannotBeAssignedToTheOldHour()
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        ledger.Observe(TimeSpan.FromMinutes(61), Totals(120), Start.AddMinutes(61),
            isCorrection: true, includesNewDrops: true);
        Assert.True(ledger.CorrectionReviewRequired);
        Assert.Null(ledger.PrepareAutomatic());
        Assert.Equal(120, Assert.IsType<GarmothUploadInterval>(ledger.PreviewManual(
            TimeSpan.FromMinutes(61), Totals(120), Start)).Totals["Trash"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnclearHourAllocationRequiresManualReviewEvenAfterResumeAndRestart(bool multipleHours)
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        Observe(ledger, multipleHours ? 120 : 61, 120);
        Observe(ledger, multipleHours ? 120 : 61, 80, correction: true);
        var saved = JsonSerializer.Deserialize<GarmothUploadState>(JsonSerializer.Serialize(ledger.ExportState()))!;
        var restored = new GarmothUploadIntervals();
        restored.RestoreState(saved);
        restored.ResumeAutomatic();

        Assert.True(restored.CorrectionReviewRequired);
        Assert.True(restored.AutomaticSuspended);
        Assert.False(restored.IsBlocked);
        Assert.Null(restored.PrepareAutomatic());
        var duration = TimeSpan.FromMinutes(multipleHours ? 120 : 61);
        Assert.Equal(80, Assert.IsType<GarmothUploadInterval>(restored.PreviewManual(duration, Totals(80), Start)).Totals["Trash"]);
        var manual = Assert.IsType<GarmothUploadInterval>(restored.PrepareManual(duration, Totals(80), Start));
        Assert.Equal(duration, manual.ActiveDuration);
        Assert.Equal(80, manual.Totals["Trash"]);
        restored.Complete(manual, Success);
        Assert.False(restored.CorrectionReviewRequired);
        restored.Reset();
        Observe(restored, 60, 7);
        Assert.Equal(7, Assert.IsType<GarmothUploadInterval>(restored.PrepareAutomatic()).Totals["Trash"]);
    }

    [Fact]
    public void CorrectionPreservesTheInFlightPayloadButPreventsAnOutdatedRejectedRetry()
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        var request = Assert.IsType<GarmothUploadInterval>(ledger.PrepareAutomatic());
        Observe(ledger, 60, 3, correction: true);
        Assert.Equal(100, request.Totals["Trash"]);
        ledger.Complete(request, new(GarmothUploadStatus.Rejected, "rejected"));
        ledger.ResumeAutomatic();
        Assert.True(ledger.CorrectionReviewRequired);
        Assert.Null(ledger.PrepareAutomatic());
        Assert.Equal(3, Assert.IsType<GarmothUploadInterval>(ledger.PrepareManual(TimeSpan.FromHours(1), Totals(3), Start)).Totals["Trash"]);
    }

    [Fact]
    public void ProposedCheckpointReconcilesHoursWithoutMutatingTheLiveLedger()
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        var proposed = ledger.ExportState(Totals(3));
        Assert.Equal(3, proposed.ObservedTotals["Trash"]);
        Assert.Equal(3, Assert.Single(proposed.Hours).Totals["Trash"]);
        Assert.Equal(100, Assert.Single(ledger.ExportState().Hours).Totals["Trash"]);
        var restarted = new GarmothUploadIntervals();
        restarted.RestoreState(proposed);
        Assert.Equal(3, Assert.IsType<GarmothUploadInterval>(restarted.PrepareAutomatic()).Totals["Trash"]);
    }

    [Fact]
    public void ProposedAmbiguousCheckpointRetainsTheReviewRequirement()
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        Observe(ledger, 120, 200);
        var proposed = ledger.ExportState(Totals(3));
        Assert.True(proposed.CorrectionReviewRequired);
        Assert.True(proposed.AutomaticSuspended);
        Assert.False(ledger.CorrectionReviewRequired);
    }

    private static Dictionary<string, long> Totals(long quantity) => new() { ["Trash"] = quantity };
    private static void Observe(GarmothUploadIntervals ledger, int minutes, long quantity, bool correction = false) =>
        ledger.Observe(TimeSpan.FromMinutes(minutes), Totals(quantity), Start.AddMinutes(minutes), correction);
}
