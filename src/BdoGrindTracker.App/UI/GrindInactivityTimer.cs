namespace BdoGrindTracker.App.UI;

/// <summary>
/// Tracks time without new confirmed loot using monotonic timestamps. Capture
/// can record activity concurrently with the UI checking for an automatic pause.
/// </summary>
internal sealed class GrindInactivityTimer(TimeProvider? timeProvider = null)
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private long _lastActivityAt;
    private bool _isRunning;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _isRunning;
            }
        }
    }

    public TimeSpan IdleDuration
    {
        get
        {
            lock (_sync)
            {
                return _isRunning
                    ? _timeProvider.GetElapsedTime(_lastActivityAt)
                    : TimeSpan.Zero;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_isRunning)
            {
                return;
            }

            _lastActivityAt = _timeProvider.GetTimestamp();
            _isRunning = true;
        }
    }

    public void Pause()
    {
        PauseAndGetIdleDuration();
    }

    /// <summary>Atomically freezes activity and returns the final idle interval.</summary>
    public TimeSpan PauseAndGetIdleDuration()
    {
        lock (_sync)
        {
            var idleDuration = _isRunning
                ? _timeProvider.GetElapsedTime(_lastActivityAt)
                : TimeSpan.Zero;
            _isRunning = false;
            return idleDuration;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _isRunning = false;
            _lastActivityAt = 0;
        }
    }

    /// <summary>
    /// Call on the capture producer only for a new, unique, positive counted
    /// drop—not repeated OCR observations or negative count corrections.
    /// </summary>
    public void RecordDrop()
    {
        lock (_sync)
        {
            if (_isRunning)
            {
                _lastActivityAt = _timeProvider.GetTimestamp();
            }
        }
    }

    public bool ShouldPause(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        lock (_sync)
        {
            return _isRunning && _timeProvider.GetElapsedTime(_lastActivityAt) >= timeout;
        }
    }
}
