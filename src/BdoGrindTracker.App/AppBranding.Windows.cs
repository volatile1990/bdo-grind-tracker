namespace BdoGrindTracker.App;

internal static partial class AppBranding
{
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
