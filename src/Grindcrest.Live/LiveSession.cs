namespace Grindcrest.Live;

/// <summary>Public data only. Never include keys, screenshots, OCR text or local paths.</summary>
public sealed record LiveSessionUpdate
{
    public Guid SessionId { get; init; }
    public long Sequence { get; init; }
    public string DisplayName { get; init; } = "";
    public string? SpotName { get; init; }
    public string? CharacterClass { get; init; }
    public string Region { get; init; } = "EU";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset SharedAt { get; init; }
    public long ActiveSeconds { get; init; }
    public bool Paused { get; init; }
    public decimal? SilverAfterTax { get; init; }
    public bool SilverIsPartial { get; init; }
    public bool PricesAreStale { get; init; }
    public Dictionary<string, long> Loot { get; init; } = [];

    public string? Validate(DateTimeOffset now)
    {
        if (SessionId == Guid.Empty || Sequence <= 0) return "Sitzungs-ID oder Sequenz ungültig.";
        if (!ValidText(DisplayName, 40)) return "Anzeigename muss 1–40 Zeichen enthalten.";
        if (SpotName is not null && !ValidText(SpotName, 80)) return "Spotname ungültig.";
        if (CharacterClass is not null && !ValidText(CharacterClass, 80)) return "Klasse ungültig.";
        if (Region is not ("EU" or "NA")) return "Region ungültig.";
        if (StartedAt < now.AddDays(-30) || StartedAt > now.AddMinutes(5)) return "Startzeit ungültig.";
        if (SharedAt < StartedAt.AddMinutes(-5) || SharedAt > now.AddMinutes(5)) return "Freigabezeit ungültig.";
        if (ActiveSeconds < 0 || ActiveSeconds > 2_592_000 ||
            ActiveSeconds > (now - StartedAt).TotalSeconds + 300) return "Aktive Dauer ungültig.";
        if (SilverAfterTax is < 0 or > 9_007_199_254_740_991m) return "Silberwert ungültig.";
        if (Loot is null || Loot.Count > 128 ||
            Loot.Any(item => !ValidText(item.Key, 120) || item.Value is <= 0 or > 1_000_000_000_000))
            return "Lootdaten ungültig.";
        return null;
    }

    private static bool ValidText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum &&
        !value.Any(char.IsControl);
}

public sealed record PublicLiveSession(LiveSessionUpdate Session, DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);

public sealed record LiveSessionList(DateTimeOffset ServerTime, IReadOnlyList<PublicLiveSession> Sessions);

public static class LiveEndpoint
{
    public static bool TryParse(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            (parsed.Scheme != Uri.UriSchemeHttps && !(parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback)))
            return false;
        uri = new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/");
        return true;
    }

    public static bool IsValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);
}
