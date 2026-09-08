namespace BdoGrindTracker.App;

/// <summary>
/// User-facing identity only. Existing storage paths, DPAPI purpose, item IDs,
/// protocol contracts and assembly namespaces deliberately keep their stable IDs.
/// </summary>
internal static class AppBranding
{
    public const string Name = "Grindcrest";
    public static string Version { get; } = typeof(AppBranding).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    public static string WindowTitle => $"Grindcrest · Black Desert Loot Tracker · {Version}";
    public static string UserAgent => $"Grindcrest/{Version}";

    public static Bitmap CreateLogo()
    {
        using var stream = OpenAsset("grindcrest-header.png");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    public static Icon CreateWindowIcon()
    {
        using var stream = OpenAsset("grindcrest.ico");
        using var source = new Icon(stream, new Size(32, 32));
        return (Icon)source.Clone();
    }

    private static Stream OpenAsset(string name) => typeof(AppBranding).Assembly
        .GetManifestResourceStream("BdoGrindTracker.App.Branding." + name)
        ?? throw new InvalidOperationException("Ein eingebettetes Grindcrest-Designasset fehlt.");
}
