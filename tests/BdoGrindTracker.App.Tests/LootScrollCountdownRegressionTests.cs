using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Ocr;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class LootScrollCountdownRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("inactive-user-20260910.png", 0f)]
    [InlineData("inactive-expanded-user-20260910.png", 0f)]
    [InlineData("inactive-zero-user-20260910.png", 0f)]
    [InlineData("inactive-zero-user-20260910.png", 2.5f)]
    [InlineData("inactive-zero-user-20260910.png", 5f)]
    [InlineData("inactive-eight-hours-user-20260910.png", 0f)]
    [InlineData("inactive-eight-hours-user-20260910.png", 2.5f)]
    [InlineData("inactive-eight-hours-user-20260910.png", 5f)]
    [InlineData("active-2-user-20260910.png", 0f)]
    public async Task RepeatedStationaryUserScreenshotWarnsThroughTheRealRecognitionPipeline(string name, float whiteLevel)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null)
        {
            output.WriteLine("Native OCR unavailable; timer state transitions have independent tests.");
            return;
        }

        using var original = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "loot-scroll", name));
        using var frame = whiteLevel == 0 ? (Bitmap)original.Clone()
            : LootScrollGaugeDetectorTests.ToneMapHdr(original, whiteLevel);
        using var monitor = new LootScrollMonitor(new LootScrollFrameDetector(new LootScrollTimerReader((image, token) =>
        {
            var text = engine.Recognize(image, token).Text;
            output.WriteLine(text);
            return text;
        })));
        var start = DateTimeOffset.UnixEpoch;
        for (var sample = 0; sample < 4; sample++)
        {
            var at = start.AddSeconds(sample * 30);
            monitor.Observe(frame, at);
            await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(10));
            var state = monitor.Snapshot(at);
            output.WriteLine($"Second {sample * 30}: {state.Status}");
            Assert.NotEqual(LootScrollStatus.Active, state.Status);
            if (sample == 0) Assert.Equal(LootScrollState.Unknown, state);
            // Seconds displays warn on the second sample. The active-looking
            // minute-only reference needs more than sixty seconds of equality.
            if (sample > 0 && (name != "active-2-user-20260910.png" || sample == 3))
                Assert.True(state.ShouldWarn);
        }
    }
}
