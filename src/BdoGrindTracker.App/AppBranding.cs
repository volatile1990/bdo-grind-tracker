namespace BdoGrindTracker.App;

/// <summary>
/// User-facing identity only. Existing storage paths, DPAPI purpose, item IDs,
/// protocol contracts and assembly namespaces deliberately keep their stable IDs.
/// </summary>
internal static partial class AppBranding
{
    public const string Name = "Grindcrest";
    public static string Version { get; } = typeof(AppBranding).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    public static string WindowTitle => $"Grindcrest · Black Desert Loot Tracker · {Version}";
    public static string UserAgent => $"Grindcrest/{Version}";

}
