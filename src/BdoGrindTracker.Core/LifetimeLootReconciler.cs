using System.Buffers.Binary;
using System.Collections.Frozen;

namespace BdoGrindTracker.Core;

/// <summary>
/// Finite-lifetime estimator. Each duration lane retains independent
/// birth/scroll alternatives and observation statistics. Public totals are a
/// reversible projection; displaying an estimate never changes the hypothesis set.
/// Birth identities describe the current best explanation, not permanent bookings.
/// </summary>
public sealed class LifetimeLootReconciler
{
    public const string AlgorithmName = "lifetime-v1";
    public const string RawTextAlgorithmName = "lifetime-v2";
    public const string VisualSlotAlgorithmName = "lifetime-v3";
    public const string UnreadableVisualSlotAlgorithmName = "lifetime-v4";
    public const string FadeAwareAlgorithmName = "lifetime-v5";
    public const int SlotCount = 5;
    private const int BeamCapacity = 24;
    private const int AgeBins = 8;
    private const int Categories = 9;
    private const double BirthStepMs = 100;
    private const double RetirementHorizonMs = 3000;
    private const int PolicyHistoryCapacity = 64;
    private Model[] models;
    private readonly LootSource source;
    private readonly int trackedSlotCount;
    private readonly bool persistentSingleRow;
    private readonly bool useLegacySingleRowLearning;
    private long? singleRowMissingSince;
    private long? previousMilliseconds;
    private long? lastCaptureMilliseconds;
    private long frameIndex;
    private long nextFit = 25;
    private long projectionRevision;
    private bool started;
    private IReadOnlyDictionary<string, long> previousTotals = FrozenDictionary<string, long>.Empty;
    private LifetimeSnapshot? previousSnapshot;
    private readonly Func<LootObservation, LifetimeParsedReading?>? rawParser;
    private readonly Func<string, IReadOnlyList<string>>? nameAliases;
    private readonly HashSet<string> knownNames = new(StringComparer.Ordinal);
    private readonly Dictionary<LootObservation, LifetimeParsedReading?> interpretations = [];
    private (string Name, int Quantity)?[]? previousVisualSlots;
    private (string Name, int Quantity)?[]? earlierVisualSlots;
    private long? previousVisualMilliseconds;
    private int previousVisualOccupiedSlots;
    private bool hasObservedFading;

    public LifetimeLootReconciler(Func<LootObservation, LifetimeParsedReading?>? rawParser = null,
        Func<string, IReadOnlyList<string>>? nameAliases = null)
        : this(rawParser, nameAliases, false)
    {
    }

    public LifetimeLootReconciler(Func<LootObservation, LifetimeParsedReading?>? rawParser,
        Func<string, IReadOnlyList<string>>? nameAliases, bool useVisualSlotCoverage,
        LootSource source = LootSource.Normal, int slotCount = SlotCount, bool useUnreadableSlotCoverage = false,
        bool useFadeEvidence = false, bool useLegacySingleRowLearning = false)
    {
        if (source is not (LootSource.Normal or LootSource.Rare))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (slotCount is not (1 or SlotCount)) throw new ArgumentOutOfRangeException(nameof(slotCount));
        if (useVisualSlotCoverage && rawParser is null)
            throw new ArgumentException("Visual slot coverage requires a raw-text parser.", nameof(rawParser));
        if (useUnreadableSlotCoverage && (!useVisualSlotCoverage || source != LootSource.Normal || slotCount != SlotCount))
            throw new ArgumentException("Unreadable slot coverage requires the normal five-slot visual counter.", nameof(useUnreadableSlotCoverage));
        if (useFadeEvidence && !useUnreadableSlotCoverage)
            throw new ArgumentException("Fade evidence requires the unreadable normal five-slot visual counter.", nameof(useFadeEvidence));
        this.rawParser = rawParser;
        this.nameAliases = nameAliases;
        this.source = source;
        trackedSlotCount = slotCount;
        persistentSingleRow = slotCount == 1;
        this.useLegacySingleRowLearning = useLegacySingleRowLearning;
        models = CreateModels();
        UsesVisualSlotCoverage = useVisualSlotCoverage;
        UsesUnreadableSlotCoverage = useUnreadableSlotCoverage;
        UsesFadeEvidence = useFadeEvidence;
    }

    public bool UsesRawText => rawParser is not null;
    public bool UsesVisualSlotCoverage { get; }
    public bool UsesUnreadableSlotCoverage { get; }
    public bool UsesFadeEvidence { get; }
    public LootSource Source => source;
    public int TrackedSlotCount => trackedSlotCount;
    public int VisualCoverageFallbackCount { get; private set; }

