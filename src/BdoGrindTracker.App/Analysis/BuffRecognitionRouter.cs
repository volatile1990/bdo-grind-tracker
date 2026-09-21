namespace BdoGrindTracker.App.Analysis;

/// <summary>An explicitly selected advanced profile remains authoritative, including validation errors.</summary>
internal sealed class BuffRecognitionRouter(Func<bool> useProfile, IBuffFrameReader automatic, IBuffFrameReader calibrated) : IBuffFrameReader
{
    public string? LastDiagnostic { get; private set; }

    public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken)
    {
        var reader = useProfile() ? calibrated : automatic;
        var result = reader.Read(frame, cancellationToken);
        LastDiagnostic = reader.LastDiagnostic;
        return result;
    }

    public BuffFrameReading? Read(Bitmap frame, DateTimeOffset capturedAt, CancellationToken cancellationToken)
    {
        var reader = useProfile() ? calibrated : automatic;
        var result = reader.Read(frame, capturedAt, cancellationToken);
        LastDiagnostic = reader.LastDiagnostic;
        return result;
    }

    public void Dispose() { automatic.Dispose(); calibrated.Dispose(); }
}
