using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

// Optional local evidence test: the multi-gigabyte user recording is deliberately not a repository fixture.
public sealed class RotationRecordingFactAttribute : FactAttribute
{
    public RotationRecordingFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROTATION_RECORDING_DIRECTORY")))
            Skip = "Set ROTATION_RECORDING_DIRECTORY to the supplied Event Horizon recording directory.";
    }
}

public sealed class RotationRecordingReplayTests
{
    [RotationRecordingFact]
    public void SuppliedUltrawideScreenshotAndDiagnosticEventsRecover()
    {
        var directory = Environment.GetEnvironmentVariable("ROTATION_RECORDING_DIRECTORY")!;
        var engine = CompanionWindowsOcrRecognizer.TryCreate("en-US", requirePreferredLanguage: true);
        Assert.NotNull(engine);
        using var bitmap = new Bitmap(Path.Combine(directory, "2026-09-19_27977950.JPG"));
        var crop = RotationMessageProfile.EventHorizon.Crop(bitmap.Width, bitmap.Height);
        using var region = bitmap.Clone(crop, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var pixels = CompanionFrameDecoder.Decode(region);
        var text = RotationMessageProfile.EventHorizon.Recognize(pixels, engine);
        Assert.Contains(EventHorizonMessages.Parse(text), e => e.Kind == "expansion");

        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        foreach (var line in File.ReadLines(Path.Combine(directory, "rotation.jsonl")))
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.GetProperty("type").GetString() != "event" || root.GetProperty("spot").GetString() != "event-horizon") continue;
            var at = root.GetProperty("at").GetDateTimeOffset();
            var kind = root.GetProperty("kind").GetString()!;
            if (kind == "start") tracker.ObserveLoot(at);
            else tracker.Observe(kind, root.GetProperty("label").GetString()!, at);
        }
        var entries = tracker.DrainTimeline();
        Assert.Contains(entries, e => e.Kind == "phase" && e.Detail.Contains("Trümmer-AFK beendet"));
        Assert.Contains(entries, e => e.Kind == "phase" && e.Detail.Contains("Wurmloch geräumt"));
    }

    [RotationRecordingFact]
    public void ReplayFullVideoWithNativeOcr()
    {
        var directory = Environment.GetEnvironmentVariable("ROTATION_RECORDING_DIRECTORY")!;
        var output = Environment.GetEnvironmentVariable("ROTATION_REPLAY_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "rotation-video-replay.jsonl");
        var video = Directory.GetFiles(directory, "*.mp4").Single();
        var extracted = Environment.GetEnvironmentVariable("ROTATION_REPLAY_CROPS");
        using var capture = new VideoCapture(video);
        Assert.True(capture.IsOpened(), "OpenCV could not open the supplied MP4.");
        var duration = capture.FrameCount / capture.Fps;
        Assert.True(duration > 3500);
        var engine = CompanionWindowsOcrRecognizer.TryCreate("en-US", requirePreferredLanguage: true);
        Assert.NotNull(engine);
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        var search = new BufferedRotationSearch(EventHorizonMessages.Parse, 7, RotationMessageProfile.EventHorizon.DuplicateSeconds);
        var times = new List<DateTimeOffset>();
        var texts = new List<string>();
        using var writer = new StreamWriter(output) { AutoFlush = true };
        using var frame = new Mat();
        var recognized = 0;
        for (double second = 0; second < duration; second += 2)
        {
            Mat crop;
            if (extracted is not null)
            {
                var file = Path.Combine(extracted, $"{(int)(second / 2) + 1:000000}.jpg");
                Assert.True(SpinWait.SpinUntil(() => File.Exists(file), TimeSpan.FromSeconds(60)), "Missing extracted frame: " + file);
                crop = Cv2.ImRead(file);
            }
            else
            {
                capture.Set(VideoCaptureProperties.PosMsec, second * 1000);
                if (!capture.Read(frame) || frame.Empty()) continue;
                var rectangle = RotationMessageProfile.EventHorizon.Crop(frame.Width, frame.Height);
                crop = new Mat(frame, new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
            }
            using var ownedCrop = crop;
            var text = RotationMessageProfile.EventHorizon.Recognize(crop, engine);
            var at = DateTimeOffset.UnixEpoch.AddSeconds(second);
            times.Add(at); texts.Add(text);
            if (times.Count > 6) { times.RemoveAt(0); texts.RemoveAt(0); }
            foreach (var e in search.Read(times, i => texts[i]))
            {
                tracker.Observe(e.Kind, e.Label, e.At); recognized++;
                writer.WriteLine(JsonSerializer.Serialize(new { type = "event", second = (e.At - DateTimeOffset.UnixEpoch).TotalSeconds, e.Kind, e.Label }));
            }
            if (second % 60 == 0) writer.WriteLine(JsonSerializer.Serialize(new { type = "progress", second, duration, recognized }));
        }
        tracker.Interrupt("Videoende");
        writer.WriteLine(JsonSerializer.Serialize(new { type = "summary", duration, recognized, runs = tracker.DrainCompleted(), timeline = tracker.DrainTimeline() }));
        Assert.True(recognized >= 100, $"Only {recognized} messages recognized; see {output}");
    }
}
