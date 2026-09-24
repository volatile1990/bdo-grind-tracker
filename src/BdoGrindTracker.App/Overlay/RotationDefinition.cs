using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Overlay;

// A spot describes its ordered mechanics, optional branches and messages, not recovery policy.
/// <param name="Midpoint">
/// Seconds each half of the step's branch usually lasts, when its message reliably marks the middle of that branch
/// (Event Horizon's "Distortion escalated" inside the debris mini AFK). A loading screen can swallow the branch's opening
/// and closing banners but never this one: seen alone, it fills in the unread opening, and the unread closing once the
/// branch is over.
/// </param>
internal sealed record RotationStep(string Id, string[] Messages, bool Optional = false, bool Afk = false,
    string? Requires = null, bool RequiredWhenBranchObserved = false, double Midpoint = 0)
{
    internal bool Matches(string kind) => Messages.Contains(kind);
}

/// <summary>How rotations with special events are compared with each other.</summary>
internal enum SpecialEventComparison
{
    /// <summary>Rotations with a special event form their own pool; the setting decides which pool is shown.</summary>
    Separate,
    /// <summary>Only rotations with the same number of special events, else the nearest higher, else the nearest lower.</summary>
    ByCount,
    /// <summary>Special events are counted, but they never change a rotation's length: every rotation is compared.</summary>
    Ignored,
}

