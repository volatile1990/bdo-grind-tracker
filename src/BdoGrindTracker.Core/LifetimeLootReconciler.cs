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
    public const int SlotCount = 5;
    private const int BeamCapacity = 24;
    private const int AgeBins = 8;
    private const int Categories = 9;
    private const double BirthStepMs = 100;
    private const double RetirementHorizonMs = 3000;
    private const int PolicyHistoryCapacity = 64;
    private Model[] models = CreateModels();
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

    public LifetimeLootReconciler(Func<LootObservation, LifetimeParsedReading?>? rawParser = null,
        Func<string, IReadOnlyList<string>>? nameAliases = null)
        : this(rawParser, nameAliases, false)
    {
    }

    public LifetimeLootReconciler(Func<LootObservation, LifetimeParsedReading?>? rawParser,
        Func<string, IReadOnlyList<string>>? nameAliases, bool useVisualSlotCoverage)
    {
        if (useVisualSlotCoverage && rawParser is null)
            throw new ArgumentException("Visual slot coverage requires a raw-text parser.", nameof(rawParser));
        this.rawParser = rawParser;
        this.nameAliases = nameAliases;
        UsesVisualSlotCoverage = useVisualSlotCoverage;
    }

    public bool UsesRawText => rawParser is not null;
    public bool UsesVisualSlotCoverage { get; }
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
        var observations = new Observation?[SlotCount];
        var nextKnown = new HashSet<string>(knownNames, StringComparer.Ordinal);
        foreach (var source in rows)
        {
            if (source.Source != LootSource.Normal || source.IsAlignmentAnchor || source.Slot >= SlotCount) continue;
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
        return ProcessNormalized(observations, capturedAt);
    }

    private LifetimeSnapshot ProcessNormalized(Observation?[] observations, DateTimeOffset capturedAt)
    {
        var now = capturedAt.ToUnixTimeMilliseconds();
        if (lastCaptureMilliseconds is { } last && now <= last)
            return previousSnapshot! with { Deltas = FrozenDictionary<string, long>.Empty };
        lastCaptureMilliseconds = now;
        var minimumCoveredSlots = UsesVisualSlotCoverage ? ObserveVisualSlots(observations, now) : 0;
        // Empty menus before the first real reading are not a representative
        // training sample of the log's in-session detection probabilities.
        if (!started)
        {
            if (!observations.Any(row => row?.Quantity is not null)) return Project(capturedAt);
            started = true;
        }
        var elapsed = previousMilliseconds is { } prior ? now - prior : BirthStepMs;
        var maximumBirths = previousMilliseconds is null ? SlotCount :
            Math.Max(1, (int)Math.Min(SlotCount, Math.Ceiling(elapsed / BirthStepMs)));
        previousMilliseconds = now;
        frameIndex = checked(frameIndex + 1);
        foreach (var model in models)
        {
            Advance(model, observations, now, elapsed, maximumBirths, minimumCoveredSlots);
            Learn(model);
            if (frameIndex == nextFit) model.Refit();
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
        VisualCoverageFallbackCount = 0;
    }

    private static Model[] CreateModels() => new[] { 1250, 1350, 1450, 1550 }
        .Select(life => new Model(life, SlotCount)).ToArray();

    private static void ValidateRawRows(IReadOnlyList<LootObservation> rows, bool allowOccupancy)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > SlotCount + 1 || rows.Any(row => row is null))
            throw new ArgumentException("At most six non-null calibrated input rows are supported.", nameof(rows));
        var slots = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row.Slot is < 0 or > SlotCount || !slots.Add(row.Slot) ||
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
            return interpretations[source] = new(parsed?.Name ?? acceptedName, source.Quantity ?? parsed?.Quantity,
                parsed?.Confidence ?? source.NameConfidence);
        return interpretations[source] = parsed;
    }

    private static Observation?[] Normalize(IReadOnlyList<CompanionRecognizedEntry> acceptedRows)
    {
        ArgumentNullException.ThrowIfNull(acceptedRows);
        if (acceptedRows.Count > SlotCount + 1 || acceptedRows.Any(row => row is null))
            throw new ArgumentException("At most six non-null calibrated input rows are supported.", nameof(acceptedRows));
        var observations = new Observation?[SlotCount];
        var slots = new HashSet<int>();
        var ordered = acceptedRows.OrderByDescending(row => row.Y).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var row = ordered[index];
            var slot = row.Slot ?? index;
            if (slot is < 0 or > SlotCount || !slots.Add(slot) ||
                string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 4096 ||
                !double.IsFinite(row.NameConfidence) || row.NameConfidence is < 0 or > 1)
                throw new ArgumentException("Invalid accepted row or duplicate physical slot.", nameof(acceptedRows));
            if (row.IsAlignmentAnchor || row.IsPlaceholder || slot == SlotCount) continue;
            observations[slot] = new(row.Name,
                row.Count is 0 or uint.MaxValue || row.Count > int.MaxValue ? null : (int)row.Count,
                row.NameConfidence);
        }
        return observations;
    }

    private void Advance(Model model, Observation?[] observations, double now, double elapsed, int maximumBirths,
        int minimumCoveredSlots)
    {
        var candidates = new Dictionary<string, State>(StringComparer.Ordinal);
        foreach (var prior in model.Beam)
        {
            var survivors = prior.Live.TakeWhile(row => now - row.BornAt < model.Duration).ToArray();
            var retired = prior.Done;
            foreach (var expired in prior.Live.Skip(survivors.Length)) retired = new(expired, retired);
            var capacity = Math.Min(maximumBirths, SlotCount - survivors.Length);
            for (var births = 0; births <= capacity; births++)
            {
                var rows = new LifeRow[survivors.Length + births];
                if (rows.Length < minimumCoveredSlots) continue;
                // Spread births over the interval with a 100 ms minimum spacing,
                // leaving a bounded margin on either end instead of inventing a
                // single identical first-observed time for every new row.
                var span = Math.Max(elapsed, (births - 1) * BirthStepMs);
                var margin = Math.Min(200, (span - (births - 1) * BirthStepMs) / (births + 1));
                for (var slot = 0; slot < births; slot++)
                    rows[slot] = new(now - margin - slot * (BirthStepMs + margin), null, null, null);
                Array.Copy(survivors, 0, rows, births, survivors.Length);
                var score = prior.Score;
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
                        outcomes[slot] = age * Categories + category;
                        if (observation is not null) rows[slot] = Observe(row, observation);
                    }
                }
                var state = new State(rows, retired, score, outcomes);
                var key = string.Join(',', rows.Select(row => row.BornAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                if (!candidates.TryGetValue(key, out var current) || score > current.Score) candidates[key] = state;
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
            if (node.Row.BornAt < oldestMutableBirth && Decide(node.Row) is { } reading)
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
        var retained = new List<LifeRow>();
        for (var node = chain; node is not null; node = node.Previous)
            if (node.Row.BornAt >= cutoff) retained.Add(node.Row);
        Finished? result = null;
        for (var index = retained.Count - 1; index >= 0; index--) result = new(retained[index], result);
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

    private static Guid BirthIdentity(double bornAt)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(bornAt));
        BinaryPrimitives.WriteInt64LittleEndian(bytes[8..], 0x4c69666574696d65);
        return new Guid(bytes);
    }

    private sealed record Observation(string? Name, int? Quantity, double Confidence,
        LootObservation? Source = null, string Text = "");
    private sealed record Reading(Observation Value, Reading? Previous);
    private sealed record LifeRow(double BornAt, Reading? Readings, string? Name, int? Quantity)
    {
        public string Text { get; init; } = "";
    }
    private sealed record Finished(LifeRow Row, Finished? Previous);
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
