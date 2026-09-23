using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Theming;
using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Persistence;

internal sealed class AppSettings
{
    private const int CurrentSettingsVersion = 7;

    public const int DefaultAutoPauseMinutes = 3;
    public const int MinimumAutoPauseMinutes = 1;
    public const int MaximumAutoPauseMinutes = 60;

    public int SettingsVersion { get; set; }

    public bool SetupCompleted { get; set; }

    public string? MonitorDeviceName { get; set; }
    public string? CaptureConfigurationPath { get; set; }
    public string? BuffRecognitionProfilePath { get; set; }
    public string GameLanguage { get; set; } = "auto";
    public string UiLanguage { get; set; } = AppText.DefaultLanguage;
    public string ThemeId { get; set; } = AppThemes.Grindcrest;
    public string? OverlayThemeId { get; set; }

    public string? SpotId { get; set; }

    public string? CharacterClassId { get; set; }

    public string[] FavoriteItems { get; set; } = [];
    public Dictionary<string, string[]> LootColumnOrders { get; set; } = new();

    public int AutoPauseMinutes { get; set; } = DefaultAutoPauseMinutes;

    public bool AutoStartGrinding { get; set; }

    public bool RotationIncludeSpecialEvents { get; set; } = true;

    public bool GarmothAutoUploadEnabled { get; set; }

    public string MarketRegion { get; set; } = LootPriceCatalog.DefaultRegion;

    public bool SilverValuePack { get; set; }

    public bool SilverMerchantRing { get; set; }

    public int SilverFamilyFame { get; set; }

    public void UpgradeDefaults()
    {
        ThemeId = AppThemes.Normalize(ThemeId);
        OverlayThemeId = AppThemes.NormalizeOverlay(OverlayThemeId);
        UiLanguage = AppText.NormalizeLanguage(UiLanguage);
        if (GameLanguage is not ("auto" or "en" or "de")) GameLanguage = "auto";
        if (AutoPauseMinutes is < MinimumAutoPauseMinutes or > MaximumAutoPauseMinutes)
        {
            AutoPauseMinutes = DefaultAutoPauseMinutes;
        }

        try
        {
            MarketRegion = LootPriceCatalog.NormalizeRegion(MarketRegion);
        }
        catch (ArgumentException)
        {
            MarketRegion = LootPriceCatalog.DefaultRegion;
        }
        SilverFamilyFame = Math.Max(0, SilverFamilyFame);

        if (SettingsVersion < CurrentSettingsVersion)
        {
            SettingsVersion = CurrentSettingsVersion;
        }
    }

    public void UpdateCapturePreferences(string? selectedMonitorDeviceName)
    {
        MonitorDeviceName = selectedMonitorDeviceName;
    }

    public SilverTaxOptions GetSilverTaxOptions() =>
        new(SilverValuePack, SilverMerchantRing, SilverFamilyFame);

    public void UpdateSilverPreferences(string marketRegion, SilverTaxOptions taxOptions)
    {
        ArgumentNullException.ThrowIfNull(taxOptions);
        // Validate before changing any persisted preference.
        var normalizedRegion = LootPriceCatalog.NormalizeRegion(marketRegion);
        var validated = new SilverTaxOptions(taxOptions.ValuePack, taxOptions.MerchantRing, taxOptions.FamilyFame);
        MarketRegion = normalizedRegion;
        SilverValuePack = validated.ValuePack;
        SilverMerchantRing = validated.MerchantRing;
        SilverFamilyFame = validated.FamilyFame;
    }
}
