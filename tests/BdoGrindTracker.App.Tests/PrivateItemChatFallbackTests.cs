using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class PrivateItemChatFallbackTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly PrivateItemChatCalibration Window = new(3, new Rectangle(10, 10, 40, 30), false);

    [Fact]
    public void GermanChatRecoversTheCanonicalPanelItemOnce()
    {
        using var frame = Frame();
        static PrivateItemChatLine GermanLine(string text, int y)
        {
            Assert.True(PrivateItemChatParser.TryParse(text, out var item, out var quantity));
            return new(item, quantity, y, text);
        }
        IReadOnlyList<PrivateItemChatLine> lines = [GermanLine("Ihr habt 1 x [Schwarzkristallfragment] erhalten.", 20)];
        var fallback = Create(() => Window, (_, _) => lines);
        fallback.Apply(frame, [Observation(1)], Start, CancellationToken.None);
        lines = [GermanLine("Ihr habt 1 x [Schwarzkristallfragment] erhalten.", 5),
            GermanLine("Ihr habt 6 x [Helm eines Anhängers Elions] erhalten.", 20)];
        var normal = NewlyScrolledPanel();
        var result = fallback.Apply(frame, normal, Start.AddMilliseconds(450), CancellationToken.None);
        Assert.Equal(new ChatQuantityCorrection(0, 250, "Elion Follower's Helmet", 6), Assert.Single(result.Diagnostics.Corrections));
        Assert.Equal(6, result.Observations[0].Quantity);
        Assert.Equal(0, fallback.Apply(frame, normal, Start.AddMilliseconds(900), CancellationToken.None).Diagnostics.QuantitiesRecovered);
    }

    [Fact]
    public void MissingWindowPreservesNormalObservationsWithoutOcr()
    {
        var reads = 0;
        var fallback = Create(() => null, (_, _) => { reads++; return []; });
        using var frame = Frame();
        var normal = new[] { Observation(null), Observation(6) with { Slot = 1, NativeY = 200 } };

        var result = fallback.Apply(frame, normal, Start, CancellationToken.None);

        Assert.Same(normal, result.Observations);
        Assert.Null(result.Region);
        Assert.Equal("no-private-item-window", result.Diagnostics.State);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void ReadsOnlyCalibratedCropAndRefreshesConfigurationEveryTwoSeconds()
    {
        var locations = 0;
        var reads = 0;
        var fallback = Create(() => { locations++; return Window; }, (crop, _) =>
        {
            reads++;
            Assert.Equal(40, crop.Width);
            Assert.Equal(30, crop.Height);
            Assert.Equal(new Vec3b(20, 30, 40), crop.At<Vec3b>(0, 0));
            return [];
        });
        using var frame = Frame();

        foreach (var milliseconds in new[] { 0, 500, 1999, 2000, 3999 })
            fallback.Apply(frame, [], Start.AddMilliseconds(milliseconds), CancellationToken.None);

        Assert.Equal(2, locations);
        Assert.Equal(5, reads);
        fallback.Apply(frame, [], Start.AddSeconds(4), CancellationToken.None);
        Assert.Equal(3, locations);
        fallback.Reset();
        fallback.Apply(frame, [], Start.AddMilliseconds(4100), CancellationToken.None);
        Assert.Equal(4, locations);
    }

    [Fact]
    public void InvalidChatBoundsSkipOcrAndKeepBaseline()
    {
        using var frame = Frame();
        var reads = 0;
        var normal = new[] { Observation(null) };
        foreach (var region in new[]
                 {
                     Rectangle.Empty, new Rectangle(-1, 0, 10, 10), new Rectangle(0, -1, 10, 10),
                     new Rectangle(70, 0, 20, 20), new Rectangle(0, 50, 20, 20),
                 })
        {
            var fallback = Create(() => Window with { Bounds = region }, (_, _) => { reads++; return []; });
            var result = fallback.Apply(frame, normal, Start, CancellationToken.None);
            Assert.Same(normal, result.Observations);
            Assert.Equal("invalid-chat-region", result.Diagnostics.State);
            Assert.Null(result.Region);
        }

        Assert.Equal(0, reads);
    }

    [Fact]
    public void OptionalReadOrConfigurationFailurePreservesBaselineAndReportsFailure()
    {
        using var frame = Frame();
        var normal = new[] { Observation(null), Observation(4) with { Slot = 1, NativeY = 200 } };
        foreach (var fallback in new[]
                 {
                     Create(() => throw new IOException("settings unavailable"), (_, _) => []),
                     Create(() => Window, (_, _) => throw new InvalidOperationException("OCR unavailable")),
                 })
        {
            var result = fallback.Apply(frame, normal, Start, CancellationToken.None);
            Assert.Same(normal, result.Observations);
            Assert.Equal("chat-read-failed", result.Diagnostics.State);
            Assert.Equal(1, result.Diagnostics.Errors);
            Assert.Equal(0, result.Diagnostics.QuantitiesRecovered);
        }
    }

    [Fact]
    public void CancellationBeforeOrDuringOcrPropagates()
    {
        using var frame = Frame();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var locations = 0;
        var fallback = Create(() => { locations++; return Window; }, (_, _) => []);
        Assert.Throws<OperationCanceledException>(() =>
            fallback.Apply(frame, [], Start, cancelled.Token));
        Assert.Equal(0, locations);

        fallback = Create(() => Window, (_, _) => throw new OperationCanceledException());
        Assert.Throws<OperationCanceledException>(() =>
            fallback.Apply(frame, [], Start, CancellationToken.None));
    }

    [Fact]
    public void EmptyChatReadDoesNotChangeExistingQuantitiesOrInventObservations()
    {
        using var frame = Frame();
        var fallback = Create(() => Window, (_, _) => []);
        var normal = new[]
        {
            Observation(1), Observation(6) with { Slot = 1, NativeY = 200 },
            Observation(null) with { Slot = 2, NativeY = 150 },
        };

        var result = fallback.Apply(frame, normal, Start, CancellationToken.None);

        Assert.Equal(normal, result.Observations);
        Assert.Equal(Window.Bounds, result.Region);
        Assert.Equal("no-readable-item-lines", result.Diagnostics.State);
        Assert.Equal(0, result.Diagnostics.QuantitiesRecovered);
    }

    [Fact]
    public void FreshScrolledMessageFillsMissingQuantityAndPreservesKnownQuantity()
    {
        using var frame = Frame();
        IReadOnlyList<PrivateItemChatLine> lines = [Chat("Black Crystal Fragment", 1, 20)];
        var fallback = Create(() => Window, (_, _) => lines);
        fallback.Apply(frame, [Observation(1)], Start, CancellationToken.None);
        lines = [Chat("Black Crystal Fragment", 1, 5), Chat("Elion Follower's Helmet", 6, 20)];
        var normal = NewlyScrolledPanel();

        var result = fallback.Apply(frame, normal, Start.AddMilliseconds(450), CancellationToken.None);

        Assert.Equal(normal[0] with { Quantity = 6 }, Assert.Single(result.Observations, row => row.Slot == 0));
        Assert.Equal(normal[1], Assert.Single(result.Observations, row => row.Slot == 1));
        Assert.Equal(1, result.Diagnostics.QuantitiesRecovered);
        Assert.Equal(new ChatQuantityCorrection(0, 250, "Elion Follower's Helmet", 6),
            Assert.Single(result.Diagnostics.Corrections));
        var unchanged = fallback.Apply(frame, normal, Start.AddMilliseconds(900), CancellationToken.None);
        Assert.Equal(normal, unchanged.Observations); // No reuse on another capture of the same row.
        var known = new[] { normal[0] with { Quantity = 1 }, normal[1] };
        var repeated = fallback.Apply(frame, known, Start.AddMilliseconds(1350), CancellationToken.None);
        Assert.Equal(known, repeated.Observations);
        Assert.Equal(0, repeated.Diagnostics.QuantitiesRecovered);
    }

    [Fact]
    public void ChangingChatWindowMakesExistingMessagesHistoryAgain()
    {
        using var frame = Frame();
        var window = Window;
        IReadOnlyList<PrivateItemChatLine> lines = [Chat("Black Crystal Fragment", 1, 20)];
        var fallback = Create(() => window, (_, _) => lines);
        fallback.Apply(frame, [Observation(1)], Start, CancellationToken.None);
        lines = [Chat("Black Crystal Fragment", 1, 5), Chat("Elion Follower's Helmet", 6, 20)];
        var normal = NewlyScrolledPanel();
        window = Window with { WindowIndex = 4, Bounds = new Rectangle(15, 10, 40, 30) };
        var result = fallback.Apply(frame, normal, Start.AddSeconds(2), CancellationToken.None);

        Assert.Null(Assert.Single(result.Observations, row => row.Slot == 0).Quantity);
        Assert.Equal(4, result.Diagnostics.WindowIndex);
        Assert.Equal(0, result.Diagnostics.QuantitiesRecovered);
    }

    [Fact]
    public void FreshnessMustBeReestablishedAfterEmptyOrFailedOcr()
    {
        using var frame = Frame();
        foreach (var failWithException in new[] { false, true })
        {
            var fail = false;
            IReadOnlyList<PrivateItemChatLine> lines = [Chat("Black Crystal Fragment", 1, 20)];
            var fallback = Create(() => Window, (_, _) => fail
                ? failWithException ? throw new IOException("transient OCR failure") : []
                : lines);
            fallback.Apply(frame, [Observation(1)], Start, CancellationToken.None);
            fail = true;
            fallback.Apply(frame, [Observation(1)], Start.AddMilliseconds(450), CancellationToken.None);
            fail = false;
            lines = [Chat("Black Crystal Fragment", 1, 5), Chat("Elion Follower's Helmet", 6, 20)];
            var normal = NewlyScrolledPanel();

            var result = fallback.Apply(frame, normal, Start.AddMilliseconds(900), CancellationToken.None);

            Assert.Null(Assert.Single(result.Observations, row => row.Slot == 0).Quantity);
            Assert.Equal(0, result.Diagnostics.QuantitiesRecovered);
        }
    }

    [Fact]
    public void BottomLineRediscoveredAfterPartialOcrCannotBecomeANewDrop()
    {
        using var frame = Frame();
        IReadOnlyList<PrivateItemChatLine> lines =
            [Chat("Black Crystal Fragment", 1, 5), Chat("Elion Follower's Helmet", 6, 20)];
        var fallback = Create(() => Window, (_, _) => lines);
        fallback.Apply(frame, [Observation(1)], Start, CancellationToken.None);
        lines = [Chat("Black Crystal Fragment", 1, 5)];
        fallback.Apply(frame, [Observation(1)], Start.AddMilliseconds(450), CancellationToken.None);
        lines = [Chat("Black Crystal Fragment", 1, 5), Chat("Elion Follower's Helmet", 6, 20)];
        var normal = NewlyScrolledPanel();

        var result = fallback.Apply(frame, normal, Start.AddMilliseconds(900), CancellationToken.None);

        Assert.Null(Assert.Single(result.Observations, row => row.Slot == 0).Quantity);
        Assert.Equal(0, result.Diagnostics.QuantitiesRecovered);
    }

    [Fact]
    public async Task AnalyzerPassesChatQuantityToExistingCounterAndResetsChatOnCompletionAndReset()
    {
        var chat = new FillQuantityFallback();
        using var analyzer = Analyzer(-1, chat);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);

        var before = Assert.Single(chat.Input!);
        Assert.Null(before.Quantity);
        Assert.Equal(before with { Quantity = 6 }, Assert.Single(result.Observations));
        Assert.Equal(Window.Bounds, result.ChatPanelRegion);
        Assert.Equal(1, result.ChatRecovery!.QuantitiesRecovered);
        var counted = Assert.Single(analyzer.CompleteSession(Start).NewEvents);
        Assert.Equal("Black Crystal Fragment", counted.ItemName);
        Assert.Equal(6, counted.Quantity);
        Assert.Equal(1, chat.Resets);
        analyzer.Reset();
        Assert.Equal(2, chat.Resets);
    }

    [Fact]
    public async Task AnalyzerKeepsNormalCounterWorkingWhenOptionalChatOcrFails()
    {
        var chat = Create(() => Window, (_, _) => throw new InvalidOperationException("OCR unavailable"));
        using var analyzer = Analyzer(17, chat);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);

        Assert.Equal(17, Assert.Single(result.Observations).Quantity);
        Assert.Equal(1, result.ChatRecovery!.Errors);
        Assert.Equal(17, Assert.Single(analyzer.CompleteSession(Start).NewEvents).Quantity);
    }

    private static PrivateItemChatFallback Create(Func<PrivateItemChatCalibration?> locate,
        Func<Mat, CancellationToken, IReadOnlyList<PrivateItemChatLine>> read) =>
        new(new CompanionItemMatcher(["Black Crystal Fragment", "Elion Follower's Helmet"]), locate, read);

    private static Mat Frame() => new(60, 80, MatType.CV_8UC3, new Scalar(20, 30, 40));

    private static LootObservation Observation(int? quantity) =>
        new(LootSource.Normal, 0, "Black Crystal Fragment", "Black Crystal Fragment", quantity,
            1, 0, null, null) { NativeY = 250 };

    private static LootObservation[] NewlyScrolledPanel() =>
    [
        Observation(null) with { ItemName = "Elion Follower's Helmet" },
        Observation(1) with { Slot = 1, NativeY = 200 },
    ];

    private static PrivateItemChatLine Chat(string item, int quantity, int y) =>
        new(item, quantity, y, $"System You have obtained [{item}] x{quantity}. (12:34)")
        {
            Bounds = new Rect(0, y, 40, 8),
        };

    private static CompanionLootFrameAnalyzer Analyzer(int quantity, IPrivateItemChatFallback chat) =>
        new(new CompanionCalibration("profile", "gamevariable.xml", "GameOption.txt", 400, 300,
                800, 600, 1f, CompanionFontType.StrongSword, 0, false),
            new CompanionItemMatcher(["Black Crystal Fragment"]), new Rows(quantity), new Names(),
            chatFallback: chat);

    private sealed class FillQuantityFallback : IPrivateItemChatFallback
    {
        public IReadOnlyList<LootObservation>? Input { get; private set; }
        public int Resets { get; private set; }

        public ChatQuantityRecoveryResult Apply(Mat frame, IReadOnlyList<LootObservation> normal,
            DateTimeOffset capturedAt, CancellationToken cancellationToken)
        {
            Input = normal.ToArray();
            return new(normal.Select(row => row with { Quantity = 6 }).ToArray(), Window.Bounds,
                new(Window.WindowIndex, "reading-private-items", 1, 1, 0));
        }

        public void Reset() => Resets++;
    }

    private sealed class Rows(int quantity) : ICompanionNormalRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) =>
            new Row(y, quantity);
        public void Dispose() { }
    }

    private sealed class Row(int y, int quantity) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => y != 250;
        public int RecognizedTextWidth => 100;
        public int TemplateQuantity => quantity;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 0;
        public float NameScale => 1;
        public Mat? NameImage { get; } = y == 250 ? new Mat(1, 1, MatType.CV_8UC1) : null;
        public void Dispose() => NameImage?.Dispose();
    }

    private sealed class Names : ICompanionNameRecognizer
    {
        public string BackendName => "test-ocr";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) =>
            new("Black Crystal Fragment", new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
    }
}
