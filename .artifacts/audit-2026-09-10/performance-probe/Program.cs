using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing.Text;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

if (args.Contains("bench")) {
    Console.WriteLine("Synthetic, local CPU microbenchmarks; no live capture, UI presentation or game running required. Release build, 5 warmups + 100 samples. No isolated-machine control.");
    foreach (var size in new [] {new System.Drawing.Size(1920, 1080), new System.Drawing.Size(3840, 2160)}) {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        for (var i=0;i<5;i++) {using var ignored = CompanionFrameDecoder.Decode(bitmap);}
        var samples = new List<double>();
        for (var i=0;i<100;i++) {var timer=Stopwatch.StartNew(); using var decoded = CompanionFrameDecoder.Decode(bitmap); samples.Add(timer.Elapsed.TotalMilliseconds);}
        samples.Sort(); Console.WriteLine($"Full frame BGRA-to-BGR decode {size.Width}x{size.Height}: meanMs={samples.Average():F3}, p95Ms={samples[94]:F3}, bufferMiB={size.Width*(double)size.Height*3/1048576:F2}");
    }
    using var renderer = new BdoGrindTracker.App.Overlay.Native.NativeOverlayRenderer();
    foreach (var preset in new [] {"compact", "dashboard", "loot"}) {
        var settings = BdoGrindTracker.App.Overlay.OverlayCatalog.Preset(preset);
        var size = new System.Drawing.Size((int)settings.Width, (int)settings.Height);
        for (var i=0;i<5;i++) {using var ignored=renderer.Render(size, settings, BdoGrindTracker.App.Overlay.OverlaySnapshot.Demo, out _);}
        var samples = new List<double>();
        for (var i=0;i<100;i++) {var timer=Stopwatch.StartNew(); using var bitmap=renderer.Render(size, settings, BdoGrindTracker.App.Overlay.OverlaySnapshot.Demo, out _); samples.Add(timer.Elapsed.TotalMilliseconds);}
        samples.Sort(); Console.WriteLine($"Offscreen native overlay preset {preset} {size.Width}x{size.Height}: meanMs={samples.Average():F3}, p95Ms={samples[94]:F3}");
    }
    return;
}
Console.WriteLine("OrtTensorTypeAndShapeInfo IDisposable=" + typeof(IDisposable).IsAssignableFrom(typeof(OrtTensorTypeAndShapeInfo)));
Console.WriteLine("OrtTensorTypeAndShapeInfo Base=" + typeof(OrtTensorTypeAndShapeInfo).BaseType);
var matcher = new CompanionItemMatcher(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "data", "items.en.txt")).Where(s=> !string.IsNullOrWhiteSpace(s) && !s.StartsWith('#')));
foreach (var text in new [] { "Bruchstück", "Bruchstüuk", "Tungrad-Ruinenfragment", "Black Stone", "Black Stane" }) {
    var ok = matcher.TryMatch(text, 12, false, out var match);
    Console.WriteLine($"Matcher {text}: accepted={ok}, canonical={match?.CanonicalName}, distance={match?.NormalizedDistance}");
}
using (var engine = PaddleLootOcrRecognizer.Create("de-DE")) {
    foreach (var text in new [] {"Bruchstück x 12", "Black Stone x 17", "Tungrad-Ruinenfragment x 12"}) {
        using var bitmap = new Bitmap(900, 100, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White); graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", 36, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString(text, font, Brushes.Black, 18, 20); graphics.Flush();
        using var image = CompanionFrameDecoder.Decode(bitmap);
        var timer = Stopwatch.StartNew(); var result = engine.Recognize(image);
        Console.WriteLine($"Synthetic rendered OCR '{text}' => '{result.Text}' confidence={result.Confidence:F4}, ms={timer.Elapsed.TotalMilliseconds:F1}");
    }
}
var initialCalibration = new CompanionCalibration("profile", "gamevariable.xml", "GameOption.txt", 960, 540, 1920, 1080, 1f, CompanionFontType.StrongSword, 0, false, HasRareLootAnchor: true, RareLootAnchorX: 800, RareLootAnchorY: 300);
foreach (var variant in new [] {
    (Name: "rare-position-moved", Calibration: initialCalibration with { RareLootAnchorX = 1100 }),
    (Name: "rare-panel-hidden", Calibration: initialCalibration with { HasRareLootAnchor = false }),
    (Name: "normal-position-moved-control", Calibration: initialCalibration with { LootAnchorX = 1100 }) }) {
    var current = initialCalibration;
    var guard = new LootPanelCaptureGuard(initialCalibration, () => current);
    guard.Validate(new System.Drawing.Size(1920, 1080), DateTimeOffset.UnixEpoch);
    current = variant.Calibration;
    try { guard.Validate(new System.Drawing.Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2)); Console.WriteLine($"Calibration change {variant.Name}: accepted-without-error"); }
    catch (LootPanelUnavailableException) { Console.WriteLine($"Calibration change {variant.Name}: correctly-rejected"); }
}
var slow = new SlowRecognizer();
using (var review = new BackgroundLootRowReview(new CompanionItemMatcher(new [] {"Black Stone"}), _ => slow, workerCount: 1)) {
    using var source = new Mat(30, 400, MatType.CV_8UC3, Scalar.All(30)); source.Set(4, 5, new Vec3b(255, 255, 255));
    var baseline = new LootObservation(LootSource.Normal, 0, "Black Stone", "Black Stone", null, 1, 0, null, null) { NativeY = 0 };
    var input = new LootRowReviewInput(baseline, LootSource.Normal, 0, 0, -1, 0, _ => null, _ => true);
    var timer = Stopwatch.StartNew();
    var result = await review.ReviewAsync(source, input, CancellationToken.None);
    Console.WriteLine($"ReviewBudgetMs={BackgroundLootRowReview.RowBudget.TotalMilliseconds}, measuredMs={timer.Elapsed.TotalMilliseconds:F1}, engineCalls={slow.Calls}, tokenCanCancel={slow.TokenCanCancel}, outcome={result.Diagnostics?.Outcome}");
}
sealed class SlowRecognizer : ISecondaryLootOcrRecognizer {
    public int Calls; public bool TokenCanCancel;
    public string BackendName => "audit-cooperative-delay"; public string LanguageTag => "en-US";
    public SecondaryLootOcrResult Recognize(Mat image, CancellationToken token = default) {
        Calls++; TokenCanCancel = token.CanBeCanceled;
        if (token.WaitHandle.WaitOne(2600)) token.ThrowIfCancellationRequested();
        return new("Black Stone x 17", .99f, new(CompanionOcrGeometryStatus.Missing, 0, 0, 0, 0));
    }
    public void Dispose() { }
}



