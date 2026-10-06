using System.ComponentModel;
using System.Diagnostics;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class StoreSessionTakeoverTests
{
    [Fact]
    public void RunningWriterBlocksTakeoverWithoutLaunchingAnotherWriter()
    {
        var name = "Grindcrest.Test.Takeover." + Guid.NewGuid();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writer = new Thread(() =>
        {
            using var mutex = new Mutex(false, name);
            mutex.WaitOne();
            held.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        writer.Start();
        try
        {
            Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
            var launches = 0;
            var result = StoreSessionTakeover.Start(Environment.ProcessPath!, Path.GetTempPath(), name,
                _ => launches++);
            Assert.False(result.Succeeded);
            Assert.Equal(StoreSessionTakeover.BusyReason, result.Error);
            Assert.Equal(0, launches);
        }
        finally
        {
            release.Set();
            Assert.True(writer.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void AvailableWriterStartsNormalModeWithTheSameSourceAndCanAcquireTheSharedLock()
    {
        var name = "Grindcrest.Test.Takeover." + Guid.NewGuid();
        var source = Path.Combine(Path.GetTempPath(), "Store data with spaces");
        ProcessStartInfo? start = null;
        var result = StoreSessionTakeover.Start(Environment.ProcessPath!, source, name, info =>
        {
            start = info;
            using var childLock = new Mutex(false, name);
            Assert.True(childLock.WaitOne(0));
            childLock.ReleaseMutex();
        });
        Assert.True(result.Succeeded);
        Assert.NotNull(start);
        Assert.False(start.UseShellExecute);
        Assert.Equal(new[] { StoreSessionViewerLaunch.TakeoverArgument }, start.ArgumentList);
        Assert.Equal(source, start.Environment[AppDataPaths.DataDirectoryVariable]);
        Assert.DoesNotContain(StoreSessionViewerLaunch.ViewerArgument, start.ArgumentList);
    }

    [Fact]
    public void FailedProcessStartReportsAnErrorAndLeavesTheLockAvailableForRetry()
    {
        var name = "Grindcrest.Test.Takeover." + Guid.NewGuid();
        var result = StoreSessionTakeover.Start(Environment.ProcessPath!, Path.GetTempPath(), name,
            _ => throw new Win32Exception(2));
        Assert.False(result.Succeeded);
        using var retryLock = new Mutex(false, name);
        Assert.True(retryLock.WaitOne(0));
        retryLock.ReleaseMutex();
    }
}
