namespace BdoGrindTracker.Core.Buffs;

/// <summary>
/// Accounts for confirmed visual buff observations. Full consumable costs and the cost of observed
/// running time are separate. Each buff counts once when its first observed countdown
/// is confirmed; every later readable timer increase counts an additional use.
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
    private readonly Dictionary<string, BuffUsage> usage = new(StringComparer.Ordinal);
    private readonly List<BuffConsumption> consumptions = [];
    private readonly HashSet<string> bookedIdentities = new(StringComparer.Ordinal);
    private readonly TimeSpan maxObservationGap;
    private DateTimeOffset? lastCapturedAt;
    private BuffLedgerSnapshot? snapshot;
    private bool applying;

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
        foreach (var id in unknown) BreakContinuity(id);

        // A duplicate identity is ambiguous (for example two similar-looking icons), not two items consumed.
        var validObservations = observations.Where(IsValidObservation)
            .GroupBy(item => item.BuffId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
        // A completely empty scan has no other readable buff to establish a
        // local gap. Preserve the hard break used for an unavailable HUD.
        if (validObservations.Length == 0 && unknown.Count == 0) BreakContinuity();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in validObservations)
        {
            seen.Add(observation.BuffId);
            var definition = definitions[observation.BuffId];
            if (!tracked.TryGetValue(observation.BuffId, out var state))
                tracked.Add(observation.BuffId, state = new());

            var isRenewal = lastReadRemaining.TryGetValue(observation.BuffId, out var previousRemaining) &&
                observation.Remaining > previousRemaining;
            lastReadRemaining[observation.BuffId] = observation.Remaining;
            if (isRenewal)
            {
                // The reader has already established the timer. A strict increase
                // is a new use immediately, including reapplication after death
                // or a capture gap; equal rounded labels never count again.
                var selected = ResolveDurationVariant(definition, observation);
                var price = ReadPrice(selected, priceResolver);
                MarkBookedIdentity(observation.BuffId);
                MarkBookedIdentity(selected.Id);
                consumptions.Add(new(selected.Id, selected.Name, selected.MarketItemId, capturedAt, price));
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
                // First confirmation belongs to this buff, not the first global
                // scan: one initially missed icon must not lose its initial use.
                var isSessionStart = !bookedIdentities.Contains(observation.BuffId);
                if (isSessionStart)
                {
                    consumptions.Add(new(selected.Id, selected.Name, selected.MarketItemId,
                        pending.Active.ObservedAt, pending.Active.Price) { IsSessionStart = true });
                    MarkBookedIdentity(observation.BuffId);
                    MarkBookedIdentity(selected.Id);
                }
                AddUsage(pending, capturedAt);
                state.Current = CreateSample(selected, observation, capturedAt,
                    pending.Active.Price ?? ReadPrice(selected, priceResolver), pending.Active.IsBaseline);
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
                // Confirm the initial/baseline countdown twice before recording
                // startup or observed usage. Timer increases are handled above.
                var previousSample = state.Current ?? state.Pending;
                // The observation keeps the family identity for continuity, while
                // accounting uses the variant chosen for this particular cycle.
                // Only confirmed cycles retain a variant across a noisy countdown.
                var selected = state.Current is { } established
                    ? established.Definition : ResolveDurationVariant(definition, observation);
                var price = previousSample is not null && previousSample.Definition.Id == selected.Id
                    ? previousSample.Active.Price
                    : ReadPrice(selected, priceResolver);
                state.Pending = CreateSample(selected, observation, capturedAt, price, previousSample?.Active.IsBaseline ?? true);
            }
            state.SeenPreviousFrame = true;
        }

        foreach (var (id, state) in tracked)
        {
            if (seen.Contains(id)) continue;
            InterruptObservation(state);
        }

        lastCapturedAt = capturedAt;
        return Snapshot;
    }

    /// <summary>Preserves history and last read timers, but never charges observed time across a gap.</summary>
    public void BreakContinuity()
    {
        if (tracked.Count > 0) snapshot = null;
        tracked.Clear();
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
        usage.Clear();
        consumptions.Clear();
        bookedIdentities.Clear();
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
        foreach (var item in restoredConsumptions) MarkBookedIdentity(item.BuffId);
        foreach (var (id, item) in restoredUsage) usage[id] = item;
        foreach (var item in snapshot.Active)
            lastReadRemaining[item.BuffId] = item.Remaining;
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

    private void MarkBookedIdentity(string id)
    {
        if (!bookedIdentities.Add(id)) return;
        // Accounting stores the purchased duration variant. A later scan or
        // restored session may identify the same effect by its shared family.
        foreach (var family in definitions.Values.Where(definition =>
            definition.DurationVariantIds is { Count: > 0 } ids && ids.Contains(id)))
            bookedIdentities.Add(family.Id);
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
        string? recognitionGroup = null;
        BuffDefinition? selected = null;
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
            if (candidate.Duration >= observation.Remaining &&
                (selected is null || (family.PreferMaximumDurationVariant
                    ? candidate.Duration > selected.Duration : candidate.Duration < selected.Duration))) selected = candidate;
        }
        return selected is not null && (!selected.RequiresConsumptionConfirmation || observation.ConsumptionAttributionConfirmed)
            ? selected : family;
    }

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
        public bool SeenPreviousFrame { get; set; }
    }

    private sealed record Sample(BuffDefinition Definition, BuffActive Active, TimeSpan Precision);
}
