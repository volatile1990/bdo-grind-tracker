using System.Text.Json;
using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothUploadRestoreTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly GarmothUploadResult Success = new(GarmothUploadStatus.Succeeded, "ok");

    [Fact]
    public void RestartPreservesQueuedHourCutoffsAndTransmittedWatermarks()
    {
        var before = new GarmothUploadIntervals();
        Observe(before, 60, 100);
        Observe(before, 120, 230);
        Observe(before, 150, 300);
        var first = Assert.IsType<GarmothUploadInterval>(before.PrepareAutomatic());
        before.Complete(first, Success);

        var after = RoundTrip(before);
        var second = Assert.IsType<GarmothUploadInterval>(after.PrepareAutomatic());
        Assert.Equal(130, second.Totals["Trash"]);
        Assert.Equal(TimeSpan.FromHours(1), second.ActiveDuration);
        Assert.Equal(Started.AddHours(1), second.StartedAt);
        after.Complete(second, Success);
        var remainder = Assert.IsType<GarmothUploadInterval>(after.PrepareManual(TimeSpan.FromMinutes(150), Totals(300), Started));
        Assert.Equal(70, remainder.Totals["Trash"]);
        Assert.Equal(TimeSpan.FromMinutes(30), remainder.ActiveDuration);
    }

    [Fact]
    public void ClosedAppTimeDoesNotCreateExtraUploadHours()
    {
        var before = new GarmothUploadIntervals();
        Observe(before, 50, 100);
        var after = RoundTrip(before);
        after.Observe(TimeSpan.FromMinutes(55), Totals(110), Started.AddDays(1));
        Assert.Null(after.PrepareAutomatic());
        after.Observe(TimeSpan.FromHours(1), Totals(120), Started.AddDays(1).AddMinutes(5));
        Assert.Equal(120, Assert.IsType<GarmothUploadInterval>(after.PrepareAutomatic()).Totals["Trash"]);
    }

    [Fact]
    public void CorrectionDebtSurvivesRestart()
    {
        var before = new GarmothUploadIntervals();
        Observe(before, 60, 100);
        before.Complete(Assert.IsType<GarmothUploadInterval>(before.PrepareAutomatic()), Success);
        Observe(before, 90, 80);
        var after = RoundTrip(before);
        Observe(after, 120, 90);
        Assert.Null(after.PrepareAutomatic());
        Observe(after, 180, 130);
        Assert.Equal(30, Assert.IsType<GarmothUploadInterval>(after.PrepareAutomatic()).Totals["Trash"]);
    }

    [Fact]
    public void CheckpointDuringRequestCannotRepeatAnUncertainUpload()
    {
        var before = new GarmothUploadIntervals();
        Observe(before, 60, 100);
        Assert.NotNull(before.PrepareAutomatic());
        var after = RoundTrip(before);
        after.ResumeAutomatic();
        Assert.True(after.IsBlocked);
        Assert.True(after.AutomaticSuspended);
        Assert.Null(after.PrepareAutomatic());
        Assert.Null(after.PrepareManual(TimeSpan.FromHours(1), Totals(100), Started));
    }

    [Fact]
    public void DefinitelyRejectedHourRetainsItsIdentityAndRequiresExplicitResume()
    {
        var before = new GarmothUploadIntervals();
        Observe(before, 60, 100);
        var request = Assert.IsType<GarmothUploadInterval>(before.PrepareAutomatic());
        before.Complete(request, new(GarmothUploadStatus.Rejected, "rejected"));
        var after = RoundTrip(before);
        Assert.Null(after.PrepareAutomatic());
        after.ResumeAutomatic();
        var retry = Assert.IsType<GarmothUploadInterval>(after.PrepareAutomatic());
        Assert.Equal(request.Id, retry.Id);
        Assert.Equal(request.Totals, retry.Totals);
    }

    [Fact]
    public void SavedDictionariesDoNotAliasLiveCounters()
    {
        var before = new GarmothUploadIntervals();
        Observe(before, 60, 100);
        var exported = before.ExportState();
        var after = new GarmothUploadIntervals();
        after.RestoreState(exported);
        exported.Hours[0].Totals["Trash"] = 999;
        exported.ObservedTotals["Trash"] = 999;
        Assert.Equal(100, Assert.IsType<GarmothUploadInterval>(after.PrepareAutomatic()).Totals["Trash"]);
        Assert.Equal(100, Assert.IsType<GarmothUploadInterval>(before.PrepareAutomatic()).Totals["Trash"]);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("next-hour")]
    [InlineData("negative-total")]
    [InlineData("duplicate-name")]
    [InlineData("missing-totals")]
    [InlineData("missing-hours")]
    [InlineData("unordered-hours")]
    public void InvalidStateCannotPartlyReplaceExistingCounters(string invalid)
    {
        var ledger = new GarmothUploadIntervals();
        Observe(ledger, 60, 100);
        var saved = ledger.ExportState();
        var broken = invalid switch
        {
            "time" => saved with { ObservedDuration = TimeSpan.FromTicks(-1) },
            "next-hour" => saved with { NextHour = TimeSpan.FromHours(1) },
            "negative-total" => saved with { ObservedTotals = new() { ["Trash"] = -1 } },
            "duplicate-name" => saved with { TransmittedTotals = new() { ["Trash"] = 1, ["trash"] = 1 } },
            "missing-totals" => saved with { ObservedTotals = null! },
            "missing-hours" => saved with { Hours = null! },
            _ => saved with { Hours = [saved.Hours[0], saved.Hours[0]] },
        };
        Assert.Throws<ArgumentException>(() => ledger.RestoreState(broken));
        Assert.Equal(100, Assert.IsType<GarmothUploadInterval>(ledger.PrepareAutomatic()).Totals["Trash"]);
    }

    private static GarmothUploadIntervals RoundTrip(GarmothUploadIntervals before)
    {
        var saved = JsonSerializer.Deserialize<GarmothUploadState>(JsonSerializer.Serialize(before.ExportState()))!;
        var after = new GarmothUploadIntervals();
        after.RestoreState(saved);
        return after;
    }
    private static Dictionary<string, long> Totals(long amount) => new() { ["Trash"] = amount };
    private static void Observe(GarmothUploadIntervals ledger, int minutes, long amount)
        => ledger.Observe(TimeSpan.FromMinutes(minutes), Totals(amount), Started.AddMinutes(minutes));
}
