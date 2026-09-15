using System.Runtime.InteropServices;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class GraphicsCaptureRateLimiterTests
{
    [Fact]
    public void SetsNativeIntervalWithHeadroomForFiveObservationsPerSecondAndReleasesInterface()
    {
        using var session = new NativeCaptureSession();

        Assert.True(GraphicsCaptureRateLimiter.TryApply(session.Pointer));

        Assert.Equal(new Guid("67c0ea62-1f85-5061-925a-239be0ac09cb"), session.RequestedInterface);
        Assert.Equal(TimeSpan.FromMilliseconds(100).Ticks, session.IntervalTicks);
        Assert.Equal(1, session.SetterCalls);
        Assert.Equal(1, session.Releases);
    }

    [Theory]
    [InlineData(unchecked((int)0x80004002))] // E_NOINTERFACE: older Windows.
    [InlineData(unchecked((int)0x80004005))] // E_FAIL: platform failure.
    public void MissingOrUnavailableInterfacePreservesUnthrottledCapture(int queryResult)
    {
        using var session = new NativeCaptureSession { QueryResult = queryResult };

        Assert.False(GraphicsCaptureRateLimiter.TryApply(session.Pointer));

        Assert.Equal(0, session.SetterCalls);
        Assert.Equal(0, session.Releases);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070057))] // E_INVALIDARG: unsupported interval.
    [InlineData(unchecked((int)0x80004001))] // E_NOTIMPL: unsupported setter.
    public void RejectedIntervalIsNonfatalAndStillReleasesInterface(int setterResult)
    {
        using var session = new NativeCaptureSession { SetterResult = setterResult };

        Assert.False(GraphicsCaptureRateLimiter.TryApply(session.Pointer));

        Assert.Equal(1, session.SetterCalls);
        Assert.Equal(1, session.Releases);
    }

    // An ABI-level double exercises QueryInterface, the by-value TimeSpan setter,
    // and COM ownership without creating a real capture session or touching a GPU.
    private sealed class NativeCaptureSession : IDisposable
    {
        private readonly QueryInterface _query;
        private readonly Release _release;
        private readonly SetInterval _setter;
        private readonly nint _vtable;
        internal nint Pointer { get; }
        internal int QueryResult { get; init; }
        internal int SetterResult { get; init; }
        internal Guid RequestedInterface { get; private set; }
        internal long IntervalTicks { get; private set; }
        internal int SetterCalls { get; private set; }
        internal int Releases { get; private set; }

        internal NativeCaptureSession()
        {
            _query = (nint _, in Guid id, out nint result) =>
            {
                RequestedInterface = id;
                result = QueryResult >= 0 ? Pointer : 0;
                return QueryResult;
            };
            _release = _ => { Releases++; return 1; };
            _setter = (_, interval) =>
            {
                IntervalTicks = interval.Duration;
                SetterCalls++;
                return SetterResult;
            };
            _vtable = Marshal.AllocHGlobal(8 * nint.Size);
            Pointer = Marshal.AllocHGlobal(nint.Size);
            for (var slot = 0; slot < 8; slot++) Marshal.WriteIntPtr(_vtable, slot * nint.Size, 0);
            Marshal.WriteIntPtr(_vtable, 0, Marshal.GetFunctionPointerForDelegate(_query));
            Marshal.WriteIntPtr(_vtable, 2 * nint.Size, Marshal.GetFunctionPointerForDelegate(_release));
            Marshal.WriteIntPtr(_vtable, 7 * nint.Size, Marshal.GetFunctionPointerForDelegate(_setter));
            Marshal.WriteIntPtr(Pointer, _vtable);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Pointer);
            Marshal.FreeHGlobal(_vtable);
            GC.KeepAlive(_query);
            GC.KeepAlive(_release);
            GC.KeepAlive(_setter);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeTimeSpan { internal long Duration; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterface(nint instance, in Guid id, out nint result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint Release(nint instance);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetInterval(nint instance, NativeTimeSpan interval);
    }
}
