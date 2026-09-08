using BdoGrindTracker.App.Updates;

namespace BdoGrindTracker.App.Tests;

public sealed class AppUpdateRuntimeTests
{
    [Theory]
    [InlineData(122, true)]
    [InlineData(15700, false)]
    public void RuntimeSelectsOnlyItsOwnBackend(int nativeResult, bool store)
    {
        var expected = new DisabledAppUpdates("1.0.1", "test");
        var runtime = new AppUpdateRuntime(AppUpdateRuntime.ClassifyPackageIdentity(nativeResult));
        var actual = runtime.CreateUpdates(true,
            () => store ? throw new InvalidOperationException("No GitHub in Store") : expected,
            () => store ? expected : throw new InvalidOperationException("No Store in GitHub"));
        Assert.Same(expected, actual);
    }

    [Theory]
    [InlineData(122, false)]
    [InlineData(15700, false)]
    [InlineData(5, true)]
    public void PreviewOrUnknownIdentityCreatesNeitherBackend(int nativeResult, bool enabled)
    {
        var runtime = new AppUpdateRuntime(AppUpdateRuntime.ClassifyPackageIdentity(nativeResult));
        var actual = runtime.CreateUpdates(enabled,
            () => throw new InvalidOperationException("No GitHub"),
            () => throw new InvalidOperationException("No Store"));
        Assert.False(actual.State.Enabled);
    }
    [Theory]
    [InlineData(122, true)] // GetCurrentPackageFullName found an identity.
    [InlineData(5, false)] // Unexpected native errors must not enable Velopack.
    [InlineData(0, false)]
    public async Task PackagedOrUnknownRuntimeNeverStartsBootstrapOrCreatesGitHubUpdates(int nativeResult,
        bool managedByStore)
    {
        var runtime = new AppUpdateRuntime(AppUpdateRuntime.ClassifyPackageIdentity(nativeResult));
        runtime.Bootstrap(() => throw new InvalidOperationException("Velopack must not run."));
        var updates = runtime.CreateUpdates(true,
            () => throw new InvalidOperationException("The GitHub backend and preferences must not be created."));

        await updates.CheckAsync();
        await updates.DownloadAsync();
        await updates.SetBetaAsync(true);
        await updates.RequestRestartAsync();

        Assert.Equal(managedByStore ? UpdatePhase.StoreManaged : UpdatePhase.Disabled, updates.State.Phase);
        Assert.False(updates.State.Enabled);
        Assert.False(updates.State.IsBeta);
        Assert.False(updates.State.IsBusy);
        Assert.False(updates.State.CanDownload);
        Assert.False(updates.State.IsReady);
    }

    [Fact]
    public void ConfirmedUnpackagedRuntimePreservesBootstrapAndUpdateFactory()
    {
        var runtime = new AppUpdateRuntime(AppUpdateRuntime.ClassifyPackageIdentity(15700));
        var steps = new List<string>();
        var expected = new DisabledAppUpdates("1.0.0", "test");

        runtime.Bootstrap(() => steps.Add("bootstrap"));
        var actual = runtime.CreateUpdates(true, () =>
        {
            steps.Add("updates");
            return expected;
        });

        Assert.Equal(["bootstrap", "updates"], steps);
        Assert.Same(expected, actual);
    }

    [Theory]
    [InlineData(15700)]
    [InlineData(122)]
    [InlineData(5)]
    public void PreviewAndSmokeTestsNeverCreateGitHubUpdates(int nativeResult)
    {
        var runtime = new AppUpdateRuntime(AppUpdateRuntime.ClassifyPackageIdentity(nativeResult));
        var updates = runtime.CreateUpdates(false, () => throw new InvalidOperationException("Preview must stay offline."));

        Assert.Equal(UpdatePhase.Disabled, updates.State.Phase);
        Assert.False(updates.State.Enabled);
    }
}
