using System.Buffers.Binary;

namespace BdoGrindTracker.Core;

/// <summary>
/// Follows physical loot slots through ordered, timestamped observations. A bounded
/// beam retains competing scroll explanations until repeated readings support an
/// identity. Lifetimes are soft occlusion priors, never a timer that renews a visible
/// row. Only observed rows can emit loot. Published identities are irreversible;
/// quantities can be corrected through the existing revision protocol.
/// </summary>
public sealed class TemporalLootReconciler
{
    public const string AlgorithmName = "temporal-v2";
    public const string LegacyAlgorithmName = "temporal-v1";
    public const int SlotCount = 6;
    public const int MaximumHypotheses = 24;
    private const int MaximumReads = 32;
    private static readonly int[] Lifetimes = [1250, 1350, 1450, 1550];
    private static readonly TimeSpan MissingGrace = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SettlementDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan CaptureGap = TimeSpan.FromMilliseconds(1800);
    private readonly Dictionary<string, uint> minimumQuantities;
    private readonly bool legacyMode;
    private readonly Dictionary<Guid, Publication> published = [];
    private List<Hypothesis> beam = [];
    private DateTimeOffset? previousAt;
    private long? previousCaptureIndex;
    public long CaptureIndex { get; private set; }
    public IReadOnlyList<NormalLootReconciliationTrace> LastTrace { get; private set; } = [];

