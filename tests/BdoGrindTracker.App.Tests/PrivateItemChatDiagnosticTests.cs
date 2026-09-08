using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class PrivateItemChatDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        $"BdoGrindTracker-ChatDiagnosticTests-{Guid.NewGuid():N}");

    [Fact]
    public void SavesSeparateChatCropAndCorrectionEvidenceWithReproducibleCounterReplay()
    {
        var capturedAt = DateTimeOffset.UnixEpoch;
        using var source = new Bitmap(120, 80);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.Red);
            graphics.FillRectangle(Brushes.Lime, 80, 40, 20, 10);
            graphics.FillRectangle(Brushes.Blue, 50, 20, 12, 8);
            graphics.FillRectangle(Brushes.Yellow, 5, 5, 30, 20);
        }

        using var recording = DiagnosticRecordingSession.Start(_directory);
        var tracker = new CompanionDiagnosticCounter([new CompanionRareCatalogEntry("Black Crystal Fragment")]);
        var observation = new LootObservation(LootSource.Normal, 0, "Black Crystal Fragment",
            "Black Crystal Fragment", 6, 1, 0, null, null) { NativeY = 250 };
        var correction = new ChatQuantityCorrection(0, 250, "Black Crystal Fragment", 6);
        var diagnostics = new ChatQuantityRecoveryDiagnostics(3, "reading-private-items", 12, 1, 0)
        {
            Corrections = [correction],
        };
        recording.RecordFrame(capturedAt, [observation], tracker.ProcessFrame(capturedAt, [observation], true),
            source, new Rectangle(80, 40, 20, 10), new Rectangle(50, 20, 12, 8),
            chatPanel: new Rectangle(5, 5, 30, 20), chatRecovery: diagnostics);
        recording.RecordCompletion(capturedAt.AddSeconds(1), tracker.CompleteSession(capturedAt.AddSeconds(1)));
        recording.Dispose();

        Assert.Null(recording.LastError);
        var entry = JsonSerializer.Deserialize<LootDiagnosticEntry>(
            File.ReadLines(recording.RecordingPath!).Skip(1).First(), LootDiagnosticFormat.JsonOptions)!;
        Assert.Collection(entry.Crops,
            crop => Assert.Equal("normal", crop.Source),
            crop => Assert.Equal("rare", crop.Source),
            crop => Assert.Equal(new LootDiagnosticCrop("chat", "000001-chat.png", 30, 20), crop));
        var savedDiagnostics = Assert.IsType<ChatQuantityRecoveryDiagnostics>(entry.ChatRecovery);
        Assert.Equal(3, savedDiagnostics.WindowIndex);
        Assert.Equal(12, savedDiagnostics.LinesRead);
        Assert.Equal(1, savedDiagnostics.QuantitiesRecovered);
        Assert.Equal(correction, Assert.Single(savedDiagnostics.Corrections));
        var path = Path.GetDirectoryName(recording.RecordingPath!)!;
        Assert.Equal(4, Directory.GetFiles(path).Length);
        using var chat = new Bitmap(Path.Combine(path, "000001-chat.png"));
        Assert.Equal(new Size(30, 20), chat.Size);
        Assert.Equal(Color.Yellow.ToArgb(), chat.GetPixel(0, 0).ToArgb());
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
        Assert.Equal(6, replay.Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public void InvalidChatCropCannotFallBackToRecordingTheDesktop()
    {
        using var source = new Bitmap(120, 80);
        using var recording = DiagnosticRecordingSession.Start(_directory);

        recording.RecordFrame(DateTimeOffset.UnixEpoch, [], new TrackerFrameResult([], []), source,
            new Rectangle(80, 40, 20, 10), null, chatPanel: new Rectangle(0, 0, 120, 80));

        Assert.False(recording.IsRecording);
        Assert.NotNull(recording.LastError);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(recording.RecordingPath!)!, "*.png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
