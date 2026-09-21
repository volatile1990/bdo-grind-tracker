namespace BdoGrindTracker.Core.Buffs;

/// <summary>
/// Accounts for confirmed visual buff observations. Full consumable costs and the cost of observed
/// running time are separate: a buff already running at the start costs running time but is not a new purchase.
/// </summary>
public sealed class BuffLedger
{
    private static readonly TimeSpan TimerJitter = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumRecordedDuration = TimeSpan.FromDays(3650);
    private const decimal MaximumUnitPrice = 100_000_000_000_000m;
    private const decimal MaximumRecordedCost = 100_000_000_000_000_000_000m;
    private readonly Dictionary<string, BuffDefinition> definitions;
    private readonly Dictionary<string, TrackedBuff> tracked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuffUsage> usage = new(StringComparer.Ordinal);
    private readonly List<BuffConsumption> consumptions = [];
    private readonly TimeSpan maxObservationGap;
    private DateTimeOffset? lastCapturedAt;

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

    public BuffLedgerSnapshot Snapshot => new(
        consumptions.ToArray(),
        usage.Values.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray(),
        tracked.Values.Where(item => item.SeenPreviousFrame && item.Pending is null && item.Current is not null)
            .Select(item => item.Current!.Active).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray());

    /// <summary>
    /// Feed one complete, readable scan. An empty list means no supported buffs were visible.
    /// Call <see cref="BreakContinuity"/> for unreadable capture, pause, or leaving the game.
    /// </summary>
    public BuffLedgerSnapshot Apply(
        IEnumerable<BuffObservation> observations,
        DateTimeOffset capturedAt,
        Func<BuffDefinition, BuffPrice?> priceResolver)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(priceResolver);
        // Delayed or duplicate frames cannot establish confirmation or move the clock backwards.
        if (lastCapturedAt is { } previous && capturedAt <= previous) return Snapshot;
        if (lastCapturedAt is { } last && capturedAt - last > maxObservationGap) BreakContinuity();
        var baselineFrame = lastCapturedAt is null;

        // A duplicate identity is ambiguous (for example two similar-looking icons), not two items consumed.
        var validObservations = observations.Where(IsValidObservation).GroupBy(item => item.BuffId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in validObservations)
        {
            seen.Add(observation.BuffId);
            var definition = definitions[observation.BuffId];
            if (!tracked.TryGetValue(observation.BuffId, out var state))
                tracked.Add(observation.BuffId, state = new());

            if (state.SeenPreviousFrame && state.Pending is { } pending && IsContinuation(pending.Sample, observation, capturedAt))
            {
                if (pending.IsConsumption)
                {
                    consumptions.Add(new(definition.Id, definition.Name, definition.MarketItemId,
                        pending.Sample.Active.ObservedAt, pending.Sample.Active.Price));
                }
                AddUsage(definition, pending.Sample, capturedAt);
                state.Current = CreateSample(definition, observation, capturedAt,
                    pending.Sample.Active.Price ?? ValidPrice(priceResolver(definition)), pending.Sample.Active.IsBaseline);
                state.Pending = null;
            }
            else if (state.SeenPreviousFrame && state.Pending is null && state.Current is { } current &&
                     IsContinuation(current, observation, capturedAt))
            {
                AddUsage(definition, current, capturedAt);
                state.Current = CreateSample(definition, observation, capturedAt,
                    current.Active.Price ?? ValidPrice(priceResolver(definition)), current.Active.IsBaseline);
            }
            else
            {
                // Confirm discontinuities twice. A single OCR spike must never create a consumption.
                // Reappearance of the same countdown after a missed frame preserves its purchase price.
                var previousSample = state.Current ??
                    (state.Pending is { Sample.Active.IsBaseline: true } baseline ? baseline.Sample : null);
                var isConsumption = previousSample is not null
                    ? IsClearRefresh(previousSample, observation, capturedAt)
                    : !baselineFrame && IsNearFullDuration(definition, observation);
                var price = previousSample is not null && !isConsumption
                    ? previousSample.Active.Price
                    : state.Pending is { } prior && prior.IsConsumption == isConsumption
                        ? prior.Sample.Active.Price
                        : ValidPrice(priceResolver(definition));
                var isBaseline = previousSample is not null && !isConsumption
                    ? previousSample.Active.IsBaseline
                    : !isConsumption;
                state.Pending = new(CreateSample(definition, observation, capturedAt, price, isBaseline), isConsumption);
            }
            state.SeenPreviousFrame = true;
        }

