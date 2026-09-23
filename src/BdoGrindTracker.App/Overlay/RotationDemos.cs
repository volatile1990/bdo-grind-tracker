using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Overlay;

/// <summary>The example rotation of each spot with a rotation profile; Hermesia for every other spot.</summary>
internal static class RotationDemos
{
    internal static double Duration(string? spotId) => spotId switch
    {
        LootSpotCatalog.AphrodonId => AphrodonRotationDemo.Reference.Duration,
        LootSpotCatalog.EventHorizonId => EventHorizonRotationDemo.Reference.Duration,
        LootSpotCatalog.MagaiaId => MagaiaRotationDemo.Reference.Duration,
        _ => HermesiaRotationDemo.Reference.Duration,
    };

    /// <summary>The spot's example at <paramref name="seconds"/>, repeating after one rotation.</summary>
    internal static RotationMonitorSnapshot At(string? spotId, double seconds)
    {
        seconds %= Duration(spotId);
        return spotId switch
        {
            LootSpotCatalog.AphrodonId => AphrodonRotationDemo.At(seconds),
            LootSpotCatalog.EventHorizonId => EventHorizonRotationDemo.At(seconds),
            LootSpotCatalog.MagaiaId => MagaiaRotationDemo.At(seconds),
            _ => HermesiaRotationDemo.At(seconds),
        };
    }
}