internal sealed record RotationDefinition(string SpotId, RotationStep[] Steps, string[] StartMessages,
    string[] AfkEndMessages, string[] FailureMessages, bool LootStart = true,
    string[]? SetupMessages = null, string[]? SetupCountMessages = null,
    string[]? AmbientMessages = null, string? AmbientAfter = null, int FailureSetupDelta = 0,
    string? StartupCounterMessage = null, int StartupCounterTarget = 0, string StartupCounterLabel = "",
    int SetupTarget = 3, string[]? SpecialMessages = null, string[]? SpecialStartMessages = null,
    SpecialEventComparison SpecialComparison = SpecialEventComparison.Separate, bool AfkEndStartsRun = false,
    IReadOnlyDictionary<string, string[]>? Evidence = null)
{
    /// <summary>
    /// Messages that say where a rotation stands without being part of its order (monster names), with the steps
    /// they can be seen in. They neither start, move nor abort a run; a rotation picked up mid-way uses them to find
    /// its place (<see cref="RotationAlignment"/>).
    /// </summary>
    internal bool IsEvidence(string kind) => Evidence?.ContainsKey(kind) == true;

    /// <summary>Rotations with a special event count as special and can be left out of the comparison.</summary>
    internal bool MarksSpecialRotations => HasSpecialEvents && SpecialComparison == SpecialEventComparison.Separate;

    /// <summary>
    /// Special events are mechanics a full rotation does not need. They appear at random, either in addition to the
    /// regular mechanics (Event Horizon's debris mini AFK) or in place of one (Aphrodon's Agris wave). All their
    /// messages are special; each start message counts one occurrence.
    /// </summary>
    internal bool IsSpecial(string kind) => SpecialMessages?.Contains(kind) == true;
    internal bool HasSpecialEvents => SpecialStartMessages is { Length: > 0 };
    internal int SpecialEventCount(IEnumerable<RotationEvent> events) =>
        SpecialStartMessages is { Length: > 0 } starts ? events.Count(e => starts.Contains(e.Kind)) : 0;
    /// <summary>When each special event occurred, in seconds from the rotation's start.</summary>
    internal IReadOnlyList<double> SpecialEventSeconds(IEnumerable<RotationEvent> events) =>
        SpecialStartMessages is { Length: > 0 } starts ? [.. events.Where(e => starts.Contains(e.Kind)).Select(e => e.Seconds)] : [];

    /// <summary>
    /// Messages that can mean nothing but the start of a fresh rotation, watched for before a session begins. A
    /// message the rotation itself uses as a step or as an ambient banner (Hermesia's offering appears about twenty
    /// times per rotation) says nothing about where a rotation began and never qualifies.
    /// </summary>
    internal string[] UnmistakableStartMessages => [.. StartMessages.Where(kind =>
        !Steps.Any(step => step.Matches(kind)) && AmbientMessages?.Contains(kind) != true)];
    /// <summary>A step shared by a regular and a replacing special message keeps separate section references for both.</summary>
    internal string SectionId(RotationStep step, string kind) =>
        IsSpecial(kind) && step.Messages.Any(message => !IsSpecial(message)) ? step.Id + "-special" : step.Id;

    internal static readonly RotationDefinition EventHorizon = new(LootSpotCatalog.EventHorizonId,
        [.. Enumerable.Range(1, 3).SelectMany(i => (RotationStep[]) [
            new($"wormhole-{i}-waves", ["anomaly"]),
            new($"wormhole-{i}-mobs", ["halted"]),
            new($"wormhole-{i}-reception", ["reception"], true),
            new($"wormhole-{i}-debris", ["debris"], true, true),
            new($"wormhole-{i}-distortion", ["distortion"], true, true, $"wormhole-{i}-debris", Midpoint: 20),
            new($"wormhole-{i}-resumed", ["spacetime"], true, false, $"wormhole-{i}-debris", true),
            .. (i < 3 ? new RotationStep[] { new($"wormhole-{i + 1}-approach", ["expansion"]) } : []) ]),
            new("boss", ["boss"]), new("afk", ["boss-kill"], Afk: true)], [], ["end"], [],
        SpecialMessages: ["debris", "distortion", "spacetime"], SpecialStartMessages: ["debris"],
        // Every debris mini AFK lengthens the rotation, so rotations are compared with those that had as many.
        SpecialComparison: SpecialEventComparison.ByCount);

    internal static readonly RotationDefinition Aphrodon = new(LootSpotCatalog.AphrodonId,
        [.. Enumerable.Range(1, 9).SelectMany(i => (RotationStep[]) [
            new($"wave-{i}", ["hog", "agris"]), new($"scarecrow-{i}", ["big-scarecrow"], true) ]),
            new("afk", ["afk"], Afk: true)], ["restart"], ["end"], ["failure"],
        SetupMessages: ["setup"], SetupCountMessages: ["small-scarecrow"], FailureSetupDelta: -1,
        SpecialMessages: ["agris"], SpecialStartMessages: ["agris"]);

    internal static readonly RotationDefinition Hermesia = new(LootSpotCatalog.HermesiaId,
        [.. Enumerable.Range(1, 5).SelectMany(i => (RotationStep[]) [
            new($"offering-{i}", ["offer"]), new($"porter-{i}", ["porter"], true) ]),
            new("drakania", ["drakania"]), new("drakania-kill", ["drakania-kill"]), new("transfer", ["transfer"]),
            .. Enumerable.Range(1, 2).SelectMany(i => (RotationStep[]) [
                new($"mine-{i}", ["mine-enter"]), new($"mine-{i}-second", ["mine-second"], true),
                new($"mine-{i}-cleared", ["mine-cleared"], i == 2) ]),
            new("dragon", ["dragon"]), new("afk", ["afk"], Afk: true)], ["offer"], ["mine-cleared"], ["failure"],
        AmbientMessages: ["offer", "porter"], AmbientAfter: "drakania", StartupCounterMessage: "offer", StartupCounterTarget: 5,
        StartupCounterLabel: "Opfergaben");

    // One Magaia rotation is three cycles whose final mechanics always follow each other: Elion's Tears, Unbroken Oath,
    // Priest of the End. "The sinners are summoned" starts it at the brazier. After each cycle's AFK phase "The history
    // of sin begins to repeat itself once more" starts the next cycle; after the third it ends the rotation and starts
    // the next one. Elion's Tears ("Sacred power …") is required: it anchors the first cycle, so tracking that began
    // in another cycle realigns there. Aetos' fragments are special events of almost every cycle; the phases they appear in
// are fixed in time, so they never change a rotation's length and every rotation is compared. Death or leaving
    // ("… begins to fade") and the return ("The voice of the speaker …") are only marked. The third knight of a cycle
    // does not have to fall before its final phase begins.
    internal const int MagaiaCycles = 3;
    internal static readonly RotationDefinition Magaia = new(LootSpotCatalog.MagaiaId,
        [.. Enumerable.Range(1, MagaiaCycles).SelectMany(c => (RotationStep[]) [
            new($"cycle-{c}-prayer", ["prayer"]),
            new($"cycle-{c}-knight-1", ["knight"]), new($"cycle-{c}-knight-2", ["knight"]),
            // The final phase can begin before the third knight falls.
            new($"cycle-{c}-knight-3", ["knight"], Optional: true),
            new($"cycle-{c}-doubt", ["doubt"]),
            .. (c == 1 ? new RotationStep[] { new("cycle-1-tears", ["sacred"]) } : []),
            new($"cycle-{c}-afk", ["afk"], Afk: true),
            .. (c < MagaiaCycles ? new RotationStep[] { new($"cycle-{c + 1}", ["end"]) } : []) ])],
        // Magaia always starts with a banner, never with loot: a rotation that began with the first trash loot
        // would measure from that kill instead of the brazier and could never be a fair best time.
        ["start"], ["end"], ["failure"], LootStart: false, AmbientMessages: ["fragment", "away", "back"],
        SpecialMessages: ["fragment"], SpecialStartMessages: ["fragment"], SpecialComparison: SpecialEventComparison.Ignored,
        AfkEndStartsRun: true,
        // The name bar shows Elion's Tear and Priest of the End while they are fought, and a while into the AFK phase.
        // Priest of the End may already be the target when the final phase's banner was not read.
        Evidence: new Dictionary<string, string[]>
        {
            ["tear"] = ["cycle-1-doubt", "cycle-1-tears", "cycle-1-afk"],
            ["priest"] = ["cycle-3-knight-3", "cycle-3-doubt", "cycle-3-afk"],
        });

    internal static RotationDefinition? Find(string? spotId) => spotId switch {
        LootSpotCatalog.HermesiaId => Hermesia, LootSpotCatalog.AphrodonId => Aphrodon,
        LootSpotCatalog.EventHorizonId => EventHorizon, LootSpotCatalog.MagaiaId => Magaia, _ => null };

    internal static RotationDefinition For(string spotId) => Find(spotId) ?? throw new ArgumentException("Unknown rotation spot", nameof(spotId));
}
