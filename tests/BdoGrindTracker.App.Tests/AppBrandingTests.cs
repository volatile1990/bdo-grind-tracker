using System.Reflection;

namespace BdoGrindTracker.App.Tests;

public sealed class AppBrandingTests
{
    [Fact]
    public void ProductIdentityIsGrindcrestButManagedAssemblyKeepsCompatibility()
    {
        var assembly = typeof(AppBranding).Assembly;
        Assert.Equal("Grindcrest", AppBranding.Name);
        Assert.Equal("Grindcrest", assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal("Grindcrest", assembly.GetCustomAttribute<AssemblyTitleAttribute>()!.Title);
        Assert.Equal("BdoGrindTracker", assembly.GetName().Name);
    }

    [Fact]
    public void EmbeddedLogoIsTransparentAndInstancesHaveIndependentLifetime()
    {
        using var first = AppBranding.CreateLogo();
        using var second = AppBranding.CreateLogo();
        Assert.Equal(new Size(128, 128), first.Size);
        Assert.Equal(0, first.GetPixel(0, 0).A);
        var visiblePixels = 0;
        for (var y = 0; y < first.Height; y++)
        for (var x = 0; x < first.Width; x++)
            if (first.GetPixel(x, y).A > 127) visiblePixels++;
        Assert.InRange(visiblePixels, 2000, 10000);
        first.Dispose();
        Assert.Equal(new Size(128, 128), second.Size);
        Assert.Equal(0, second.GetPixel(127, 127).A);
    }

    [Fact]
    public void EmbeddedWindowsIconProvidesAllSevenSizesAndCanBeLoaded()
    {
        using var stream = typeof(AppBranding).Assembly.GetManifestResourceStream(
            "BdoGrindTracker.App.Branding.grindcrest.ico");
        Assert.NotNull(stream);
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(7, reader.ReadUInt16());
        foreach (var expected in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            Assert.Equal(expected, width == 0 ? 256 : width);
            Assert.Equal(expected, height == 0 ? 256 : height);
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            Assert.InRange((long)length, 1, stream.Length);
            Assert.InRange((long)offset + length, 118, stream.Length);
        }
        using var icon = AppBranding.CreateWindowIcon();
        Assert.Equal(new Size(32, 32), icon.Size);
        using var bitmap = icon.ToBitmap();
        Assert.Equal(new Size(32, 32), bitmap.Size);
    }
}
