using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Uses the five-slot finite-lifetime estimator; legacy counters remain replayable.</summary>
internal sealed class LifetimeNormalReconciliationAdapter : ICompanionReconciliation
{
    private readonly LifetimeLootReconciler _reconciler;
    private readonly LifetimeLootTextParser? _textParser;
    private readonly LifetimeParsingContext? _initialContext;
    private DateTimeOffset _lastAt;
    private long _captureIndex;
    public string AlgorithmName => _reconciler.UsesVisualSlotCoverage ? LifetimeLootReconciler.VisualSlotAlgorithmName :
        _textParser is null ? LifetimeLootReconciler.AlgorithmName : LifetimeLootReconciler.RawTextAlgorithmName;
    public bool UsesRawText => _textParser is not null;
    public LifetimeParsingContext? ParsingContext => _textParser?.Context;
    public bool TracksRows => true;
    public long? CaptureIndex => _captureIndex;
    public LifetimeSnapshot? Projection { get; private set; }
    public IReadOnlyList<NormalLootReconciliationTrace> LastTrace { get; private set; } = [];

    public LifetimeNormalReconciliationAdapter(LifetimeParsingContext? context = null) : this(context, false) { }

    public LifetimeNormalReconciliationAdapter(LifetimeParsingContext? context, bool useVisualSlotCoverage)
    {
        _initialContext = context;
        if (context is not null) _textParser = new(context);
        _reconciler = new(_textParser is null ? null : _textParser.Parse,
            _textParser is null ? null : _textParser.GetAliases, useVisualSlotCoverage: useVisualSlotCoverage);
    }

    public void UpdateParsingContext(LifetimeParsingContext context)
    {
        if (_textParser is null) throw new InvalidOperationException("Raw parsing is not enabled for this counter.");
        if (_textParser.Context.Revision == context.Revision && _textParser.Context.HasSameCatalog(context)) return;
        _textParser.UpdateContext(context);
    }

    public IReadOnlyList<CompanionRecognizedEntry> ProcessObservations(IReadOnlyList<LootObservation> observations,
        DateTimeOffset capturedAt)
    {
        Projection = _reconciler.ProcessObservations(observations, capturedAt);
        _captureIndex++;
        if (capturedAt > _lastAt) _lastAt = capturedAt;
        RecordTrace();
        return [];
    }

    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries) =>
        throw new InvalidOperationException("Lifetime reconciliation requires a capture timestamp.");

    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries,
        DateTimeOffset capturedAt)
    {
        Projection = _reconciler.ProcessFrame(entries, capturedAt);
        _captureIndex++;
        if (capturedAt > _lastAt) _lastAt = capturedAt;
        RecordTrace();
        // These totals can both increase and decrease. They are published as one
        // atomic projection after normal/rare fusion, never irreversible drop IDs.
        return [];
    }

    public IReadOnlyList<CompanionRecognizedEntry> Complete()
    {
        if (Projection is null) { LastTrace = []; return []; }
        Projection = _reconciler.Complete(_lastAt);
        RecordTrace();
        return [];
    }

    private void RecordTrace()
    {
        var estimate = Projection!;
        LastTrace = [new(_captureIndex, _lastAt, _captureIndex > 1 ? _captureIndex - 1 : null, 0, 0,
            estimate.Lanes.Select(lane => new NormalLootOverlapAttempt(0, true,
                $"lifetime-model:{lane.LifetimeMs}ms;beam:{lane.Hypotheses};selected:{lane.LifetimeMs == estimate.SelectedLifetimeMs}" +
                    (_reconciler.UsesVisualSlotCoverage ? $";coverage-fallbacks:{estimate.VisualCoverageFallbackCount}" : string.Empty),
                null, null)).ToArray(), [])];
    }

    public void Reset()
    {
        _reconciler.Reset();
        if (_initialContext is not null) _textParser!.UpdateContext(_initialContext);
        _lastAt = default;
        _captureIndex = 0;
        Projection = null;
        LastTrace = [];
    }
}
