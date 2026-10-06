using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.Services;

/// <summary>
/// Gives Windows scheduling hints for Grindcrest's own work. It never selects CPU numbers,
/// changes affinity, inspects a game process, or modifies a shared thread-pool thread.
/// </summary>
internal static class CpuScheduling
{
    private static readonly ICpuSchedulingNative Native = new WindowsCpuSchedulingNative();
    private static readonly CpuSchedulingPolicy Policy = new(Native);

    internal static void ApplyBackgroundProcessPriority() => Policy.ApplyBackgroundProcessPriority();

    internal static void ApplyBackgroundProcessPriority(ICpuSchedulingNative native) =>
        new CpuSchedulingPolicy(native).ApplyBackgroundProcessPriority();

    // Acquire before starting capture; release after capture and all OCR inputs have drained.
    internal static IDisposable PreserveCapturePriority() => Policy.PreserveCapturePriority();

    /// <summary>
    /// Runs synchronous, non-urgent work on a new thread with BelowNormal thread priority.
    /// EcoQoS is applied only to this thread when Windows and the CPU topology support it,
    /// because Windows cannot query a thread's previous power-throttling policy for restoration.
    /// The thread exits after this action; UI and thread-pool threads never inherit its hint.
    /// </summary>
    internal static Task RunEfficientBackgroundWork(Action action) => RunEfficientBackgroundWork(action, Native);

    internal static Task RunEfficientBackgroundWork(Action action, ICpuSchedulingNative native)
    {
        ArgumentNullException.ThrowIfNull(action);
        bool enableEcoQoS;
        try
        {
            enableEcoQoS = native.UseEfficientWorker;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            Debug.WriteLine($"Efficient background scheduling is unavailable: {exception.Message}");
            enableEcoQoS = false;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                try
                {
                    Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                }
                catch (Exception exception) when (IsUnavailable(exception))
                {
                    Debug.WriteLine($"Worker priority hint is unavailable: {exception.Message}");
                }

                try
                {
                    if (enableEcoQoS)
                    {
                        _ = native.EnableCurrentThreadEcoQoS();
                    }
                }
                catch (Exception exception) when (IsUnavailable(exception))
                {
                    Debug.WriteLine($"Worker EcoQoS is unavailable: {exception.Message}");
                }

                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Grindcrest background analysis"
        };

        try
        {
            thread.Start();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // Start did not succeed, so the action has not run and can be queued once.
            Debug.WriteLine($"Dedicated background worker is unavailable: {exception.Message}");
            return Task.Run(action);
        }

        return completion.Task;
    }

    internal static bool HasHeterogeneousEfficiencyClasses(ReadOnlySpan<byte> information)
    {
        const int headerSize = 8;
        const int cpuSetSize = 32;
        var classesByCore = new Dictionary<(ushort Group, byte Core), byte>();
        while (!information.IsEmpty)
        {
            if (information.Length < headerSize)
            {
                return false;
            }

            var size = BinaryPrimitives.ReadUInt32LittleEndian(information);
            if (size < headerSize || size > information.Length)
            {
                return false;
            }

            var record = information[..(int)size];
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            if (type == 0) // CpuSetInformation
            {
                if (size < cpuSetSize)
                {
                    return false;
                }

                var core = (BinaryPrimitives.ReadUInt16LittleEndian(record[12..]), record[15]);
                var efficiencyClass = record[18];
                if (classesByCore.TryGetValue(core, out var previousClass) && previousClass != efficiencyClass)
                {
                    // SMT siblings are one physical core, not different core types.
                    return false;
                }

                classesByCore[core] = efficiencyClass;
            }

            information = information[(int)size..];
        }

        return classesByCore.Values.Distinct().Take(2).Count() == 2;
    }

    internal static bool IsUnavailable(Exception exception) => exception is Win32Exception
        or UnauthorizedAccessException or InvalidOperationException or NotSupportedException
        or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
}

/// <summary>
/// Owns only the process-priority change it successfully made at startup. Capture leases
/// restore that original class until the last live capture or pending OCR analysis finishes.
/// External priority changes are respected by relinquishing ownership.
/// </summary>
internal sealed class CpuSchedulingPolicy(ICpuSchedulingNative native)
{
    private readonly object _sync = new();
    private bool _initialized;
    private ProcessPriorityClass? _ownedOriginalPriority;
    private int _captureLeases;