    public TemporalLootReconciler(IReadOnlyDictionary<string, uint>? minimumQuantities = null, bool legacyMode = false)
    {
        this.legacyMode = legacyMode;
        this.minimumQuantities = new(StringComparer.Ordinal);
        if (minimumQuantities is not null)
            foreach (var (name, amount) in minimumQuantities)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                if (amount is 0 or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(minimumQuantities));
                this.minimumQuantities.Add(name, amount);
            }
        Seed();
    }

    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(
        IReadOnlyList<CompanionRecognizedEntry> entries, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(entries);
        // Validate before changing state, so malformed recordings cannot poison a session.
        var observations = Normalize(entries);
        CaptureIndex++;
        LastTrace = [];
        if (previousAt is { } last && capturedAt <= last)
        {
            LastTrace = [new(CaptureIndex, capturedAt, previousCaptureIndex, 0, 0,
                [new(0, false, "temporal-stale-timestamp", null, null)], [])];
            return [];
        }

        var gap = previousAt is { } prior && capturedAt - prior > CaptureGap;
        var next = new List<Hypothesis>(beam.Count * (SlotCount + 1));
        var identities = Enumerable.Range(0, SlotCount).Select(slot => Identity(capturedAt, CaptureIndex, slot)).ToArray();
        foreach (var state in beam)
            for (var shift = 0; shift <= SlotCount; shift++)
            {
                // A positive shift must contain a real newly visible bottom row.
                // Empty frames and alignment-only evidence must not invent arrivals.
                if (shift > 0 && !observations.Take(shift).Any(IsReadable) &&
                    (legacyMode || !observations.Any(row => row?.AppearanceEvidence?.FadedPreviousSlots > 0)) &&
                    !observations.Select((row, slot) => (row, slot)).Any(pair =>
                        pair.row is { IsAlignmentAnchor: true, AlignmentPreviousSlot: { } source } &&
                        pair.slot - source == shift)) continue;
                var candidate = Advance(state, observations, identities, shift, capturedAt, gap);
                if (candidate is not null) next.Add(candidate);
            }

        if (next.Count == 0)
        {
            // Inconsistent recovery anchors are not permission to lose the session.
            // Degrade them to unknown slots, retaining all ordinary OCR observations.
            observations = observations.Select(row => row is { IsAlignmentAnchor: true }
                ? row with { IsAlignmentAnchor = false, IsPlaceholder = true } : row).ToArray();
            foreach (var state in beam)
                next.Add(Advance(state, observations, identities, 0, capturedAt, gap)!);
        }
        beam = next.GroupBy(Signature, StringComparer.Ordinal)
            .Select(group => group.MaxBy(state => state.Score)!)
            .OrderByDescending(state => state.Score).ThenBy(state => state.Shift)
            .Take(MaximumHypotheses).ToList();
        // Unknown-only observations always have a hold explanation.
        if (beam.Count == 0) throw new InvalidOperationException("No temporal row explanation survived.");
        var bestScore = beam[0].Score;
        foreach (var state in beam) state.Score -= bestScore;
        previousAt = capturedAt;
        var outputs = Publish(capturedAt, force: false);
        RecordTrace(observations, outputs, capturedAt, gap);
        previousCaptureIndex = CaptureIndex;
        Compact();
        return outputs;
    }

    /// <summary>Flushes pending evidence without forgetting visible identities (pause/resume).</summary>
    public IReadOnlyList<CompanionRecognizedEntry> Complete()
    {
        LastTrace = [];
        if (previousAt is not { } at) return [];
        var outputs = Publish(at, force: true);
        if (outputs.Count > 0) RecordTrace(new CompanionRecognizedEntry?[SlotCount], outputs, at, false);
        Compact();
        return outputs;
    }

    public void Reset()
    {
        published.Clear();
        previousAt = null;
        previousCaptureIndex = null;
        CaptureIndex = 0;
        LastTrace = [];
        Seed();
    }

    private void Seed() => beam = Lifetimes.Select(life => new Hypothesis(life)).ToList();

    private static CompanionRecognizedEntry?[] Normalize(IReadOnlyList<CompanionRecognizedEntry> entries)
    {
        if (entries.Count > SlotCount) throw new ArgumentException("At most six calibrated rows are supported.", nameof(entries));
        var rows = new CompanionRecognizedEntry?[SlotCount];
        var ordered = entries.OrderByDescending(row => row?.Y ?? int.MinValue).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var row = ordered[index] ?? throw new ArgumentException("A frame cannot contain null rows.", nameof(entries));
            var slot = row.Slot ?? index;
            if (slot is < 0 or >= SlotCount || rows[slot] is not null)
                throw new ArgumentException("Slots must be unique and between zero and five.", nameof(entries));
            if (!double.IsFinite(row.NameConfidence) || row.NameConfidence is < 0 or > 1)
                throw new ArgumentException("Name confidence must be between zero and one.", nameof(entries));
            row.AppearanceEvidence?.Validate();
            rows[slot] = row;
        }
        return rows;
    }

    private Hypothesis? Advance(Hypothesis prior, CompanionRecognizedEntry?[] observations,
        Guid[] identities, int shift, DateTimeOffset at, bool gap)
    {
        var state = new Hypothesis(prior.Lifetime)
        {
            Score = prior.Score - shift * .65,
            Retired = new(prior.Retired),
            Shift = shift,
        };
        for (var slot = 0; slot < SlotCount; slot++)
            if (prior.Rows[slot] is { } row)
            {
                var destination = slot + shift;
                if (destination >= SlotCount) state.Retired.Add(row.Retire(at));
                else state.Rows[destination] = row;
            }

        for (var slot = 0; slot < SlotCount; slot++)
        {
            var observation = observations[slot];
            var old = state.Rows[slot];
            if (!legacyMode && old is not null && slot >= shift && IsReadable(observation) &&
                observation!.AppearanceEvidence is { } appearance &&
                (appearance.FadedPreviousSlots & (1 << (slot - shift))) != 0)
                return null;
            if (observation is { IsAlignmentAnchor: true })
            {
                // Alignment is evidence of an older row, never an additional OCR vote.
                var source = observation.AlignmentPreviousSlot;
                if (old is null) continue;
                if (source is { } previousSlot && previousSlot + shift != slot) return null;
                state.Score += 2;
                state.Rows[slot] = old.SeenAnchor(at, slot);
                continue;
            }
            if (!IsReadable(observation))
            {
                if (old is null) continue;
                var missing = old.MissingFrames + 1;
                var age = (at - old.FirstSeenAt).TotalMilliseconds;
                state.Score -= age < prior.Lifetime * .7 ? 1.8 : .25;
                // One failed OCR frame or an interior gap retains its physical place.
                // Explicit placeholders are unknown observations, not proof of absence.
                var hasOlderAnchor = observations.Skip(slot + 1).Any(IsReadable);
                var absent = observation is not { IsPlaceholder: true } && !hasOlderAnchor;
                if (absent && missing >= 2 && at - old.LastSeenAt >= MissingGrace ||
                    at - old.LastSeenAt > TimeSpan.FromMilliseconds(prior.Lifetime + 1000))
                {
                    state.Retired.Add(old.Retire(at));
                    state.Rows[slot] = null;
                }
                else state.Rows[slot] = old.Missing(missing, slot);
                continue;
            }

            if (old is null)
            {
                state.Rows[slot] = Track.Start(identities[slot], observation!, at, slot);
                state.Score -= 1.2;
                // Growth above the previous top is usually scrolling, whereas a
                // recovered interior OCR hole has a previously visible older anchor.
                // Slots vacated by this candidate's scroll are real new-arrival
                // positions, not suspicious growth above the old top row.
                if ((legacyMode || slot >= shift) && slot > 0 && prior.Rows.Take(slot).Any(row => row is not null) &&
                    !prior.Rows.Skip(slot + 1).Any(row => row is not null)) state.Score -= 1.5;
                continue;
            }
            var expectedName = published.TryGetValue(old.Id, out var booking) ? booking.Name : old.Name;
            if (expectedName == observation!.Name)
            {
                state.Score += 4 * Math.Max(.5, observation.NameConfidence);
                var quantity = Limit(observation.Count, observation.QuantityBounds);
                if (quantity is { } count && old.Quantity is { } previousQuantity && count != previousQuantity)
                    state.Score -= .4; // A quantity disagreement is weak identity evidence.
            }
            else
            {
                // A published item's name cannot change. Continuing to attach
                // another item to that ID would silently discard all its votes.
                // Keep a competing OCR-error explanation, but let repeated reads
                // of a new item overcome the older path's accumulated score.
                state.Score -= !legacyMode && booking is not null ? 6 : gap ? 4 : 1.5;
            }
            state.Rows[slot] = old.Observe(observation, at, slot);
        }
        return state;
    }

    private List<CompanionRecognizedEntry> Publish(DateTimeOffset at, bool force)
    {
        var outputs = new List<CompanionRecognizedEntry>();
        var chosen = beam[0];
        foreach (var track in chosen.AllRows().OrderBy(row => row.FirstSeenAt).ThenBy(row => row.InitialSlot).ToArray())
        {
            published.TryGetValue(track.Id, out var booking);
            var reading = Vote(track, booking);
            if (reading is null) continue;
            var retired = track.RetiredAt is not null;
            var mature = retired && at - track.RetiredAt!.Value >= SettlementDelay;
            var stable = reading.NameVotes >= 2 && reading.QuantityVotes >= 2 &&
                at - track.FirstSeenAt >= MissingGrace;
            if (!force && !mature && !stable) continue;
            var competitive = beam.Where(state => state.Score >= chosen.Score - 1.5).ToArray();
            if (!force && !mature && competitive.Any(state => !state.AllRows().Any(row => row.Id == track.Id))) continue;
            var quantity = reading.Quantity;
            var estimated = false;
            if (quantity is null)
            {
                if (!force && !mature) continue;
                quantity = reading.Bounds?.Minimum ?? minimumQuantities.GetValueOrDefault(reading.Name);
                if (quantity == 0) continue;
                estimated = true;
            }
            if (booking is not null && quantity == booking.Quantity)
            {
                if (booking.Estimated && !estimated) published[track.Id] = booking with { Estimated = false };
                continue;
            }
            // Never replace a measured quantity with an unresolved estimate.
            if (booking is { Estimated: false } && estimated) continue;
            var amount = checked((int)quantity.Value);
            var revision = booking is null ? 0 : booking.Revision + 1;
            var delta = checked(amount - (booking?.Quantity ?? 0));
            published[track.Id] = new(reading.Name, amount, revision, estimated);
            outputs.Add(new(reading.Name, quantity.Value, track.LastY)
            {
                Slot = track.Slot,
                EventId = track.Id,
                Revision = revision,
                QuantityDelta = delta,
                TotalDropQuantity = amount,
                IsMinimumQuantityEstimate = estimated,
                QuantityBounds = reading.Bounds,
                NameConfidence = reading.Confidence,
                DetectedAt = track.FirstSeenAt,
            });
            // Committing an identity constrains the remaining explanations. This is
            // intentional: the session protocol permits quantity revisions, not
            // silent item changes or retrospective deletion of published drops.
            beam.RemoveAll(state => !state.AllRows().Any(row => row.Id == track.Id));
        }
        return outputs;
    }

    private VoteResult? Vote(Track track, Publication? booking)
    {
        var names = track.Reads.GroupBy(read => read.Name, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Sum(read => read.Confidence))
            .ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var name = booking?.Name ?? names.FirstOrDefault()?.Key;
        if (name is null) return null;
        var reads = track.Reads.Where(read => read.Name == name).ToArray();
        if (reads.Length == 0) return null;
        var bounds = reads.LastOrDefault(read => read.Bounds is not null)?.Bounds;
        // Spot confirmation can narrow a shared item's policy. Re-evaluate all
        // votes under the current bounds; an old majority must not override them.
        var quantities = reads.Select(read => Limit(read.Quantity ?? uint.MaxValue, bounds))
            .Where(quantity => quantity is not null).GroupBy(quantity => quantity!.Value)
            .OrderByDescending(group => group.Count())
            // Preserve a published value at a tie; initial ties conservatively choose
            // the smaller observed value, never an average that was not actually read.
            .ThenByDescending(group => booking is not null && group.Key == booking.Quantity)
            .ThenBy(group => group.Key).ToArray();
        var winner = quantities.FirstOrDefault();
        return new(name, winner?.Key, reads.Length, winner?.Count() ?? 0,
            bounds, reads.Average(read => read.Confidence));
    }

    private void Compact()
    {
        // Published retired rows are complete. Unresolved retired rows are kept for
        // only a settlement window, then dropped if no readable item/quantity exists.
        foreach (var state in beam)
            state.Retired.RemoveAll(row => published.ContainsKey(row.Id) ||
                previousAt - row.RetiredAt > TimeSpan.FromSeconds(3));
        var retained = beam.SelectMany(state => state.AllRows()).Select(row => row.Id).ToHashSet();
        foreach (var id in published.Keys.Where(id => !retained.Contains(id)).ToArray()) published.Remove(id);
    }

    private void RecordTrace(CompanionRecognizedEntry?[] observations,
        IReadOnlyList<CompanionRecognizedEntry> outputs, DateTimeOffset at, bool gap)
    {
        var state = beam[0];
        var rows = new List<NormalLootRowTrace>();
        foreach (var output in outputs)
            rows.Add(new(output.Slot, output.Y, output.Name, output.Count, output.Count,
                output.QuantityBounds, output.IsMinimumQuantityEstimate, false, 0, output.EventId,
                null, null, "temporal-evidence", output.Revision == 0 ? "counted-new" : "quantity-revised",
                output.Revision, output.QuantityDelta ?? checked((int)output.Count)));
        for (var slot = 0; slot < SlotCount; slot++)
        {
            var observation = observations[slot];
            var track = state.Rows[slot];
            if (observation is null && track is null) continue;
            if (track is not null && outputs.Any(output => output.EventId == track.Id)) continue;
            rows.Add(new(slot, observation?.Y, observation?.Name, observation is null ? null : Limit(observation.Count, null),
                track?.Quantity, observation?.QuantityBounds, false, observation is { IsPlaceholder: true },
                0, track?.Id, track?.Id, track is null ? null : slot - state.Shift,
                gap ? "temporal-capture-gap" : state.Shift == 0 ? "temporal-held" :
                    !legacyMode && observations.Any(row => row?.AppearanceEvidence?.FadedPreviousSlots > 0)
                        ? "temporal-visual-scroll" : "temporal-scroll",
                observation is { IsAlignmentAnchor: true } ? "alignment-anchor" :
                track is not null && published.ContainsKey(track.Id) ? "matched-existing" : "temporal-pending",
                track is not null && published.TryGetValue(track.Id, out var book) ? book.Revision : 0, 0)
                { MatchedPreviousTrackId = track?.Id, MatchedPreviousSlot = track is null ? null : slot - state.Shift });
        }
        LastTrace = (rows.Count == 0 ? [Array.Empty<NormalLootRowTrace>()] : rows.Chunk(32))
            .Select(part => new NormalLootReconciliationTrace(CaptureIndex, at, previousCaptureIndex, 0, 0,
                [new(state.Shift, true, $"temporal-beam:{beam.Count};life:{state.Lifetime}ms", null, null)], part)).ToArray();
    }

    private static bool IsReadable(CompanionRecognizedEntry? row) =>
        row is { IsPlaceholder: false, IsAlignmentAnchor: false };

    private static uint? Limit(uint quantity, DropQuantityBounds? bounds)
    {
        if (quantity is 0 or uint.MaxValue || quantity > int.MaxValue) return bounds?.IsFixedUnit == true ? 1u : null;
        if (bounds is null) return quantity;
        return Math.Min(Math.Max(quantity, bounds.Minimum), bounds.Maximum ?? int.MaxValue);
    }

    private static Guid Identity(DateTimeOffset at, long index, int slot)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, at.UtcTicks);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[8..], checked(index * SlotCount + slot + 1));
        return new Guid(bytes);
    }

    private static string Signature(Hypothesis state) => state.Lifetime + ":" +
        string.Join(',', state.Rows.Select(row => row?.Id.ToString("N") ?? "-")) + ":" +
        string.Join(',', state.Retired.Select(row => row.Id.ToString("N")));

    private sealed record Read(string Name, uint? Quantity, DropQuantityBounds? Bounds, double Confidence);
    private sealed record Publication(string Name, int Quantity, int Revision, bool Estimated);
    private sealed record VoteResult(string Name, uint? Quantity, int NameVotes, int QuantityVotes,
        DropQuantityBounds? Bounds, double Confidence);
    private sealed record Track(Guid Id, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt,
        int InitialSlot, int Slot, int LastY, int MissingFrames, Read[] Reads, DateTimeOffset? RetiredAt = null)
    {
        public string? Name => Reads.GroupBy(read => read.Name).OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Sum(read => read.Confidence)).FirstOrDefault()?.Key;
        public uint? Quantity => Reads.Where(read => read.Name == Name && read.Quantity is not null)
            .GroupBy(read => read.Quantity).OrderByDescending(group => group.Count()).FirstOrDefault()?.Key;
        public static Track Start(Guid id, CompanionRecognizedEntry row, DateTimeOffset at, int slot) =>
            new(id, at, at, slot, slot, row.Y, 0, [MakeRead(row)]);
        public Track Observe(CompanionRecognizedEntry row, DateTimeOffset at, int slot) => this with
        {
            LastSeenAt = at, Slot = slot, LastY = row.Y, MissingFrames = 0,
            Reads = Reads.TakeLast(MaximumReads - 1).Append(MakeRead(row)).ToArray(),
        };
        public Track Missing(int frames, int slot) => this with { MissingFrames = frames, Slot = slot };
        public Track SeenAnchor(DateTimeOffset at, int slot) => this with { LastSeenAt = at, Slot = slot, MissingFrames = 0 };
        public Track Retire(DateTimeOffset at) => this with { RetiredAt = at };
        private static Read MakeRead(CompanionRecognizedEntry row) =>
            new(row.Name, Limit(row.Count, row.QuantityBounds), row.QuantityBounds, row.NameConfidence);
    }
    private sealed class Hypothesis(int lifetime)
    {
        public int Lifetime { get; } = lifetime;
        public Track?[] Rows { get; } = new Track?[SlotCount];
        public List<Track> Retired { get; set; } = [];
        public double Score { get; set; }
        public int Shift { get; set; }
        public IEnumerable<Track> AllRows() => Rows.OfType<Track>().Concat(Retired);
    }
}
