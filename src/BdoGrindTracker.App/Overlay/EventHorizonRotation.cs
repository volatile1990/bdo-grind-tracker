namespace BdoGrindTracker.App.Overlay;

/// <summary>Compatibility name and legacy record validator. Runtime rules live in RotationDefinition.</summary>
internal sealed class EventHorizonRotationTracker(string? path = null)
    : RotationPlatform(RotationDefinition.EventHorizon, path)
{
    internal const int Wormholes = 3;
    internal new static string DefaultPath => RotationPlatform.DefaultPath(BdoGrindTracker.Core.LootSpotCatalog.EventHorizonId);
    private static readonly string[] MessageOrder =
        ["anomaly", "halted", "expansion", "anomaly", "halted", "expansion", "anomaly", "halted", "boss", "boss-kill"];

    /// <summary>
    /// A complete rotation: all three wormhole starts, the boss kill and the AFK end, with every recognized
    /// wormhole, activation and boss banner in its place. Only these define the timing; any of the others can be
    /// hidden by a window or a black screen without shortening the measured time.
    /// </summary>
    internal static bool Valid(RotationRun run)
    {
        if (run?.Events is not { Count: > 2 and < 1000 } events || !double.IsFinite(run.Duration) || run.Duration is <= 0 or >= 7200 ||
            events.Any(e => e is null || string.IsNullOrWhiteSpace(e.Kind) || string.IsNullOrWhiteSpace(e.Label) ||
                !double.IsFinite(e.Seconds) || e.Seconds < 0 || e.Seconds > run.Duration) ||
            events[0].Kind != "start" || events[0].Seconds != 0 || events[^1].Kind != "end" || events[^1].Seconds != run.Duration ||
            events.Select(e => e.Key).Distinct().Count() != events.Count ||
            events.Zip(events.Skip(1)).Any(pair => pair.First.Seconds > pair.Second.Seconds))
            return false;
        if (events.Count(e => e.Kind == "anomaly") != Wormholes || events.Count(e => e.Kind == "boss-kill") != 1) return false;
        var position = 0;
        foreach (var kind in events.Select(e => e.Kind).Where(MessageOrder.Contains))
        {
            while (position < MessageOrder.Length && MessageOrder[position] != kind) position++;
            if (position++ >= MessageOrder.Length) return false;
        }
        return true;
    }

}
