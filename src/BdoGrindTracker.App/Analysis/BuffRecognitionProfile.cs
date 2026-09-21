using System.Security;
using System.Text.Json;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Analysis;

internal sealed record BuffRegion(int X, int Y, int Width, int Height)
{
    internal Rectangle Rectangle => new(X, Y, Width, Height);
    internal bool IsValid => Width is > 0 and <= 8192 && Height is > 0 and <= 8192 &&
        X is >= -32768 and <= 32768 && Y is >= -32768 and <= 32768;
}

/// <summary>The timer rectangle is relative to the template's top-left pixel, not the bar.</summary>
internal sealed record BuffIconTemplate(string BuffId, string IconPath, BuffRegion TimerRegion)
{
    public double MinimumSimilarity { get; init; } = .92;
    public bool ConsumptionAttributionConfirmed { get; init; }
}

/// <summary>
/// A calibration from the user's visible HUD. No UIData index or item/buff-icon equivalence is assumed.
/// With UiDataIndex, Region.X/Y are offsets from that saved panel anchor at the calibration UI scale.
/// Otherwise Region is an absolute rectangle in a frame of exactly ScreenWidth x ScreenHeight.
/// </summary>
internal sealed record BuffRecognitionProfile
{
    public int Version { get; init; } = 1;
    public int ScreenWidth { get; init; }
    public int ScreenHeight { get; init; }
    public double UiScale { get; init; } = 1;
    public BuffRegion Region { get; init; } = new(0, 0, 0, 0);
    public uint? UiDataIndex { get; init; }
    public string? BlackDesertDirectory { get; init; }
    public string? GameVariablePath { get; init; }
    public IReadOnlyList<BuffIconTemplate> Templates { get; init; } = [];

    internal bool IsValid => ValidationError is null;

    internal string? ValidationError
    {
        get
        {
            if (Version != 1) return "Die Version des Buff-Erkennungsprofils wird nicht unterstützt.";
            if (ScreenWidth is <= 0 or > 32768 || ScreenHeight is <= 0 or > 32768 ||
                !double.IsFinite(UiScale) || UiScale is < .5 or > 3 || Region is not { IsValid: true })
                return "Bildschirmgröße, UI-Skalierung oder Buffleistenbereich sind ungültig. Bitte neu kalibrieren.";
            if (Templates is not { Count: > 0 and <= 64 })
                return "Bitte 1 bis 64 Buff-Vorlagen auswählen.";
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var template in Templates)
            {
                if (template is null || string.IsNullOrWhiteSpace(template.BuffId) ||
                    string.IsNullOrWhiteSpace(template.IconPath) || template.TimerRegion is not { IsValid: true } ||
                    template.TimerRegion.Width > 256 || template.TimerRegion.Height > 128 ||
                    !double.IsFinite(template.MinimumSimilarity) || template.MinimumSimilarity is < .85 or > .999)
                    return "Buff-Vorlage, Zeitbereich oder Ähnlichkeitsschwelle sind ungültig. Bitte neu kalibrieren.";
                var definition = BuffPriceCatalog.ResolveRecognitionDefinition(template.BuffId);
                if (definition is null) return $"Buff-ID ist nicht in der ausgewählten Kostenliste enthalten: {template.BuffId}";
                if (!groups.Add(string.IsNullOrWhiteSpace(definition.RecognitionGroup) ? definition.Id : definition.RecognitionGroup))
                    return "Pro Buff-Familie darf nur eine Preis- und Laufzeitvariante gewählt werden. Bitte doppelte Varianten entfernen.";
                if (definition.RequiresConsumptionConfirmation && !template.ConsumptionAttributionConfirmed)
                    return "Gruppenbuff: eigenen Verbrauch im Profil bestätigen.";
            }
            return null;
        }
    }
}

internal sealed class BuffRecognitionProfileStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    internal string? LastError { get; private set; }

    internal BuffRecognitionProfile? Load()
    {
        LastError = null;
        if (string.IsNullOrWhiteSpace(path)) { LastError = "Kein Buff-Erkennungsprofil ausgewählt."; return null; }
        try
        {
            var fullPath = Path.GetFullPath(path);
            using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 1024 * 1024) throw new InvalidDataException("Das Buff-Erkennungsprofil ist zu groß.");
            var profile = JsonSerializer.Deserialize<BuffRecognitionProfile>(file, Json);
            if (profile is null || profile.ValidationError is { })
                throw new InvalidDataException(profile?.ValidationError ?? "Das Buff-Erkennungsprofil ist leer.");
            var directory = Path.GetDirectoryName(fullPath)!;
            var templates = profile.Templates.Select(template => template with
            {
                IconPath = Path.GetFullPath(template.IconPath, directory),
            }).ToArray();
            foreach (var template in templates)
                if (!File.Exists(template.IconPath)) throw new InvalidDataException($"Buff-Vorlage fehlt: {Path.GetFileName(template.IconPath)}");
            return profile with
            {
                Templates = templates,
                BlackDesertDirectory = string.IsNullOrWhiteSpace(profile.BlackDesertDirectory) ? null :
                    Path.GetFullPath(profile.BlackDesertDirectory, directory),
                GameVariablePath = string.IsNullOrWhiteSpace(profile.GameVariablePath) ? null :
                    Path.GetFullPath(profile.GameVariablePath, directory),
            };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or SecurityException or
                   JsonException or ArgumentException or NotSupportedException)
        {
            LastError = error is InvalidDataException ? error.Message : "Das Buff-Erkennungsprofil konnte nicht geladen werden.";
            return null;
        }
    }
}
