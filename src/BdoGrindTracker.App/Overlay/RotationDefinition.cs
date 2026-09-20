using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Overlay;

// A spot describes its ordered mechanics, optional branches and messages, not recovery policy.
internal sealed record RotationStep(string Id, string[] Messages, bool Optional = false, bool Afk = false,
    string? Requires = null, bool RequiredWhenBranchObserved = false)
{
    internal bool Matches(string kind) => Messages.Contains(kind);
}

internal sealed record RotationDefinition(string SpotId, RotationStep[] Steps, string[] StartMessages,
    string[] AfkEndMessages, string[] FailureMessages, bool LootStart = true,
    string[]? SetupMessages = null, string[]? SetupCountMessages = null,
    string[]? AmbientMessages = null, string? AmbientAfter = null, int FailureSetupDelta = 0,
    string? StartupCounterMessage = null, int StartupCounterTarget = 0, string StartupCounterLabel = "",
    int SetupTarget = 3)
{
    internal static readonly RotationDefinition EventHorizon = new(LootSpotCatalog.EventHorizonId,
        [.. Enumerable.Range(1, 3).SelectMany(i => (RotationStep[]) [
            new($"wormhole-{i}-waves", ["anomaly"]),
            new($"wormhole-{i}-mobs", ["halted"]),
            new($"wormhole-{i}-reception", ["reception"], true),
            new($"wormhole-{i}-debris", ["debris"], true, true),
            new($"wormhole-{i}-distortion", ["distortion"], true, true, $"wormhole-{i}-debris"),
            new($"wormhole-{i}-resumed", ["spacetime"], true, false, $"wormhole-{i}-debris", true),
            .. (i < 3 ? new RotationStep[] { new($"wormhole-{i + 1}-approach", ["expansion"]) } : []) ]),
            new("boss", ["boss"]), new("afk", ["boss-kill"], Afk: true)], [], ["end"], []);

    internal static readonly RotationDefinition Aphrodon = new(LootSpotCatalog.AphrodonId,
        [.. Enumerable.Range(1, 9).SelectMany(i => (RotationStep[]) [
            new($"wave-{i}", ["hog", "agris"]), new($"scarecrow-{i}", ["big-scarecrow"], true) ]),
            new("afk", ["afk"], Afk: true)], ["restart"], ["end"], ["failure"],
        SetupMessages: ["setup"], SetupCountMessages: ["small-scarecrow"], FailureSetupDelta: -1);

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

    internal static RotationDefinition For(string spotId) => spotId switch {
        LootSpotCatalog.HermesiaId => Hermesia, LootSpotCatalog.AphrodonId => Aphrodon,
        LootSpotCatalog.EventHorizonId => EventHorizon, _ => throw new ArgumentException("Unknown rotation spot", nameof(spotId)) };
}
