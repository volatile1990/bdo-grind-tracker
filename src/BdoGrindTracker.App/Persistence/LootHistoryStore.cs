using System.Buffers;
using System.Text.Json;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Persistence;

internal sealed class LootHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _historyPath;
    internal const int MaximumCachedEntries = 4096;
    internal const long MaximumCacheBytes = 8 * 1024 * 1024;
    private const int MaximumCachedEntryBytes = 512 * 1024;
    private static readonly HashSet<string> ValidSpotIds = LootSpotCatalog.Spots
        .Select(static spot => spot.Id).ToHashSet(StringComparer.Ordinal);
    private readonly Dictionary<Guid, CachedEntry> _cache = [];
    internal long CachedBytes { get; private set; }
    internal int CachedEntryCount => _cache.Count;
    internal long SerializedEntryCount { get; private set; }
    public string? LoadError { get; private set; }

    private sealed record CachedEntry(LootHistoryEntry Source, LootHistoryEntry Normalized,
        byte[] Json, long Bytes);

    public LootHistoryStore(string historyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyPath);
        _historyPath = Path.GetFullPath(historyPath);
    }

    public IReadOnlyList<LootHistoryEntry> Load()
    {
        try
        {
            using var stream = new FileStream(_historyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var document = JsonSerializer.Deserialize<LootHistoryDocument>(stream, JsonOptions);
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
            System.Diagnostics.Trace.TraceWarning("History load failed for {0}: {1} (0x{2:X8}).",
                Path.GetFileName(_historyPath), exception.GetType().Name, exception.HResult);
            LoadError = "Der Verlauf konnte nicht gelesen werden und wird nicht überschrieben. " +
                "Bitte prüfe die Datei " + _historyPath + " und versuche das Speichern erneut.";
            return [];
        }
    }

    public void Save(IEnumerable<LootHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (LoadError is not null) throw new IOException(LoadError);
        var normalized = entries.Where(IsValid)
            .Select(entry => (Source: entry, Normalized: FindCached(entry)?.Normalized ?? NormalizeEntry(entry)))
            .Where(pair => HasContent(pair.Normalized))
            .OrderByDescending(pair => pair.Normalized.UpdatedAt)
            .DistinctBy(pair => pair.Normalized.SessionId).ToArray();
        var retained = normalized.Select(pair => pair.Source.SessionId).ToHashSet();
        foreach (var id in _cache.Keys.Where(id => !retained.Contains(id)).ToArray()) RemoveCached(id);
        var directory = Path.GetDirectoryName(_historyPath)
            ?? throw new InvalidOperationException("Der Verlaufsordner ist ungültig.");
        Directory.CreateDirectory(directory);

        AtomicFile.Write(_historyPath, stream =>
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = JsonOptions.WriteIndented });
            writer.WriteStartObject();
            writer.WriteNumber(nameof(LootHistoryDocument.Version), 1);
            writer.WriteStartArray(nameof(LootHistoryDocument.Entries));
            foreach (var (source, entry) in normalized)
            {
                var cached = FindCached(source);
                if (cached is not null) writer.WriteRawValue(cached.Json, skipInputValidation: true);
                else WriteEntry(writer, source, entry);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private CachedEntry? FindCached(LootHistoryEntry entry)
    {
        if (!_cache.TryGetValue(entry.SessionId, out var cached)) return null;
        if (entry.Totals is not null && LootHistoryEntrySnapshot.Matches(entry, cached.Source)) return cached;
        RemoveCached(entry.SessionId);
        return null;
    }

    private void WriteEntry(Utf8JsonWriter writer, LootHistoryEntry source, LootHistoryEntry normalized)
    {
        SerializedEntryCount++;
        // Large sessions still stream normally; cache admission never limits
        // the amount of history that can be saved or restored.
        var estimate = LootHistoryEntrySnapshot.EstimateBytes(source);
        var comparer = source.Totals.Comparer;
        if (!(ReferenceEquals(comparer, StringComparer.Ordinal) || ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase) ||
                ReferenceEquals(comparer, EqualityComparer<string>.Default)) ||
            _cache.Count >= MaximumCachedEntries || estimate > MaximumCachedEntryBytes ||
            CachedBytes + estimate >= MaximumCacheBytes)
        {
            JsonSerializer.Serialize(writer, normalized, JsonOptions);
            return;
        }
        var bytes = SerializeEntry(normalized);
        var weight = estimate + bytes.Length * 3L;
        if (bytes.Length > MaximumCachedEntryBytes || CachedBytes + weight > MaximumCacheBytes)
        {
            writer.WriteRawValue(bytes, skipInputValidation: true);
            return;
        }
        var snapshot = LootHistoryEntrySnapshot.Copy(source);
        _cache.Add(source.SessionId, new(snapshot, normalized, bytes, weight));
        CachedBytes += weight;
        writer.WriteRawValue(bytes, skipInputValidation: true);
    }

    private static byte[] SerializeEntry(LootHistoryEntry entry)
    {
        // Serialize at the document's actual depth, retaining the serializer's
        // exact escaping and indentation. The reader finds the value boundaries.
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = JsonOptions.WriteIndented }))
        {
            writer.WriteStartArray();
            writer.WriteStartArray();
            JsonSerializer.Serialize(writer, entry, JsonOptions);
            writer.WriteEndArray();
            writer.WriteEndArray();
        }
        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        reader.Read();
        reader.Read();
        var start = checked((int)reader.BytesConsumed);
        reader.Read();
        reader.Skip();
        return buffer.WrittenSpan.Slice(start, checked((int)reader.BytesConsumed) - start).ToArray();
    }

    private void RemoveCached(Guid id)
    {
        var cached = _cache[id];
        _cache.Remove(id);
        CachedBytes -= cached.Bytes;
    }

    public void ClearCache()
    {
        _cache.Clear();
        CachedBytes = 0;
    }

    private static bool IsValid(LootHistoryEntry entry) => entry is not null &&
        entry.SessionId != Guid.Empty && entry.Duration >= TimeSpan.Zero && ValidSpotIds.Contains(entry.SpotId);

    private static bool HasContent(LootHistoryEntry entry) =>
        entry.Totals.Count > 0 || entry.Rotations.Count > 0 || entry.RotationTimeline.Count > 0;

    private static IReadOnlyList<LootHistoryEntry> Normalize(IEnumerable<LootHistoryEntry> entries) => entries
        .Where(IsValid).Select(NormalizeEntry).Where(HasContent)
        .OrderByDescending(static entry => entry.UpdatedAt)
        .DistinctBy(static entry => entry.SessionId).ToArray();

    private static LootHistoryEntry NormalizeEntry(LootHistoryEntry entry) =>
        NormalizeExperience(NormalizeAgrisDurations(entry with
            {
                Rotations = entry.Rotations ?? [],
                RotationTimeline = entry.RotationTimeline ?? [],
                CombatStats = CombatStatsSpotRules.ForSpot(entry.CombatStats, entry.SpotId),
                DropHistory = SessionDropHistory.Normalize(entry.DropHistory, entry.Duration, entry.Totals),
                Pauses = SessionPauses.Normalize(entry.Pauses, entry.Duration),
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
            }));

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
