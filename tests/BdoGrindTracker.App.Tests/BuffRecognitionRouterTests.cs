using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffRecognitionRouterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimestampedReadPreservesTheCaptureTimeAndSelectedReader(bool useProfile)
    {
        var automatic = new TimestampReader("automatic");
        var calibrated = new TimestampReader("calibrated");
        using var router = new BuffRecognitionRouter(() => useProfile, automatic, calibrated);
        using var frame = new Bitmap(10, 10);
        using var cancellation = new CancellationTokenSource();
        var capturedAt = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.FromHours(2)).AddTicks(123);
        var selected = useProfile ? calibrated : automatic;
        var other = useProfile ? automatic : calibrated;

        var result = router.Read(frame, capturedAt, cancellation.Token);

        Assert.Same(selected.Result, result);
        Assert.NotNull(selected.CapturedAt);
        Assert.True(capturedAt.EqualsExact(selected.CapturedAt.Value));
        Assert.Equal(cancellation.Token, selected.Token);
        Assert.Null(other.CapturedAt);
        Assert.Equal(0, selected.LegacyCalls);
        Assert.Equal(0, other.LegacyCalls);
        Assert.Equal(selected.LastDiagnostic, router.LastDiagnostic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingReadOverloadStillRoutesWithoutInventingATimestamp(bool useProfile)
    {
        var automatic = new TimestampReader("automatic");
        var calibrated = new TimestampReader("calibrated");
        using var router = new BuffRecognitionRouter(() => useProfile, automatic, calibrated);
        using var frame = new Bitmap(10, 10);
        var selected = useProfile ? calibrated : automatic;
        var other = useProfile ? automatic : calibrated;

        Assert.Same(selected.Result, router.Read(frame, CancellationToken.None));

        Assert.Equal(1, selected.LegacyCalls);
        Assert.Equal(0, other.LegacyCalls);
        Assert.Null(selected.CapturedAt);
        Assert.Equal(selected.LastDiagnostic, router.LastDiagnostic);
    }

    [Fact]
    public void TimestampedInterfaceReadRemainsCompatibleWithExistingReaders()
    {
        using IBuffFrameReader reader = new LegacyReader();
        using var frame = new Bitmap(10, 10);

        Assert.NotNull(reader.Read(frame, DateTimeOffset.UnixEpoch, CancellationToken.None));
    }

    private sealed class TimestampReader(string diagnostic) : IBuffFrameReader
    {
        internal BuffFrameReading Result { get; } = new([]);
        internal DateTimeOffset? CapturedAt { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal int LegacyCalls { get; private set; }
        public string? LastDiagnostic => diagnostic;
        public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken)
        { LegacyCalls++; return Result; }
        public BuffFrameReading? Read(Bitmap frame, DateTimeOffset capturedAt, CancellationToken cancellationToken)
        { CapturedAt = capturedAt; Token = cancellationToken; return Result; }
        public void Dispose() { }
    }

    private sealed class LegacyReader : IBuffFrameReader
    {
        public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken) => new([]);
        public void Dispose() { }
    }
}
