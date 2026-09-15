using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.Capture;

/// <summary>Bounds retries while a nonblocking D3D11 read map is still busy.</summary>
internal sealed class GpuReadbackWaiter
{
    internal const int WasStillDrawing = unchecked((int)0x887A000A);
    internal const uint DoNotWait = 0x100000;
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private readonly Action<TimeSpan, CancellationToken> _wait;

    internal GpuReadbackWaiter(TimeProvider? timeProvider = null, TimeSpan? timeout = null,
        Action<TimeSpan, CancellationToken>? wait = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _wait = wait ?? WaitForRetry;
    }

    internal void Wait(Func<int> tryMap, Action flush, Func<int> getDeviceRemovedReason,
        CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var submitted = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_timeProvider.GetElapsedTime(started) >= _timeout)
                throw new TimeoutException("Die Grafikkarte hat das aufgenommene Bild nicht rechtzeitig bereitgestellt. " +
                    "Tracking wurde gestoppt; bitte erneut starten.", DeviceError(getDeviceRemovedReason()));

            var result = tryMap();
            // The caller now owns a mapped resource. Return before checking any
            // cancellation so it can record that ownership and always Unmap.
            if (result >= 0) return;
            if (result != WasStillDrawing)
                throw new GpuReadbackException(result, getDeviceRemovedReason());

            if (!submitted)
            {
                // DO_NOT_WAIT must not leave the copy sitting in a command
                // buffer while this worker sleeps. Submit once, never per poll.
                flush();
                submitted = true;
            }

            var remaining = _timeout - _timeProvider.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero)
                _wait(remaining < PollInterval ? remaining : PollInterval, cancellationToken);
        }
    }

    private static void WaitForRetry(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (cancellationToken.WaitHandle.WaitOne(delay)) cancellationToken.ThrowIfCancellationRequested();
    }

    private static COMException? DeviceError(int reason) => reason < 0
        ? new COMException("Direct3D hat einen Grafikgerätefehler gemeldet.", reason) : null;
}

/// <summary>Preserves both the failing map HRESULT and the device's underlying reason.</summary>
internal sealed class GpuReadbackException : ExternalException
{
    internal GpuReadbackException(int result, int deviceReason)
        : base("Die Fensteraufnahme wurde wegen eines Grafikfehlers gestoppt. Tracking bitte erneut starten.",
            deviceReason < 0 && deviceReason != result
                ? new COMException("Direct3D hat einen Grafikgerätefehler gemeldet.", deviceReason) : null)
    {
        HResult = result;
    }
}
