using System.Text.Json;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Persistence;

internal sealed class LootHistoryStore
{
    internal const int MaximumEntries = 500;
    private const long MaximumFileBytes = 64 * 1024 * 1024;
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
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
            throw new InvalidDataException("Die Verlaufsdatei ist größer als das unterstützte Dateilimit.");
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
                entry.Duration >= TimeSpan.Zero &&
                validSpotIds.Contains(entry.SpotId))
            .Select(static entry => entry with
            {
                Rotations = entry.Rotations ?? [],
                RotationTimeline = entry.RotationTimeline ?? [],
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
            .Select(NormalizeAgrisDurations)
            .Select(NormalizeExperience)
            .Where(static entry => entry.Totals.Count > 0 || entry.Rotations.Count > 0 || entry.RotationTimeline.Count > 0)
            .OrderByDescending(static entry => entry.UpdatedAt)
            .DistinctBy(static entry => entry.SessionId)
            .Take(MaximumEntries)
            .ToArray();
    }

    private static LootHistoryEntry NormalizeAgrisDurations(LootHistoryEntry entry)
    {
        if (entry.AgrisActiveDuration is not { } active || entry.AgrisObservedDuration is not { } observed)
            return entry with { AgrisActiveDuration = null, AgrisObservedDuration = null };
        observed = TimeSpan.FromTicks(Math.Clamp(observed.Ticks, 0, entry.Duration.Ticks));
        active = TimeSpan.FromTicks(Math.Clamp(active.Ticks, 0, observed.Ticks));
        return entry with { AgrisActiveDuration = active, AgrisObservedDuration = observed };
    }

    private static LootHistoryEntry NormalizeExperience(LootHistoryEntry entry)
    {
        var observed = entry.ExperienceObservedDuration is { } duration
            ? TimeSpan.FromTicks(Math.Clamp(duration.Ticks, 0, entry.Duration.Ticks))
            : (TimeSpan?)null;
        if (entry.ExperienceGainedPercentagePoints is null || observed is null || observed <= TimeSpan.Zero ||
            entry.ExperienceStartLevel is not (>= 1 and <= 100) || entry.ExperienceEndLevel is not (>= 1 and <= 100))
            return entry with
            {
                ExperienceGainedPercentagePoints = null,
                ExperienceObservedDuration = observed is null ? null : TimeSpan.Zero,
                ExperienceStartLevel = null,
                ExperienceEndLevel = null,
            };
        return entry with { ExperienceObservedDuration = observed };
    }

    private sealed class LootHistoryDocument
    {
        public required int Version { get; init; }
        public required List<LootHistoryEntry> Entries { get; init; }
    }
}
