using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class CpuSchedulingTests
{
    [Theory]
    [InlineData(ProcessPriorityClass.Normal)]
    [InlineData(ProcessPriorityClass.AboveNormal)]
    [InlineData(ProcessPriorityClass.High)]
    [InlineData(ProcessPriorityClass.RealTime)]
    public void LowersOnlyTheCurrentProcessPriority(ProcessPriorityClass original)
    {
        var native = new FakeNative { Priority = original };

        CpuScheduling.ApplyBackgroundProcessPriority(native);

        Assert.Equal(new[] { ProcessPriorityClass.BelowNormal }, native.PriorityChanges);
    }

    [Theory]
    [InlineData(ProcessPriorityClass.BelowNormal)]
    [InlineData(ProcessPriorityClass.Idle)]
    [InlineData((ProcessPriorityClass)0)]
    [InlineData((ProcessPriorityClass)12345)]
    public void PreservesAnExistingLowerPriorityAndFailedOrUnknownQueries(ProcessPriorityClass original)
    {
        var native = new FakeNative { Priority = original };

        CpuScheduling.ApplyBackgroundProcessPriority(native);

        Assert.Empty(native.PriorityChanges);
    }

    [Fact]
    public void UnsupportedPlatformDoesNotQueryOrSetPriority()
    {
        var native = new FakeNative { IsWindows = false };

        CpuScheduling.ApplyBackgroundProcessPriority(native);

        Assert.Equal(0, native.PriorityQueries);
        Assert.Empty(native.PriorityChanges);
    }

    [Fact]
    public void PriorityAccessFailureDoesNotPreventStartup()
    {
        var native = new FakeNative { PriorityFailure = new Win32Exception(5) };

        CpuScheduling.ApplyBackgroundProcessPriority(native);

        Assert.Empty(native.PriorityChanges);
    }

    [Fact]
    public void FailedPriorityWriteDoesNotPreventStartup()
    {
        var native = new FakeNative { PriorityWriteSucceeds = false };

        CpuScheduling.ApplyBackgroundProcessPriority(native);

        Assert.Single(native.PriorityChanges);
    }

    [Theory]
    [InlineData(ProcessPriorityClass.Normal)]
    [InlineData(ProcessPriorityClass.AboveNormal)]
    [InlineData(ProcessPriorityClass.High)]
    [InlineData(ProcessPriorityClass.RealTime)]
    public void CaptureLeaseRestoresTheOwnedOriginalPriorityUntilCaptureAndOcrDrain(ProcessPriorityClass original)
    {
        var native = new FakeNative { Priority = original };
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        Assert.Equal(ProcessPriorityClass.BelowNormal, native.Priority);

        var lease = policy.PreserveCapturePriority();
        Assert.Equal(original, native.Priority);

        lease.Dispose();
        Assert.Equal(ProcessPriorityClass.BelowNormal, native.Priority);
        Assert.Equal(new[] { ProcessPriorityClass.BelowNormal, original, ProcessPriorityClass.BelowNormal },
            native.PriorityChanges);
    }

    [Fact]
    public void NestedCaptureLeasesRestoreOnlyOnceAndWaitForTheLastRelease()
    {
        var native = new FakeNative();
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        var first = policy.PreserveCapturePriority();
        var second = policy.PreserveCapturePriority();

        first.Dispose();
        first.Dispose();
        Assert.Equal(ProcessPriorityClass.Normal, native.Priority);
        Assert.Equal(2, native.PriorityChanges.Count);

        second.Dispose();
        second.Dispose();
        Assert.Equal(ProcessPriorityClass.BelowNormal, native.Priority);
        Assert.Equal(3, native.PriorityChanges.Count);
    }

    [Fact]
    public void LaterCaptureSessionsCanRestoreTheSameOwnedOriginalPriority()
    {
        var native = new FakeNative();
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        using (policy.PreserveCapturePriority())
        {
            Assert.Equal(ProcessPriorityClass.Normal, native.Priority);
        }

        using (policy.PreserveCapturePriority())
        {
            Assert.Equal(ProcessPriorityClass.Normal, native.Priority);
        }

        Assert.Equal(ProcessPriorityClass.BelowNormal, native.Priority);
        Assert.Equal(5, native.PriorityChanges.Count);
    }

    [Theory]
    [InlineData(ProcessPriorityClass.BelowNormal)]
    [InlineData(ProcessPriorityClass.Idle)]
    public void CaptureDoesNotTakeOwnershipOfAnExistingLowerPriority(ProcessPriorityClass original)
    {
        var native = new FakeNative { Priority = original };
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();

        using (policy.PreserveCapturePriority())
        {
            Assert.Equal(original, native.Priority);
        }

        Assert.Empty(native.PriorityChanges);
    }

    [Fact]
    public void ExternalPriorityChangeBeforeCaptureRelinquishesOwnership()
    {
        var native = new FakeNative();
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        native.Priority = ProcessPriorityClass.AboveNormal;

        using (policy.PreserveCapturePriority())
        {
            Assert.Equal(ProcessPriorityClass.AboveNormal, native.Priority);
        }

        native.Priority = ProcessPriorityClass.BelowNormal;
        using (policy.PreserveCapturePriority()) { }
        policy.ApplyBackgroundProcessPriority();
        Assert.Single(native.PriorityChanges);
    }

    [Fact]
    public void ExternalPriorityChangeDuringCaptureIsPreservedAfterTheLastLease()
    {
        var native = new FakeNative();
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        var lease = policy.PreserveCapturePriority();
        native.Priority = ProcessPriorityClass.High;

        lease.Dispose();
        Assert.Equal(ProcessPriorityClass.High, native.Priority);

        using (policy.PreserveCapturePriority()) { }
        Assert.Equal(new[] { ProcessPriorityClass.BelowNormal, ProcessPriorityClass.Normal }, native.PriorityChanges);
    }

    [Fact]
    public void FailedStartupWriteDoesNotEstablishOwnership()
    {
        var native = new FakeNative { PriorityWriteSucceeds = false };
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        native.PriorityWriteSucceeds = true;

        using (policy.PreserveCapturePriority()) { }

        Assert.Single(native.PriorityChanges);
        Assert.Equal(ProcessPriorityClass.Normal, native.Priority);
    }

    [Fact]
    public void FailedCaptureRestoreRelinquishesOwnershipAndReleaseDoesNotWriteAgain()
    {
        var native = new FakeNative();
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        native.PriorityWriteSucceeds = false;

        using (policy.PreserveCapturePriority()) { }
        native.PriorityWriteSucceeds = true;
        using (policy.PreserveCapturePriority()) { }

        Assert.Equal(new[] { ProcessPriorityClass.BelowNormal, ProcessPriorityClass.Normal }, native.PriorityChanges);
        Assert.Equal(ProcessPriorityClass.BelowNormal, native.Priority);
    }

    [Fact]
    public void FailedReleaseWriteDoesNotRepeatedlyOverrideProcessPriority()
    {
        var native = new FakeNative();
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        var lease = policy.PreserveCapturePriority();
        native.PriorityWriteSucceeds = false;

        lease.Dispose();
        native.PriorityWriteSucceeds = true;
        using (policy.PreserveCapturePriority()) { }

        Assert.Equal(3, native.PriorityChanges.Count);
        Assert.Equal(ProcessPriorityClass.Normal, native.Priority);
    }

    [Fact]
    public void FailedPriorityQueryDuringCaptureReleaseRelinquishesOwnership()
    {
        var native = new FakeNative();
        var policy = new CpuSchedulingPolicy(native);
        policy.ApplyBackgroundProcessPriority();
        var lease = policy.PreserveCapturePriority();
        native.PriorityFailure = new Win32Exception(5);

        lease.Dispose();
        native.PriorityFailure = null;
        using (policy.PreserveCapturePriority()) { }

        Assert.Equal(2, native.PriorityChanges.Count);
        Assert.Equal(ProcessPriorityClass.Normal, native.Priority);
    }

    [Fact]
    public void StartupCannotLowerPriorityWhileACaptureLeaseAlreadyExists()
    {
        var native = new FakeNative();
        var policy = new CpuSchedulingPolicy(native);

        using (policy.PreserveCapturePriority())
        {
            policy.ApplyBackgroundProcessPriority();
            Assert.Equal(ProcessPriorityClass.Normal, native.Priority);
        }

        Assert.Empty(native.PriorityChanges);
    }

    [Fact]
    public async Task EcoQoSIsAppliedOnlyToADedicatedBackgroundThread()
    {
        var callerThread = Thread.CurrentThread;
        var callerPriority = callerThread.Priority;
        var callerThreadId = Environment.CurrentManagedThreadId;
        var native = new FakeNative { UseEfficientWorker = true };
        var workerThreadId = 0;
        var isThreadPoolThread = true;
        var isBackgroundThread = false;
        ThreadPriority? workerPriority = null;

        var work = CpuScheduling.RunEfficientBackgroundWork(() =>
        {
            workerThreadId = Environment.CurrentManagedThreadId;
            isThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;
            isBackgroundThread = Thread.CurrentThread.IsBackground;
            workerPriority = Thread.CurrentThread.Priority;
        }, native);
        Assert.Equal(callerPriority, callerThread.Priority);
        await work;

        Assert.NotEqual(callerThreadId, workerThreadId);
        Assert.False(isThreadPoolThread);
        Assert.True(isBackgroundThread);
        Assert.Equal(ThreadPriority.BelowNormal, workerPriority);
        Assert.Equal(new[] { workerThreadId }, native.EcoQoSThreads);
        Assert.Empty(native.PriorityChanges);
    }

    [Fact]
    public async Task UnsupportedOrHomogeneousHardwareUsesOwnBelowNormalThreadWithoutEcoQoS()
    {
        var native = new FakeNative { UseEfficientWorker = false };
        var isThreadPoolThread = true;
        ThreadPriority? workerPriority = null;

        await CpuScheduling.RunEfficientBackgroundWork(() =>
        {
            isThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;
            workerPriority = Thread.CurrentThread.Priority;
        }, native);

        Assert.False(isThreadPoolThread);
        Assert.Equal(ThreadPriority.BelowNormal, workerPriority);
        Assert.Empty(native.EcoQoSThreads);
    }

    [Fact]
    public async Task UnavailableTopologyQueryStillRunsWorkOnce()
    {
        var native = new FakeNative { DetectionFailure = new EntryPointNotFoundException() };
        var calls = 0;
        var isThreadPoolThread = true;

        await CpuScheduling.RunEfficientBackgroundWork(() =>
        {
            Interlocked.Increment(ref calls);
            isThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;
        }, native);

        Assert.Equal(1, calls);
        Assert.False(isThreadPoolThread);
        Assert.Empty(native.EcoQoSThreads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedEcoQoSHintStillRunsWorkOnce(bool throws)
    {
        var native = new FakeNative
        {
            UseEfficientWorker = true,
            EcoQoSSucceeds = false,
            EcoQoSFailure = throws ? new Win32Exception(87) : null
        };
        var calls = 0;

        await CpuScheduling.RunEfficientBackgroundWork(() => Interlocked.Increment(ref calls), native);

        Assert.Equal(1, calls);
        Assert.Single(native.EcoQoSThreads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActionFailureFaultsReturnedTask(bool useEfficientWorker)
    {
        var native = new FakeNative { UseEfficientWorker = useEfficientWorker };
        var expected = new InvalidOperationException("analysis failed");

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CpuScheduling.RunEfficientBackgroundWork(() => throw expected, native));

        Assert.Same(expected, observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActionCancellationPreservesTaskRunFailureSemantics(bool useEfficientWorker)
    {
        var native = new FakeNative { UseEfficientWorker = useEfficientWorker };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var task = CpuScheduling.RunEfficientBackgroundWork(
            () => throw new OperationCanceledException(cancellation.Token), native);

        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.True(task.IsFaulted);
        Assert.Equal(cancellation.Token, observed.CancellationToken);
    }

    [Fact]
    public async Task TaskRemainsPendingUntilTheSynchronousActionCompletes()
    {
        var native = new FakeNative { UseEfficientWorker = true };
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var task = CpuScheduling.RunEfficientBackgroundWork(() =>
        {
            entered.Set();
            release.Wait();
        }, native);

        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(task.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        await task;
    }

    [Fact]
    public void DetectsDifferentClassesAcrossPhysicalCoresWithoutCpuNumberAssumptions()
    {
        var data = Combine(CpuSet(7, 63, 22, 8), CpuSet(7, 14, 22, 8), CpuSet(7, 8, 5, 0));

        Assert.True(CpuScheduling.HasHeterogeneousEfficiencyClasses(data));
    }

    [Fact]
    public void ProcessorGroupsHaveIndependentCoreIndexes()
    {
        Assert.True(CpuScheduling.HasHeterogeneousEfficiencyClasses(
            Combine(CpuSet(0, 0, 0, 0), CpuSet(1, 0, 0, 1))));
    }

    [Fact]
    public void SmtSiblingsCannotCreateDifferentPhysicalCoreClasses()
    {
        Assert.False(CpuScheduling.HasHeterogeneousEfficiencyClasses(
            Combine(CpuSet(0, 0, 0, 0), CpuSet(0, 1, 0, 1))));
    }

    [Fact]
    public void HomogeneousTopologyDoesNotEnableEcoQoS()
    {
        Assert.False(CpuScheduling.HasHeterogeneousEfficiencyClasses(
            Combine(CpuSet(0, 0, 0, 0), CpuSet(0, 1, 1, 0), CpuSet(0, 2, 2, 0))));
        Assert.False(CpuScheduling.HasHeterogeneousEfficiencyClasses(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void VariableRecordSizesAndUnknownRecordTypesAreHandled()
    {
        var unknown = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(unknown, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(unknown.AsSpan(4), 99);

        Assert.True(CpuScheduling.HasHeterogeneousEfficiencyClasses(
            Combine(CpuSet(0, 0, 0, 0, 48), unknown, CpuSet(0, 1, 1, 1, 40))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(24)]
    [InlineData(256)]
    public void MalformedCpuRecordsFailClosed(int reportedSize)
    {
        var malformed = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(malformed, (uint)reportedSize);

        Assert.False(CpuScheduling.HasHeterogeneousEfficiencyClasses(
            Combine(CpuSet(0, 0, 0, 0), CpuSet(0, 1, 1, 1), malformed)));
    }

    [Fact]
    public void TruncatedRecordHeaderFailsClosed()
    {
        Assert.False(CpuScheduling.HasHeterogeneousEfficiencyClasses(
            Combine(CpuSet(0, 0, 0, 0), CpuSet(0, 1, 1, 1), new byte[3])));
    }

    [Fact]
    public void PowerThrottlingNativeLayoutMatchesWindows()
    {
        Assert.Equal(12, Marshal.SizeOf<WindowsCpuSchedulingNative.ThreadPowerThrottlingState>());
        Assert.Equal(0, Marshal.OffsetOf<WindowsCpuSchedulingNative.ThreadPowerThrottlingState>("Version").ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<WindowsCpuSchedulingNative.ThreadPowerThrottlingState>("ControlMask").ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<WindowsCpuSchedulingNative.ThreadPowerThrottlingState>("StateMask").ToInt32());
    }

    private static byte[] CpuSet(ushort group, byte logicalProcessor, byte core, byte efficiencyClass, int size = 32)
    {
        var record = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(12), group);
        record[14] = logicalProcessor;
        record[15] = core;
        record[18] = efficiencyClass;
        return record;
    }

    private static byte[] Combine(params byte[][] records) => records.SelectMany(record => record).ToArray();

    private sealed class FakeNative : ICpuSchedulingNative
    {
        private bool _useEfficientWorker;
        public bool IsWindows { get; init; } = true;
        public bool UseEfficientWorker
        {
            get => DetectionFailure is { } exception ? throw exception : _useEfficientWorker;
            init => _useEfficientWorker = value;
        }
        public ProcessPriorityClass Priority { get; set; } = ProcessPriorityClass.Normal;
        public bool PriorityWriteSucceeds { get; set; } = true;
        public bool EcoQoSSucceeds { get; init; } = true;
        public Exception? PriorityFailure { get; set; }
        public Exception? DetectionFailure { get; init; }
        public Exception? EcoQoSFailure { get; init; }
        public int PriorityQueries { get; private set; }
        public List<ProcessPriorityClass> PriorityChanges { get; } = [];
        public List<int> EcoQoSThreads { get; } = [];

        public ProcessPriorityClass GetCurrentProcessPriorityClass()
        {
            PriorityQueries++;
            return PriorityFailure is { } exception ? throw exception : Priority;
        }

        public bool SetCurrentProcessPriorityClass(ProcessPriorityClass priorityClass)
        {
            PriorityChanges.Add(priorityClass);
            if (PriorityWriteSucceeds)
            {
                Priority = priorityClass;
            }
            return PriorityWriteSucceeds;
        }

        public bool EnableCurrentThreadEcoQoS()
        {
            EcoQoSThreads.Add(Environment.CurrentManagedThreadId);
            return EcoQoSFailure is { } exception ? throw exception : EcoQoSSucceeds;
        }
    }
}
