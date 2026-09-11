using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Capture-coordinate provenance without a Windows user path or unrelated cache data.</summary>
internal sealed record LootCalibrationDiagnostics(
    string ProfileId,
    string ProfileSelection,
    int ScreenWidth,
    int ScreenHeight,
    float UiScale,
    int NormalAnchorX,
    int NormalAnchorY,
    int? RareAnchorX,
    int? RareAnchorY,
    RareLootAnchorResolution? RareResolution)
{
    public static LootCalibrationDiagnostics From(CompanionCalibration calibration) => new(
        Path.GetFileName(calibration.ProfileDirectoryPath), "gamevariable-last-write",
        calibration.ScreenWidth, calibration.ScreenHeight, calibration.UiScale,
        calibration.LootAnchorX, calibration.LootAnchorY,
        calibration.HasRareLootAnchor ? calibration.RareLootAnchorX : null,
        calibration.HasRareLootAnchor ? calibration.RareLootAnchorY : null,
        calibration.RareLootResolution);
}
