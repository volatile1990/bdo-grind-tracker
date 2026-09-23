namespace BdoGrindTracker.Core.Buffs;

/// <summary>
/// Accounts for confirmed visual buff observations. Full consumable costs and the cost of observed
/// running time are separate. Initially active buffs establish a baseline without a purchase;
/// new applications and later readable timer increases count as consumption.
/// </summary>
public sealed class BuffLedger
{
    private static readonly TimeSpan TimerJitter = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumRecordedDuration = TimeSpan.FromDays(3650);
    private const decimal MaximumUnitPrice = 100_000_000_000_000m;
    private const decimal MaximumRecordedCost = 100_000_000_000_000_000_000m;
    private readonly Dictionary<string, BuffDefinition> definitions;
    private readonly Dictionary<string, TrackedBuff> tracked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan> lastReadRemaining = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan> lastReadPrecision = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> lastReadAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuffUsage> usage = new(StringComparer.Ordinal);
    private readonly List<BuffConsumption> consumptions = [];
    private readonly HashSet<string> observedIdentities = new(StringComparer.Ordinal);
    private readonly TimeSpan maxObservationGap;
    private DateTimeOffset? lastCapturedAt;
    private BuffLedgerSnapshot? snapshot;
    private bool applying;
    private bool hasReadableBaseline;

    public BuffLedger(IEnumerable<BuffDefinition> definitions, TimeSpan? maxObservationGap = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        this.definitions = new(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
            if (definition.Duration <= TimeSpan.Zero || definition.Duration > TimeSpan.FromDays(365))
                throw new ArgumentOutOfRangeException(nameof(definitions), "Buff duration must be positive.");
            this.definitions.Add(definition.Id, definition);
        }

        this.maxObservationGap = maxObservationGap ?? TimeSpan.FromSeconds(45);
        if (this.maxObservationGap <= TimeSpan.Zero || this.maxObservationGap > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(maxObservationGap));
    }

