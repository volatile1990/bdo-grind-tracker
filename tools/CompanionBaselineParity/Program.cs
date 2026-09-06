using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using BdoGrindTracker.Core;

// This is an offline migration check against our own unmodified, verified 0.5.1 assembly.
// It never opens an application, captures the screen, or inspects another process.
const string expectedHash = "8978479DDAC7705291AF0FF3D86C58A6A90A9FD8B596C0789AE172877D65AB2B";
string baselinePath = Path.GetFullPath(args.Length > 0
    ? args[0]
    : "artifacts/current/BdoGrindTracker.Core.dll");
using (FileStream stream = File.OpenRead(baselinePath))
{
    string actualHash = Convert.ToHexString(SHA256.HashData(stream));
    if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Baseline is not the verified 0.5.1 Core DLL: {actualHash}");
    }
}

using Baseline baseline = new(baselinePath);
string[] names =
[
    "Broken Vestige of Ebonmere", "Broken Vestige of Goldroot", "Broken Vestige of Everlight",
    "BON Origin Shard", "JIN Origin Shard", "WON Origin Shard", "Black Crystal Fragment",
    "Black Gem Fragment", "Dawn Crystal", "Fortunate Golden Pig King", "Caphras Stone",
    "Apeiron Ring", "Apeiron Earring", "Twilight's End Ring", "Silent Crystal of Origin",
    "Crystal of Origin", "Decayed Cloth", "Bronze Fragment of Delusion",
];
CompanionRareCatalogEntry[] catalog = names.Select(name => new CompanionRareCatalogEntry(name,
    name.EndsWith("Ring", StringComparison.Ordinal) ? "new_icon/16_ring/example.dds" :
    name.EndsWith("Earring", StringComparison.Ordinal) ? "new_icon/17_earring/example.dds" : null)).ToArray();
int checks = 0;

void Compare(string context, object? expected, object? actual)
{
    checks++;
    string expectedText = Baseline.Describe(expected);
    string actualText = Baseline.Describe(actual);
    if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{context}\nBaseline: {expectedText}\nRestored: {actualText}");
    }
}

// Native ten-frame overlap, frame-tag wrap, prefix reordering, OCR gaps, missing
// quantities, full panels, partial flushes, scaled Y coordinates, and resets.
for (int seed = 0; seed < 40; seed++)
{
    Random random = new(seed);
    CompanionFrameReconciler current = new();
    object previous = baseline.New("CompanionFrameReconciler");
    List<CompanionRecognizedEntry> visible = [];
    int scale = seed % 3 + 1;
    for (int frame = 0; frame < 250; frame++)
    {
        int mode = random.Next(10);
        if (mode < 5)
        {
            for (int insert = random.Next(1, 5); insert > 0; insert--)
            {
                visible.Insert(0, new CompanionRecognizedEntry(
                    names[random.Next(names.Length)], (uint)random.Next(1, 65)));
            }
            visible = visible.Take(6).ToList();
        }
        else if (mode == 9)
        {
            visible.Clear();
        }

        List<CompanionRecognizedEntry> observed = [];
        for (int slot = 0; slot < visible.Count; slot++)
        {
            if (random.Next(12) == 0)
            {
                continue;
            }
            CompanionRecognizedEntry entry = visible[slot];
            uint count = random.Next(15) == 0 ? uint.MaxValue : entry.Count;
            observed.Add(new CompanionRecognizedEntry(entry.Name, count, (250 - slot * 50) * scale));
        }

        Compare($"normal seed={seed} frame={frame}",
            baseline.Frame(previous, "CompanionRecognizedEntry", observed.Select(entry =>
                new object?[] { entry.Name, entry.Count, entry.Y })),
            current.ProcessFrame(observed));
        if (frame % 37 == 0)
        {
            Compare($"normal partial flush seed={seed} frame={frame}",
                baseline.Call(previous, "Complete"), current.Complete());
        }
        if (frame == 173)
        {
            baseline.Call(previous, "Reset");
            current.Reset();
        }
    }
    Compare($"normal complete seed={seed}", baseline.Call(previous, "Complete"), current.Complete());
    Compare($"normal repeated complete seed={seed}", baseline.Call(previous, "Complete"), current.Complete());
}

