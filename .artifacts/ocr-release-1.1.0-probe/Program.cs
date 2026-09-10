using System.Drawing.Imaging;
using System.Drawing.Text;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

using var engine = PaddleLootOcrRecognizer.Create("de-DE");
foreach (var text in new[] { "Bruchstück x 12", "Black Stone x 17" })
{
    using var font = new Font("Segoe UI", 36, FontStyle.Regular, GraphicsUnit.Pixel);
    foreach (var padding in new[] { -1, 0, 2, 4, 8 })
    {
        var height = padding == -1 ? 100 : font.Height + padding * 2;
        using var bitmap = new Bitmap(900, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(text, font, Brushes.Black, padding == -1 ? 18 : padding, padding == -1 ? 20 : padding);
        graphics.Flush();
        using var image = CompanionFrameDecoder.Decode(bitmap);
        var result = engine.Recognize(image);
        Console.WriteLine($"{text} padding={padding}, fontHeight={font.Height}, imageHeight={height}: {result.Text} ({result.Confidence})");
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"{(text.StartsWith("Bla") ? "en" : "de")}-{padding}.png"));
    }
}
