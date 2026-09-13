using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class WindowsOcrInstallationProgressTests
{
    private const string LogPath = @"C:\Users\Test User\App Data\ocr installation.log";

    [Fact]
    public async Task ReportsThirtySecondAndLongWaitsButOnlySucceedsAfterProcessExit()
    {
        var time = new ManualTimeProvider();
        var progress = new RecordingProgress();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installer = new WindowsOcrLanguageInstaller(_ => processExit.Task, () => LogPath, time);

        var pending = installer.InstallAsync("en-US", progress);

        Assert.Equal(TimeSpan.Zero, (await progress.NextAsync()).Elapsed);
        time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(1, progress.Count);
        Assert.False(pending.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        var firstWait = await progress.NextAsync();
        Assert.Equal(TimeSpan.FromSeconds(30), firstWait.Elapsed);
        Assert.Contains("30 Sekunden", firstWait.Message);
        Assert.Equal(LogPath, firstWait.LogPath);
        Assert.Contains(LogPath, firstWait.Message);

        time.Advance(TimeSpan.FromSeconds(90));
        var longWait = await progress.NextAsync();
        Assert.Equal(TimeSpan.FromMinutes(2), longWait.Elapsed);
        Assert.Contains("2 Minuten", longWait.Message);
        Assert.Contains("keine zweite Installation", longWait.Message);
        Assert.Contains("PC nicht neu", longWait.Message);
        Assert.False(pending.IsCompleted);

        time.Advance(TimeSpan.FromMinutes(1));
        var continuedWait = await progress.NextAsync();
        Assert.Equal(TimeSpan.FromMinutes(3), continuedWait.Elapsed);
        Assert.Contains("3 Minuten", continuedWait.Message);
        Assert.False(pending.IsCompleted);

        processExit.SetResult(0);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WindowsOcrInstallStatus.Installed, result.Status);
        Assert.Equal(LogPath, result.LogPath);
        Assert.Null(result.Error);
        Assert.True(time.TimerDisposed);

        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(4, progress.Count);
    }

    [Theory]
    [InlineData(3010, nameof(WindowsOcrInstallStatus.RestartRequired), null)]
    [InlineData(unchecked((int)0x800F0954), nameof(WindowsOcrInstallStatus.Failed), "0x800F0954")]
    public async Task DelayedRestartOrFailureRetainsLogAndStopsProgress(
        int exitCode, string expectedStatus, string? expectedError)
    {
        var time = new ManualTimeProvider();
        var progress = new RecordingProgress();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installer = new WindowsOcrLanguageInstaller(_ => processExit.Task, () => LogPath, time);

        var pending = installer.InstallAsync("de-DE", progress);
        await progress.NextAsync();
        time.Advance(TimeSpan.FromSeconds(30));
        await progress.NextAsync();
        Assert.False(pending.IsCompleted);

        processExit.SetResult(exitCode);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expectedStatus, result.Status.ToString());
        Assert.Equal(LogPath, result.LogPath);
        if (expectedError is null)
        {
            Assert.Null(result.Error);
        }
        else
        {
            Assert.Contains(expectedError, result.Error);
            Assert.Contains(LogPath, result.Error);
        }
        Assert.True(time.TimerDisposed);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, progress.Count);
    }

    [Fact]
    public async Task DeferredProcessExceptionIncludesLogAndStopsProgress()
    {
        var time = new ManualTimeProvider();
        var progress = new RecordingProgress();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installer = new WindowsOcrLanguageInstaller(_ => processExit.Task, () => LogPath, time);

        var pending = installer.InstallAsync("en-US", progress);
        await progress.NextAsync();
        time.Advance(TimeSpan.FromSeconds(30));
        await progress.NextAsync();
        processExit.SetException(new InvalidOperationException("Testprozess fehlgeschlagen."));

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WindowsOcrInstallStatus.Failed, result.Status);
        Assert.Equal(LogPath, result.LogPath);
        Assert.Contains("Testprozess fehlgeschlagen.", result.Error);
        Assert.Contains("0x80131509", result.Error);
        Assert.Contains(LogPath, result.Error);
        Assert.True(time.TimerDisposed);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, progress.Count);
    }

    [Fact]
    public async Task LogPathWithSpacesIsPassedAsOneArgument()
    {
        ProcessStartInfo? invocation = null;
        var installer = new WindowsOcrLanguageInstaller(startInfo =>
        {
            invocation = startInfo;
            return Task.FromResult(0);
        }, () => LogPath);

        var result = await installer.InstallAsync("en-US");

        Assert.Equal(WindowsOcrInstallStatus.Installed, result.Status);
        Assert.Equal(LogPath, result.LogPath);
        Assert.NotNull(invocation);
        Assert.Equal($"/LogPath:{LogPath}", Assert.Single(
            invocation.ArgumentList, argument => argument.StartsWith("/LogPath:", StringComparison.Ordinal)));
        Assert.Equal(6, invocation.ArgumentList.Count);
        Assert.Empty(invocation.Arguments);
    }

    [Fact]
    public async Task SynchronousLaunchFailureIncludesAllocatedLogPath()
    {
        var time = new ManualTimeProvider();
        var progress = new RecordingProgress();
        var installer = new WindowsOcrLanguageInstaller(
            _ => throw new Win32Exception(5, "Teststart verweigert."), () => LogPath, time);

        var result = await installer.InstallAsync("en-US", progress);

        Assert.Equal(WindowsOcrInstallStatus.Failed, result.Status);
        Assert.Equal(LogPath, result.LogPath);
        Assert.Contains("0x00000005", result.Error);
        Assert.Contains("Teststart verweigert.", result.Error);
        Assert.Contains(LogPath, result.Error);
        Assert.Equal(1, progress.Count);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, progress.Count);
    }

    [Fact]
    public async Task LogPathCreationFailureIsReportedBeforeLaunchingProcess()
    {
        var calls = 0;
        var time = new ManualTimeProvider();
        var progress = new RecordingProgress();
        var installer = new WindowsOcrLanguageInstaller(_ =>
        {
            calls++;
            return Task.FromResult(0);
        }, () => throw new UnauthorizedAccessException("Testprotokoll kann nicht erstellt werden."), time);

        var result = await installer.InstallAsync("en-US", progress);

        Assert.Equal(WindowsOcrInstallStatus.Failed, result.Status);
        Assert.Null(result.LogPath);
        Assert.Contains("0x80070005", result.Error);
        Assert.Contains("Testprotokoll kann nicht erstellt werden.", result.Error);
        Assert.Equal(0, calls);
        Assert.Equal(0, progress.Count);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, progress.Count);
    }

    [Fact]
    public async Task DisplaysChangingPercentagesAndRetainsOneHundredPercentUntilProcessExit()
    {
        var time = new ManualTimeProvider();
        var progress = new RecordingProgress();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int? percentage = 12;
        var installer = new WindowsOcrLanguageInstaller(_ => processExit.Task, () => LogPath, time,
            _ => percentage);

        var pending = installer.InstallAsync("en-US", progress);
        Assert.Null((await progress.NextAsync()).Percent);

        time.Advance(TimeSpan.FromSeconds(1));
        var firstUpdate = await progress.NextAsync();
        Assert.Equal(12, firstUpdate.Percent);
        Assert.Equal(TimeSpan.FromSeconds(1), firstUpdate.Elapsed);
        Assert.Contains("12", firstUpdate.Message);
        Assert.False(pending.IsCompleted);

        percentage = 100;
        time.Advance(TimeSpan.FromSeconds(1));
        var completedPercentage = await progress.NextAsync();
        Assert.Equal(100, completedPercentage.Percent);
        Assert.Equal(TimeSpan.FromSeconds(2), completedPercentage.Elapsed);
        Assert.False(pending.IsCompleted);

        // A temporarily unreadable progress file must not erase the latest known percentage.
        percentage = null;
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(100, (await progress.NextAsync()).Percent);
        Assert.False(pending.IsCompleted);

        processExit.SetResult(0);
        Assert.Equal(WindowsOcrInstallStatus.Installed,
            (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.True(time.TimerDisposed);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(4, progress.Count);
    }

    [Theory]
    [InlineData("[========== 12.3% ==========]", 12)]
    [InlineData("[==========0.0%==========]", 0)]
    [InlineData("[==========100.0%==========]", 100)]
    [InlineData("\r[=== 1.0% ===]\r[=== 72.9% ===]\r", 72)]
    [InlineData("DISM header\r\n[=== 25.0% ===]\r\n[=== 99.99% ===]\r\n", 99)]
    [InlineData("[=== 12.5% ===]\r[=== 101.0% ===]", 12)]
    [InlineData("[=== 12.5% ===]\rError: download 100% failed.", 12)]
    [InlineData("", null)]
    [InlineData("Error: download reached 100% but failed.", null)]
    [InlineData("Error [=== 100.0% ===]", null)]
    [InlineData("[=== -1.0% ===]", null)]
    [InlineData("[=== 100.1% ===]", null)]
    [InlineData("[=== 99999999999999999999.0% ===]", null)]
    [InlineData("[=== 12,3% ===]", null)]
    [InlineData("[=== NaN% ===]", null)]
    [InlineData("[=== 12.3%", null)]
    public void ParsesOnlyTheLastValidCompleteDismProgressLine(string output, int? expected)
    {
        Assert.Equal(expected, WindowsOcrLanguageInstaller.ParseProgressPercentage(output));
    }

    [Fact]
    public void ParsesEnglishDecimalPercentagesUnderGermanCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(42, WindowsOcrLanguageInstaller.ParseProgressPercentage("[===== 42.9% =====]"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void ReadsLatestProgressFromLargeFilesAndToleratesLockedOrMissingFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grindcrest-ocr-progress-{Guid.NewGuid():N}.txt");
        try
        {
            Assert.Null(WindowsOcrLanguageInstaller.ReadProgressPercentage(path));
            File.WriteAllText(path,
                "[=== 5.0% ===]\r" + new string('x', 128 * 1024) + "\r[=== 37.8% ===]\r[=== 85.9% ===]\r");
            Assert.Equal(85, WindowsOcrLanguageInstaller.ReadProgressPercentage(path));

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Null(WindowsOcrLanguageInstaller.ReadProgressPercentage(path));
            }
            Assert.Null(WindowsOcrLanguageInstaller.ReadProgressPercentage(Path.GetTempPath()));
            Assert.Null(WindowsOcrLanguageInstaller.ReadProgressPercentage("\0"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingProgressFileKeepsInstallationPendingAndDoesNotTurnSuccessIntoFailure()
    {
        var time = new ManualTimeProvider();
        var progress = new RecordingProgress();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var missingPath = Path.Combine(Path.GetTempPath(), $"grindcrest-missing-progress-{Guid.NewGuid():N}.txt");
        var installer = new WindowsOcrLanguageInstaller(_ => processExit.Task, () => LogPath, time,
            _ => WindowsOcrLanguageInstaller.ReadProgressPercentage(missingPath));

        var pending = installer.InstallAsync("en-US", progress);
        await progress.NextAsync();
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Null((await progress.NextAsync()).Percent);
        Assert.False(pending.IsCompleted);

        processExit.SetResult(0);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WindowsOcrInstallStatus.Installed, result.Status);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ThrowingProgressHandlerCannotFinishAnInstallationWhileTheProcessStillRuns()
    {
        var time = new ManualTimeProvider();
        var progress = new RecordingProgress(throwAfterReport: true);
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installer = new WindowsOcrLanguageInstaller(_ => processExit.Task, () => LogPath, time);

        var pending = installer.InstallAsync("en-US", progress);
        await progress.NextAsync();
        Assert.False(pending.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(30));
        await progress.NextAsync();
        Assert.False(pending.IsCompleted);

        processExit.SetResult(0);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WindowsOcrInstallStatus.Installed, result.Status);
        Assert.Null(result.Error);
        Assert.True(time.TimerDisposed);
    }

    private sealed class RecordingProgress(bool throwAfterReport = false) : IProgress<WindowsOcrInstallProgress>
    {
        private readonly Channel<WindowsOcrInstallProgress> _reports = Channel.CreateUnbounded<WindowsOcrInstallProgress>();
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Report(WindowsOcrInstallProgress value)
        {
            Interlocked.Increment(ref _count);
            _reports.Writer.TryWrite(value);
            if (throwAfterReport) throw new InvalidOperationException("Testfortschrittsanzeige fehlgeschlagen.");
        }

        public Task<WindowsOcrInstallProgress> NextAsync() =>
            _reports.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private ManualTimer? _timer;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public bool TimerDisposed => _timer?.IsDisposed == true;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Null(_timer);
            _timer = new ManualTimer(this, callback, state, dueTime, period);
            return _timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            var timestamp = Interlocked.Add(ref _timestamp, elapsed.Ticks);
            _timer?.FireIfDue(timestamp);
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly object _gate = new();
            private readonly ManualTimeProvider _time;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private long _nextTick;
            private long _period;
            private bool _disposed;

            public ManualTimer(ManualTimeProvider time, TimerCallback callback, object? state,
                TimeSpan dueTime, TimeSpan period)
            {
                _time = time;
                _callback = callback;
                _state = state;
                Change(dueTime, period);
            }

            public bool IsDisposed
            {
                get { lock (_gate) return _disposed; }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_gate)
                {
                    if (_disposed) return false;
                    _nextTick = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : _time.GetTimestamp() + dueTime.Ticks;
                    _period = period.Ticks;
                    return true;
                }
            }

            public void FireIfDue(long timestamp)
            {
                lock (_gate)
                {
                    if (_disposed || timestamp < _nextTick) return;
                    // PeriodicTimer coalesces missed ticks into a single notification.
                    _nextTick = _period > 0
                        ? _nextTick + ((timestamp - _nextTick) / _period + 1) * _period
                        : long.MaxValue;
                }
                _callback(_state);
            }

            public void Dispose()
            {
                lock (_gate) _disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
