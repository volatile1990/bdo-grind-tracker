namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private static void TracePersistenceFailure(string operation, string fileName, Exception error) =>
        System.Diagnostics.Trace.TraceWarning("Grindcrest {0} failed for {1}: {2} (0x{3:X8}).",
            operation, fileName, error.GetType().Name, error.HResult);

    private static void TraceOperationFailure(string operation, Exception error) =>
        System.Diagnostics.Trace.TraceWarning("Grindcrest {0} failed: {1} (0x{2:X8}).",
            operation, error.GetType().Name, error.HResult);
}
