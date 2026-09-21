using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(132)]
    [InlineData(129)]
    [InlineData(101)]
    public async Task MixedProjectedCorrectionAndNewDropCannotSendTheOldQueuedHour(int correctedTotal)
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        var started = fixture.Time.GetUtcNow();
        var firstArrival = started.AddMinutes(59);
        await ProcessProjectionAfter(fixture, TimeSpan.FromMinutes(59), 1, 100, 1, firstArrival, 0);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 1, 100, 1, firstArrival, 0);
        var boundaryArrival = started.AddHours(1);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(58), 2, 101, 2, boundaryArrival, 0);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 2, 101, 2, boundaryArrival, 0);
        Assert.False(fixture.Service.State.AutomaticUploadNeedsReview);
        var mixedArrival = started.AddMinutes(61);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(58), 3, correctedTotal, 3, mixedArrival, 1);

        // The correction is known before the two-second buffer releases its new
        // quantities. The old completed hour must already be held at this point.
        Assert.True(fixture.Service.State.AutomaticUploadNeedsReview);
        await fixture.Service.UploadHourlyToGarmothAsync();
        Assert.Empty(fixture.Requests);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 3, correctedTotal, 3, mixedArrival, 1);
        Assert.Equal(correctedTotal, fixture.Service.State.Loot.Totals[ProjectionPublicationItem]);
        Assert.Equal(correctedTotal, fixture.Service.State.CurrentGarmothUpload.Draft!.Totals[ProjectionPublicationItem]);
    }

    [Fact]
    public async Task ProvenNewProjectedDropsAfterHourBoundaryDoNotSuspendAutomaticUpload()
    {
        await using var fixture = new Fixture();
        fixture.Begin();
        var started = fixture.Time.GetUtcNow();
        var firstArrival = started.AddMinutes(59);
        await ProcessProjectionAfter(fixture, TimeSpan.FromMinutes(59), 1, 100, 1, firstArrival, 0);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 1, 100, 1, firstArrival, 0);
        var boundaryArrival = started.AddHours(1);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(58), 2, 101, 2, boundaryArrival, 0);
        await ProcessProjectionAfter(fixture, TimeSpan.FromSeconds(2), 2, 101, 2, boundaryArrival, 0);

        Assert.False(fixture.Service.State.AutomaticUploadNeedsReview);
        await fixture.Service.UploadHourlyToGarmothAsync();
        AssertPayload(Assert.Single(fixture.Requests), 60, 100);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(130)]
    public async Task CorrectionBeforeEnablingAutomaticUploadUpdatesTheQueuedHourAndCheckpoint(int quantity)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 100));
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            "Black Crystal Fragment", quantity, 100)).Succeeded);
        var checkpoint = new CurrentSessionStore(Path.Combine(fixture.DirectoryPath, CurrentSessionStore.FileName)).Load();
        Assert.NotNull(checkpoint?.Uploads);
        Assert.Equal(quantity, Assert.Single(checkpoint.Uploads.Hours).Totals["Black Crystal Fragment"]);
        Assert.True((await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoUpload = true })).Succeeded);
        await fixture.Service.UploadHourlyToGarmothAsync();
        AssertPayload(Assert.Single(fixture.Requests), 60, quantity);
    }

    [Fact]
    public async Task CorrectionAcrossQueuedHoursStopsAutomaticUploadAndKeepsCorrectManualPreview()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 100));
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 100));
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            "Black Crystal Fragment", 3, 200)).Succeeded);
        Assert.True(fixture.Service.State.AutomaticUploadNeedsReview);
        Assert.Contains("manuell senden", fixture.Service.State.Status);
        await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoUpload = true }, resumeAutomaticUpload: true);
        await fixture.Service.UploadHourlyToGarmothAsync();
        Assert.Empty(fixture.Requests);
        Assert.True(fixture.Service.State.AutomaticSuspended);
        var preview = fixture.Service.State.CurrentGarmothUpload;
        Assert.True(preview.IsReady, preview.Error);
        Assert.Equal(3, preview.Draft!.Totals["Black Crystal Fragment"]);
        Assert.True((await fixture.Service.UploadConfirmedAsync(preview)).Succeeded);
        AssertPayload(Assert.Single(fixture.Requests), 120, 3);
    }

    [Fact]
    public async Task SavingRestoredHistoryDoesNotDiscardSessionsBeyondFiveHundred()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 3));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var current = Assert.Single(fixture.HistoryStore.Load());
        var entries = Enumerable.Range(1, 600).Select(index => current with
        {
            SessionId = Guid.NewGuid(), UpdatedAt = current.UpdatedAt.AddDays(-index),
            StartedAt = current.StartedAt.AddDays(-index),
        }).Append(current).ToArray();
        fixture.HistoryStore.Save(entries);
        await using var restored = RestartForGarmothTest(fixture);
        Assert.Equal(601, restored.History.Count);
        Assert.True((await restored.SaveSessionAsync()).Succeeded);
        Assert.Equal(601, restored.History.Count);
        Assert.Equal(601, fixture.HistoryStore.Load().Count);
    }
}
