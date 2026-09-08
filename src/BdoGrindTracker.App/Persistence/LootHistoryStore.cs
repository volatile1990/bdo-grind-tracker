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

    public LootHistoryStore(string historyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyPath);
        _historyPath = Path.GetFullPath(historyPath);
    }

    public IReadOnlyList<LootHistoryEntry> Load()
    {
        try
        {
            var file = new FileInfo(_historyPath);
            if (!file.Exists || file.Length > MaximumFileBytes)
                return [];

            var json = File.ReadAllText(_historyPath);
            var document = JsonSerializer.Deserialize<LootHistoryDocument>(json, JsonOptions);
            return Normalize(document?.Entries ?? []);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<LootHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var normalized = Normalize(entries);
        var directory = Path.GetDirectoryName(_historyPath)
            ?? throw new InvalidOperationException("Der Verlaufsordner ist ungültig.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _historyPath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(new LootHistoryDocument
            {
                Version = 1,
                Entries = normalized.ToList()
            }, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _historyPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // The completed history file is authoritative. A locked temporary
                // file can safely be replaced during the next save.
            }
            catch (UnauthorizedAccessException)
            {
                // Saving already reported the material failure to the caller.
            }
        }
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
                        StringComparer.OrdinalIgnoreCase)
            })
            .Where(static entry => entry.Totals.Count > 0)
            .OrderByDescending(static entry => entry.UpdatedAt)
            .DistinctBy(static entry => entry.SessionId)
            .Take(MaximumEntries)
            .ToArray();
    }

    private sealed class LootHistoryDocument
    {
        public int Version { get; init; } = 1;
        public List<LootHistoryEntry> Entries { get; init; } = [];
    }
}
