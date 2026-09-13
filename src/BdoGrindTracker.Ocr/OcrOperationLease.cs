namespace BdoGrindTracker.Ocr;

/// <summary>Owns a native operation's copied input until the operation itself finishes.</summary>
internal static class OcrOperationLease
{
    internal static T Wait<T>(Task<T> operation, IDisposable input, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try { return operation.WaitAsync(timeout, cancellationToken).GetAwaiter().GetResult(); }
        finally
        {
            if (operation.IsCompleted) input.Dispose();
            else _ = ReleaseWhenCompletedAsync(operation, input);
        }
    }

    private static async Task ReleaseWhenCompletedAsync(Task operation, IDisposable input)
    {
        try { await operation.ConfigureAwait(false); }
        catch (Exception) { /* The waiting caller already left after cancellation or timeout. */ }
        finally { input.Dispose(); }
    }
}
