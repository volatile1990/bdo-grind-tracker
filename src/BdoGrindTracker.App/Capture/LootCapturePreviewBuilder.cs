using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Capture;

/// <summary>Builds a preview from one captured frame without changing its pixels or ownership.</summary>
internal static class LootCapturePreviewBuilder
{
    internal const int MaximumPreviewWidth = 1600;

    public static CaptureConfigurationPreview Create(Bitmap frame, CaptureConfigurationOption configuration)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Error is { } configurationError)
            return new(configuration, Error: configurationError);
        if (frame.Width != configuration.ScreenWidth || frame.Height != configuration.ScreenHeight)
            return new(configuration, Error:
                $"Das aufgenommene Spielbild hat {frame.Width} × {frame.Height} Pixel; " +
                $"die ausgewählte Konfiguration erwartet {configuration.ScreenWidth} × {configuration.ScreenHeight} Pixel. " +
                "Wähle die zur aktuellen Spielauflösung passende GameVariable.xml.");
        if (configuration.NormalBounds is not { } normalBounds || !FitsFrame(normalBounds, frame))
            return new(configuration, Error:
                "Der Bereich des normalen Droplogs fehlt oder liegt nicht vollständig im aufgenommenen Spielbild. " +
                "Prüfe die ausgewählte GameVariable.xml.");
        if (configuration.RareBounds is { } specialBounds && !FitsFrame(specialBounds, frame))
            return new(configuration, Error:
                "Der Bereich des Special-Droplogs liegt nicht vollständig im aufgenommenen Spielbild. " +
                "Prüfe die ausgewählte GameVariable.xml.");

        var capturedAt = DateTimeOffset.UtcNow;
        using var snapshot = CreateSnapshot(frame);
        using var normal = frame.Clone(normalBounds, PixelFormat.Format32bppArgb);
        using var rare = configuration.RareBounds is { } rareBounds
            ? frame.Clone(rareBounds, PixelFormat.Format32bppArgb) : null;
        return new(configuration,
            Encode(snapshot, ImageFormat.Jpeg, "image/jpeg"),
            Encode(normal, ImageFormat.Png, "image/png"),
            rare is null ? null : Encode(rare, ImageFormat.Png, "image/png"),
            capturedAt);
    }

    private static bool FitsFrame(Rectangle bounds, Bitmap frame) =>
        bounds.X >= 0 && bounds.Y >= 0 && bounds.Width > 0 && bounds.Height > 0 &&
        (long)bounds.X + bounds.Width <= frame.Width && (long)bounds.Y + bounds.Height <= frame.Height;

    private static Bitmap CreateSnapshot(Bitmap frame)
    {
        var width = Math.Min(frame.Width, MaximumPreviewWidth);
        var height = Math.Max(1, (int)Math.Round(frame.Height * (width / (double)frame.Width)));
        var snapshot = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(snapshot);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(frame, new Rectangle(0, 0, width, height),
                new Rectangle(0, 0, frame.Width, frame.Height), GraphicsUnit.Pixel);
            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    private static string Encode(Image image, ImageFormat format, string mimeType)
    {
        using var stream = new MemoryStream();
        image.Save(stream, format);
        return $"data:{mimeType};base64,{Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length))}";
    }
}
