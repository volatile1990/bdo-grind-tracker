using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace BdoGrindTracker.App.Integrations.Garmoth;

internal enum GarmothSpecialization { Succession, Awakening, Unique }

internal sealed record GarmothSessionDraft(
    Guid LocalSessionId,
    string SpotId,
    string ClassName,
    GarmothSpecialization Specialization,
    TimeSpan ActiveDuration,
    IReadOnlyDictionary<string, long> Totals,
    long TotalSilver,
    DateTimeOffset StartedAt)
{
    public Guid? SourceSessionId { get; init; }
}

/// <summary>
/// External API contract reconstructed from Companion 0.7.4's request builder.
/// Local item names are resolved only through verified metadata, never guessed IDs.
/// </summary>
internal sealed record GarmothSessionPayload
{
    [JsonPropertyName("grindspot_id")] public required int GrindspotId { get; init; }
    [JsonPropertyName("minutes")] public required long Minutes { get; init; }
    [JsonPropertyName("hourly")] public required long Hourly { get; init; }
    [JsonPropertyName("total")] public required long Total { get; init; }
    [JsonPropertyName("drops")] public required IReadOnlyDictionary<string, long> Drops { get; init; }
    [JsonPropertyName("global")] public bool Global => false;
    [JsonPropertyName("note")] public required string Note { get; init; }
    [JsonPropertyName("class_id")] public required int ClassId { get; init; }
    [JsonPropertyName("spec")] public required int Spec { get; init; }
    [JsonIgnore] public IReadOnlyList<string> OmittedItems { get; init; } = [];

    public static IReadOnlyDictionary<string, long> GetUploadableDrops(string spotId,
        IReadOnlyDictionary<string, long> totals) => SelectDrops(spotId, totals).Drops;

    public static IReadOnlyList<string> GetOmittedItems(string spotId,
        IReadOnlyDictionary<string, long> totals) => SelectDrops(spotId, totals).OmittedItems;

    public static GarmothSessionPayload Create(GarmothSessionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.LocalSessionId == Guid.Empty)
            throw new ArgumentException("Die lokale Sitzungs-ID fehlt.");
        if (draft.ActiveDuration < TimeSpan.FromMinutes(1))
            throw new ArgumentException("Garmoth benötigt mindestens eine volle Minute Sitzungsdauer.");
        if (draft.TotalSilver < 0)
            throw new ArgumentException("Der bestätigte Gesamtwert in Silber darf nicht negativ sein.");
        ArgumentNullException.ThrowIfNull(draft.Totals);
        if (!GarmothCatalog.TryGetSpot(draft.SpotId, out var spotId))
            throw new ArgumentException("Der Grindspot ist nicht für den Garmoth-Upload zugeordnet.");
        if (!GarmothCatalog.TryGetClass(draft.ClassName, draft.Specialization, out var classId, out var spec))
            throw new ArgumentException("Klasse oder Spezialisierung ist nicht für Garmoth zugeordnet.");

        var (drops, omittedItems) = SelectDrops(draft.SpotId, draft.Totals);
        if (drops.Count == 0)
            throw new ArgumentException("Die Sitzung enthält noch keinen für diesen Garmoth-Spot unterstützten Loot.");

        // Native Companion truncates seconds / 60 and silver / (seconds / 3600).
        // Widened integer arithmetic prevents overflow and preserves exact truncation.
        var minutes = draft.ActiveDuration.Ticks / TimeSpan.TicksPerMinute;
        var hourly = (BigInteger)draft.TotalSilver * TimeSpan.TicksPerHour / draft.ActiveDuration.Ticks;
        if (hourly > long.MaxValue)
            throw new ArgumentException("Der Silberwert pro Stunde ist zu groß für den Garmoth-Upload.");

        return new GarmothSessionPayload
        {
            GrindspotId = spotId,
            Minutes = minutes,
            Hourly = (long)hourly,
            Total = draft.TotalSilver,
            Drops = drops,
            OmittedItems = omittedItems,
            ClassId = classId,
            Spec = spec,
            Note = AppBranding.Name + " · " + draft.StartedAt.ToString("yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture) + " · " + draft.LocalSessionId.ToString("N") +
                (draft.SourceSessionId is { } sourceId ? " · Sitzung " + sourceId.ToString("N") : ""),
        };
    }

    private static (IReadOnlyDictionary<string, long> Drops, IReadOnlyList<string> OmittedItems)
        SelectDrops(string spotId, IReadOnlyDictionary<string, long> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        if (!GarmothCatalog.TryGetSpot(spotId, out _))
            throw new ArgumentException("Der Grindspot ist nicht für den Garmoth-Upload zugeordnet.");

        var drops = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var omitted = new List<string>();
        foreach (var (name, quantity) in totals)
        {
            if (string.IsNullOrWhiteSpace(name) || quantity <= 0)
                throw new ArgumentException("Alle hochgeladenen Lootmengen müssen positiv sein.");
            if (!GarmothCatalog.TryGetDropKeyForSpot(spotId, name, out var key))
            {
                omitted.Add(name);
                continue;
            }
            if (!drops.TryAdd(key, quantity))
                throw new ArgumentException("Mehrere Items würden demselben Garmoth-Eintrag zugeordnet.");
        }

        omitted.Sort(StringComparer.Ordinal);
        return (new ReadOnlyDictionary<string, long>(drops), omitted.AsReadOnly());
    }
}
