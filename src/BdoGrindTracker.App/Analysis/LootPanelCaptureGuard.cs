using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Validates saved panel geometry, independently of whether loot is currently visible.</summary>
internal sealed class LootPanelCaptureGuard(
    CompanionCalibration calibration,
    Func<CompanionCalibration>? readCurrent = null)
{
    internal const string MissingPanelMessage =
        "Die Position des Haupt-Droplogs konnte nicht erkannt werden. " +
        "Aktiviere das Droplog in der BDO-Oberfläche, speichere die UI-Einstellungen " +
        "und starte Grindcrest neu. Ohne diesen Bereich ist kein Tracking möglich.";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private DateTimeOffset _nextCheck;
    private string? _error;
    public string? Error => Volatile.Read(ref _error);

    internal static CompanionCalibration ReadCalibration(Func<CompanionCalibration> read)
    {
        try
        {
            var result = read();
            // Fail during initialization, before creating OCR resources.
            if (CompanionNormalLootGeometry.CalculateSlotCrops(result).Count == 0)
                throw new InvalidDataException("The normal loot panel contains no usable rows.");
            return result;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Xml.XmlException or ArgumentException or OverflowException)
        {
            throw new LootPanelUnavailableException(MissingPanelMessage, exception);
        }
    }

    public void Validate(Size frameSize, DateTimeOffset now, bool force = false)
    {
        if (Error is { } previous) throw new LootPanelUnavailableException(previous);
        try
        {
            if (frameSize.Width != calibration.ScreenWidth || frameSize.Height != calibration.ScreenHeight)
                throw new LootPanelUnavailableException(
                    "Die gespeicherte Droplog-Position passt nicht zum gewählten Bildschirm. " +
                    "Wähle in den Einstellungen den Spielmonitor und prüfe die BDO-Auflösung. " +
                    "Starte Grindcrest danach neu.");
            if (readCurrent is null || (!force && now < _nextCheck && _nextCheck - now <= CheckInterval)) return;
            var current = ReadCalibration(readCurrent);
            if (current.GameVariablePath != calibration.GameVariablePath ||
                current.LootAnchorX != calibration.LootAnchorX || current.LootAnchorY != calibration.LootAnchorY ||
                current.ScreenWidth != calibration.ScreenWidth || current.ScreenHeight != calibration.ScreenHeight ||
                current.UiScale != calibration.UiScale || current.FontType != calibration.FontType)
                throw new LootPanelUnavailableException(
                    "Die Droplog-Position oder die BDO-Anzeigeeinstellungen haben sich geändert. " +
                    "Das Tracking wurde angehalten. Starte Grindcrest neu, damit der richtige Bereich erfasst wird.");
            _nextCheck = now + CheckInterval;
        }
        catch (LootPanelUnavailableException exception)
        {
            Volatile.Write(ref _error, exception.Message);
            throw;
        }
    }
}

internal sealed class LootPanelUnavailableException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);
