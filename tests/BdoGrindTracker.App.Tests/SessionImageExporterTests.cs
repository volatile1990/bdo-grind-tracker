using System.Buffers.Binary;
using System.Drawing.Imaging;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class SessionImageExporterTests
{
    [Fact]
    public void PngExportPreservesTheOriginalLosslessImage()
    {
        using var image = new Bitmap(12, 8);
        image.SetPixel(4, 3, Color.Teal);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        var original = stream.ToArray();

        var bytes = WindowsSessionImageExporter.DecodePng(DataUrl(original));

        Assert.Equal(original, bytes);
        using var decodedStream = new MemoryStream(bytes);
        using var decoded = new Bitmap(decodedStream);
        Assert.Equal(image.Size, decoded.Size);
        Assert.Equal(Color.Teal.ToArgb(), decoded.GetPixel(4, 3).ToArgb());
    }

    [Theory]
    [InlineData("data:image/jpeg;base64,AAAA")]
    [InlineData("data:image/png;base64,not-base64")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("https://example.com/image.png")]
    public async Task InvalidImagesCannotReachTheFileDialogOrClipboard(string value)
    {
        var dispatched = false;
        var exporter = new WindowsSessionImageExporter(new NativeWindow(), action =>
        {
            dispatched = true;
            return action();
        }, () => "de");

        await Assert.ThrowsAsync<InvalidDataException>(() => exporter.SaveAsync(value, "session.png"));
        Assert.Throws<InvalidDataException>(() => { _ = exporter.CopyAsync(value); });
        Assert.False(dispatched);
    }

    [Fact]
    public void OversizedImagesAreRejectedBeforeBitmapAllocation()
    {
        byte[] bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 1200);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), 20000);

        Assert.Throws<InvalidDataException>(() => WindowsSessionImageExporter.DecodePng(DataUrl(bytes)));
    }

    private static string DataUrl(byte[] bytes) => "data:image/png;base64," + Convert.ToBase64String(bytes);
}