// Rare mode's independent recent-frame/support rules and signed alias/gear
// corrections use the same shared normal-loot ledger as 0.5.1.
for (int seed = 0; seed < 30; seed++)
{
    Random random = new(1000 + seed);
    CompanionLootLedger ledger = new();
    object oldLedger = baseline.New("CompanionLootLedger");
    CompanionRareFrameReconciler current = new(catalog, ledger);
    object previous = baseline.New("CompanionRareFrameReconciler", baseline.Catalog(catalog), oldLedger);
    string name = names[3];
    for (int frame = 0; frame < 200; frame++)
    {
        if (random.Next(5) < 2)
        {
            name = names[random.Next(names.Length)];
        }
        if (random.Next(7) == 0)
        {
            string normalName = names[random.Next(names.Length)];
            long count = random.Next(1, 35);
            ledger.Add(normalName, count);
            baseline.Call(oldLedger, "Add", normalName, count);
        }

        List<CompanionRareRecognizedEntry> observed = [];
        if (random.Next(5) > 0)
        {
            observed.Add(new CompanionRareRecognizedEntry(name,
                random.Next(13) == 0 ? -1 : random.Next(1, 5), random.Next(2) * 50,
                suppressCounting: random.Next(11) == 0));
        }
        if (random.Next(9) == 0)
        {
            observed.Add(new CompanionRareRecognizedEntry(names[random.Next(names.Length)], 1, 250));
        }
        Compare($"rare seed={seed} frame={frame}",
            baseline.Frame(previous, "CompanionRareRecognizedEntry", observed.Select(entry =>
                new object?[] { entry.Name, entry.Count, entry.Y, entry.SuppressCounting })),
            current.ProcessFrame(observed));
        Compare($"rare ledger seed={seed} frame={frame}",
            baseline.Property(oldLedger, "Totals"), ledger.Totals);
        Compare($"rare support seed={seed} frame={frame}",
            baseline.Property(previous, "Support"), current.Support);
        Compare($"rare last seen seed={seed} frame={frame}",
            baseline.Property(previous, "LastSeen"), current.LastSeen);
        Compare($"rare frame index seed={seed} frame={frame}",
            baseline.Property(previous, "FrameIndex"), current.FrameIndex);
        if (frame % 31 == 0)
        {
            Compare($"rare partial flush seed={seed} frame={frame}",
                baseline.Call(previous, "Complete"), current.Complete());
        }
        if (frame == 147)
        {
            baseline.Call(previous, "Reset");
            current.Reset();
            Compare($"rare reset preserves shared ledger seed={seed}",
                baseline.Property(oldLedger, "Totals"), ledger.Totals);
        }
    }
    Compare($"rare complete seed={seed}", baseline.Call(previous, "Complete"), current.Complete());
    Compare($"rare repeated complete seed={seed}", baseline.Call(previous, "Complete"), current.Complete());
    Compare($"rare final ledger seed={seed}", baseline.Property(oldLedger, "Totals"), ledger.Totals);
}

// Exercise both metadata-backed and plain-name construction. Candidate scores,
// exact matches, count-one trash, related rare candidates and BON penalty remain
// the original rules, not the retired spot/confidence matcher.
for (int metadataMode = 0; metadataMode < 2; metadataMode++)
{
    CompanionItemMatcher current = metadataMode == 1 ? new(catalog) : new(names);
    object previous = baseline.New("CompanionItemMatcher",
        metadataMode == 1 ? baseline.Catalog(catalog) : names);
    Random random = new(8722);
    for (int sample = 0; sample < 2500; sample++)
    {
        string observed = names[random.Next(names.Length)];
        int changes = random.Next(5);
        for (int change = 0; change < changes; change++)
        {
            int position = random.Next(observed.Length + 1);
            observed = random.Next(4) switch
            {
                0 when position < observed.Length => observed.Remove(position, 1),
                1 when position < observed.Length => observed[..position] + "?" + observed[(position + 1)..],
                2 => observed.Insert(position, "ö"),
                _ => observed.Insert(position, " "),
            };
        }
        if (sample % 9 == 0)
        {
            observed = "You obtained " + observed + " today";
        }
        int count = sample % 3 == 0 ? 1 : 25;
        bool rareMode = sample % 2 == 0;
        object?[] arguments = [observed, count, rareMode, null];
        object? result = baseline.CallArguments(previous, "TryMatch", arguments);
        bool matched = current.TryMatch(observed, count, rareMode, out CompanionItemMatch? match);
        Compare($"matcher decision mode={metadataMode} sample={sample}", result, matched);
        Compare($"matcher result mode={metadataMode} sample={sample}", arguments[3], match);
    }
}

