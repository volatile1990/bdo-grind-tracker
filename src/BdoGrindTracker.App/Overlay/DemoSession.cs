using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Overlay;

/// <summary>
/// Example data for the overlay editor: a real Hermesia session (Shai, 18 September 2026, 1:34 h active time,
/// seven complete rotations), valued with that day's market prices and tax settings. The tracker keeps no drop
/// times after a session, so the drop timeline spreads each item irregularly over the active hour in which the
/// session counted it; totals, prices and rotations are the recorded values.
/// </summary>
internal static class DemoSession
{
    internal const string Date = "18.09.2026";
    private const string Trash = "Black Crystal Fragment";
    private static readonly DateTimeOffset PricedAt = new(2026, 9, 18, 15, 14, 41, TimeSpan.Zero);
    internal static readonly SilverTaxOptions Tax = new(true, true, 24_000);

    internal static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(5661.995);
    internal const int ConfirmedEventCount = 3691;
    internal const decimal ExperienceGainedPercentagePoints = 0.762m;
    internal static readonly TimeSpan ExperienceObservedDuration = TimeSpan.FromSeconds(3244.274);
    internal const int ExperienceLevel = 65;

    // Name, session total, count after the first active hour, taxable and untaxed unit price that day.
    private static readonly (string Name, long Total, long FirstHour, decimal Taxable, decimal Untaxed, LootPriceOrigin Origin)[] Items =
    [
        ("Black Crystal Fragment", 24438, 16001, 0m, 160539m, LootPriceOrigin.FixedCatalog),
        ("Ancient Spirit Dust", 76, 56, 151200m, 0m, LootPriceOrigin.DerivedMarket),
        ("Black Stone", 147, 95, 129000m, 0m, LootPriceOrigin.CachedMarket),
        ("BON Origin Shard", 15, 12, 0m, 15000000m, LootPriceOrigin.FixedCatalog),
        ("Caphras Stone", 37, 26, 885000m, 0m, LootPriceOrigin.CachedMarket),
        ("Corrupt Oil of Immortality", 3, 3, 17100000m, 0m, LootPriceOrigin.CachedMarket),
        ("Fusion Shard", 1, 1, 29100000m, 0m, LootPriceOrigin.CachedMarket),
        ("Nev's Fragment", 1, 1, 46600000m, 0m, LootPriceOrigin.CachedMarket),
        ("Violet Primordial Luster - Edana", 3, 2, 108000000m, 0m, LootPriceOrigin.CachedMarket),
        ("Crimson Primordial Luster - Sovereign", 1, 0, 108000000m, 0m, LootPriceOrigin.CachedMarket),
        ("Laila's Petal", 1, 0, 0m, 500000m, LootPriceOrigin.FixedCatalog),
        ("Twilight of the End - Ring", 4, 3, 515000000m, 0m, LootPriceOrigin.CachedMarket),
        ("Refined Essence of Devouring", 1, 1, 765000000m, 0m, LootPriceOrigin.CachedMarket),
    ];

