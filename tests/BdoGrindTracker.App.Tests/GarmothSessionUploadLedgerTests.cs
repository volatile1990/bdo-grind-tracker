using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothSessionUploadLedgerTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WholeSessionObservationsNeverCreateHourlyCutoffsOrCorrectionReview()
    {
        var ledger = new GarmothUploadIntervals();
        ledger.ObserveSessionTotals(TimeSpan.FromHours(1), Totals(100), StartedAt.AddHours(1));
        ledger.ObserveSessionTotals(TimeSpan.FromHours(2), Totals(200), StartedAt.AddHours(2));
        ledger.ObserveSessionTotals(TimeSpan.FromHours(2), Totals(3), StartedAt.AddHours(2),
            isCorrection: true, requiresReview: true);

        Assert.Null(ledger.PrepareAutomatic());
        Assert.False(ledger.AutomaticSuspended);
        Assert.False(ledger.CorrectionReviewRequired);
        var saved = ledger.ExportState(Totals(4));
        Assert.Empty(saved.Hours);
        Assert.Equal(TimeSpan.FromHours(3), saved.NextHour);
        Assert.False(saved.CorrectionReviewRequired);
        var restored = new GarmothUploadIntervals();
        restored.RestoreState(saved);
        var upload = Assert.IsType<GarmothUploadInterval>(restored.PrepareManual(
            TimeSpan.FromHours(2), Totals(4), StartedAt));
        Assert.Equal(TimeSpan.FromHours(2), upload.ActiveDuration);
        Assert.Equal(4, upload.Totals["Trash"]);
    }

    [Fact]
    public void MigratingAnUnsentLegacySessionDiscardsOnlyObsoleteHourlyReview()
    {
        var legacy = new GarmothUploadIntervals();
        legacy.Observe(TimeSpan.FromHours(1), Totals(100), StartedAt.AddHours(1));
        legacy.Observe(TimeSpan.FromHours(2), Totals(200), StartedAt.AddHours(2));
        legacy.Observe(TimeSpan.FromHours(2), Totals(3), StartedAt.AddHours(2), isCorrection: true);
        Assert.True(legacy.CorrectionReviewRequired);
        var ledger = new GarmothUploadIntervals();
        ledger.RestoreState(legacy.ExportState());

        ledger.ObserveSessionTotals(TimeSpan.FromHours(2), Totals(3), StartedAt.AddHours(2));

        Assert.Empty(ledger.ExportState().Hours);
        Assert.False(ledger.CorrectionReviewRequired);
        Assert.False(ledger.AutomaticSuspended);
        Assert.Equal(3, ledger.PreviewManual(TimeSpan.FromHours(2), Totals(3), StartedAt)!.Totals["Trash"]);
    }

    [Fact]
    public void MigratingLegacyUploadedHoursPreservesWatermarksAndRemainingDuration()
    {
        var legacy = new GarmothUploadIntervals();
        legacy.Observe(TimeSpan.FromHours(1), Totals(100), StartedAt.AddHours(1));
        var sent = Assert.IsType<GarmothUploadInterval>(legacy.PrepareAutomatic());
        legacy.Complete(sent, new(GarmothUploadStatus.Succeeded, "ok"));
        var ledger = new GarmothUploadIntervals();
        ledger.RestoreState(legacy.ExportState());

        ledger.ObserveSessionTotals(TimeSpan.FromMinutes(90), Totals(140), StartedAt.AddMinutes(90));

        Assert.True(ledger.HasTransmittedLoot);
        Assert.Empty(ledger.ExportState().Hours);
        var remainder = Assert.IsType<GarmothUploadInterval>(ledger.PrepareManual(
            TimeSpan.FromMinutes(90), Totals(140), StartedAt));
        Assert.Equal(TimeSpan.FromMinutes(30), remainder.ActiveDuration);
        Assert.Equal(40, remainder.Totals["Trash"]);
        Assert.Equal(StartedAt.AddHours(1), remainder.StartedAt);
    }

    [Fact]
    public void MigratingAnUncertainLegacyRequestKeepsBothUploadPathsBlocked()
    {
        var legacy = new GarmothUploadIntervals();
        legacy.Observe(TimeSpan.FromHours(1), Totals(100), StartedAt.AddHours(1));
        Assert.NotNull(legacy.PrepareAutomatic());
        var ledger = new GarmothUploadIntervals();
        ledger.RestoreState(legacy.ExportState());

        ledger.ObserveSessionTotals(TimeSpan.FromHours(2), Totals(200), StartedAt.AddHours(2));
        ledger.ResumeAutomatic();

        Assert.True(ledger.IsBlocked);
        Assert.True(ledger.AutomaticSuspended);
        Assert.Null(ledger.PrepareAutomatic());
        Assert.Null(ledger.PrepareManual(TimeSpan.FromHours(2), Totals(200), StartedAt));
    }

    [Fact]
    public void SessionCorrectionWhileRequestIsPendingRetainsReviewAfterRejection()
    {
        var ledger = new GarmothUploadIntervals();
        ledger.ObserveSessionTotals(TimeSpan.FromMinutes(30), Totals(100), StartedAt.AddMinutes(30));
        var request = Assert.IsType<GarmothUploadInterval>(ledger.PrepareManual(
            TimeSpan.FromMinutes(30), Totals(100), StartedAt));

        ledger.ObserveSessionTotals(TimeSpan.FromMinutes(30), Totals(80), StartedAt.AddMinutes(30), isCorrection: true);
        ledger.Complete(request, new(GarmothUploadStatus.Rejected, "rejected"));
        ledger.ObserveSessionTotals(TimeSpan.FromMinutes(30), Totals(80), StartedAt.AddMinutes(30));

        Assert.True(ledger.AutomaticSuspended);
        Assert.True(ledger.CorrectionReviewRequired);
        Assert.False(ledger.IsBlocked);
        Assert.Equal(80, ledger.PreviewManual(TimeSpan.FromMinutes(30), Totals(80), StartedAt)!.Totals["Trash"]);
    }

    [Fact]
    public void WholeSessionObservationsPreserveExplicitSuspensionAndMonotonicDuration()
    {
        var ledger = new GarmothUploadIntervals();
        ledger.ObserveSessionTotals(TimeSpan.FromHours(1), Totals(100), StartedAt.AddHours(1));
        ledger.SuspendAutomatic();

        ledger.ObserveSessionTotals(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1),
            Totals(80), StartedAt.AddHours(1), isCorrection: true);

        Assert.True(ledger.AutomaticSuspended);
        var saved = ledger.ExportState();
        Assert.Equal(TimeSpan.FromHours(1), saved.ObservedDuration);
        Assert.Equal(80, saved.ObservedTotals["Trash"]);
    }

    private static Dictionary<string, long> Totals(long quantity) => new() { ["Trash"] = quantity };
}