// Ledger validation and mutations are part of the behavioral contract too.
{
    CompanionLootLedger current = new();
    object previous = baseline.New("CompanionLootLedger");
    foreach (string name in names)
    {
        for (long count = 1; count <= 3; count++)
        {
            current.Add(name, count);
            baseline.Call(previous, "Add", name, count);
        }
        for (int removal = 0; removal < 8; removal++)
        {
            Compare($"ledger removal {name}/{removal}", baseline.Call(previous, "TryRemoveOne", name),
                current.TryRemoveOne(name));
        }
    }
    Compare("ledger final totals", baseline.Property(previous, "Totals"), current.Totals);
    baseline.Call(previous, "Reset");
    current.Reset();
    Compare("ledger reset", baseline.Property(previous, "Totals"), current.Totals);
}

Console.WriteLine($"PASS: {checks:N0} exact comparisons against verified 0.5.1 Core; " +
    "10,000 normal frames, 6,000 rare frames, 5,000 matcher inputs, ledger and resets.");
Console.WriteLine($"Baseline SHA256: {expectedHash}");
return 0;

sealed class Baseline : IDisposable
{
    private readonly AssemblyLoadContext context = new("VerifiedCompanion051", isCollectible: true);
    private readonly Assembly assembly;

    public Baseline(string path) => assembly = context.LoadFromAssemblyPath(path);

    public object New(string type, params object?[] arguments) =>
        Activator.CreateInstance(Type(type), arguments) ?? throw new InvalidOperationException(type);

    public object? Call(object target, string method, params object?[] arguments) =>
        CallArguments(target, method, arguments);

    public object? CallArguments(object target, string method, object?[] arguments) =>
        target.GetType().GetMethod(method)!.Invoke(target, arguments);

    public object? Property(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target);

    public object Catalog(IEnumerable<CompanionRareCatalogEntry> entries) => Array("CompanionRareCatalogEntry",
        entries.Select(entry => new object?[] { entry.Name, entry.IconPath }));

    public object? Frame(object target, string entryType, IEnumerable<object?[]> entries) =>
        Call(target, "ProcessFrame", Array(entryType, entries));

    private Array Array(string type, IEnumerable<object?[]> entries)
    {
        object?[][] values = entries.ToArray();
        Array result = System.Array.CreateInstance(Type(type), values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            result.SetValue(New(type, values[index]), index);
        }
        return result;
    }

    private Type Type(string name) => assembly.GetType("BdoGrindTracker.Core." + name, throwOnError: true)!;

    public static string Describe(object? value)
    {
        if (value is null)
        {
            return "null";
        }
        if (value is string text)
        {
            return "\"" + text + "\"";
        }
        if (value is IDictionary dictionary)
        {
            List<string> items = [];
            foreach (DictionaryEntry entry in dictionary)
            {
                items.Add(Describe(entry.Key) + ":" + Describe(entry.Value));
            }
            return "{" + string.Join(",", items.Order(StringComparer.Ordinal)) + "}";
        }
        if (value is IEnumerable enumerable)
        {
            return "[" + string.Join(",", enumerable.Cast<object?>().Select(Describe)) + "]";
        }
        if (value.GetType().IsPrimitive || value is decimal)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        }
        return "{" + string.Join(",", value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => property.Name + ":" + Describe(property.GetValue(value)))) + "}";
    }

    public void Dispose() => context.Unload();
}