    // Start (seconds after the first rotation), duration and recorded messages of each completed rotation.
    internal static readonly (double StartedAt, RotationRun Run)[] Rotations =
    [
        (0, Run(640.726,
            ("offer", 0), ("offer", 17.045), ("offer", 50.989), ("offer", 69.246), ("offer", 89.397), ("drakania", 99.72),
            ("drakania-kill", 141.086), ("transfer", 141.692), ("mine-enter", 145.974), ("offer", 154.508), ("offer", 180.7), ("offer", 199.504),
            ("offer", 217.161), ("offer", 235.442), ("mine-second", 245.77), ("offer", 253.122), ("offer", 275.014), ("offer", 293.918),
            ("offer", 312.228), ("offer", 331.068), ("mine-cleared", 340.796), ("mine-enter", 344.514), ("offer", 351.854), ("offer", 373.164),
            ("offer", 394.509), ("offer", 413.401), ("offer", 432.293), ("mine-second", 444.504), ("offer", 451.815), ("offer", 470.113),
            ("offer", 488.352), ("offer", 505.415), ("offer", 522.543), ("dragon", 532.295), ("porter", 544.518), ("afk", 579.267))),
        (1089.835, Run(650.007,
            ("offer", 0), ("offer", 21.38), ("offer", 40.837), ("offer", 64.647), ("offer", 84.756), ("drakania", 96.302),
            ("drakania-kill", 130.44), ("transfer", 130.964), ("mine-enter", 134.681), ("offer", 141.921), ("offer", 158.432), ("offer", 192.528),
            ("offer", 212.556), ("offer", 232.077), ("mine-second", 243.653), ("offer", 252.246), ("offer", 270.485), ("offer", 286.937),
            ("offer", 304.641), ("offer", 325.962), ("mine-cleared", 336.302), ("mine-enter", 339.926), ("offer", 350.895), ("offer", 369.876),
            ("offer", 388.121), ("offer", 413.754), ("offer", 433.205), ("mine-second", 445.845), ("offer", 455.567), ("offer", 479.982),
            ("offer", 497.663), ("offer", 513.538), ("offer", 533.042), ("dragon", 543.406), ("porter", 555.593), ("afk", 589.084))),
        (1757.534, Run(607.017,
            ("offer", 0), ("offer", 15.187), ("offer", 32.856), ("offer", 52.93), ("offer", 72.399), ("drakania", 83.98),
            ("drakania-kill", 115.053), ("transfer", 116.295), ("mine-enter", 118.677), ("offer", 124.782), ("offer", 142.463), ("offer", 161.308),
            ("offer", 184.97), ("offer", 205.662), ("mine-second", 218.396), ("offer", 228.766), ("offer", 247.023), ("offer", 263.462),
            ("offer", 282.378), ("offer", 301.241), ("mine-cleared", 312.728), ("mine-enter", 315.834), ("offer", 326.786), ("offer", 345.619),
            ("offer", 365.076), ("offer", 383.998), ("offer", 402.825), ("mine-second", 412.536), ("offer", 422.365), ("offer", 439.951),
            ("offer", 455.761), ("offer", 471.701), ("offer", 491.787), ("dragon", 500.263), ("porter", 511.856), ("afk", 546.035))),
        (3025.134, Run(622.774,
            ("offer", 0), ("offer", 17.069), ("offer", 35.844), ("offer", 58.318), ("offer", 78.381), ("drakania", 90.562),
            ("drakania-kill", 126.341), ("transfer", 127.376), ("mine-enter", 129.858), ("offer", 137.098), ("offer", 158.373), ("offer", 175.989),
            ("offer", 192.44), ("offer", 210.65), ("offer", 229.972), ("mine-second", 241.547), ("offer", 251.199), ("offer", 268.633),
            ("offer", 286.913), ("offer", 317.916), ("mine-cleared", 327.045), ("mine-enter", 330.15), ("offer", 338.638), ("offer", 356.871),
            ("offer", 374.463), ("offer", 390.268), ("offer", 411.624), ("mine-second", 423.706), ("offer", 433.446), ("offer", 452.344),
            ("offer", 469.331), ("offer", 485.776), ("offer", 504.604), ("dragon", 515.908), ("porter", 526.878), ("afk", 561.545))),
        (3662.548, Run(634.561,
            ("offer", 0), ("offer", 14.575), ("offer", 29.762), ("offer", 47.366), ("offer", 66.746), ("drakania", 77.128),
            ("drakania-kill", 111.095), ("transfer", 111.713), ("mine-enter", 114.095), ("offer", 121.429), ("offer", 139.016), ("offer", 157.843),
            ("offer", 175.459), ("offer", 195.493), ("mine-second", 204.704), ("offer", 216.232), ("offer", 232.648), ("offer", 252.111),
            ("offer", 268.362), ("offer", 288.389), ("mine-cleared", 299.865), ("mine-enter", 304.211), ("offer", 310.199), ("offer", 358.33),
            ("offer", 376.028), ("offer", 397.943), ("offer", 422.206), ("mine-second", 431.417), ("offer", 438.016), ("offer", 456.744),
            ("offer", 467.631), ("offer", 485.917), ("offer", 507.262), ("dragon", 517.014), ("porter", 527.931), ("afk", 573.656))),
        (4313.331, Run(601.288,
            ("offer", 0), ("offer", 15.193), ("offer", 32.891), ("offer", 49.925), ("offer", 68.864), ("drakania", 81.057),
            ("drakania-kill", 116.342), ("transfer", 116.342), ("mine-enter", 119.447), ("offer", 127.923), ("offer", 146.827), ("offer", 163.272),
            ("offer", 184.617), ("offer", 201.669), ("mine-second", 211.373), ("offer", 220.584), ("offer", 238.818), ("offer", 254.11),
            ("offer", 269.915), ("offer", 292.195), ("mine-cleared", 302.012), ("mine-enter", 308.117), ("offer", 315.434), ("offer", 336.702),
            ("offer", 351.913), ("offer", 369.564), ("offer", 387.756), ("mine-second", 397.461), ("offer", 410.307), ("offer", 426.1),
            ("offer", 443.127), ("offer", 465.013), ("offer", 485.582), ("dragon", 493.575), ("porter", 504.545), ("afk", 539.806))),
        (4928.73, Run(617.475,
            ("offer", 0), ("offer", 15.822), ("offer", 33.391), ("offer", 48.601), ("offer", 66.87), ("drakania", 80.08),
            ("drakania-kill", 122.553), ("transfer", 122.553), ("mine-enter", 131.005), ("offer", 139.504), ("offer", 159.579), ("offer", 177.289),
            ("offer", 194.975), ("offer", 215.685), ("mine-second", 227.26), ("offer", 234.601), ("offer", 250.44), ("offer", 261.998),
            ("offer", 281.431), ("offer", 302.147), ("mine-cleared", 310.664), ("mine-enter", 314.31), ("offer", 323.545), ("offer", 341.737),
            ("offer", 357.583), ("offer", 374.657), ("offer", 394.014), ("mine-second", 407.942), ("offer", 417.177), ("offer", 437.834),
            ("offer", 454.05), ("offer", 469.954), ("offer", 487.617), ("dragon", 498.598), ("porter", 509.55), ("afk", 555.893))),
    ];

