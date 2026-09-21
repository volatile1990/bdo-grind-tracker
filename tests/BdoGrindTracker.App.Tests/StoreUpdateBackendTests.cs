using BdoGrindTracker.App.Updates;
using Windows.Services.Store;

namespace BdoGrindTracker.App.Tests;

public sealed class StoreUpdateBackendTests
{
    [Fact]
    public void MultiPackageDownloadWaitsForEverySizeAndReplacesRepeatedPackageReports()
    {
        var tracker = new StoreUpdateProgressTracker(["app", "optional"]);

        var first = tracker.Update(Status("app", downloaded: 100, size: 1_000, progress: .05));
        Assert.Equal(100UL, first.DownloadedBytes);
        Assert.Null(first.TotalDownloadBytes);

        var second = tracker.Update(Status("optional", downloaded: 200, size: 3_000, progress: .075));
        Assert.Equal(300UL, second.DownloadedBytes);
        Assert.Equal(4_000UL, second.TotalDownloadBytes);

        var repeated = tracker.Update(Status("app", downloaded: 150, size: 1_100, progress: .08));
        Assert.Equal(350UL, repeated.DownloadedBytes);
        Assert.Equal(4_100UL, repeated.TotalDownloadBytes);
    }

    [Fact]
    public void ByteUpdatesAreAvailableBeforeTheWholePercentageChanges()
    {
        var tracker = new StoreUpdateProgressTracker(["app"]);

        var first = tracker.Update(Status("app", downloaded: 10, size: 100_000, progress: .0001));
        var next = tracker.Update(Status("app", downloaded: 20, size: 100_000, progress: .0002));

        Assert.Equal(0, first.DownloadPercent);
        Assert.Equal(0, next.DownloadPercent);
        Assert.Equal(10UL, first.DownloadedBytes);
        Assert.Equal(20UL, next.DownloadedBytes);
        Assert.Equal(StoreUpdateStage.Downloading, next.Stage);
    }

    [Fact]
    public void PendingOrZeroSizeReportsDoNotClaimAKnownDownloadTarget()
    {
        var tracker = new StoreUpdateProgressTracker(["app"]);

        var pending = tracker.Update(Status("app", state: StorePackageUpdateState.Pending));
        Assert.Equal(StoreUpdateStage.Pending, pending.Stage);
        Assert.Null(pending.DownloadedBytes);
        Assert.Null(pending.TotalDownloadBytes);

        var downloading = tracker.Update(Status("app", downloaded: 300));
        Assert.Equal(300UL, downloading.DownloadedBytes);
        Assert.Null(downloading.TotalDownloadBytes);
    }

    [Fact]
    public void CompletingOnePackageDoesNotFinishAnotherPackagesInstallation()
    {
        var tracker = new StoreUpdateProgressTracker(["app", "optional"]);
        tracker.Update(Status("app", downloaded: 1_000, size: 1_000, state: StorePackageUpdateState.Deploying));

        var partial = tracker.Update(Status("optional", downloaded: 2_000, size: 2_000,
            state: StorePackageUpdateState.Completed));
        Assert.Equal(StoreUpdateStage.Installing, partial.Stage);
        Assert.Equal(3_000UL, partial.DownloadedBytes);
        Assert.Equal(3_000UL, partial.TotalDownloadBytes);

        var finished = tracker.Update(Status("app", downloaded: 1_000, size: 1_000, progress: 1,
            state: StorePackageUpdateState.Completed));
        Assert.Equal(StoreUpdateStage.Completed, finished.Stage);
        Assert.Equal(100, finished.DownloadPercent);
    }

    [Fact]
    public void StageOnlyReportsKeepTheLastObservedBytesWithoutInventingDownloadCompletion()
    {
        var tracker = new StoreUpdateProgressTracker(["app", "optional"]);
        tracker.Update(Status("app", downloaded: 900, size: 1_000));
        tracker.Update(Status("optional", downloaded: 300, size: 2_000));

        tracker.Update(Status("app", state: StorePackageUpdateState.Deploying));
        var completed = tracker.Update(Status("app", state: StorePackageUpdateState.Completed));

        Assert.Equal(1_200UL, completed.DownloadedBytes);
        Assert.Equal(3_000UL, completed.TotalDownloadBytes);
        Assert.Equal(StoreUpdateStage.Downloading, completed.Stage);

        var revised = tracker.Update(Status("optional", downloaded: 500, size: 1_800));
        Assert.Equal(1_400UL, revised.DownloadedBytes);
        Assert.Equal(2_800UL, revised.TotalDownloadBytes);
    }

    [Fact]
    public void UnidentifiedPackageDoesNotInventAggregateBytesOrCompletion()
    {
        var tracker = new StoreUpdateProgressTracker(["app", "optional"]);

        var progress = tracker.Update(Status("", downloaded: 1_000, size: 1_000, progress: .5,
            state: StorePackageUpdateState.Completed));

        Assert.Equal(50, progress.DownloadPercent);
        Assert.Null(progress.DownloadedBytes);
        Assert.Null(progress.TotalDownloadBytes);
        Assert.NotEqual(StoreUpdateStage.Completed, progress.Stage);
    }

    private static StorePackageUpdateStatus Status(string family, ulong downloaded = 0, ulong size = 0,
        double progress = 0, StorePackageUpdateState state = StorePackageUpdateState.Downloading) => new()
        {
            PackageFamilyName = family,
            PackageBytesDownloaded = downloaded,
            PackageDownloadSizeInBytes = size,
            TotalDownloadProgress = progress,
            PackageUpdateState = state
        };
}
