using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

// Profile availability and presentation are also used by the browser preview,
// which cannot load the Windows image recognition implementations.
internal static partial class RotationProfiles
{
    internal static bool Supports(string? spotId) => spotId is LootSpotCatalog.HermesiaId or LootSpotCatalog.AphrodonId;

    internal static RotationMonitorSnapshot Present(string? spotId, RotationMonitorSnapshot? snapshot = null)
    {
        var supported = Supports(spotId);
        var current = supported && snapshot is not null && snapshot.SpotId == spotId ? snapshot : new RotationMonitorSnapshot();
        return current with
        {
            SpotId = spotId, SpotName = spotId is null ? "Noch kein Spot erkannt" : Presentation.SpotName(spotId),
            HasProfile = supported,
            Status = spotId is null ? "Warte auf Spot-Erkennung" : !supported ?
                "Für diesen Spot sind noch keine Rotationsdaten hinterlegt" : current.Status,
        };
    }
}
