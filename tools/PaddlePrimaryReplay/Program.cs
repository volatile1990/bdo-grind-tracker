using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace PaddlePrimaryReplay;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = LootDiagnosticFormat.JsonOptions;
    private static readonly JsonSerializerOptions Pretty = new(Json) { WriteIndented = true };

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine(ReplayOptions.Usage);
            return args.Length == 0 ? 2 : 0;
        }
        try { return await Run(ReplayOptions.Parse(args)).ConfigureAwait(false); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task<int> Run(ReplayOptions options)
    {
        Directory.CreateDirectory(options.Output);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        var wall = Stopwatch.StartNew();
        Console.WriteLine($"{options.Mode}: hashing source inputs and validating crop geometry...");
        var input = new ReplayInput(options);
        var geometry = new ReplayGeometry(input);
        var catalog = FrameAnalyzerFactory.LoadCatalog(Path.Combine(options.Data, "items.en.txt"),
            Path.Combine(options.Data, "icons", "catalog.json"));
        if (catalog.Length == 0) throw new InvalidDataException("The current production item catalog is empty.");
        var modelDirectory = Path.Combine(options.Data, "ocr", "paddle-v6-small");
        var currentAssembly = typeof(CompanionLootFrameAnalyzer).Assembly;
        var manifest = new
        {
            options, sourceEngine = input.Header.EngineVersion, sourceAppVersion = input.Header.AppVersion,
            sourceStartedAt = input.Header.StartedAt, sourceTimestampPolicy = "Original per-frame UTC timestamps are used verbatim, never replay wall-clock time.",
            engine = LootDiagnosticFormat.EngineVersion, application = currentAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assemblySha256 = ReplayInput.Hash(File.ReadAllBytes(currentAssembly.Location)),
            modelSha256 = ReplayInput.Hash(File.ReadAllBytes(Path.Combine(modelDirectory, "inference.onnx"))),
            geometry, totalSourceFrames = input.Entries.Count(entry => entry.Kind == "frame"), selectedFrames = input.FrameCount,
            partial = input.IsPartial, sourceFiles = input.Files.OrderBy(file => file.Name).ToArray(),
            cropReferences = "PNG names in the output journal refer to the original recording directory in options.recording; no PNG is copied or changed.",
        };
        WriteFile("manifest.json", manifest);
        var matcher = new CompanionItemMatcher(catalog);
        var nameRecognizer = new CountingNameRecognizer(new CompanionNameRecognizer(
            CompanionWindowsOcrRecognizer.TryCreate(FrameAnalyzerFactory.GetOcrLanguageTag(options.Language),
                throwIfUnavailable: true, requirePreferredLanguage: true)!));
        using var templates = new CompanionDigitTemplateLoader().LoadNormalQuantity(
            CompanionDigitCatalog.CreateSources(), (CompanionUiFontType)(byte)options.Font, options.UiScale);
        if (templates.Templates.Count != 10) throw new InvalidDataException("Incomplete production digit templates.");
        using var analyzer = new CompanionLootFrameAnalyzer(geometry.Calibration, matcher,
            new CompanionNormalRowPipeline(new CompanionQuantityRecognizer(templates.Templates)), nameRecognizer,
            reconciliation: new CompanionReconciliationAdapter(TrashLootMinimumCatalog.MinimumQuantities, trackRows: true),
            normalRecovery: new NormalLootRecovery(matcher, nameRecognizer),
            rowReview: new BackgroundLootRowReview(matcher, tag => PaddleLootOcrRecognizer.Create(tag, modelDirectory)),
            primaryRowReader: options.Mode == "paddle"
                ? new PaddlePrimaryLootReader(matcher, tag => PaddleLootOcrRecognizer.Create(tag, modelDirectory)) : null);
        analyzer.ConfigureGameLanguage(options.Language);
        using var journal = NewWriter("observations.jsonl");
        using var measurements = NewWriter("frames.jsonl");
        var freshHeader = new LootDiagnosticHeader("header", LootDiagnosticFormat.Version, LootDiagnosticFormat.EngineVersion,
            input.Header.StartedAt, null, "Fresh PNG OCR through the current production analyzer; subsequent counter-only replay may reuse these new observations.")
        {
            Catalog = catalog, MinimumTrashQuantities = TrashLootMinimumCatalog.MinimumQuantities,
            AppVersion = currentAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            ReconciliationTraceVersion = 1,
        };
        journal.WriteLine(JsonSerializer.Serialize(freshHeader, Json));
        var stats = new ReplayStatistics();
        var sourceTotals = new Dictionary<string, long>(StringComparer.Ordinal);
        var allSourceTotals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in input.Entries) ReplayStatistics.Add(allSourceTotals, entry.Events);
        foreach (var item in allSourceTotals.Keys) stats.Totals.TryAdd(item, 0);
        var links = new Dictionary<long, (int Sequence, string? Crop)>();
        var lastTimestamp = input.Header.StartedAt;
        var completedLast = false;
        var outputSequence = 0;
        var process = Process.GetCurrentProcess();
        var initialCpu = process.TotalProcessorTime;
        var processed = 0;
        string? errorText = null;
        var originalUnchanged = false;
        try
        {
            foreach (var source in input.Selected)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                lastTimestamp = source.Timestamp;
                var timer = Stopwatch.StartNew();
                var beforeWindows = nameRecognizer.Calls;
                FrameAnalysisResult result;
                if (source.Kind == "frame")
                {
                    using var frame = geometry.ReconstructVerifiedFrame(input, source);
                    var preparationMilliseconds = timer.Elapsed.TotalMilliseconds;
                    result = await analyzer.AnalyzeAsync(frame, source.Timestamp, source.IsHdr!.Value,
                        source.IsToneMapped!.Value, cancellation.Token).ConfigureAwait(false);
                    stats.AddFrameTiming(preparationMilliseconds, timer.Elapsed.TotalMilliseconds - preparationMilliseconds);
                    processed++;
                    completedLast = false;
                }
                else { result = analyzer.CompleteSession(source.Timestamp); completedLast = true; }
                ReplayStatistics.Add(sourceTotals, source.Events);
                WriteResult(source, result, timer.Elapsed.TotalMilliseconds, nameRecognizer.Calls - beforeWindows);
                if (source.Kind == "frame" && (processed % 100 == 0 || processed == input.FrameCount))
                {
                    journal.Flush(); measurements.Flush();
                    Console.WriteLine($"{options.Mode}: {processed}/{input.FrameCount} frames; {wall.Elapsed.TotalSeconds:F1}s; " +
                        $"Helmet={stats.Totals.GetValueOrDefault("Elion Follower's Helmet")}; Black Stone={stats.Totals.GetValueOrDefault("Black Stone")}; " +
                        $"Dust={stats.Totals.GetValueOrDefault("Ancient Spirit Dust")}; Windows OCR={nameRecognizer.Calls}");
                }
            }
            if (!completedLast)
                WriteResult(new LootDiagnosticEntry("complete", input.Selected[^1].Sequence + 1, lastTimestamp, [], [], [], []),
                    analyzer.CompleteSession(lastTimestamp), 0, 0);
            journal.Flush(); measurements.Flush();
            Console.WriteLine($"{options.Mode}: verifying all original SHA-256 hashes...");
            input.VerifyOriginalFilesUnchanged();
            originalUnchanged = true;
        }
        catch (Exception error)
        {
            errorText = error.ToString();
            journal.Flush(); measurements.Flush();
            throw;
        }
        finally
        {
            process.Refresh();
            var comparisons = allSourceTotals.Keys.Concat(stats.Totals.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(item => new { item, fresh = stats.Totals.GetValueOrDefault(item), sourceSelected = sourceTotals.GetValueOrDefault(item),
                    sourceFull = allSourceTotals.GetValueOrDefault(item), differenceFromSelected = stats.Totals.GetValueOrDefault(item) - sourceTotals.GetValueOrDefault(item) }).ToArray();
            WriteFile("summary.json", new
            {
                status = errorText is null ? "complete" : "failed", error = errorText, mode = options.Mode,
                sourceRecording = options.Recording, sourceJsonSha256 = input.Files.Single(file => file.Name == LootDiagnosticFormat.RecordingFileName).Sha256,
                originalFilesUnchanged = originalUnchanged, verifiedInputFiles = originalUnchanged ? input.Files.Count : 0,
                sourceFirstTimestamp = input.Selected.First(entry => entry.Kind == "frame").Timestamp,
                sourceLastTimestamp = lastTimestamp, processedFrames = processed, selectedFrames = input.FrameCount, partial = input.IsPartial,
                stats.Totals, comparisons, stats.AcceptedObservationsBySource, stats.EventCountBySource, stats.QuantityBySource,
                stats.RowReviewOutcomes, stats.RowReviewReadings, stats.ObservedOcrErrors, stats.OcrCallsReportedByAnalyzer,
                windowsOcrCalls = nameRecognizer.Calls, stats.ChangedObservationFrames, stats.ChangedEventFrames,
                timing = new { wallSeconds = wall.Elapsed.TotalSeconds, cpuSeconds = (process.TotalProcessorTime - initialCpu).TotalSeconds,
                    framePreparationMilliseconds = stats.PreparationMilliseconds, frameAnalysisMilliseconds = stats.AnalysisMilliseconds,
                    averageAnalysisMilliseconds = processed == 0 ? 0 : stats.AnalysisMilliseconds / processed,
                    peakWorkingSetBytes = process.PeakWorkingSet64,
                    caveat = "Sequential pipeline replay, no artificial capture pacing. Concurrent runs contend for CPU; this is not an isolated CPU benchmark." },
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { mode = options.Mode, frames = processed, stats.Totals, originalFilesUnchanged = true }, Json));
        return 0;

        void WriteResult(LootDiagnosticEntry source, FrameAnalysisResult result, double milliseconds, long windowsCalls)
        {
            outputSequence++;
            if (source.Kind == "frame" && result.TrackingResult.NormalCaptureIndex is { } capture)
                links[capture] = (outputSequence, source.Crops.FirstOrDefault(crop => crop.Source == "normal")?.FileName);
            var traces = result.TrackingResult.NormalReconciliation.Select(trace =>
            {
                var current = links.GetValueOrDefault(trace.CaptureIndex);
                var previous = trace.PreviousCaptureIndex is { } before ? links.GetValueOrDefault(before) : default;
                return new RecordedNormalReconciliation(trace, current.Sequence == 0 ? null : current.Sequence, current.Crop,
                    previous.Sequence == 0 ? null : previous.Sequence, previous.Crop);
            }).ToArray();
            var fresh = new LootDiagnosticEntry(source.Kind, outputSequence, source.Timestamp, result.Observations,
                result.TrackingResult.NewEvents, result.TrackingResult.Decisions, source.Crops)
            {
                RareEnabled = source.RareEnabled, IsHdr = source.IsHdr, IsToneMapped = source.IsToneMapped,
                RecognitionVariant = result.VariantName, Recovery = result.Recovery, RowReviews = result.RowReviews,
                NormalCaptureIndex = result.TrackingResult.NormalCaptureIndex, NormalReconciliation = traces,
            };
            journal.WriteLine(JsonSerializer.Serialize(fresh, Json));
            stats.Observe(source, fresh, result);
            measurements.WriteLine(JsonSerializer.Serialize(new
            {
                kind = source.Kind, sequence = outputSequence, sourceSequence = source.Sequence, sourceTimestamp = source.Timestamp,
                milliseconds, windowsCalls, result.OcrRowCount, result.PreparedRowCount, result.NonBlankRowCount,
                result.SpotId, result.TextRecognitionBackend, result.TextRecognitionLanguage,
                recordedObservations = source.Observations, freshObservations = result.Observations,
                recordedEvents = source.Events, freshEvents = result.TrackingResult.NewEvents,
                primaryDiagnostics = result.RowReviews.Where(review => review.Reason == "primary-ocr").ToArray(),
            }, Json));
        }
        StreamWriter NewWriter(string name) => new(new FileStream(Path.Combine(options.Output, name), FileMode.CreateNew,
            FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        void WriteFile(string name, object value) => File.WriteAllText(Path.Combine(options.Output, name), JsonSerializer.Serialize(value, Pretty));
    }

    private sealed class CountingNameRecognizer(ICompanionNameRecognizer inner) : ICompanionNameRecognizer
    {
        public long Calls { get; private set; }
        public string BackendName => inner.BackendName;
        public string? LanguageTag => inner.LanguageTag;
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.Recognize(image, cancellationToken);
        }
    }
}