    public BuffLedgerSnapshot Snapshot
    {
        get
        {
            if (snapshot is not null) return snapshot;
            var current = new BuffLedgerSnapshot(
                Array.AsReadOnly(consumptions.ToArray()),
                Array.AsReadOnly(usage.Values.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(tracked.Values.Where(item => item.SeenPreviousFrame && item.Pending is null && item.Current is not null)
                    .Select(item => item.Current!.Active).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray()));
            // A user-supplied price resolver may inspect the ledger mid-update.
            if (!applying) snapshot = current;
            return current;
        }
    }

    /// <summary>
    /// Feed one readable scan, identifying any individually unreadable buffs in unknownBuffIds.
    /// An empty list means no supported buffs were visible. Unreadable capture, pause, or leaving
    /// the game require a complete continuity break.
    /// </summary>
    public BuffLedgerSnapshot Apply(
        IEnumerable<BuffObservation> observations,
        DateTimeOffset capturedAt,
        Func<BuffDefinition, BuffPrice?> priceResolver,
        IEnumerable<string>? unknownBuffIds = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(priceResolver);
        // Delayed or duplicate frames cannot establish confirmation or move the clock backwards.
        if (lastCapturedAt is { } previous && capturedAt <= previous) return Snapshot;
        snapshot = null;
        applying = true;
        try
        {
            var result = ApplyCore(observations, capturedAt, priceResolver, unknownBuffIds);
            snapshot = result;
            return result;
        }
        finally { applying = false; }
    }

    private BuffLedgerSnapshot ApplyCore(IEnumerable<BuffObservation> observations, DateTimeOffset capturedAt,
        Func<BuffDefinition, BuffPrice?> priceResolver, IEnumerable<string>? unknownBuffIds)
    {
        if (lastCapturedAt is { } last && capturedAt - last > maxObservationGap) BreakContinuity();
        var unknown = new HashSet<string>(unknownBuffIds ?? [], StringComparer.Ordinal);
        foreach (var id in unknown)
        {
            // A newly visible icon can precede its first readable timer. Keep
            // this evidence briefly, without treating an initially unknown buff
            // as an application made during the session.
            if (hasReadableBaseline && !observedIdentities.Contains(id) && definitions.ContainsKey(id))
            {
                if (!tracked.TryGetValue(id, out var newlyVisible)) tracked.Add(id, newlyVisible = new());
                newlyVisible.NewApplication = new(capturedAt);
            }
            RememberIdentity(id);
            BreakContinuity(id);
        }

        // A duplicate identity is ambiguous (for example two similar-looking icons), not two items consumed.
        var reported = observations.ToArray();
        var groups = reported.Where(IsValidObservation).GroupBy(item => item.BuffId, StringComparer.Ordinal).ToArray();
        var validObservations = groups.Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
        foreach (var observation in reported.Where(item => item is not null && !IsValidObservation(item)))
            RememberIdentity(observation.BuffId);
        foreach (var group in groups.Where(group => group.Count() > 1)) RememberIdentity(group.Key);
        // Missing identities lose runtime continuity below, including an empty
        // readable scan. A brief local gap must not erase evidence that a new
        // application appeared before its countdown could be confirmed.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in validObservations)
        {
            seen.Add(observation.BuffId);
            var definition = definitions[observation.BuffId];
            if (!tracked.TryGetValue(observation.BuffId, out var state))
                tracked.Add(observation.BuffId, state = new());
            if (state.NewApplication is { } application &&
                (capturedAt - application.FirstSeenAt > maxObservationGap ||
                 application.FirstReadable is { } firstApplicationSample && !IsContinuation(firstApplicationSample, observation, capturedAt)))
                state.NewApplication = null;

            var isNewAppearance = hasReadableBaseline && !observedIdentities.Contains(observation.BuffId);
            RememberIdentity(observation.BuffId);
            var isRenewal = lastReadRemaining.TryGetValue(observation.BuffId, out var previousRemaining) &&
                observation.Remaining > previousRemaining &&
                !IsHourTimerRefinement(observation, previousRemaining, capturedAt);
            lastReadRemaining[observation.BuffId] = observation.Remaining;
            lastReadPrecision[observation.BuffId] = observation.TimerPrecision;
            lastReadAt[observation.BuffId] = capturedAt;
            if (isRenewal)
            {
                // A strict increase at the same precision is a new use immediately,
                // including reapplication after death or a capture gap. A refined
                // reading within a previous floored hour interval is not a renewal.
                var selected = ResolveDurationVariant(definition, observation);
                var price = ReadPrice(selected, priceResolver);
                consumptions.Add(new(selected.Id, selected.Name, selected.MarketItemId, capturedAt, price));
                state.NewApplication = null;
                if (state.SeenPreviousFrame && state.Pending is null && state.Current is { } running)
                    AddUsage(running, capturedAt);
                state.Current = CreateSample(selected, observation, capturedAt, price, isBaseline: false);
                state.Pending = null;
                state.SeenPreviousFrame = true;
                continue;
            }

            // A baseline must still fit its selected duration variant.
            if (state.Current is { } priorCurrent && !FitsSelectedDuration(priorCurrent, observation))
            {
                state.Current = null;
                state.Pending = null;
                state.SeenPreviousFrame = false;
            }

            if (state.SeenPreviousFrame && state.Pending is { } pending && FitsSelectedDuration(pending, observation) &&
                IsContinuation(pending, observation, capturedAt))
            {
                var selected = pending.Definition;
                // Only a fresh application appearing after a readable absence
                // may book its first countdown. Initial/late partial buffs are baselines.
                var confirmedApplication = pending.IsNewApplication && state.NewApplication is not null;
                if (confirmedApplication)
                {
                    var firstReadable = state.NewApplication!.FirstReadable ?? pending;
                    consumptions.Add(new(selected.Id, selected.Name, selected.MarketItemId,
                        firstReadable.Active.ObservedAt, firstReadable.Active.Price));
                }
                state.NewApplication = null;
                AddUsage(pending, capturedAt);
                state.Current = CreateSample(selected, observation, capturedAt,
                    pending.Active.Price ?? ReadPrice(selected, priceResolver),
                    pending.Active.IsBaseline || pending.IsNewApplication && !confirmedApplication);
                state.Pending = null;
            }
            else if (state.SeenPreviousFrame && state.Pending is null && state.Current is { } current &&
                     FitsSelectedDuration(current, observation) && IsContinuation(current, observation, capturedAt))
            {
                AddUsage(current, capturedAt);
                state.Current = CreateSample(current.Definition, observation, capturedAt,
                    current.Active.Price ?? ReadPrice(current.Definition, priceResolver), current.Active.IsBaseline);
            }
            else
            {
                // Confirm the countdown twice before recording a new appearance
                // or observed usage. Timer increases are handled above.
                var previousSample = state.Current ?? state.Pending;
                // The observation keeps the family identity for continuity, while
                // accounting uses the variant chosen for this particular cycle.
                // Only confirmed cycles retain a variant across a noisy countdown.
                var selected = state.Current is { } established ? established.Definition
                    : state.NewApplication?.FirstReadable is { } firstApplication && FitsSelectedDuration(firstApplication, observation)
                        ? firstApplication.Definition : ResolveDurationVariant(definition, observation);
                var price = previousSample is not null && previousSample.Definition.Id == selected.Id
                    ? previousSample.Active.Price
                    : ReadPrice(selected, priceResolver);
                var isNewApplication = (isNewAppearance || state.NewApplication is not null) &&
                    IsNearFullDuration(selected, observation);
                var isBaseline = previousSample is { IsNewApplication: false }
                    ? previousSample.Active.IsBaseline : !isNewApplication;
                state.Pending = CreateSample(selected, observation, capturedAt, price,
                    isBaseline) with { IsNewApplication = isNewApplication };
                if (isNewApplication)
                {
                    state.NewApplication ??= new(capturedAt);
                    state.NewApplication = state.NewApplication with
                    {
                        FirstReadable = state.NewApplication.FirstReadable ?? state.Pending,
                    };
                }
                else state.NewApplication = null;
            }
            state.SeenPreviousFrame = true;
        }

        foreach (var (id, state) in tracked)
        {
            if (seen.Contains(id)) continue;
            InterruptObservation(state);
        }

        lastCapturedAt = capturedAt;
        hasReadableBaseline = true;
        return Snapshot;
    }

    /// <summary>Preserves history and last read timers, but never charges observed time across a gap.</summary>
    public void BreakContinuity()
    {
        if (tracked.Count > 0) snapshot = null;
        tracked.Clear();
        hasReadableBaseline = false;
    }

    /// <summary>
    /// Restarts one unreadable buff's confirmation and usage while preserving
    /// its last readable timer for subsequent consumption comparisons.
    /// </summary>
    public void BreakContinuity(string buffId)
    {
        if (!string.IsNullOrWhiteSpace(buffId) && tracked.TryGetValue(buffId, out var state))
        {
            snapshot = null;
            InterruptObservation(state);
        }
    }

    public void Reset()
    {
        snapshot = null;
        BreakContinuity();
        lastCapturedAt = null;
        lastReadRemaining.Clear();
        lastReadPrecision.Clear();
        lastReadAt.Clear();
        usage.Clear();
        consumptions.Clear();
        observedIdentities.Clear();
    }

    /// <summary>Restores history and timer comparisons without resuming active timers or usage across offline time.</summary>
    public void Restore(BuffLedgerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Consumptions is null || snapshot.Usage is null || snapshot.Active is null ||
            snapshot.Consumptions.Count > 100_000 || snapshot.Usage.Count > definitions.Count || snapshot.Active.Count > definitions.Count)
            throw new ArgumentException("Invalid buff history collections.", nameof(snapshot));
        var restoredConsumptions = snapshot.Consumptions.ToArray();
        var restoredUsage = new Dictionary<string, BuffUsage>(StringComparer.Ordinal);
        foreach (var item in restoredConsumptions)
        {
            if (item is null || !IsValidIdentity(item.BuffId, item.Name, item.MarketItemId) || !IsValidPrice(item.Price))
                throw new ArgumentException("Invalid buff consumption history.", nameof(snapshot));
        }
        foreach (var item in snapshot.Usage)
        {
            if (item is null || !IsValidIdentity(item.BuffId, item.Name, item.MarketItemId) ||
                item.ObservedDuration < TimeSpan.Zero || item.ObservedDuration > MaximumRecordedDuration ||
                item.UnpricedDuration < TimeSpan.Zero || item.UnpricedDuration > item.ObservedDuration ||
                item.KnownProratedCost < 0 || item.KnownProratedCost > MaximumRecordedCost ||
                (item.ObservedDuration == item.UnpricedDuration && item.KnownProratedCost != 0) ||
                !restoredUsage.TryAdd(item.BuffId, item))
                throw new ArgumentException("Invalid buff usage history.", nameof(snapshot));
        }
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in snapshot.Active)
        {
            if (item is null || !IsValidIdentity(item.BuffId, item.Name, item.MarketItemId) ||
                item.Remaining <= TimeSpan.Zero || item.Remaining > definitions[item.BuffId].Duration * 2 ||
                !IsValidPrice(item.Price) || !activeIds.Add(item.BuffId))
                throw new ArgumentException("Invalid active buff history.", nameof(snapshot));
        }
        // Validate completely before replacing the current ledger, including when restoring untrusted JSON.
        Reset();
        consumptions.AddRange(restoredConsumptions);
        foreach (var item in restoredConsumptions) RememberIdentity(item.BuffId);
        foreach (var (id, item) in restoredUsage)
        {
            usage[id] = item;
            RememberIdentity(id);
        }
        foreach (var item in snapshot.Active)
        {
            lastReadRemaining[item.BuffId] = item.Remaining;
            RememberIdentity(item.BuffId);
        }
        // Active snapshots store concrete duration variants; map a shared
        // identity only when exactly one restored active cycle belongs to it.
        foreach (var family in definitions.Values.Where(definition => definition.DurationVariantIds is { Count: > 0 }))
        {
            var candidates = snapshot.Active.Where(item => family.DurationVariantIds.Contains(item.BuffId)).Take(2).ToArray();
            if (candidates.Length == 1) lastReadRemaining[family.Id] = candidates[0].Remaining;
        }
        lastCapturedAt = snapshot.Active.Count > 0 ? snapshot.Active.Max(item => item.ObservedAt) : null;
    }

    private bool IsValidObservation(BuffObservation observation) =>
        observation is not null && !string.IsNullOrWhiteSpace(observation.BuffId) &&
        definitions.TryGetValue(observation.BuffId, out var definition) &&
        (!definition.RequiresConsumptionConfirmation || observation.ConsumptionAttributionConfirmed) &&
        observation.Remaining > TimeSpan.Zero && observation.TimerPrecision >= TimeSpan.Zero &&
        observation.TimerPrecision <= definition.Duration &&
        observation.Remaining.Ticks - observation.TimerPrecision.Ticks <= definition.Duration.Ticks;

    private void RememberIdentity(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !observedIdentities.Add(id)) return;
        // Accounting stores concrete variants while visual observations may
        // identify the same effect by its shared family.
        foreach (var family in definitions.Values.Where(definition =>
            definition.DurationVariantIds is { Count: > 0 } ids && (definition.Id == id || ids.Contains(id))))
        {
            observedIdentities.Add(family.Id);
            observedIdentities.UnionWith(family.DurationVariantIds);
        }
    }

    private static Sample CreateSample(BuffDefinition definition, BuffObservation observation,
        DateTimeOffset capturedAt, BuffPrice? price, bool isBaseline) => new(
        definition,
        new(definition.Id, definition.Name, definition.MarketItemId,
            observation.Remaining, capturedAt, price, isBaseline), observation.TimerPrecision);

    private BuffDefinition ResolveDurationVariant(BuffDefinition family, BuffObservation observation)
    {
        if (family.DurationVariantIds is not { Count: > 0 } ids) return family;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenDurations = new HashSet<TimeSpan>();
        var candidates = new List<BuffDefinition>();
        string? recognitionGroup = null;
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || id == family.Id || !seenIds.Add(id) ||
                !definitions.TryGetValue(id, out var candidate) ||
                candidate.DurationVariantIds is not { Count: 0 } ||
                !seenDurations.Add(candidate.Duration) || candidate.Duration > family.Duration ||
                string.IsNullOrWhiteSpace(candidate.RecognitionGroup) ||
                recognitionGroup is not null && candidate.RecognitionGroup != recognitionGroup)
                return family;
            recognitionGroup = candidate.RecognitionGroup;
            candidates.Add(candidate);
        }

        var minimumDuration = observation.Remaining;
        BuffDefinition? selected = null;
        if (observation.TimerPrecision >= TimeSpan.FromHours(1))
        {
            // Whole-hour labels are floored: a newly applied three-hour buff
            // already reads "2h". Only a unique duration in that interval is identifiable.
            minimumDuration += observation.TimerPrecision;
            var matching = candidates.Where(candidate => candidate.Duration > observation.Remaining &&
                candidate.Duration <= minimumDuration).Take(2).ToArray();
            if (matching.Length > 1) return family;
            if (matching.Length == 1) selected = matching[0];
        }
        selected ??= candidates.Where(candidate => candidate.Duration >= minimumDuration)
            .MinBy(candidate => candidate.Duration);
        return selected is not null && (!selected.RequiresConsumptionConfirmation || observation.ConsumptionAttributionConfirmed)
            ? selected : family;
    }

    private bool IsNearFullDuration(BuffDefinition definition, BuffObservation observation)
    {
        var maximumDuration = observation.Remaining + maxObservationGap + observation.TimerPrecision;
        if (definition.DurationVariantIds is not { Count: > 0 } ids)
            return definition.Duration <= maximumDuration;

        // A new effect can prove consumption while its rounded hour label
        // leaves the purchased duration ambiguous. Keep that booking unpriced.
        return ids.Any(id => definitions.TryGetValue(id, out var candidate) &&
            candidate.Duration >= observation.Remaining && candidate.Duration <= maximumDuration &&
            (!candidate.RequiresConsumptionConfirmation || observation.ConsumptionAttributionConfirmed));
    }

    private bool IsHourTimerRefinement(BuffObservation observation, TimeSpan previousRemaining, DateTimeOffset capturedAt) =>
        lastReadPrecision.TryGetValue(observation.BuffId, out var previousPrecision) &&
        lastReadAt.TryGetValue(observation.BuffId, out var previousAt) &&
        previousPrecision >= TimeSpan.FromHours(1) && observation.TimerPrecision < previousPrecision &&
        observation.Remaining < previousRemaining + previousPrecision - (capturedAt - previousAt);

    private static BuffPrice? ReadPrice(BuffDefinition definition, Func<BuffDefinition, BuffPrice?> priceResolver) =>
        // Invalid or unresolved duration families never borrow a candidate's price.
        definition.DurationVariantIds is { Count: 0 } ? ValidPrice(priceResolver(definition)) : null;

    private static bool IsContinuation(Sample previous, BuffObservation observation, DateTimeOffset capturedAt)
    {
        var elapsed = capturedAt - previous.Active.ObservedAt;
        var expected = Max(TimeSpan.Zero, previous.Active.Remaining - elapsed);
        return (observation.Remaining - expected).Duration() <= Tolerance(previous, observation);
    }

    private static bool FitsSelectedDuration(Sample sample, BuffObservation observation) =>
        sample.Definition.Id == observation.BuffId || observation.Remaining <= sample.Definition.Duration;

    private static TimeSpan Tolerance(Sample previous, BuffObservation observation) =>
        Max(previous.Precision, observation.TimerPrecision) + TimerJitter;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static void InterruptObservation(TrackedBuff state)
    {
        state.SeenPreviousFrame = false;
        state.Current = null;
        state.Pending = null;
    }

    private static BuffPrice? ValidPrice(BuffPrice? price) => IsValidPrice(price) ? price : null;

    private static bool IsValidPrice(BuffPrice? price) => price is null ||
        (price.UnitPrice >= 0 && price.UnitPrice <= MaximumUnitPrice &&
         !string.IsNullOrWhiteSpace(price.Region) && price.Region.Length <= 32 && Enum.IsDefined(price.Source));

    private bool IsValidIdentity(string id, string name, int? marketItemId) =>
        !string.IsNullOrWhiteSpace(id) && definitions.TryGetValue(id, out var definition) &&
        !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && marketItemId == definition.MarketItemId;

    private void AddUsage(Sample previous, DateTimeOffset capturedAt)
    {
        var definition = previous.Definition;
        var elapsed = capturedAt - previous.Active.ObservedAt;
        var duration = elapsed < previous.Active.Remaining ? elapsed : previous.Active.Remaining;
        if (duration <= TimeSpan.Zero) return;
        var old = usage.GetValueOrDefault(definition.Id) ??
            new BuffUsage(definition.Id, definition.Name, definition.MarketItemId, TimeSpan.Zero, 0, TimeSpan.Zero);
        var price = previous.Active.Price;
        usage[definition.Id] = old with
        {
            ObservedDuration = old.ObservedDuration + duration,
            KnownProratedCost = old.KnownProratedCost + (price is null ? 0 : price.UnitPrice * duration.Ticks / definition.Duration.Ticks),
            UnpricedDuration = old.UnpricedDuration + (price is null ? duration : TimeSpan.Zero),
            HasStalePrice = old.HasStalePrice || price?.IsStale == true
        };
    }

    private sealed class TrackedBuff
    {
        public Sample? Current { get; set; }
        public Sample? Pending { get; set; }
        public NewApplicationEvidence? NewApplication { get; set; }
        public bool SeenPreviousFrame { get; set; }
    }

    private sealed record NewApplicationEvidence(DateTimeOffset FirstSeenAt, Sample? FirstReadable = null);

    private sealed record Sample(BuffDefinition Definition, BuffActive Active, TimeSpan Precision)
    {
        public bool IsNewApplication { get; init; }
    }
}
