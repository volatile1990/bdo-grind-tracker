namespace BdoGrindTracker.Ocr.Tests;

public sealed class OcrOperationLeaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonedWaitKeepsNativeInputAliveUntilUnderlyingOperationFinishes(bool cancel)
    {
        var operation = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new OwnedInput();
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel();
        try
        {
            var error = Record.Exception(() => OcrOperationLease.Wait(operation.Task, input,
                TimeSpan.FromMilliseconds(20), cancellation.Token));
            if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error);
            else Assert.IsType<TimeoutException>(error);
            Assert.False(input.Disposed.Task.IsCompleted);
        }
        finally { operation.TrySetResult(7); }
        await input.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, input.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedNativeOperationReleasesInputOnSuccessAndFailure(bool fail)
    {
        var input = new OwnedInput();
        var operation = fail ? Task.FromException<int>(new InvalidOperationException("native failure")) : Task.FromResult(7);
        if (fail) Assert.Throws<InvalidOperationException>(() => OcrOperationLease.Wait(operation, input,
            TimeSpan.FromSeconds(1), CancellationToken.None));
        else Assert.Equal(7, OcrOperationLease.Wait(operation, input, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.Equal(1, input.Disposals);
    }

    private sealed class OwnedInput : IDisposable
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals;
        public void Dispose() { Interlocked.Increment(ref Disposals); Disposed.TrySetResult(); }
    }
}