    internal static IReadOnlyDictionary<string, long> Totals { get; } =
        Items.ToDictionary(item => item.Name, item => item.Total, StringComparer.Ordinal);

    internal static LootPriceSnapshot Prices { get; } = new("eu",
        Items.Select(item => new LootPriceQuote(item.Name, item.Taxable, item.Untaxed, item.Origin, PricedAt)), PricedAt);

    internal static IReadOnlyList<SessionDropSample> DropHistory { get; } = CreateDropHistory();

    private static RotationRun Run(double duration, params (string Kind, double Seconds)[] messages)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        RotationEvent Event(string kind, double seconds) =>
            new(kind, Label(kind), seconds, occurrences[kind] = occurrences.GetValueOrDefault(kind) + 1);
        return new RotationRun(duration, [Event("start", 0), .. messages.Select(m => Event(m.Kind, m.Seconds)), Event("end", duration)])
            { TimingVersion = 2 };
    }

    private static string Label(string kind) => kind switch
    {
        "start" => "Rotationsstart", "offer" => "Opfergabe angeordnet", "drakania" => "Drakania-Spawn",
        "drakania-kill" => "Drakania besiegt", "transfer" => "Minenrechte übertragen", "mine-enter" => "Mine betreten",
        "mine-second" => "Zweite Minenphase", "mine-cleared" => "Mine abgeschlossen", "dragon" => "Drachen-Spawn",
        "porter" => "Träger-Spawn", "afk" => "AFK-Beginn", _ => "AFK-Ende",
    };

    private static SessionDropSample[] CreateDropHistory()
    {
        var random = new Random(918);
        var hour = TimeSpan.FromHours(1);
        var drops = new List<SessionDropSample>();
        foreach (var (from, to, firstHour) in new[] { (TimeSpan.Zero, hour, true), (hour, Elapsed, false) })
        {
            // A pickup every one to two seconds, like the session's 3,691 confirmed drops.
            var pickups = new List<TimeSpan>();
            for (var at = from + TimeSpan.FromSeconds(random.NextDouble()); at < to; at += TimeSpan.FromSeconds(1 + random.NextDouble() * 1.1))
                pickups.Add(at);
            foreach (var (item, itemIndex) in Items.Select((item, itemIndex) => (item, itemIndex)))
            {
                var quantity = firstHour ? item.FirstHour : item.Total - item.FirstHour;
                if (quantity <= 0) continue;
                if (item.Name != Trash)
                {
                    // Spread over the hour; a golden-ratio offset per item keeps different rare drops apart.
                    var offset = itemIndex * .618 % 1;
                    for (var unit = 0; unit < quantity; unit++)
                    {
                        var at = from + (to - from) * (((unit + offset + (random.NextDouble() - .5) * .3) / quantity % 1 + 1) % 1);
                        drops.Add(new(pickups.MinBy(pickup => (pickup - at).Duration()), item.Name, 1));
                    }
                    continue;
                }
                // Trash arrives with every pickup in varying amounts; rounding the running share keeps the exact total.
                var weights = pickups.Select(_ => .5 + random.NextDouble()).ToArray();
                var sum = weights.Sum();
                double share = 0;
                long assigned = 0;
                for (var index = 0; index < pickups.Count; index++)
                {
                    share += weights[index];
                    var next = (long)Math.Round(share / sum * quantity);
                    if (next > assigned) drops.Add(new(pickups[index], item.Name, next - assigned));
                    assigned = next;
                }
            }
        }
        return drops.OrderBy(drop => drop.Elapsed).ToArray();
    }

    /// <summary>Completed rotations before the latest one, with the walk back to the following start.</summary>
    internal static IReadOnlyList<SessionRotationTiming> CompletedBefore(int count) => Rotations.Take(count).Select((rotation, index) =>
    {
        var gap = Rotations[index + 1].StartedAt - rotation.StartedAt - rotation.Run.Duration;
        // Longer gaps were breaks in the session, not the way back.
        return new SessionRotationTiming(rotation.Run.Duration, gap <= 120 ? Math.Max(0, gap) : null);
    }).ToArray();

    /// <summary>The same rotations on the session's own time axis, with their mechanics for the session timeline.</summary>
    internal static IReadOnlyList<SessionRotationTiming> OnSessionAxis(int count) =>
        CompletedBefore(count).Select((timing, index) => timing with
        {
            StartedAfter = TimeSpan.FromSeconds(Rotations[index].StartedAt), Events = Rotations[index].Run.Events,
        }).ToArray();

    /// <summary>The same rotations on a session timeline that ends at <paramref name="observedAt"/>.</summary>
    internal static IReadOnlyList<SessionRotationTiming> CompletedAt(DateTimeOffset observedAt, int count) =>
        CompletedBefore(count).Select((timing, index) => timing with
        {
            StartedAt = observedAt - Elapsed + TimeSpan.FromSeconds(Rotations[index].StartedAt),
        }).ToArray();
}