    public LifetimeSnapshot ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> acceptedRows, DateTimeOffset capturedAt)
    {
        // Normalize all inputs before touching frame/model state, even for a stale
        // delivery. Companion may expose a sixth crop; this five-slot model ignores
        // that oldest crop while still validating its metadata.
        var observations = Normalize(acceptedRows);
        return ProcessNormalized(observations, capturedAt);
    }

    /// <summary>
    /// Keeps unaccepted text only when it resembles an item already read in this
    /// session. Such partial text supports row association, never a quantity vote.
    /// The parser is called again for retained readings using its current context.
    /// </summary>
    public LifetimeSnapshot ProcessObservations(IReadOnlyList<LootObservation> rows, DateTimeOffset capturedAt)
    {
        if (!UsesRawText) throw new InvalidOperationException("Raw observations require the lifetime-v2 parser.");
        ValidateRawRows(rows, UsesVisualSlotCoverage);
        var now = capturedAt.ToUnixTimeMilliseconds();
        // Stale delivery must not teach the known-name context or call a parser
        // whose result could change history as a side effect.
        if (lastCaptureMilliseconds is { } last && now <= last)
            return previousSnapshot! with { Deltas = FrozenDictionary<string, long>.Empty };
        interpretations.Clear();
        var observations = new Observation?[trackedSlotCount];
        var nextKnown = new HashSet<string>(knownNames, StringComparer.Ordinal);
        foreach (var source in rows)
        {
            if (source.Source != this.source || source.IsAlignmentAnchor || source.Slot >= trackedSlotCount) continue;
            if (this.source == LootSource.Rare && source.RejectionReason == LootObservation.RarePaddleUnconfirmedReason)
                continue;
            var parsed = Interpret(source);
            if (parsed?.IsExcluded == true) continue;
            var name = parsed?.Name;
            if (name is not null) nextKnown.Add(name);
            observations[source.Slot] = new(name, parsed?.Quantity, parsed?.Confidence ?? source.NameConfidence,
                source, LifetimeTextSimilarity.Normalize(source.RawText));
        }
        // A complete reading in another slot of this very frame is sufficient
        // context, independent of the order in which OCR happened to return rows.
        for (var slot = 0; slot < observations.Length; slot++)
        {
            if (observations[slot] is not { Name: null } observation) continue;
            var partial = LifetimeTextSimilarity.NormalizeNameFragment(observation.Source!.RawText);
            var candidates = partial.Length < 4 ? [] : nextKnown
                .Where(name => LifetimeTextSimilarity.Normalize(name).Contains(partial, StringComparison.Ordinal) ||
                    nameAliases?.Invoke(name).Any(alias => LifetimeTextSimilarity.Normalize(alias)
                        .Contains(partial, StringComparison.Ordinal)) == true).ToArray();
            if (candidates.Length == 0) observations[slot] = null;
            else if (candidates.Length == 1) observations[slot] = observation with { Name = candidates[0] };
        }
        knownNames.UnionWith(nextKnown);
        return ProcessNormalized(observations, capturedAt, rows);
    }

    private LifetimeSnapshot ProcessNormalized(Observation?[] observations, DateTimeOffset capturedAt,
        IReadOnlyList<LootObservation>? rawRows = null)
    {
        var now = capturedAt.ToUnixTimeMilliseconds();
        if (lastCaptureMilliseconds is { } last && now <= last)
            return previousSnapshot! with { Deltas = FrozenDictionary<string, long>.Empty };
        lastCaptureMilliseconds = now;
        // Stronger physical coverage needs age measurements to disambiguate
        // later fading. During warm-up, or in an older stream without them,
        // retain the previous visual counter's constraints.
        if (previousVisualMilliseconds is { } previousCapture && now - previousCapture > 600)
            hasObservedFading = false;
        hasObservedFading |= UsesFadeEvidence && observations.Any(row => row?.Source?.FadeEvidence is
            { Correlation: >= .90, ContrastRatio: >= .12 and <= .85 });
        var unreadableCoverage = UsesUnreadableSlotCoverage
            ? UnreadableSlotCoverage(observations, rawRows, now) : null;
        if (UsesFadeEvidence && hasObservedFading && AdditionalPhysicalCoverage(observations, rawRows, now) is { } physical &&
            physical.Count > (unreadableCoverage?.Count ?? 0)) unreadableCoverage = physical;
        var minimumCoveredSlots = UsesVisualSlotCoverage ? ObserveVisualSlots(observations, now) : 0;
        minimumCoveredSlots = Math.Max(minimumCoveredSlots, unreadableCoverage?.Count ?? 0);
        if (UsesUnreadableSlotCoverage) previousVisualOccupiedSlots = OccupiedPrefix(observations, rawRows);
        // Empty menus before the first real reading are not a representative
        // training sample of the log's in-session detection probabilities.
        if (!started)
        {
            if (!observations.Any(row => row?.Quantity is not null)) return Project(capturedAt);
            started = true;
        }
        var elapsed = previousMilliseconds is { } prior ? now - prior : BirthStepMs;
        var maximumBirths = previousMilliseconds is null ? trackedSlotCount :
            Math.Max(1, (int)Math.Min(trackedSlotCount, Math.Ceiling(elapsed / BirthStepMs)));
        // A special notification can remain visible indefinitely. Only an
        // observed absence can close it; elapsed capture time alone is not a
        // second physical drop. Brief unreadable frames retain the same row.
        var unconfirmedSingleRow = persistentSingleRow && source == LootSource.Rare &&
            rawRows?.Any(row => row.Source == LootSource.Rare && row.Slot == 0 && !row.IsAlignmentAnchor &&
                row.RejectionReason == LootObservation.RarePaddleUnconfirmedReason) == true;
        if (persistentSingleRow && elapsed > 1550) singleRowMissingSince = null;
        var closeSingleRow = persistentSingleRow && !unconfirmedSingleRow && observations[0] is null &&
            singleRowMissingSince is { } missing && now - missing >= 1550;
        if (persistentSingleRow)
        {
            if (observations[0] is null && !unconfirmedSingleRow) singleRowMissingSince ??= now;
            else singleRowMissingSince = null;
        }
        previousMilliseconds = now;
        frameIndex = checked(frameIndex + 1);
        foreach (var model in models)
        {
            // Failed verification proves neither a new drop nor an empty panel.
            // Preserve every existing hypothesis without adding item/amount votes
            // or accumulating absence likelihood against the visible notification.
            if (unconfirmedSingleRow) continue;
            if (persistentSingleRow) AdvanceSingleRow(model, observations[0], now, closeSingleRow);
            else Advance(model, observations, now, elapsed, maximumBirths, minimumCoveredSlots, unreadableCoverage);
            // A persistent banner supplies arbitrarily many correlated reads of
            // one drop. Learning from them makes a new notification less likely
            // than an OCR conflict or an empty panel, so short consecutive drops
            // disappear. Keep the single-row observation priors stable; only the
            // finite-lifetime normal log supplies representative training samples.
            if (!persistentSingleRow || useLegacySingleRowLearning)
            {
                Learn(model);
                if (frameIndex == nextFit) model.Refit();
            }
            if (frameIndex % 50 == 0) Settle(model, now);
        }
        if (frameIndex == nextFit) nextFit = checked(nextFit + Math.Min(nextFit, 250));
        return Project(capturedAt);
    }

    // No arbitrary flush votes or forced public ID commitment: live and retired
    // supported rows already contribute to the current estimate.
    public LifetimeSnapshot Complete(DateTimeOffset completedAt)
    {
        interpretations.Clear();
        return Project(completedAt);
    }

    public void Reset()
    {
        models = CreateModels();
        previousMilliseconds = null;
        lastCaptureMilliseconds = null;
        frameIndex = 0;
        nextFit = 25;
        projectionRevision = 0;
        started = false;
        previousTotals = FrozenDictionary<string, long>.Empty;
        previousSnapshot = null;
        knownNames.Clear();
        interpretations.Clear();
        previousVisualSlots = null;
        earlierVisualSlots = null;
        previousVisualMilliseconds = null;
        previousVisualOccupiedSlots = 0;
        hasObservedFading = false;
        VisualCoverageFallbackCount = 0;
        singleRowMissingSince = null;
    }

    private Model[] CreateModels() => (persistentSingleRow ? new[] { 1550 } : new[] { 1250, 1350, 1450, 1550 })
        .Select(life => new Model(life, trackedSlotCount)).ToArray();

    private void ValidateRawRows(IReadOnlyList<LootObservation> rows, bool allowOccupancy)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var inputSlotCount = trackedSlotCount == SlotCount ? SlotCount + 1 : trackedSlotCount;
        if (rows.Count > inputSlotCount || rows.Any(row => row is null))
            throw new ArgumentException("At most six non-null calibrated input rows are supported.", nameof(rows));
        var slots = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row.Slot < 0 || row.Slot >= inputSlotCount || !slots.Add(row.Slot) ||
                row.RawText is null || row.RawText.Length > 16384 ||
                row.ItemName is { } name && (string.IsNullOrWhiteSpace(name) || name.Length > 4096) ||
                row.Quantity is <= 0 || !double.IsFinite(row.NameConfidence) || row.NameConfidence is < 0 or > 1 ||
                !double.IsFinite(row.QuantityConfidence) || row.QuantityConfidence is < 0 or > 1)
                throw new ArgumentException("Invalid raw reading or duplicate physical slot.", nameof(rows));
            if (row.OccupancyEvidence is { } occupancy)
            {
                if (!allowOccupancy)
                    throw new ArgumentException("Occupancy evidence requires lifetime-v3.", nameof(rows));
                occupancy.Validate();
            }
            if (row.FadeEvidence is { } fade)
            {
                if (!UsesFadeEvidence || row.Source != LootSource.Normal || row.ItemName is null ||
                    row.RejectionReason is not null || row.IsAlignmentAnchor)
                    throw new ArgumentException("Fade evidence requires a recognized normal row in the fade-aware counter.", nameof(rows));
                fade.Validate();
            }
        }
    }

    private int ObserveVisualSlots(Observation?[] observations, long now)
    {
        if (previousVisualMilliseconds is { } last && now - last > 600)
        {
            previousVisualSlots = null;
            earlierVisualSlots = null;
        }
        var current = observations.Select(row => row is { Name: { } name, Quantity: { } quantity }
            ? ((string Name, int Quantity)?)(name, quantity) : null).ToArray();
        var count = CompletePrefix(current);
        var minimum = 0;
        // In the fade-aware mode, the visible, fully read prefix must still fit
        // the explanation. Age evidence must not improve a score by discarding
        // a real upper row. Glyph occupancy corroborates the oldest position;
        // this bounds the live row count without asserting a particular scroll.
        if (UsesFadeEvidence && hasObservedFading && count >= 2 && observations.Take(count).All(row => row?.Source is
                { ItemName: not null, Quantity: not null, RejectionReason: null, IsAlignmentAnchor: false }) &&
            observations[count - 1]?.Source?.OccupancyEvidence?.Matches.Any(match => match.Correlation >= .90) == true)
            minimum = count;
        // Protect physical coverage only when two complete prior readings agree
        // and their names/amounts still fit after the visible stack grows. A
        // matching prior glyph mask confirms the current upper row is occupied;
        // it cannot identify which equal-named drop it is or vote for an amount.
        // Candidate birth counts remain unchanged: an older row may still fit.
        if (count >= 2 && previousVisualSlots is { } previous && earlierVisualSlots is { } earlier)
        {
            var oldCount = CompletePrefix(previous);
            if (oldCount > 0 && oldCount == CompletePrefix(earlier) && count > oldCount &&
                Enumerable.Range(0, oldCount).All(slot => previous[slot] == earlier[slot] &&
                    previous[slot] == current[slot + count - oldCount]) &&
                observations[count - 1]?.Source?.OccupancyEvidence?.Matches
                    .Any(match => match.Correlation >= .90) == true)
                minimum = count;
        }
        earlierVisualSlots = previousVisualSlots;
        previousVisualSlots = current;
        previousVisualMilliseconds = now;
        return minimum;

        static int CompletePrefix((string Name, int Quantity)?[] rows) =>
            rows.TakeWhile(row => row is not null).Count();
    }

    private LifetimeParsedReading? Interpret(LootObservation source)
    {
        if (interpretations.TryGetValue(source, out var existing)) return existing;
        var parsed = rawParser!(source);
        if (parsed?.IsExcluded == true) return interpretations[source] = parsed;
        if (parsed is not null && (string.IsNullOrWhiteSpace(parsed.Name) || parsed.Name.Length > 4096 ||
            parsed.Quantity is <= 0 || !double.IsFinite(parsed.Confidence) || parsed.Confidence is < 0 or > 1))
            throw new ArgumentException("The raw parser returned an invalid interpretation.", nameof(rawParser));
        // A later text interpretation may fix a name or recover a formerly
        // unreadable amount. It must not undo a validated template/review amount.
        // Explicit policy exclusion above is different from a failed text parse.
        if (source.ItemName is { } acceptedName && source.RejectionReason is null)
        {
            // Accepted rare identities have passed the primary/secondary review.
            // Raw-text matching may not substitute an unverified item afterwards,
            // or borrow its amount when the verified item's quantity is missing.
            if (this.source == LootSource.Rare)
                return interpretations[source] = new(acceptedName,
                    source.Quantity ?? (parsed?.Name == acceptedName ? parsed.Quantity : null),
                    source.NameConfidence);
            return interpretations[source] = new(parsed?.Name ?? acceptedName, source.Quantity ?? parsed?.Quantity,
                parsed?.Confidence ?? source.NameConfidence);
        }
        return interpretations[source] = parsed;
    }

    private Observation?[] Normalize(IReadOnlyList<CompanionRecognizedEntry> acceptedRows)
    {
        ArgumentNullException.ThrowIfNull(acceptedRows);
        var inputSlotCount = trackedSlotCount == SlotCount ? SlotCount + 1 : trackedSlotCount;
        if (acceptedRows.Count > inputSlotCount || acceptedRows.Any(row => row is null))
            throw new ArgumentException("At most six non-null calibrated input rows are supported.", nameof(acceptedRows));
        var observations = new Observation?[trackedSlotCount];
        var slots = new HashSet<int>();
        var ordered = acceptedRows.OrderByDescending(row => row.Y).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var row = ordered[index];
            var slot = row.Slot ?? index;
            if (slot < 0 || slot >= inputSlotCount || !slots.Add(slot) ||
                string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 4096 ||
                !double.IsFinite(row.NameConfidence) || row.NameConfidence is < 0 or > 1)
                throw new ArgumentException("Invalid accepted row or duplicate physical slot.", nameof(acceptedRows));
            if (row.IsAlignmentAnchor || row.IsPlaceholder || slot >= trackedSlotCount) continue;
            observations[slot] = new(row.Name,
                row.Count is 0 or uint.MaxValue || row.Count > int.MaxValue ? null : (int)row.Count,
                row.NameConfidence);
        }
        return observations;
    }

    private void AdvanceSingleRow(Model model, Observation? observation, double now, bool closeRow)
    {
        var candidates = new Dictionary<string, State>(StringComparer.Ordinal);
        foreach (var prior in model.Beam)
        {
            var row = prior.Live.FirstOrDefault();
            var retired = prior.Done;
            if (row is not null && closeRow)
            {
                retired = new(row, retired, now);
                row = null;
            }
            if (row is null)
            {
                var occupied = observation is null ? 0 : 1;
                Add(new([], retired, prior.Score + model.EmptyLogProbability[occupied], [-1 - occupied]));
                if (observation is not null)
                    AddNewRow(retired, prior.Score);
                continue;
            }

            // Use the normal observation likelihoods and reversible name/amount
            // votes. The notification stays in its readable age band while it
            // remains on screen instead of aging into invented repeat drops.
            const int age = 2;
            var category = Category(observation, row);
            // A quantity-less reading of the same persistent notification is
            // association evidence, using the normal partial-text likelihood.
            // It supplies no quantity vote and must not make an infinitely
            // visible row progressively less likely than an empty panel.
            if (category == 3 && observation?.Name == row.Name && observation?.Quantity is null && row.Quantity is not null)
                category = 5;
            var continued = observation is null ? row : Observe(row, observation);
            Add(new([TrimSingleRowReadings(continued)], retired,
                prior.Score + model.HeldLogProbability[age][category], [age * Categories + category]));
            if (observation?.Name is { } name && row.Name is { } priorName && name != priorName)
            {
                // Keep both explanations until subsequent evidence chooses:
                // a changed notification, or an OCR conflict in the old row.
                // A replacement prior prevents a single wrong name followed by
                // the original name from winning as two physical arrivals.
                AddNewRow(new(row, retired, now), prior.Score + Math.Log(.5));
            }
        }
        SelectCandidates(model, candidates);

        void AddNewRow(Finished? retired, double score)
        {
            var born = new LifeRow(now, null, null, null);
            var category = Category(observation, born);
            Add(new([Observe(born, observation!)], retired,
                score + model.HeldLogProbability[2][category], [2 * Categories + category]));
        }

        void Add(State state)
        {
            var key = state.Live.Length == 0 ? "empty" :
                state.Live[0].BornAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (!candidates.TryGetValue(key, out var current) || state.Score > current.Score)
                candidates[key] = state;
        }
    }

    private UnreadableCoverageConstraint? UnreadableSlotCoverage(Observation?[] observations,
        IReadOnlyList<LootObservation>? rawRows, long now)
    {
        if (rawRows is null || previousVisualSlots is not { } previous ||
            previousVisualMilliseconds is not { } last || now - last > 600)
            return null;
        var oldCount = previous.TakeWhile(row => row is not null).Count();
        var readableCount = observations.TakeWhile(row => row is { Name: not null, Quantity: not null }).Count();
        // Limit this extra constraint to the five-slot capacity boundary. In a
        // smaller stack, a recovered fading row can shift the lifetime estimate
        // and split later readings. Those ambiguous cases keep the original
        // temporal alternatives instead of forcing an additional supported row.
        // The previous stack must be fully readable: an already occupied but
        // unreadable tail is not an empty place. Treating it as absent would
        // exaggerate the scroll distance when it is read again in the next frame.
        if (oldCount < 2 || oldCount != previousVisualOccupiedSlots ||
            readableCount != trackedSlotCount - 1 || readableCount + 1 <= oldCount)
            return null;
        var oldest = rawRows.FirstOrDefault(row => row.Source == source && row.Slot == readableCount);
        if (oldest is null || oldest.IsAlignmentAnchor ||
            oldest.RejectionReason == AutomaticLootSpotLock.OutsideSpotPoolReason || Interpret(oldest)?.IsExcluded == true ||
            oldest.ItemName is not null && oldest.RejectionReason is not null ||
            observations[readableCount]?.Name is { } oldestName && oldestName != previous[oldCount - 1]!.Value.Name ||
            oldest.OccupancyEvidence?.Matches.Any(match => match.Correlation >= .90 &&
                match.PreviousSlot < previous.Length && previous[match.PreviousSlot] is { } template &&
                template.Name == previous[oldCount - 1]!.Value.Name) != true)
            return null;
        var covered = readableCount + 1;
        var shift = covered - oldCount;
        for (var slot = 0; slot < oldCount - 1; slot++)
        {
            var current = observations[slot + shift];
            if (current is null || (current.Name, current.Quantity) != previous[slot]) return null;
        }
        // Require the newly occupied oldest position to continue a known row;
        // an anonymous latent hypothesis must not satisfy physical coverage.
        // Its old reading remains the amount vote, never the matching glyph mask.
        var expectedOldest = previous[oldCount - 1]!.Value;
        return new(covered, expectedOldest.Name, expectedOldest.Quantity);
    }

    private UnreadableCoverageConstraint? AdditionalPhysicalCoverage(Observation?[] observations,
        IReadOnlyList<LootObservation>? rawRows, long now)
    {
        if (rawRows is null || previousVisualMilliseconds is not { } last || now - last > 600) return null;
        var count = observations.TakeWhile(row => row is { Name: not null, Quantity: not null } &&
            row.Source is { ItemName: not null, Quantity: not null, RejectionReason: null, IsAlignmentAnchor: false }).Count();
        if (count < 2 || count >= trackedSlotCount || previousVisualOccupiedSlots < 2 || count + 1 <= previousVisualOccupiedSlots) return null;
        var oldest = rawRows.FirstOrDefault(row => row.Source == source && row.Slot == count);
        if (oldest is not { ItemName: null, IsAlignmentAnchor: false } ||
            oldest.RejectionReason == AutomaticLootSpotLock.OutsideSpotPoolReason || Interpret(oldest)?.IsExcluded == true ||
            oldest.OccupancyEvidence?.Matches.Any(match => match.Correlation >= .90) != true) return null;
        // Occupancy supplies no name or amount. The candidate must already
        // support every covered row from text readings, including this tail.
        return new(count + 1, null, null);
    }

    private sealed record UnreadableCoverageConstraint(int Count, string? OldestName, int? OldestQuantity);

    private int OccupiedPrefix(Observation?[] observations, IReadOnlyList<LootObservation>? rawRows)
    {
        for (var slot = 0; slot < trackedSlotCount; slot++)
        {
            if (observations[slot]?.Name is not null) continue;
            var row = rawRows?.FirstOrDefault(row => row.Source == source && row.Slot == slot && !row.IsAlignmentAnchor);
            if (row?.OccupancyEvidence?.Matches.Any(match => match.Correlation >= .90) != true) return slot;
        }
        return trackedSlotCount;
    }

    private static LifeRow TrimSingleRowReadings(LifeRow row)
    {
        // Persistent notifications must not retain an entire session of bitmap
        // text reads. Keep recent original evidence plus one older numeric read
        // so a long partially readable tail cannot erase the known quantity.
        var retained = new List<Observation>();
        var node = row.Readings;
        while (node is not null && retained.Count < 64)
        {
            retained.Add(node.Value);
            node = node.Previous;
        }
        if (node is null) return row;
        if (!retained.Any(value => value.Name is not null && value.Quantity is not null))
        {
            for (; node is not null; node = node.Previous)
                if (node.Value.Name is not null && node.Value.Quantity is not null)
                {
                    retained.Add(node.Value);
                    break;
                }
        }
        Reading? readings = null;
        for (var index = retained.Count - 1; index >= 0; index--) readings = new(retained[index], readings);
        return row with { Readings = readings };
    }

    private void Advance(Model model, Observation?[] observations, double now, double elapsed, int maximumBirths,
        int minimumCoveredSlots, UnreadableCoverageConstraint? unreadableCoverage = null)
    {
        var candidates = new Dictionary<string, State>(StringComparer.Ordinal);
        foreach (var prior in model.Beam)
        {
            // A birth is placed inside its capture interval, not timestamped by
            // the game. An already fading, still visible tail may therefore
            // survive slightly beyond that point estimate. Retain ordinary
            // expiry alternatives too, so this cannot block a fresh arrival.
            var ordinaryCount = prior.Live.TakeWhile(row => now - row.BornAt < model.Duration).Count();
            var tailCount = ordinaryCount;
            if (UsesFadeEvidence)
                while (tailCount < prior.Live.Length &&
                    now - prior.Live[tailCount].BornAt < model.Duration + prior.Live[tailCount].BirthUncertaintyMs &&
                    prior.Live[tailCount].Readings?.Value.Source?.FadeEvidence is
                        { Correlation: >= .90, ContrastRatio: >= .12 and <= .85 }) tailCount++;
            for (var survivorCount = ordinaryCount; survivorCount <= tailCount; survivorCount++)
            {
                var survivors = prior.Live.Take(survivorCount).ToArray();
                var retired = prior.Done;
                foreach (var expired in prior.Live.Skip(survivors.Length)) retired = new(expired, retired);
                var capacity = Math.Min(maximumBirths, SlotCount - survivors.Length);
                for (var births = 0; births <= capacity; births++)
                {
                    // Extra survival only resolves an occupied-position conflict;
                    // it does not generally lengthen the duration model.
                    if (survivorCount > ordinaryCount && ordinaryCount + births >= minimumCoveredSlots) continue;
                    if (Enumerable.Range(ordinaryCount, survivorCount - ordinaryCount).Any(index =>
                        observations[index + births]?.Name != survivors[index].Name ||
                        observations[index + births]?.Source?.OccupancyEvidence?.Matches.Any(match =>
                            match.Correlation >= .90 && match.PreviousSlot == survivors[index].Readings!.Value.Source!.Slot) != true))
                        continue;
                    var rows = new LifeRow[survivors.Length + births];
                    if (rows.Length < minimumCoveredSlots) continue;
                    // Spread births over the interval with a 100 ms minimum spacing,
                    // leaving a bounded margin on either end instead of inventing a
                    // single identical first-observed time for every new row.
                    var span = Math.Max(elapsed, (births - 1) * BirthStepMs);
                    var margin = Math.Min(200, (span - (births - 1) * BirthStepMs) / (births + 1));
                    for (var slot = 0; slot < births; slot++)
                        rows[slot] = new(now - margin - slot * (BirthStepMs + margin), null, null, null) { BirthUncertaintyMs = (slot + 1) * margin };
                    Array.Copy(survivors, 0, rows, births, survivors.Length);
                    var score = prior.Score;
                    // A uniform capture-time uncertainty leaves progressively
                    // less support near the latest possible expiry.
                    for (var index = ordinaryCount; index < survivorCount; index++)
                        score += Math.Log(1 - (now - survivors[index].BornAt - model.Duration) / survivors[index].BirthUncertaintyMs);
                    var outcomes = new int[SlotCount];
                    for (var slot = 0; slot < SlotCount; slot++)
                    {
                        var observation = observations[slot];
                        if (slot >= rows.Length)
                        {
                            var occupied = observation is null ? 0 : 1;
                            score += model.EmptyLogProbability[occupied];
                            outcomes[slot] = -1 - occupied;
                        }
                        else
                        {
                            var row = rows[slot];
                            var age = Math.Clamp((int)Math.Floor((now - row.BornAt) / 200), 0, AgeBins - 1);
                            var category = Category(observation, row);
                            score += model.HeldLogProbability[age][category];
                            if (UsesFadeEvidence && observation?.Source?.FadeEvidence is
                                { Correlation: >= .90, ContrastRatio: >= .12 and <= .85 } fade)
                            {
                                // Independently isolated lifetimes put the opaque
                                // phase near 1,000 ms and fading near 400 ms. Leave
                                // 100 ms for capture uncertainty; penalize a proposed
                                // row that is too young for its observed fading.
                                // This is age evidence, not identity: indistinguishable
                                // bright arrivals remain possible until later frames.
                                var earliestAge = 900 + 400 * (1 - fade.ContrastRatio);
                                var tooYoung = Math.Max(0, earliestAge - (now - row.BornAt)) / 100;
                                score -= tooYoung * tooYoung;
                            }
                            outcomes[slot] = age * Categories + category;
                            if (observation is not null) rows[slot] = Observe(row, observation);
                        }
                    }
                    if (unreadableCoverage is { } coverage &&
                        (rows.Take(coverage.Count).Any(row => row.Name is null || row.Quantity is null) ||
                         (coverage.OldestName is not null && (rows[coverage.Count - 1].Name, rows[coverage.Count - 1].Quantity) !=
                         (coverage.OldestName, coverage.OldestQuantity))))
                        continue;
                    var state = new State(rows, retired, score, outcomes);
                    var key = string.Join(',', rows.Select(row => row.BornAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                    if (!candidates.TryGetValue(key, out var current) || score > current.Score) candidates[key] = state;
                }
            }
        }
        if (candidates.Count == 0 && minimumCoveredSlots > 0)
        {
            // Contradictory measurements must not empty a duration lane. Keep the
            // original alternatives for this frame and expose the rejected constraint.
            VisualCoverageFallbackCount = checked(VisualCoverageFallbackCount + 1);
            Advance(model, observations, now, elapsed, maximumBirths, 0);
            return;
        }
        SelectCandidates(model, candidates);
    }

    private static void SelectCandidates(Model model, Dictionary<string, State> candidates)
    {
        model.Beam = candidates.Values.OrderByDescending(state => state.Score).Take(BeamCapacity).ToArray();
        var best = model.Beam[0].Score;
        model.Evidence += best;
        for (var index = 0; index < model.Beam.Length; index++)
            model.Beam[index] = model.Beam[index] with { Score = model.Beam[index].Score - best };
    }

    private int Category(Observation? observation, LifeRow row)
    {
        if (observation is null) return 0;
        if (row.Name is null && (!UsesRawText || row.Text.Length == 0)) return 8;
        if (row.Name is not null && observation.Name is not null)
        {
            if (row.Name != observation.Name) return 4;
            if (row.Quantity is null || observation.Quantity is null) return 3;
            return row.Quantity == observation.Quantity ? 1 : 2;
        }
        if (row.Text.Length == 0 || observation.Text.Length == 0) return 6;
        var similarity = LifetimeTextSimilarity.JaroWinkler(row.Text, observation.Text);
        return similarity >= .94 ? 5 : similarity >= .82 ? 6 : 7;
    }

    private static LifeRow Observe(LifeRow row, Observation observation)
    {
        var readings = new Reading(observation, row.Readings);
        // A newest-first scan provides a deterministic recent-read tie breaker.
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        string? name = null;
        var bestSupport = 0;
        for (var node = readings; node is not null; node = node.Previous)
        {
            if (node.Value.Name is null) continue;
            var support = names.GetValueOrDefault(node.Value.Name) + 1;
            names[node.Value.Name] = support;
            if (support > bestSupport) { name = node.Value.Name; bestSupport = support; }
        }
        var quantities = new Dictionary<int, int>();
        int? quantity = null;
        bestSupport = 0;
        for (var node = readings; node is not null; node = node.Previous)
        {
            if (node.Value.Name != name || node.Value.Quantity is not { } value) continue;
            var support = quantities.GetValueOrDefault(value) + 1;
            quantities[value] = support;
            if (support > bestSupport) { quantity = value; bestSupport = support; }
        }
        return row with { Readings = readings, Name = name, Quantity = quantity,
            Text = observation.Text.Length > 0 ? observation.Text : row.Text };
    }

    private (string Name, int Quantity)? Decide(LifeRow row)
    {
        var votes = new Dictionary<(string Name, int Quantity), (int Count, double Confidence)>();
        for (var node = row.Readings; node is not null; node = node.Previous)
        {
            var value = node.Value;
            var current = value.Source is { } source ? Interpret(source) :
                value.Name is { } name ? new LifetimeParsedReading(name, value.Quantity, value.Confidence) : null;
            if (current is null || current.IsExcluded || current.Quantity is not { } quantity) continue;
            var key = (current.Name, quantity);
            if (votes.TryGetValue(key, out var support)) votes[key] = (support.Count + 1, support.Confidence);
            else votes[key] = (1, current.Confidence);
        }
        return votes.Count == 0 ? null : votes.OrderByDescending(pair => pair.Value.Count)
            .ThenByDescending(pair => pair.Value.Confidence).First().Key;
    }

    private static void Learn(Model model)
    {
        foreach (var outcome in model.Beam[0].Outcomes)
            if (outcome < 0) model.EmptyCounts[-1 - outcome]++;
            else model.HeldCounts[outcome / Categories][outcome % Categories]++;
    }

    private void Settle(Model model, double now)
    {
        var oldestMutableBirth = now - model.Duration - RetirementHorizonMs;
        for (var node = model.Beam[0].Done; node is not null; node = node.Previous)
            if ((node.RetiredAt ?? node.Row.BornAt) < oldestMutableBirth && Decide(node.Row) is { } reading)
            {
                AddQuantity(model.Settled, reading.Name, reading.Quantity);
                model.SettledDropCount = checked(model.SettledDropCount + 1);
                var arrival = ArrivalTime(node.Row.BornAt);
                if (model.SettledLatestArrivalAt is null || arrival > model.SettledLatestArrivalAt)
                    model.SettledLatestArrivalAt = arrival;
                model.RecentSettledDrops.Add(new(BirthIdentity(node.Row.BornAt), reading.Name, reading.Quantity, arrival));
            }
        model.RecentSettledDrops.Sort(static (left, right) =>
        {
            var order = left.DetectedAt.CompareTo(right.DetectedAt);
            return order != 0 ? order : left.EventId.CompareTo(right.EventId);
        });
        if (model.RecentSettledDrops.Count > PolicyHistoryCapacity)
            model.RecentSettledDrops.RemoveRange(0, model.RecentSettledDrops.Count - PolicyHistoryCapacity);
        for (var index = 0; index < model.Beam.Length; index++)
            model.Beam[index] = model.Beam[index] with { Done = RetainRecent(model.Beam[index].Done, oldestMutableBirth) };
    }

    private static Finished? RetainRecent(Finished? chain, double cutoff)
    {
        var retained = new List<Finished>();
        for (var node = chain; node is not null; node = node.Previous)
            if ((node.RetiredAt ?? node.Row.BornAt) >= cutoff) retained.Add(node);
        Finished? result = null;
        for (var index = retained.Count - 1; index >= 0; index--)
            result = new(retained[index].Row, result, retained[index].RetiredAt);
        return result;
    }

    private static void AddQuantity(Dictionary<string, long> totals, string name, int quantity) =>
        totals[name] = checked(totals.GetValueOrDefault(name) + quantity);

    private LifetimeSnapshot Project(DateTimeOffset at)
    {
        var best = models.OrderByDescending(model => model.Evidence).First();
        var totals = new Dictionary<string, long>(best.Settled, StringComparer.Ordinal);
        var observed = new List<LifetimeObservedDrop>();
        for (var row = best.Beam[0].Done; row is not null; row = row.Previous) ObserveProjected(row.Row);
        foreach (var row in best.Beam[0].Live) ObserveProjected(row);
        var deltas = totals.Keys.Concat(previousTotals.Keys).Distinct(StringComparer.Ordinal)
            .Select(name => (Name: name, Delta: checked(totals.GetValueOrDefault(name) - previousTotals.GetValueOrDefault(name))))
            .Where(pair => pair.Delta != 0).ToDictionary(pair => pair.Name, pair => pair.Delta, StringComparer.Ordinal);
        var supportedDrops = checked(best.SettledDropCount + observed.Count);
        var latestArrival = best.SettledLatestArrivalAt;
        foreach (var drop in observed)
            if (latestArrival is null || drop.DetectedAt > latestArrival) latestArrival = drop.DetectedAt;
        if (deltas.Count > 0 || previousSnapshot is { } previous &&
            (previous.SupportedDropCount != supportedDrops || previous.LatestArrivalAt != latestArrival))
            projectionRevision = checked(projectionRevision + 1);
        previousTotals = totals.ToFrozenDictionary(StringComparer.Ordinal);
        previousSnapshot = new(frameIndex, projectionRevision, at, best.Duration, previousTotals,
            deltas.ToFrozenDictionary(StringComparer.Ordinal),
            Array.AsReadOnly(models.Select(model => new LaneEstimate(model.Duration, model.Evidence, model.Beam.Length)).ToArray()),
            supportedDrops, latestArrival, Array.AsReadOnly(observed.OrderBy(drop => drop.DetectedAt).ThenBy(drop => drop.EventId).ToArray()))
        {
            // This is a replacement policy history for the selected explanation,
            // not an append-only stream of provisional identities. Rejected
            // alternatives and revised amounts cannot remain as stale samples.
            PolicyDrops = Array.AsReadOnly(best.RecentSettledDrops.Concat(observed)
                .OrderBy(drop => drop.DetectedAt).ThenBy(drop => drop.EventId)
                .TakeLast(PolicyHistoryCapacity).ToArray()),
            VisualCoverageFallbackCount = VisualCoverageFallbackCount,
        };
        return previousSnapshot;

        void ObserveProjected(LifeRow row)
        {
            if (Decide(row) is not { } reading) return;
            AddQuantity(totals, reading.Name, reading.Quantity);
            observed.Add(new(BirthIdentity(row.BornAt), reading.Name, reading.Quantity, ArrivalTime(row.BornAt)));
        }
    }

    private static DateTimeOffset ArrivalTime(double bornAt) => DateTimeOffset.FromUnixTimeMilliseconds(
        (long)Math.Clamp(Math.Round(bornAt, MidpointRounding.ToEven),
            DateTimeOffset.MinValue.ToUnixTimeMilliseconds(), DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()));

    private Guid BirthIdentity(double bornAt)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(bornAt));
        BinaryPrimitives.WriteInt64LittleEndian(bytes[8..], 0x4c69666574696d65);
        if (source == LootSource.Rare) bytes[15] ^= 0x80;
        return new Guid(bytes);
    }

    private sealed record Observation(string? Name, int? Quantity, double Confidence,
        LootObservation? Source = null, string Text = "");
    private sealed record Reading(Observation Value, Reading? Previous);
    private sealed record LifeRow(double BornAt, Reading? Readings, string? Name, int? Quantity)
    {
        // Latest birth still consistent with the sampled interval and the
        // existing minimum spacing between simultaneous arrivals.
        public double BirthUncertaintyMs { get; init; }
        public string Text { get; init; } = "";
    }
    private sealed record Finished(LifeRow Row, Finished? Previous, double? RetiredAt = null);
    private sealed record State(LifeRow[] Live, Finished? Done, double Score, int[] Outcomes);

    private sealed class Model
    {
        public int Duration { get; }
        public State[] Beam { get; set; }
        public double Evidence { get; set; }
        public Dictionary<string, long> Settled { get; } = new(StringComparer.Ordinal);
        public int SettledDropCount { get; set; }
        public DateTimeOffset? SettledLatestArrivalAt { get; set; }
        public List<LifetimeObservedDrop> RecentSettledDrops { get; } = [];
        public double[][] HeldCounts { get; } = Enumerable.Range(0, AgeBins).Select(_ => Enumerable.Repeat(5d, Categories).ToArray()).ToArray();
        public double[] EmptyCounts { get; } = [5, 5];
        public double[][] HeldLogProbability { get; private set; }
        public double[] EmptyLogProbability { get; private set; } = Normalize([.97, .03]);

        public Model(int duration, int slots)
        {
            Duration = duration;
            Beam = [new([], null, 0, new int[slots])];
            HeldLogProbability = Enumerable.Range(0, AgeBins).Select(age =>
            {
                var absent = age < 5 ? .25 : .7;
                var seen = 1 - absent;
                // missing; same name/quantity; quantity conflict; unreadable amount;
                // name conflict; strong/medium/weak partial text; first reading.
                return Normalize([absent, seen * .57, seen * .05, seen * .02,
                    seen * .02, seen * .10, seen * .04, seen * .04, seen * .16]);
            }).ToArray();
        }

        public void Refit()
        {
            HeldLogProbability = HeldCounts.Select(Normalize).ToArray();
            EmptyLogProbability = Normalize(EmptyCounts);
        }
        private static double[] Normalize(double[] values)
        {
            var sum = values.Sum();
            return values.Select(value => Math.Log(value / sum)).ToArray();
        }
    }
}

public sealed record LifetimeSnapshot(long Frame, long Revision, DateTimeOffset At, int SelectedLifetimeMs,
    IReadOnlyDictionary<string, long> Totals, IReadOnlyDictionary<string, long> Deltas, IReadOnlyList<LaneEstimate> Lanes,
    int SupportedDropCount, DateTimeOffset? LatestArrivalAt, IReadOnlyList<LifetimeObservedDrop> ObservedDrops)
{
    public IReadOnlyList<LifetimeObservedDrop> PolicyDrops { get; init; } = Array.AsReadOnly(Array.Empty<LifetimeObservedDrop>());
    public int VisualCoverageFallbackCount { get; init; }
}
public sealed record LaneEstimate(int LifetimeMs, double LogEvidence, int Hypotheses);
public sealed record LifetimeObservedDrop(Guid EventId, string Name, int Quantity, DateTimeOffset DetectedAt);