    internal void ApplyBackgroundProcessPriority()
    {
        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            if (_captureLeases != 0 || !TryGetPriority(out var priority))
            {
                return;
            }

            // Do not own an existing lower priority or a failed/unknown query.
            if ((priority is ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal
                or ProcessPriorityClass.High or ProcessPriorityClass.RealTime)
                && TrySetPriority(ProcessPriorityClass.BelowNormal))
            {
                _ownedOriginalPriority = priority;
            }
        }
    }

    internal IDisposable PreserveCapturePriority()
    {
        lock (_sync)
        {
            _captureLeases++;
            if (_captureLeases == 1 && _ownedOriginalPriority is { } original)
            {
                if (!TryGetPriority(out var current) || current != ProcessPriorityClass.BelowNormal
                    || !TrySetPriority(original))
                {
                    _ownedOriginalPriority = null;
                }
            }
        }

        return new CapturePriorityLease(this);
    }

    private void ReleaseCapturePriority()
    {
        lock (_sync)
        {
            if (--_captureLeases != 0 || _ownedOriginalPriority is not { } original)
            {
                return;
            }

            if (!TryGetPriority(out var current) || current != original
                || !TrySetPriority(ProcessPriorityClass.BelowNormal))
            {
                _ownedOriginalPriority = null;
            }
        }
    }

    private bool TryGetPriority(out ProcessPriorityClass priority)
    {
        priority = default;
        try
        {
            if (!native.IsWindows)
            {
                return false;
            }

            priority = native.GetCurrentProcessPriorityClass();
            return priority is ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal
                or ProcessPriorityClass.High or ProcessPriorityClass.RealTime
                or ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Idle;
        }
        catch (Exception exception) when (CpuScheduling.IsUnavailable(exception))
        {
            Debug.WriteLine($"Process priority query is unavailable: {exception.Message}");
            return false;
        }
    }

    private bool TrySetPriority(ProcessPriorityClass priority)
    {
        try
        {
            return native.SetCurrentProcessPriorityClass(priority);
        }
        catch (Exception exception) when (CpuScheduling.IsUnavailable(exception))
        {
            Debug.WriteLine($"Process priority hint is unavailable: {exception.Message}");
            return false;
        }
    }

    private sealed class CapturePriorityLease(CpuSchedulingPolicy owner) : IDisposable
    {
        private CpuSchedulingPolicy? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseCapturePriority();
    }
}

/// <summary>Small seam for testing scheduling decisions without changing test-runner policy.</summary>
internal interface ICpuSchedulingNative
{
    bool IsWindows { get; }
    bool UseEfficientWorker { get; }
    ProcessPriorityClass GetCurrentProcessPriorityClass();
    bool SetCurrentProcessPriorityClass(ProcessPriorityClass priorityClass);
    bool EnableCurrentThreadEcoQoS();
}

internal sealed class WindowsCpuSchedulingNative : ICpuSchedulingNative
{
    private const uint ThreadPowerThrottling = 3;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 1;
    private const int ErrorInsufficientBuffer = 122;
    private readonly Lazy<bool> _useEfficientWorker = new(DetectEfficientWorkerSupport);

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool UseEfficientWorker => _useEfficientWorker.Value;

    public ProcessPriorityClass GetCurrentProcessPriorityClass() =>
        (ProcessPriorityClass)GetPriorityClass(GetCurrentProcess());

    public bool SetCurrentProcessPriorityClass(ProcessPriorityClass priorityClass) =>
        SetPriorityClass(GetCurrentProcess(), (uint)priorityClass);

    public bool EnableCurrentThreadEcoQoS()
    {
        var state = new ThreadPowerThrottlingState
        {
            Version = PowerThrottlingCurrentVersion,
            ControlMask = PowerThrottlingExecutionSpeed,
            StateMask = PowerThrottlingExecutionSpeed
        };
        return SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref state,
            (uint)Marshal.SizeOf<ThreadPowerThrottlingState>());
    }

    private static bool DetectEfficientWorkerSupport()
    {
        // Before Windows 11 this policy is LowQoS rather than EcoQoS.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return false;
        }

        var process = GetCurrentProcess();
        var success = GetSystemCpuSetInformation(null, 0, out var requiredLength, process, 0);
        if ((!success && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer) || requiredLength == 0)
        {
            return false;
        }

        // Retry a growing topology a bounded number of times; a failure leaves normal scheduling.
        for (var attempt = 0; attempt < 3 && requiredLength <= 16 * 1024 * 1024; attempt++)
        {
            var buffer = new byte[(int)requiredLength];
            success = GetSystemCpuSetInformation(buffer, (uint)buffer.Length, out requiredLength, process, 0);
            if (success)
            {
                return requiredLength <= buffer.Length
                    && CpuScheduling.HasHeterogeneousEfficiencyClasses(buffer.AsSpan(0, (int)requiredLength));
            }

            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || requiredLength == 0)
            {
                return false;
            }
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ThreadPowerThrottlingState
    {
        internal uint Version;
        internal uint ControlMask;
        internal uint StateMask;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPriorityClass(nint process, uint priorityClass);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadInformation(nint thread, uint informationClass,
        ref ThreadPowerThrottlingState information, uint informationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemCpuSetInformation([Out] byte[]? information, uint bufferLength,
        out uint returnedLength, nint process, uint flags);
}
