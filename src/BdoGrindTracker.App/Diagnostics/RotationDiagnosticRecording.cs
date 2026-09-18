using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.Diagnostics;

/// <summary>
/// An explicitly enabled recording of the Rotation Monitor: the message crop of every probe, every OCR read,
/// confirmed events, tracker states after each event and completed rotations. Keep one instance for one
/// session, including pauses. Recording failures never affect tracking and are reported through
/// <see cref="LastError"/>.
/// </summary>
internal sealed class RotationDiagnosticRecording : IDisposable
{
    internal const string FileName = "rotation.jsonl";
    internal const string ImageDirectory = "crops";
    internal const int FormatVersion = 1;
    // A 2560×1440 Hermesia crop is about 46 KB as JPEG: roughly 0.9 GB or 16 hours of probes.
    internal const int DefaultMaximumImages = 20_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly int _maximumImages;
    private StreamWriter? _journal;
    private string? _directory;
    private int _images;

    private RotationDiagnosticRecording(int maximumImages) => _maximumImages = maximumImages;

    public string? RecordingPath { get; private set; }
    public string? LastError { get; private set; }
    public bool IsRecording { get { lock (_sync) return _journal is not null; } }
    internal int ImageCount { get { lock (_sync) return _images; } }

    public static RotationDiagnosticRecording Start(string baseDirectory, int maximumImages = DefaultMaximumImages)
    {
        var recording = new RotationDiagnosticRecording(maximumImages);
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumImages);
            var directory = Path.Combine(Path.GetFullPath(baseDirectory),
                $"rotation-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(directory, ImageDirectory));
            recording._directory = directory;
            recording.RecordingPath = Path.Combine(directory, FileName);
            recording._journal = new StreamWriter(new FileStream(recording.RecordingPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            recording.Write(new
            {
                Type = "header", Version = FormatVersion, StartedAt = DateTimeOffset.UtcNow,
                AppVersion = typeof(RotationDiagnosticRecording).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                SampleIntervalMilliseconds = BufferedRotationProfileMonitor.SampleInterval.TotalMilliseconds,
                ProbeIntervalMilliseconds = BufferedRotationProfileMonitor.ProbeInterval.TotalMilliseconds,
                Description = "probe: crop of the newest buffered sample; read: OCR text of one sample; " +
                    "event: confirmed message with its first visible time; state: tracker after that event",
            });
        }
        catch (Exception exception) when (IsRecordingException(exception))
        {
            recording.Stop(exception.Message);
        }
        return recording;
    }

    /// <summary>Saves the crop a probe reads first, so a missed or misread message can be checked later.</summary>
    internal void Probe(string spotId, DateTimeOffset sampleAt, Bitmap crop, Size frame, Rectangle region)
    {
        lock (_sync)
        {
            if (_journal is null || _directory is null) return;
            string? image = null;
            try
            {
                if (_images < _maximumImages)
                {
                    image = $"{ImageDirectory}/{++_images:D6}.jpg";
                    SaveJpeg(crop, Path.Combine(_directory, image));
                }
                Write(new
                {
                    Type = "probe", Spot = spotId, SampleAt = sampleAt, Image = image,
                    FrameWidth = frame.Width, FrameHeight = frame.Height,
                    Crop = new { region.X, region.Y, region.Width, region.Height },
                });
                if (image is not null && _images == _maximumImages)
                    Write(new { Type = "note", Spot = spotId, At = sampleAt, Kind = "image-limit",
                        Message = $"Bildgrenze von {_maximumImages} erreicht; weitere Prüfungen nur als Text." });
            }
            catch (Exception exception) when (IsRecordingException(exception)) { Stop(exception.Message); }
        }
    }

    internal void Read(string spotId, DateTimeOffset sampleAt, string text, IEnumerable<string> kinds) =>
        TryWrite(() => new { Type = "read", Spot = spotId, SampleAt = sampleAt, Text = text, Kinds = kinds.ToArray() });

    internal void Event(string spotId, string kind, string label, DateTimeOffset at, DateTimeOffset probedAt) =>
        TryWrite(() => new { Type = "event", Spot = spotId, Kind = kind, Label = label, At = at, ProbedAt = probedAt });

    internal void State(string spotId, DateTimeOffset at, RotationMonitorSnapshot state) =>
        TryWrite(() => new
        {
            Type = "state", Spot = spotId, At = at, state.Status, state.Synchronized, state.IsAfk, state.Elapsed,
            state.SmallScarecrows, Events = state.Events.Select(e => new { e.Kind, e.Occurrence, e.Seconds }).ToArray(),
        });

    internal void Completed(string spotId, DateTimeOffset startedAt, RotationRun run) =>
        TryWrite(() => new
        {
            Type = "completed", Spot = spotId, StartedAt = startedAt, run.Duration,
            Events = run.Events.Select(e => new { e.Kind, e.Occurrence, e.Seconds }).ToArray(),
        });

    internal void Note(string? spotId, DateTimeOffset at, string kind, string message) =>
        TryWrite(() => new { Type = "note", Spot = spotId, At = at, Kind = kind, Message = message });

    public void Dispose()
    {
        lock (_sync)
        {
            _journal?.Dispose();
            _journal = null;
        }
    }

    private void TryWrite(Func<object> entry)
    {
        lock (_sync)
        {
            if (_journal is null) return;
            try { Write(entry()); }
            catch (Exception exception) when (IsRecordingException(exception)) { Stop(exception.Message); }
        }
    }

    // Called while holding _sync or before the instance is shared.
    private void Write(object entry) => _journal!.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));

    private void Stop(string message)
    {
        LastError = message;
        try { _journal?.Dispose(); }
        catch (Exception exception) when (IsRecordingException(exception)) { }
        _journal = null;
    }

    private static void SaveJpeg(Bitmap crop, string path)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
        crop.Save(path, encoder, parameters);
    }

    private static bool IsRecordingException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ExternalException or ArgumentException or NotSupportedException;
}
