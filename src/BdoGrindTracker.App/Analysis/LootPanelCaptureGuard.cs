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
    private (DateTime Variables, DateTime Options)? _lastReadVersion;
    private bool _rareLayoutRequiresRestart;
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
            return ValidateOptionalRarePanel(result);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Xml.XmlException or ArgumentException or OverflowException)
        {
            throw new LootPanelUnavailableException(MissingPanelMessage, exception);
        }
    }

    public CompanionCalibration Validate(Size frameSize, DateTimeOffset now, bool force = false)
    {
        if (Error is { } previous) throw new LootPanelUnavailableException(previous);
        try
        {
            if (frameSize.Width != calibration.ScreenWidth || frameSize.Height != calibration.ScreenHeight)
                throw new LootPanelUnavailableException(
                    "Die gespeicherte Droplog-Position passt nicht zur Größe des aufgenommenen Spielbilds. " +
                    "Prüfe die BDO-Auflösung und speichere die UI-Einstellungen im Spiel. " +
                    "Starte Grindcrest danach neu.");
            if (readCurrent is null) return calibration;
            // A cheap file-version check catches a saved move before the next
            // crop; XML parsing remains throttled when neither file changed.
            var version = ReadConfigurationVersion();
            // OCR can lag behind capture. An already queued screenshot still
            // belongs to the old layout when it predates the saved move.
            if (!force && version is { } saved && _lastReadVersion is { } read &&
                (saved.Variables != read.Variables && saved.Variables > now.UtcDateTime ||
                 saved.Options != read.Options && saved.Options > now.UtcDateTime)) return calibration;
            if (!force && version is not null && version == _lastReadVersion &&
                now < _nextCheck && _nextCheck - now <= CheckInterval) return calibration;
            var current = ReadCalibration(readCurrent);
            if (current.GameVariablePath != calibration.GameVariablePath ||
                current.ScreenWidth != calibration.ScreenWidth || current.ScreenHeight != calibration.ScreenHeight ||
                current.UiScale != calibration.UiScale || current.FontType != calibration.FontType)
                throw new LootPanelUnavailableException(
                    "Das BDO-Profil oder die Anzeigeeinstellungen haben sich geändert. " +
                    "Das Tracking wurde angehalten. Starte Grindcrest neu, damit der richtige Bereich erfasst wird.");
            if (!HasCompatibleRows(calibration, current))
                throw new LootPanelUnavailableException(
                    "Die neue Droplog-Position verändert die sichtbaren Zeilen am Bildschirmrand. " +
                    "Platziere das Droplog vollständig im Spielbild, speichere die BDO-UI-Einstellungen " +
                    "und starte Grindcrest neu.");

            // A clipped optional band must not stop the normal feed. Do not
            // repeatedly re-enable a changed band in this session: it could
            // expose an old message as a new drop in the rare counter.
            if (_rareLayoutRequiresRestart || !HasCompatibleRareBand(calibration, current))
            {
                _rareLayoutRequiresRestart = true;
                current = DisableRare(current, "rare-band-shape-changed-restart-required");
            }

            // The fresh configuration already contains the corrected position.
            // Keep font/scale-dependent OCR resources and session reconciliation;
            // the analyzer adopts these coordinates before reading the next rows.
            calibration = calibration with
            {
                LootAnchorX = current.LootAnchorX,
                LootAnchorY = current.LootAnchorY,
                HasRareLootAnchor = current.HasRareLootAnchor,
                RareLootAnchorX = current.RareLootAnchorX,
                RareLootAnchorY = current.RareLootAnchorY,
                RareLootResolution = current.RareLootResolution,
            };
            _nextCheck = now + CheckInterval;
            _lastReadVersion = version;
            return calibration;
        }
        catch (LootPanelUnavailableException exception)
        {
            Volatile.Write(ref _error, exception.Message);
            throw;
        }
    }

    private (DateTime Variables, DateTime Options)? ReadConfigurationVersion()
    {
        try
        {
            return (File.GetLastWriteTimeUtc(calibration.GameVariablePath),
                File.GetLastWriteTimeUtc(calibration.GameOptionPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // If metadata cannot be read, perform the normal validated config read.
            return null;
        }
    }

    private static bool HasCompatibleRows(CompanionCalibration previous, CompanionCalibration current)
    {
        var previousPanel = CompanionNormalLootGeometry.CalculatePanelBounds(previous);
        var currentPanel = CompanionNormalLootGeometry.CalculatePanelBounds(current);
        var previousRows = CompanionNormalLootGeometry.CalculateSlotCrops(previous);
        var currentRows = CompanionNormalLootGeometry.CalculateSlotCrops(current);
        // Edge clipping can shift a physical row to a different logical slot or
        // reveal older rows. A translation must preserve the existing row identities.
        if (previousPanel.Bottom - previous.LootAnchorY != currentPanel.Bottom - current.LootAnchorY ||
            !previousRows.Select(row => (row.Top - previousPanel.Top, row.Height))
                .SequenceEqual(currentRows.Select(row => (row.Top - currentPanel.Top, row.Height))))
            return false;
        return true;
    }

    private static bool HasCompatibleRareBand(CompanionCalibration previous, CompanionCalibration current)
    {
        if (previous.HasRareLootAnchor && current.HasRareLootAnchor)
        {
            var previousBand = CompanionNormalLootGeometry.CalculateRareBandCrop(previous);
            var currentBand = CompanionNormalLootGeometry.CalculateRareBandCrop(current);
            if (previousBand.Top - previous.RareLootAnchorY != currentBand.Top - current.RareLootAnchorY ||
                previousBand.Height != currentBand.Height) return false;
        }
        return true;
    }

    private static CompanionCalibration ValidateOptionalRarePanel(CompanionCalibration current)
    {
        if (!current.HasRareLootAnchor) return current;
        try
        {
            var band = CompanionNormalLootGeometry.CalculateRareBandCrop(current);
            if (band.Width > 0 && band.Height > 0 &&
                new Rectangle(0, 0, current.ScreenWidth, current.ScreenHeight).Contains(band)) return current;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            // Only the optional rare geometry is unavailable; the main panel
            // has already passed its independent validation.
        }
        return DisableRare(current, "rare-band-outside-frame");
    }

    private static CompanionCalibration DisableRare(CompanionCalibration current, string reason) => current with
    {
        HasRareLootAnchor = false,
        RareLootAnchorX = 0,
        RareLootAnchorY = 0,
        RareLootResolution = new(RareLootAnchorStatus.Invalid,
            current.RareLootResolution?.Source ?? "active", reason),
    };
}

internal sealed class LootPanelUnavailableException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);
