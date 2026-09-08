#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Development;

internal static class DemoSessionGenerator
{
    internal sealed record Dataset(string[] Names, long[] Prices, Dictionary<string, decimal[]> Spots);
    internal static Dataset LoadDataset()
    {
        using var stream = typeof(DemoSessionGenerator).Assembly.GetManifestResourceStream("Development.Garmoth320.json")
            ?? throw new InvalidDataException("Offline-Demoraten fehlen.");
        return JsonSerializer.Deserialize<Dataset>(stream)!;
    }
    internal static bool IsDemo(LootHistoryEntry entry) => (entry.CharacterClass ?? "").Split('·')
        .Any(part => part.Trim().StartsWith("Demo", StringComparison.OrdinalIgnoreCase));

    internal static List<LootHistoryEntry> Generate(Dataset data, string spotId, int count, DateTimeOffset now)
    {
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count), "1 bis 100 Sessions pro Spot wählen.");
        var spots = spotId == "all" ? data.Spots.Keys.ToArray() : [spotId];
        var result = new List<LootHistoryEntry>();
        var fixedQuotes = LootPriceCatalog.FixedSnapshot("eu").Quotes;
        var quotes = data.Names.Select((name, i) => fixedQuotes.TryGetValue(name, out var quote) ? quote
            : new LootPriceQuote(name, data.Prices[i], 0, LootPriceOrigin.CachedMarket, null)).ToArray();
        var prices = new LootPriceSnapshot("eu", quotes);
        string[] classes = ["Maegu", "Dark Knight", "Warrior", "Witch", "Lahn", "Guardian"];
        foreach (var spot in spots)
        {
            var profile = LootSpotCatalog.GetRequired(spot);
            if (!data.Spots.TryGetValue(spot, out var rates)) throw new ArgumentException("Keine Offline-Raten für diesen Spot.");
            var totals = Enumerable.Range(0, count).Select(_ => new Dictionary<string, long>(StringComparer.Ordinal)).ToArray();
            for (var item = 0; item < data.Names.Length; item++)
            {
                if (rates[item] <= 0 || !profile.Allows(data.Names[item])) continue;
                // Round the combined expected drops once, then distribute whole items.
                // Rare loot remains rare; no forced one-item minimum per hour.
                var expected = (long)Math.Round(rates[item] * count, MidpointRounding.AwayFromZero);
                var weights = Enumerable.Range(0, count).Select(i => 0.85m + ((i * 7 + item * 3) % 11) * 0.03m).ToArray();
                var sum = weights.Sum(); decimal cumulative = 0; long assigned = 0;
                for (var i = 0; i < count; i++)
                {
                    cumulative += weights[i];
                    var next = (long)Math.Round(expected * cumulative / sum, MidpointRounding.AwayFromZero);
                    if (next > assigned) totals[i][data.Names[item]] = next - assigned;
                    assigned = next;
                }
            }
            for (var i = 0; i < count; i++)
            {
                var end = now.AddHours(-(i * spots.Length + Array.IndexOf(spots, spot) + 1));
                var value = SilverValuation.Calculate(totals[i], prices, new SilverTaxOptions(ValuePack: true));
                result.Add(new() { SessionId = Guid.NewGuid(), StartedAt = end.AddHours(-1), UpdatedAt = end,
                    Duration = TimeSpan.FromHours(1), SpotId = spot, CharacterClass = classes[i % classes.Length] + " · Awakening · Demo320",
                    Totals = totals[i], SilverBeforeTax = value.BeforeTax, SilverAfterTax = value.AfterTax,
                    SilverIsComplete = value.IsComplete, GarmothUploadBlocked = true });
            }
        }
        return result;
    }

    internal static int Run(string[] args)
    {
        try
        {
            if (Process.GetProcessesByName("BdoGrindTracker").Any(p => p.Id != Environment.ProcessId))
                throw new InvalidOperationException("Bitte den Tracker vor dem Erzeugen von Demo-Sessions schließen.");
            var count = int.Parse(args.Single(a => a.StartsWith("--demo-sessions=", StringComparison.Ordinal)).Split('=')[1], CultureInfo.InvariantCulture);
            var spot = args.FirstOrDefault(a => a.StartsWith("--demo-spot=", StringComparison.Ordinal))?.Split('=')[1] ?? "all";
            var path = Path.Combine(new SettingsStore().BaseDirectory, "loot-history-v1.json");
            if (File.Exists(path)) { using var check = JsonDocument.Parse(File.ReadAllText(path)); }
            var store = new LootHistoryStore(path);
            var existing = store.Load();
            var retained = args.Contains("--replace-demo-sessions") ? existing.Where(entry => !IsDemo(entry)).ToList() : existing.ToList();
            var generated = Generate(LoadDataset(), spot, count, DateTimeOffset.Now);
            if (retained.Count + generated.Count > LootHistoryStore.MaximumEntries) throw new InvalidOperationException("Zu viele Sessions: maximal 500. Alte Demo-Sessions zuerst ersetzen.");
            if (File.Exists(path)) File.Copy(path, path + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            store.Save(retained.Concat(generated));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "demo-generation.json"), JsonSerializer.Serialize(new { Generated = generated.Count, Retained = retained.Count, Total = store.Load().Count, Source = "Garmoth 320% / 100%, 2026-08-20 bis 2026-09-10", Path = path }));
            Console.WriteLine($"{generated.Count} Demo-Sessions erzeugt, {retained.Count} vorhandene Sessions erhalten.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
#endif
