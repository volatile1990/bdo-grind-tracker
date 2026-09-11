using System.Text.Json;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Persistence;

internal sealed record CurrentSessionSnapshot
{
    public required Guid SessionId { get; init; }
    public required DateTimeOffset? StartedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string? SpotId { get; init; }
    public required string? CharacterClassId { get; init; }
    public required bool SessionSubmitted { get; init; }
    public required Dictionary<string, long> Totals { get; init; }
    public required int ConfirmedEventCount { get; init; }
    public required string[] ManualLootItems { get; init; }
    public required bool GarmothLocallyModified { get; init; }
    public required TimeSpan AgrisActiveDuration { get; init; }
    public required TimeSpan AgrisObservedDuration { get; init; }
    public required decimal? ExperienceGainedPercentagePoints { get; init; }
    public required TimeSpan ExperienceObservedDuration { get; init; }
    public required int? ExperienceStartLevel { get; init; }
    public required int? ExperienceEndLevel { get; init; }
    public required string GameLanguage { get; init; }
    public required string? MonitorDeviceName { get; init; }
    public required bool RecordLoot { get; init; }
    public GarmothUploadState? Uploads { get; init; }
}

/// <summary>Explicit current-session checkpoint; historical sessions are never inferred as current.</summary>
internal sealed class CurrentSessionStore(string path)
{
    internal const string FileName = "current-session-v1.json";
    internal static readonly TimeSpan UploadClockTolerance = TimeSpan.FromMilliseconds(250);
    private const int MaximumFileBytes = 8 * 1024 * 1024;
    private readonly string _path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string? LoadError { get; private set; }

    public CurrentSessionSnapshot? Load()
    {
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumFileBytes) throw new InvalidDataException("Die Sitzungsdatei ist zu groß.");
            var document = JsonSerializer.Deserialize<Document>(file, JsonOptions);
            if (document is null || document.Version != 1)
                throw new InvalidDataException("Das Format der Sitzungsdatei wird nicht unterstützt.");
            var snapshot = document.Session is null ? null : Validate(document.Session);
            LoadError = null;
            return snapshot;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            LoadError = null;
            return null;
        }
        catch (Exception error) when (error is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or OverflowException)
        {
            LoadError = "Die aktuelle Session konnte nicht gelesen werden und wird nicht überschrieben. " +
                "Bitte prüfe die Datei " + _path + ". " + error.Message;
            return null;
        }
    }

    public void Save(CurrentSessionSnapshot? snapshot)
    {
        if (LoadError is not null) throw new IOException(LoadError);
        var document = new Document { Version = 1, Session = snapshot is null ? null : Validate(snapshot) };
        var json = JsonSerializer.Serialize(document, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
            throw new InvalidDataException("Die Sitzungsdatei ist zu groß.");
        AtomicFile.WriteAllText(_path, json);
    }

    private static CurrentSessionSnapshot Validate(CurrentSessionSnapshot snapshot)
    {
        if (snapshot.SessionId == Guid.Empty || snapshot.Duration < TimeSpan.Zero ||
            snapshot.SpotId is { } spot && !LootSpotCatalog.Spots.Any(value => value.Id == spot) ||
            snapshot.CharacterClassId is { } character && CompanionCharacterClassCatalog.FindById(character) is null ||
            snapshot.ConfirmedEventCount < 0 || snapshot.Totals is null || snapshot.ManualLootItems is null ||
            snapshot.GameLanguage is not ("auto" or "en" or "de") || snapshot.MonitorDeviceName?.Length > 1024)
            throw new InvalidDataException("Die aktuelle Session enthält ungültige Stammdaten.");
        if (snapshot.Totals.Count > 10000 || snapshot.Totals.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 4096 || pair.Value < 0))
            throw new InvalidDataException("Die aktuelle Session enthält ungültige Lootmengen.");
        var totals = new Dictionary<string, long>(snapshot.Totals, StringComparer.OrdinalIgnoreCase);
        _ = totals.Values.Sum();
        if (snapshot.ManualLootItems.Any(name => string.IsNullOrWhiteSpace(name) || !totals.ContainsKey(name)) ||
            snapshot.AgrisActiveDuration < TimeSpan.Zero || snapshot.AgrisActiveDuration > snapshot.AgrisObservedDuration ||
            snapshot.AgrisObservedDuration > snapshot.Duration || snapshot.ExperienceObservedDuration < TimeSpan.Zero ||
            snapshot.ExperienceObservedDuration > snapshot.Duration)
            throw new InvalidDataException("Die aktuelle Session enthält ungültige Beobachtungszeiten.");
        if (snapshot.ExperienceGainedPercentagePoints is null
                ? snapshot.ExperienceObservedDuration != TimeSpan.Zero || snapshot.ExperienceStartLevel is not null || snapshot.ExperienceEndLevel is not null
                : snapshot.ExperienceObservedDuration <= TimeSpan.Zero || snapshot.ExperienceStartLevel is not (>= 1 and <= 100) ||
                    snapshot.ExperienceEndLevel is not (>= 1 and <= 100))
            throw new InvalidDataException("Die aktuelle Session enthält ungültige Erfahrungsdaten.");
        if (snapshot.Uploads is { } uploads)
        {
            // Capture samples the activity timer and session clock separately.
            // The monotonic upload watermark can retain a small lead after an
            // idle-tail trim. Preserve both measurements and exact hour cutoffs;
            // never inflate the user's saved active duration to match the ledger.
            if (ExceedsSavedDuration(uploads.ObservedDuration, snapshot.Duration) ||
                ExceedsSavedDuration(uploads.ConsumedDuration, snapshot.Duration))
                throw new InvalidDataException("Die aktuelle Session enthält ungültige Uploadzeiten.");
            // Validate the complete upload ledger before any live state is restored.
            var validator = new GarmothUploadIntervals();
            validator.RestoreState(uploads);
        }
        return snapshot with
        {
            Totals = totals,
            ManualLootItems = snapshot.ManualLootItems.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    private static bool ExceedsSavedDuration(TimeSpan value, TimeSpan duration) =>
        value > duration && value - duration > UploadClockTolerance;

    private sealed record Document
    {
        public required int Version { get; init; }
        public required CurrentSessionSnapshot? Session { get; init; }
    }
}
