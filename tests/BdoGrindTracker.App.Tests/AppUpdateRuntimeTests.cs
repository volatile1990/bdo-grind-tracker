using BdoGrindTracker.App.Updates;

namespace BdoGrindTracker.App.Tests;

public sealed class AppUpdateRuntimeTests
{
    [Fact]
    public void PackagedRuntimeCreatesTheStoreBackendOnce()
    {
        var expected = new DisabledAppUpdates("1.0.1", "test");
        var runtime = new AppUpdateRuntime(AppPackageIdentity.Packaged);
        var calls = 0;

        var actual = runtime.CreateUpdates(true, () => { calls++; return expected; });

        Assert.Same(expected, actual);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(122, false)] // Packaged preview.
    [InlineData(15700, false)] // Unpackaged preview.
    [InlineData(5, false)] // Unknown preview.
    [InlineData(15700, true)] // Development builds never initialize updates.
    [InlineData(5, true)] // Unknown identity must not enable a network backend.
    [InlineData(0, true)] // Unexpected success code without a package name.
    public async Task PreviewUnpackagedAndUnknownRuntimeNeverCreateOrCallABackend(int nativeResult, bool enabled)
    {
        var runtime = new AppUpdateRuntime(AppUpdateRuntime.ClassifyPackageIdentity(nativeResult));
        var updates = runtime.CreateUpdates(enabled,
            () => throw new InvalidOperationException("No update backend may be created."));
        var state = updates.State;

        await updates.CheckAsync();
        await updates.DownloadAsync();
        await updates.RequestRestartAsync();

        Assert.Same(state, updates.State);
        Assert.Equal(UpdatePhase.Disabled, updates.State.Phase);
        Assert.Equal(AppBranding.Version, updates.State.InstalledVersion);
        Assert.False(updates.State.Enabled);
        Assert.False(updates.State.UsesStore);
        Assert.False(updates.State.IsBusy);
        Assert.False(updates.State.CanDownload);
        Assert.False(updates.State.IsReady);
    }

    [Fact]
    public async Task PackagedRuntimeWithoutAnAvailableBackendRemainsStoreManagedAndInactive()
    {
        var updates = new AppUpdateRuntime(AppPackageIdentity.Packaged).CreateUpdates(true);

        await updates.CheckAsync();
        await updates.DownloadAsync();
        await updates.RequestRestartAsync();

        Assert.Equal(UpdatePhase.StoreManaged, updates.State.Phase);
        Assert.Equal(AppBranding.Version, updates.State.InstalledVersion);
        Assert.False(updates.State.Enabled);
        Assert.False(updates.State.IsBusy);
        Assert.False(updates.State.CanDownload);
    }

    [Theory]
    [InlineData(122, (int)AppPackageIdentity.Packaged)]
    [InlineData(15700, (int)AppPackageIdentity.Unpackaged)]
    [InlineData(5, (int)AppPackageIdentity.Unknown)]
    [InlineData(0, (int)AppPackageIdentity.Unknown)]
    public void NativeIdentityResultsDoNotAssumeAnInstallationOnFailure(int result, int expected)
    {
        Assert.Equal((AppPackageIdentity)expected, AppUpdateRuntime.ClassifyPackageIdentity(result));
    }
}
