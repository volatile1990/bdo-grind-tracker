using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Diagnostics;

/// <summary>
/// An explicitly enabled recording of calibrated loot crops and Companion counter inputs.
/// Keep one instance for one tracker state, including pauses; never start midway through a session.
/// Recordings have no total size or duration limit unless a caller explicitly supplies one.
/// Recording failures are isolated from tracking and reported through <see cref="LastError"/>.
/// </summary>
internal sealed class DiagnosticRecordingSession : IDisposable
{
    internal const long MaximumCropPixels = 8L * 1024 * 1024;

    private readonly long? maximumBytes;
    private readonly int? maximumFrames;
    private readonly object sync = new();
    private FileStream? journal;
    private long writtenBytes;
    private int entrySequence;
    private int frameCount;
    private bool disposed;
    private readonly Dictionary<long, (int Sequence, string? Crop)> normalFrameReferences = [];
    private readonly LootCountAudit countAudit = new();
    private long summaryBytes;

    private DiagnosticRecordingSession(long? maximumBytes, int? maximumFrames)
    {
        this.maximumBytes = maximumBytes;
        this.maximumFrames = maximumFrames;
    }

    public string? RecordingPath { get; private set; }

    public string? LastError { get; private set; }

    public bool IsRecording => journal is not null && !disposed;

    public int RecordedFrameCount => frameCount;

    public static DiagnosticRecordingSession Start(string baseDirectory, string? spotId = null,
        IReadOnlyDictionary<string, uint>? minimumTrashQuantities = null,
        TimeSpan? targetFrameInterval = null, int? maximumQueuedFrames = null) =>
        Start(baseDirectory, spotId, maximumBytes: null, maximumFrames: null, minimumTrashQuantities,
            targetFrameInterval, maximumQueuedFrames);

