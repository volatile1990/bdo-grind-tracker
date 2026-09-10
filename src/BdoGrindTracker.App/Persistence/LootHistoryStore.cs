using System.Text.Json;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Persistence;

internal sealed record LootHistoryEntry
{
    public required Guid SessionId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string SpotId { get; init; }
    public string? CharacterClass { get; init; }
    public required Dictionary<string, long> Totals { get; init; }
    public required decimal SilverBeforeTax { get; init; }
    public required decimal SilverAfterTax { get; init; }
    public required bool SilverIsComplete { get; init; }
    public DateTimeOffset? GarmothUploadedAt { get; init; }
    public bool GarmothUploadBlocked { get; init; }
    public string[] ManualLootItems { get; init; } = [];
    public bool GarmothLocallyModified { get; init; }
    // Corrections after a request snapshot was frozen; only a possibly committed
    // matching interval turns these into a visible remote-divergence warning.
    public Guid[] GarmothPendingCorrectionIntervals { get; init; } = [];
}

internal sealed class LootHistoryStore
{
    internal const int MaximumEntries = 500;
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _historyPath;
    public string? LoadError { get; private set; }

    public LootHistoryStore(string historyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyPath);
        _historyPath = Path.GetFullPath(historyPath);
    }

    public IReadOnlyList<LootHistoryEntry> Load()
    {
        try
        {
            using var stream = new FileStream(_historyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumFileBytes)
                throw new InvalidDataException("Die Verlaufsdatei ist größer als das unterstützte Dateilimit.");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var document = JsonSerializer.Deserialize<LootHistoryDocument>(json, JsonOptions);
            if (document is null || document.Version != 1 || document.Entries is null)
                throw new InvalidDataException("Das Format der Verlaufsdatei wird nicht unterstützt.");
            if (document.Entries.Any(entry => entry is null || entry.Totals is null))
                throw new InvalidDataException("Die Verlaufsdatei enthält einen unvollständigen Sessiondatensatz.");
            var entries = Normalize(document.Entries);
            LoadError = null;
            return entries;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            LoadError = null;
            return [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            LoadError = "Der Verlauf konnte nicht gelesen werden und wird nicht überschrieben. " +
                "Bitte prüfe die Datei " + _historyPath + " und versuche das Speichern erneut. " + exception.Message;
            return [];
        }
    }

    public void Save(IEnumerable<LootHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (LoadError is not null) throw new IOException(LoadError);
        var normalized = Normalize(entries);
        var directory = Path.GetDirectoryName(_historyPath)
            ?? throw new InvalidOperationException("Der Verlaufsordner ist ungültig.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(new LootHistoryDocument
        {
            Version = 1,
            Entries = normalized.ToList()
        }, JsonOptions);
        AtomicFile.WriteAllText(_historyPath, json);
    }

    private static IReadOnlyList<LootHistoryEntry> Normalize(IEnumerable<LootHistoryEntry> entries)
    {
        var validSpotIds = LootSpotCatalog.Spots
            .Select(static spot => spot.Id)
            .ToHashSet(StringComparer.Ordinal);

        return entries
            .Where(entry => entry is not null &&
                entry.SessionId != Guid.Empty &&
                entry.Duration > TimeSpan.Zero &&
                validSpotIds.Contains(entry.SpotId))
            .Select(static entry => entry with
            {
                CharacterClass = string.IsNullOrWhiteSpace(entry.CharacterClass)
                    ? null
                    : entry.CharacterClass.Trim(),
                Totals = entry.Totals
                    .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value >= 0)
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase),
                ManualLootItems = (entry.ManualLootItems ?? [])
                    .Where(name => !string.IsNullOrWhiteSpace(name) && entry.Totals.ContainsKey(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                GarmothPendingCorrectionIntervals = (entry.GarmothPendingCorrectionIntervals ?? [])
                    .Where(id => id != Guid.Empty).Distinct().ToArray(),
            })
            .Where(static entry => entry.Totals.Count > 0)
            .OrderByDescending(static entry => entry.UpdatedAt)
            .DistinctBy(static entry => entry.SessionId)
            .Take(MaximumEntries)
            .ToArray();
    }

    private sealed class LootHistoryDocument
    {
        public required int Version { get; init; }
        public required List<LootHistoryEntry> Entries { get; init; }
    }
}