        foreach (var (id, state) in tracked)
        {
            if (seen.Contains(id)) continue;
            state.SeenPreviousFrame = false;
            // Preserve evidence that a buff predated the session even if its second reading was missed.
            // Confirmation still requires two consecutive visible readings after it reappears.
            if (state.Current is not null || state.Pending?.Sample.Active.IsBaseline != true)
                state.Pending = null;
        }

        lastCapturedAt = capturedAt;
        return Snapshot;
    }

    /// <summary>Preserves booked history but starts fresh confirmation without charging time across a gap.</summary>
    public void BreakContinuity()
    {
        tracked.Clear();
        lastCapturedAt = null;
    }

    public void Reset()
    {
        BreakContinuity();
        usage.Clear();
        consumptions.Clear();
    }

    /// <summary>Restores booked history. Persisted active timers are deliberately not resumed across offline time.</summary>
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
        foreach (var (id, item) in restoredUsage) usage[id] = item;
    }

    private bool IsValidObservation(BuffObservation observation) =>
        observation is not null && !string.IsNullOrWhiteSpace(observation.BuffId) &&
        definitions.TryGetValue(observation.BuffId, out var definition) &&
        (!definition.RequiresConsumptionConfirmation || observation.ConsumptionAttributionConfirmed) &&
        observation.Remaining > TimeSpan.Zero && observation.TimerPrecision >= TimeSpan.Zero &&
        observation.TimerPrecision <= definition.Duration &&
        observation.Remaining.Ticks - observation.TimerPrecision.Ticks <= definition.Duration.Ticks;

    private bool IsNearFullDuration(BuffDefinition definition, BuffObservation observation) =>
        definition.Duration - observation.Remaining <= maxObservationGap + observation.TimerPrecision + TimerJitter;

    private static Sample CreateSample(BuffDefinition definition, BuffObservation observation,
        DateTimeOffset capturedAt, BuffPrice? price, bool isBaseline) => new(
        new(definition.Id, definition.Name, definition.MarketItemId,
            observation.Remaining, capturedAt, price, isBaseline), observation.TimerPrecision);

    private static bool IsContinuation(Sample previous, BuffObservation observation, DateTimeOffset capturedAt)
    {
        var elapsed = capturedAt - previous.Active.ObservedAt;
        var expected = Max(TimeSpan.Zero, previous.Active.Remaining - elapsed);
        return (observation.Remaining - expected).Duration() <= Tolerance(previous, observation);
    }

    private static bool IsClearRefresh(Sample previous, BuffObservation observation, DateTimeOffset capturedAt)
    {
        var expected = Max(TimeSpan.Zero, previous.Active.Remaining - (capturedAt - previous.Active.ObservedAt));
        return observation.Remaining - expected > Tolerance(previous, observation);
    }

    private static TimeSpan Tolerance(Sample previous, BuffObservation observation) =>
        Max(previous.Precision, observation.TimerPrecision) + TimerJitter;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
    private static BuffPrice? ValidPrice(BuffPrice? price) => IsValidPrice(price) ? price : null;

    private static bool IsValidPrice(BuffPrice? price) => price is null ||
        (price.UnitPrice >= 0 && price.UnitPrice <= MaximumUnitPrice &&
         !string.IsNullOrWhiteSpace(price.Region) && price.Region.Length <= 32 && Enum.IsDefined(price.Source));

    private bool IsValidIdentity(string id, string name, int? marketItemId) =>
        !string.IsNullOrWhiteSpace(id) && definitions.TryGetValue(id, out var definition) &&
        !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && marketItemId == definition.MarketItemId;

    private void AddUsage(BuffDefinition definition, Sample previous, DateTimeOffset capturedAt)
    {
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
        public PendingConfirmation? Pending { get; set; }
        public bool SeenPreviousFrame { get; set; }
    }

    private sealed record Sample(BuffActive Active, TimeSpan Precision);
    private sealed record PendingConfirmation(Sample Sample, bool IsConsumption);
}
