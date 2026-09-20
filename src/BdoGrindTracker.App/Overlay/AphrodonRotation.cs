namespace BdoGrindTracker.App.Overlay;

/// <summary>Compatibility name; every spot uses the shared lifecycle and recovery policy.</summary>
internal sealed class AphrodonRotationTracker(string? path = null)
    : RotationPlatform(RotationDefinition.Aphrodon, path)
{
    internal new static string DefaultPath => RotationPlatform.DefaultPath(BdoGrindTracker.Core.LootSpotCatalog.AphrodonId);
}