    internal static DiagnosticRecordingSession Start(
        string baseDirectory,
        string? spotId,
        long? maximumBytes,
        int? maximumFrames,
        IReadOnlyDictionary<string, uint>? minimumTrashQuantities = null,
        TimeSpan? targetFrameInterval = null, int? maximumQueuedFrames = null)
    {
        var session = new DiagnosticRecordingSession(maximumBytes, maximumFrames);
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
            if (maximumBytes is { } byteLimit) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);
            if (maximumFrames is { } frameLimit) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameLimit);
            if (targetFrameInterval is { } interval && interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(targetFrameInterval));
            if (maximumQueuedFrames is { } queueLimit) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueLimit);
            var directory = Path.Combine(
                Path.GetFullPath(baseDirectory),
                $"loot-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            session.RecordingPath = Path.Combine(directory, LootDiagnosticFormat.RecordingFileName);
            session.journal = new FileStream(
                session.RecordingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            session.WriteJson(new LootDiagnosticHeader(
                "header",
                LootDiagnosticFormat.Version,
                LootDiagnosticFormat.EngineVersion,
                DateTimeOffset.UtcNow,
                spotId,
                "matched-loot-counter-only; recorded recognition variant selects the counter; OCR and spot matching are recorded inputs")
            {
                TargetFrameIntervalMilliseconds = targetFrameInterval?.TotalMilliseconds,
                MaximumQueuedFrames = maximumQueuedFrames,
                AppVersion = typeof(DiagnosticRecordingSession).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                MinimumTrashQuantities = minimumTrashQuantities ?? new Dictionary<string, uint>(),
                Catalog = FrameAnalyzerFactory.LoadCatalog(
                    Path.Combine(AppContext.BaseDirectory, "data", "items.en.txt"),
                    Path.Combine(AppContext.BaseDirectory, "data", "icons", "catalog.json")),
                ReconciliationTraceVersion = 1,
            });
        }
        catch (Exception exception) when (IsRecordingException(exception))
        {
            session.StopWithError(exception.Message);
        }

        return session;
    }

    public void RecordFrame(
        DateTimeOffset capturedAt,
        IReadOnlyList<LootObservation> observations,
        TrackerFrameResult result,
        Bitmap sourceFrame,
        Rectangle? normalPanel,
        Rectangle? rareBand,
        NormalLootRecoveryDiagnostics? recovery = null,
        bool? isHdr = null,
        bool? isToneMapped = null,
        IReadOnlyList<LootRowReviewDiagnostics>? rowReviews = null,
        string? recognitionVariant = null,
        LootCaptureTiming? captureTiming = null)
    {
        lock (sync)
        {
            if (!IsRecording)
            {
                return;
            }

            try
            {
                if (maximumFrames is { } frameLimit && frameCount >= frameLimit)
                {
                    StopWithError($"Diagnose-Limit von {maximumFrames} Frames erreicht; Tracking läuft weiter.");
                    return;
                }

                ValidateObservations(observations);
                ValidateResult(result);
                captureTiming?.Validate();
                var sequence = entrySequence + 1;
                var crops = new List<LootDiagnosticCrop>(2);
                var encodedCrops = new List<(string Path, byte[] Bytes)>(2);
                AddCrop(sourceFrame, normalPanel, "normal", sequence, crops, encodedCrops);
                AddCrop(sourceFrame, rareBand, "rare", sequence, crops, encodedCrops);
                if (result.NormalCaptureIndex is { } captureIndex)
                {
                    normalFrameReferences[captureIndex] = (sequence, crops.FirstOrDefault(crop => crop.Source == "normal")?.FileName);
                    // A normal batch spans ten captures, plus its preceding anchor.
                    // Pause/completion records do not consume capture references.
                    if (normalFrameReferences.Count > 32) normalFrameReferences.Remove(normalFrameReferences.Keys.Min());
                }
                var reconciliation = LinkTraces(result.NormalReconciliation);
                var entry = new LootDiagnosticEntry(
                    "frame", sequence, capturedAt, observations, result.NewEvents, result.Decisions, crops)
                {
                    RareEnabled = rareBand is not null,
                    Recovery = recovery,
                    IsHdr = isHdr,
                    IsToneMapped = isToneMapped,
                    RecognitionVariant = recognitionVariant,
                    CaptureTiming = captureTiming,
                    RowReviews = rowReviews is { Count: > 0 } ? rowReviews : null,
                    NormalCaptureIndex = result.NormalCaptureIndex,
                    NormalReconciliation = reconciliation,
                };
                var jsonBytes = SerializeLine(entry);
                EnsureBudget(jsonBytes.LongLength + encodedCrops.Sum(static crop => crop.Bytes.LongLength));
                foreach (var crop in encodedCrops)
                {
                    using var output = new FileStream(crop.Path, FileMode.CreateNew, FileAccess.Write);
                    output.Write(crop.Bytes);
                    writtenBytes += crop.Bytes.LongLength;
                }

                WriteBytes(jsonBytes);
                entrySequence = sequence;
                frameCount++;
                countAudit.Observe(result, reconciliation);
            }
            catch (Exception exception) when (IsRecordingException(exception))
            {
                StopWithError(exception.Message);
            }
        }
    }

    public void RecordCompletion(DateTimeOffset completedAt, TrackerFrameResult result)
    {
        lock (sync)
        {
            if (!IsRecording)
            {
                return;
            }

            try
            {
                ValidateResult(result);
                var reconciliation = LinkTraces(result.NormalReconciliation);
                WriteJson(new LootDiagnosticEntry(
                    "complete", entrySequence + 1, completedAt, [], result.NewEvents, result.Decisions, [])
                    { NormalCaptureIndex = result.NormalCaptureIndex, NormalReconciliation = reconciliation });
                entrySequence++;
                countAudit.Observe(result, reconciliation);
            }
            catch (Exception exception) when (IsRecordingException(exception))
            {
                StopWithError(exception.Message);
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                journal?.Dispose();
            }
            catch (Exception exception) when (IsRecordingException(exception))
            {
                LastError ??= exception.Message;
            }

            journal = null;
        }
    }

    public void SaveCountSummary(Guid sessionId, DateTimeOffset savedAt, TimeSpan activeTime,
        IReadOnlyDictionary<string, long> savedTotals)
    {
        lock (sync)
        {
            if (!IsRecording) return;
            var path = Path.Combine(Path.GetDirectoryName(RecordingPath!)!, LootDiagnosticFormat.CountSummaryFileName);
            var temporary = path + ".tmp";
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(countAudit.Snapshot(sessionId, savedAt, activeTime, savedTotals),
                    new JsonSerializerOptions(LootDiagnosticFormat.JsonOptions) { WriteIndented = true });
                EnsureBudget(Math.Max(0, bytes.LongLength - summaryBytes));
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
                writtenBytes += bytes.LongLength - summaryBytes;
                summaryBytes = bytes.LongLength;
            }
            catch (Exception exception) when (IsRecordingException(exception)) { StopWithError(exception.Message); }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception exception) when (IsRecordingException(exception)) { LastError ??= exception.Message; }
            }
        }
    }

    private IReadOnlyList<RecordedNormalReconciliation>? LinkTraces(IReadOnlyList<NormalLootReconciliationTrace> traces) =>
        traces.Count == 0 ? null : traces.Select(trace =>
        {
            var current = normalFrameReferences.GetValueOrDefault(trace.CaptureIndex);
            var previous = trace.PreviousCaptureIndex is { } prior ? normalFrameReferences.GetValueOrDefault(prior) : default;
            return new RecordedNormalReconciliation(trace, current.Sequence == 0 ? null : current.Sequence, current.Crop,
                previous.Sequence == 0 ? null : previous.Sequence, previous.Crop);
        }).ToArray();

    private void AddCrop(
        Bitmap frame,
        Rectangle? region,
        string source,
        int sequence,
        List<LootDiagnosticCrop> crops,
        List<(string Path, byte[] Bytes)> encodedCrops)
    {
        if (region is not { } bounds)
        {
            return;
        }

        // Never silently expand, clamp, or fall back to a complete screenshot.
        if (bounds.Width <= 0 || bounds.Height <= 0 ||
            bounds.X < 0 || bounds.Y < 0 ||
            (long)bounds.X + bounds.Width > frame.Width ||
            (long)bounds.Y + bounds.Height > frame.Height ||
            bounds == new Rectangle(Point.Empty, frame.Size) ||
            (long)bounds.Width * bounds.Height > MaximumCropPixels)
        {
            throw new InvalidDataException("Ungültiger oder zu großer Loot-Ausschnitt; Diagnose-Aufnahme gestoppt.");
        }

        using var crop = frame.Clone(bounds, PixelFormat.Format32bppArgb);
        using var encoded = new MemoryStream();
        crop.Save(encoded, ImageFormat.Png);
        var fileName = $"{sequence:D6}-{source}.png";
        var path = Path.Combine(Path.GetDirectoryName(RecordingPath!)!, fileName);
        crops.Add(new LootDiagnosticCrop(source, fileName, bounds.Width, bounds.Height));
        encodedCrops.Add((path, encoded.ToArray()));
    }

    private void WriteJson<T>(T value) => WriteBytes(SerializeLine(value));

    private static byte[] SerializeLine<T>(T value)
    {
        var serialized = JsonSerializer.Serialize(value, LootDiagnosticFormat.JsonOptions);
        if (Encoding.UTF8.GetByteCount(serialized) > LootDiagnosticFormat.MaximumJsonLineBytes)
        {
            throw new InvalidDataException("Diagnose-Eintrag ist zu groß.");
        }

        return Encoding.UTF8.GetBytes(serialized + "\n");
    }

    private void WriteBytes(byte[] bytes)
    {
        EnsureBudget(bytes.LongLength);
        journal!.Write(bytes);
        journal.Flush();
        writtenBytes += bytes.LongLength;
    }

    private void EnsureBudget(long nextBytes)
    {
        if (maximumBytes is { } byteLimit && nextBytes > byteLimit - writtenBytes)
        {
            throw new InvalidDataException(
                "Diagnose-Speicherlimit erreicht; Aufnahme gestoppt, Tracking läuft weiter.");
        }
    }

    private void StopWithError(string message)
    {
        LastError = message;
        try
        {
            journal?.Dispose();
        }
        catch (Exception exception) when (IsRecordingException(exception))
        {
            // The first recording failure is the actionable one.
        }

        journal = null;
    }

    internal static void ValidateObservations(IReadOnlyList<LootObservation> observations)
    {
        if (observations is null || observations.Count > LootDiagnosticFormat.MaximumObservationsPerFrame)
        {
            throw new InvalidDataException("Ungültige Anzahl an Diagnose-Beobachtungen.");
        }

        foreach (var observation in observations)
        {
            if (observation is null || !Enum.IsDefined(observation.Source) ||
                observation.Slot is < 0 or > 31 ||
                observation.NativeY is < 0 or > 16384 ||
                (observation.NativeY is null && observation.RejectionReason is null &&
                    !string.IsNullOrWhiteSpace(observation.ItemName)) ||
                observation.RawText is null ||
                observation.RawText.Length > LootDiagnosticFormat.MaximumTextLength ||
                observation.ItemName?.Length > LootDiagnosticFormat.MaximumTextLength ||
                observation.RejectionReason?.Length > LootDiagnosticFormat.MaximumTextLength ||
                observation.Quantity is < 0 ||
                (observation.UsesImplicitUnitQuantity && (observation.Source != LootSource.Rare || observation.Quantity != 1)) ||
                (observation.UsesFixedUnitQuantity && (observation.Quantity != 1 || observation.QuantityBounds?.IsFixedUnit != true ||
                    observation.UsesImplicitUnitQuantity)) ||
                !double.IsFinite(observation.NameConfidence) ||
                !double.IsFinite(observation.QuantityConfidence) ||
                observation.NameConfidence is < 0 or > 1 ||
                observation.QuantityConfidence is < 0 or > 1)
            {
                throw new InvalidDataException("Ungültige Diagnose-Beobachtung.");
            }
        }
    }

    private static void ValidateResult(TrackerFrameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.NewEvents is null || result.NewEvents.Count > 256 ||
            result.Decisions is null || result.Decisions.Count > 256 || result.NormalReconciliation is null ||
            result.NormalReconciliation.Count > CompanionFrameReconciler.BatchSize ||
            result.NormalReconciliation.Any(trace => trace is null || trace.Rows.Count > 32 || trace.OverlapAttempts.Count > 32))
        {
            throw new InvalidDataException("Ungültiges Diagnose-Ergebnis.");
        }
    }

    private static bool IsRecordingException(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Runtime.InteropServices.ExternalException or JsonException;
}
