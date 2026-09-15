using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;

namespace BdoGrindTracker.App.Capture;

/// <summary>Reduces native capture work when the installed Windows version supports it.</summary>
internal static unsafe class GraphicsCaptureRateLimiter
{
    // Leave a fresh frame between the tracker's 200 ms observations. Matching that
    // cadence exactly can miss the next frame because WGC timestamps must be newer
    // than the observation request, and capture/polling clocks are not synchronized.
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);
    private static readonly Guid Session5Id = new("67c0ea62-1f85-5061-925a-239be0ac09cb");
    private const int ENoInterface = unchecked((int)0x80004002);

    internal static bool TryApply(GraphicsCaptureSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            // UniversalApiContract v19 is newer than the Windows 10 SDK target.
            // Keep the existing capture behavior on Windows without this property.
            if (!ApiInformation.IsWriteablePropertyPresent(
                    "Windows.Graphics.Capture.GraphicsCaptureSession", "MinUpdateInterval"))
                return false;

            using var reference = WinRT.MarshalInspectable<GraphicsCaptureSession>.CreateMarshaler(session);
            return TryApply(reference.ThisPtr);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or
            NotSupportedException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Windows capture rate limiting is unavailable: {0}", error.Message);
            return false;
        }
    }

    internal static bool TryApply(nint session)
    {
        if (session == 0) throw new ArgumentException("A capture session is required.", nameof(session));
        nint rateSession = 0;
        try
        {
            var interfaceId = Session5Id;
            var query = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)session)[0];
            var result = query(session, &interfaceId, &rateSession);
            if (result < 0)
            {
                if (result != ENoInterface) LogFailure(result);
                return false;
            }

            if (rateSession == 0) return false;

            // Windows SDK 10.0.26100.0, windows.graphics.capture.h:
            // IGraphicsCaptureSession5 inherits IInspectable, then get/put_MinUpdateInterval.
            // windows.foundation.h defines TimeSpan as one signed 64-bit Duration.
            var setInterval = (delegate* unmanaged[Stdcall]<nint, AbiTimeSpan, int>)(*(nint**)rateSession)[7];
            result = setInterval(rateSession, new AbiTimeSpan { Duration = MinimumInterval.Ticks });
            if (result >= 0) return true;
            LogFailure(result);
            return false;
        }
        finally
        {
            if (rateSession != 0)
                _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)rateSession)[2])(rateSession);
        }
    }

    private static void LogFailure(int hresult) =>
        Trace.TraceWarning("Windows capture rate limiting was not applied (HRESULT 0x{0:X8}).", hresult);

    [StructLayout(LayoutKind.Sequential)]
    private struct AbiTimeSpan
    {
        internal long Duration;
    }
}
