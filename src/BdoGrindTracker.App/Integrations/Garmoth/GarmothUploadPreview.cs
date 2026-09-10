using System.Collections.ObjectModel;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Integrations.Garmoth;

/// <summary>One projection for preflight, confirmation, and the request being sent.</summary>
internal sealed record GarmothUploadPreview
{
    public bool IsReady => Draft is not null && Payload is not null && Error is null;
    public string? Error { get; init; }
    public string? CorrectionHref { get; init; }
    public string? CorrectionLabel { get; init; }
    public GarmothSessionDraft? Draft { get; init; }
    public GarmothSessionPayload? Payload { get; init; }
    public bool SilverIsComplete { get; init; }
    public bool SilverIsStale { get; init; }

    public static GarmothUploadPreview Unavailable(string reason, string? href = null, string? label = null) =>
        new() { Error = reason, CorrectionHref = href, CorrectionLabel = label };

    public static GarmothUploadPreview ForHistory(LootHistoryEntry entry, LootPriceSnapshot prices, SilverTaxOptions tax)
    {
        if (entry.GarmothUploadBlocked || entry.GarmothUploadedAt is not null)
            return Unavailable("Erneuter Gesamt-Upload gesperrt. Den Übertragungsstatus bitte in Garmoth prüfen.");
        return Create(entry.SessionId, entry.SessionId, entry.SpotId, entry.CharacterClass,
            entry.Duration, entry.Totals, entry.StartedAt, prices, tax,
            $"/history/spots/{Uri.EscapeDataString(entry.SpotId)}/{entry.SessionId}?edit=1", "Session im Verlauf bearbeiten");
    }

    public static GarmothUploadPreview Create(Guid intervalId, Guid sourceSessionId, string? spotId,
        string? characterLabel, TimeSpan duration, IReadOnlyDictionary<string, long> totals,
        DateTimeOffset startedAt, LootPriceSnapshot prices, SilverTaxOptions tax,
        string correctionHref = "/settings", string correctionLabel = "Klasse in Einstellungen auswählen")
    {
        if (duration < TimeSpan.FromMinutes(1))
            return Unavailable("Der noch nicht übertragene Anteil benötigt mindestens eine volle aktive Minute.");
        if (string.IsNullOrWhiteSpace(characterLabel))
            return Unavailable("Für diesen Grind fehlt die Charakterklasse.", correctionHref, correctionLabel);
        var normalized = characterLabel.Replace("Â·", "·", StringComparison.Ordinal);
        var separator = normalized.IndexOf('·');
        var className = (separator >= 0 ? normalized[..separator] : normalized).Trim().TrimEnd('Â').TrimEnd();
        var specialization = normalized.Contains("Awakening", StringComparison.OrdinalIgnoreCase)
            ? GarmothSpecialization.Awakening : normalized.Contains("Succession", StringComparison.OrdinalIgnoreCase)
                ? GarmothSpecialization.Succession : GarmothSpecialization.Unique;
        if (!GarmothCatalog.TryGetClass(className, specialization, out _, out _))
            return Unavailable("Klasse oder Spezialisierung ist nicht für Garmoth zugeordnet.", correctionHref, correctionLabel);
        if (!GarmothCatalog.TryGetSpot(spotId ?? "", out _))
            return Unavailable("Der Grindspot muss zuerst erkannt und für Garmoth unterstützt werden.", "/", "Zur Live-Session");
        var positiveTotals = new ReadOnlyDictionary<string, long>(totals.Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
        if (positiveTotals.Count == 0)
            return Unavailable("Seit dem letzten Upload ist kein neuer Loot vorhanden.");
        var valuation = SilverValuation.Calculate(positiveTotals, prices, tax);
        if (!valuation.HasKnownValue)
            return Unavailable("Für den Upload ist noch kein Silberpreis verfügbar. Preise aktualisieren.");
        if (valuation.AfterTax < 0 || valuation.AfterTax > long.MaxValue || valuation.OverflowItems.Count > 0)
            return Unavailable("Der berechnete Silberwert ist nicht für Garmoth darstellbar.");
        var draft = new GarmothSessionDraft(intervalId, spotId!, className, specialization, duration,
            positiveTotals, (long)decimal.Truncate(valuation.AfterTax), startedAt) { SourceSessionId = sourceSessionId };
        try
        {
            return new() { Draft = draft, Payload = GarmothSessionPayload.Create(draft),
                SilverIsComplete = valuation.IsComplete, SilverIsStale = valuation.IsStale };
        }
        catch (ArgumentException exception) { return Unavailable(exception.Message); }
    }

    // Price changes don't invalidate a confirmed amount. Time, class, and counters
    // must still match exactly; otherwise the user needs to review a fresh preview.
    public bool HasSameSessionData(GarmothUploadPreview other) => Draft is { } left && other.Draft is { } right &&
        left.SourceSessionId == right.SourceSessionId && left.SpotId == right.SpotId &&
        left.ClassName == right.ClassName && left.Specialization == right.Specialization &&
        left.ActiveDuration == right.ActiveDuration && left.StartedAt == right.StartedAt &&
        left.Totals.Count == right.Totals.Count && left.Totals.All(pair => right.Totals.TryGetValue(pair.Key, out var quantity) && pair.Value == quantity);
}
