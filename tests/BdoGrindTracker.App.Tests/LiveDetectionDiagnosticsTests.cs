using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class LiveDetectionDiagnosticsTests
{
    [Fact]
    public void DebugViewExposesOnlyCompanionPipelineDiagnostics()
    {
        RunInSta(() =>
        {
            using var view = new LiveDetectionDebugView();

            Assert.IsType<Label>(FindByAccessibleName(view, "Companion-Panel"));
            Assert.IsType<Label>(FindByAccessibleName(view, "Diagnose Erkennung"));
            Assert.IsType<Label>(FindByAccessibleName(view, "Diagnose Aufnahme"));
            Assert.IsType<Label>(FindByAccessibleName(view, "Diagnose OCR-Zeilen"));
            Assert.IsType<DetectionRegionPreview>(
                FindByAccessibleName(view, "Diagnose Erkennungsbereich"));

            var copyButton = Assert.IsType<Button>(
                FindByAccessibleName(view, "Diagnose kopieren"));
            Assert.False(copyButton.Enabled);
        });
    }

    [Theory]
    [InlineData(3840, 2160, 420, 236)]
    [InlineData(2160, 3840, 133, 236)]
    [InlineData(320, 180, 320, 180)]
    public void ThumbnailIsBoundedAndPreservesAspectRatio(
        int sourceWidth,
        int sourceHeight,
        int expectedWidth,
        int expectedHeight)
    {
        using var source = new Bitmap(sourceWidth, sourceHeight);
        using var thumbnail = DetectionRegionPreview.CreateThumbnail(source);

        Assert.Equal(new Size(expectedWidth, expectedHeight), thumbnail.Size);
        Assert.True(thumbnail.Width <= DetectionRegionPreview.MaximumPreviewSize.Width);
        Assert.True(thumbnail.Height <= DetectionRegionPreview.MaximumPreviewSize.Height);
    }

    [Fact]
    public void SnapshotShowsFixedPanelSixBandsAndExactPipelineCounters()
    {
        RunInSta(() =>
        {
            using var view = new LiveDetectionDebugView();
            view.SetMonitor(new Rectangle(-3840, 0, 3840, 2160));
            var panel = new Rectangle(2450, 710, 633, 447);
            var bands = Enumerable.Range(0, 6)
                .Select(index => new Rectangle(2510, 710 + index * 75, 573, 75))
                .ToArray();
            var snapshot = CreateSnapshot(panel, bands);

            view.UpdateSnapshot(snapshot, new Bitmap(64, 36));

            Assert.Contains("COMPANION-ABGLEICH · AUTO-SPOTFILTER", view.AccessibleDescription);
            var panelText = FindByAccessibleName(view, "Companion-Panel").Text;
            Assert.Contains("Panel (gold): x 2.450, y 710, 633 × 447 px", panelText);
            Assert.Contains("6 Bänder (grün)", panelText);

            var pipelineText = FindByAccessibleName(view, "Diagnose Erkennung").Text;
            Assert.Contains("Bänder 6", pipelineText);
            Assert.Contains("nicht leer 5", pipelineText);
            Assert.Contains("OCR 4", pipelineText);
            Assert.Contains("Katalogtreffer 3", pipelineText);
            Assert.Contains("Buchungen/Korrekturen 2", pipelineText);

            var diagnosticText = view.DiagnosticText;
            Assert.Contains("Grindcrest – Ereignisdiagnose", diagnosticText);
            Assert.Contains("Monitor (Desktop): x=-3840, y=0, width=3840, height=2160", diagnosticText);
            Assert.Contains("Panel: x=2450, y=710, width=633, height=447", diagnosticText);
            Assert.Contains("Windows-OCR-Aufrufe: 4", diagnosticText);
            Assert.Contains("Ausgegebene Buchungen/Korrekturen: 2", diagnosticText);
            Assert.Contains("windows-media-ocr / en-US", diagnosticText);
            Assert.Contains("- Black Crystal Fragment x370", diagnosticText);

            var copyButton = Assert.IsType<Button>(
                FindByAccessibleName(view, "Diagnose kopieren"));
            Assert.True(copyButton.Enabled);
        });
    }

    private static LiveDetectionDebugSnapshot CreateSnapshot(
        Rectangle panel,
        IReadOnlyList<Rectangle> bands) =>
        new(
            CapturedAt: new DateTimeOffset(2026, 9, 3, 7, 45, 12, TimeSpan.Zero),
            CapturedSequence: 42,
            AnalysisDuration: TimeSpan.FromMilliseconds(123.4),
            FrameSize: new Size(3840, 2160),
            PanelRegion: panel,
            SlotRegions: bands,
            PreparedRowCount: 6,
            NonBlankRowCount: 5,
            OcrRowCount: 4,
            CatalogMatchCount: 3,
            RecognizedLines: ["Black Crystal Fragment x370", "Caphras Stone x4"],
            NewEventCount: 2,
            MeanMatchConfidence: 0.876,
            VariantName: "bdo-companion-0.7.4-normal",
            TextRecognitionBackend: "windows-media-ocr",
            TextRecognitionLanguage: "en-US");

    private static Control FindByAccessibleName(Control root, string accessibleName)
    {
        var matches = DescendantsAndSelf(root)
            .Where(control => string.Equals(
                control.AccessibleName,
                accessibleName,
                StringComparison.Ordinal))
            .ToArray();

        return Assert.Single(matches);
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI test did not finish.");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
