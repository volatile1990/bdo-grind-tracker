using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Diagnostics;

/// <summary>
/// An explicitly enabled recording of calibrated loot crops and Companion counter inputs.
/// Keep one instance for one tracker state, including pauses; never start midway through a session.
/// Recording failures are isolated from tracking and reported through <see cref="LastError"/>.
/// </summary>
internal sealed class DiagnosticRecordingSession : IDisposable
{
    internal const long DefaultMaximumBytes = 250L * 1024 * 1024;
    internal const int DefaultMaximumFrames = 2000;
    internal const long MaximumCropPixels = 8L * 1024 * 1024;

    private readonly long maximumBytes;
    private readonly int maximumFrames;
    private readonly object sync = new();
    private FileStream? journal;
    private long writtenBytes;
    private long journalBytes;
    private int entrySequence;
    private int frameCount;
    private bool disposed;

    private DiagnosticRecordingSession(long maximumBytes, int maximumFrames)
    {
        this.maximumBytes = maximumBytes;
        this.maximumFrames = maximumFrames;
    }

    public string? RecordingPath { get; private set; }

    public string? LastError { get; private set; }

    public bool IsRecording => journal is not null && !disposed;

    public int RecordedFrameCount => frameCount;

    public static DiagnosticRecordingSession Start(string baseDirectory, string? spotId = null) =>
        Start(baseDirectory, spotId, DefaultMaximumBytes, DefaultMaximumFrames);

    internal static DiagnosticRecordingSession Start(
        string baseDirectory,
        string? spotId,
        long maximumBytes,
        int maximumFrames)
    {
        var session = new DiagnosticRecordingSession(maximumBytes, maximumFrames);
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrames);
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
                "companion-counter-only; OCR and spot matching are recorded inputs, not re-executed")
            {
                Catalog = FrameAnalyzerFactory.LoadCatalog(
                    Path.Combine(AppContext.BaseDirectory, "data", "items.en.txt"),
                    Path.Combine(AppContext.BaseDirectory, "data", "icons", "catalog.json")),
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
        NormalLootRecoveryDiagnostics? recovery = null)
    {
        lock (sync)
        {
            if (!IsRecording)
            {
                return;
            }

            try
            {
                EnsureActionBudget();
                if (frameCount >= maximumFrames)
                {
                    StopWithError($"Diagnose-Limit von {maximumFrames} Frames erreicht; Tracking läuft weiter.");
                    return;
                }

                ValidateObservations(observations);
                ValidateResult(result);
                var sequence = entrySequence + 1;
                var crops = new List<LootDiagnosticCrop>(2);
                var encodedCrops = new List<(string Path, byte[] Bytes)>(2);
                AddCrop(sourceFrame, normalPanel, "normal", sequence, crops, encodedCrops);
                AddCrop(sourceFrame, rareBand, "rare", sequence, crops, encodedCrops);
                var entry = new LootDiagnosticEntry(
                    "frame", sequence, capturedAt, observations, result.NewEvents, result.Decisions, crops)
                {
                    RareEnabled = rareBand is not null,
                    Recovery = recovery,
                };
                var jsonBytes = SerializeLine(entry);
                EnsureBudget(jsonBytes.LongLength + encodedCrops.Sum(static crop => crop.Bytes.LongLength));
                EnsureJournalBudget(jsonBytes.LongLength);
                foreach (var crop in encodedCrops)
                {
                    using var output = new FileStream(crop.Path, FileMode.CreateNew, FileAccess.Write);
                    output.Write(crop.Bytes);
                    writtenBytes += crop.Bytes.LongLength;
                }

                WriteBytes(jsonBytes);
                entrySequence = sequence;
                frameCount++;
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
                EnsureActionBudget();
                ValidateResult(result);
                WriteJson(new LootDiagnosticEntry(
                    "complete", entrySequence + 1, completedAt, [], result.NewEvents, result.Decisions, []));
                entrySequence++;
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
        EnsureJournalBudget(bytes.LongLength);
        journal!.Write(bytes);
        journal.Flush();
        writtenBytes += bytes.LongLength;
        journalBytes += bytes.LongLength;
    }

    private void EnsureJournalBudget(long nextBytes)
    {
        if (nextBytes > LootDiagnosticFormat.MaximumReplayJsonBytes - journalBytes)
        {
            throw new InvalidDataException("Diagnose-Protokolllimit erreicht; Tracking läuft weiter.");
        }
    }

    private void EnsureActionBudget()
    {
        if (entrySequence >= LootDiagnosticFormat.MaximumActions)
        {
            throw new InvalidDataException("Diagnose-Aktionslimit erreicht; Tracking läuft weiter.");
        }
    }

    private void EnsureBudget(long nextBytes)
    {
        if (nextBytes > maximumBytes - writtenBytes)
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
            result.Decisions is null || result.Decisions.Count > 256)
        {
            throw new InvalidDataException("Ungültiges Diagnose-Ergebnis.");
        }
    }

    private static bool IsRecordingException(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Runtime.InteropServices.ExternalException or JsonException;
}
