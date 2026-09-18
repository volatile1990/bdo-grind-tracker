using System.Text.RegularExpressions;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal sealed record RotationMessageProfile(
    Func<string, IReadOnlyList<(string Kind, string Label)>> Parse,
    Func<int, int, Rectangle> Crop, bool SingleLine = false)
{
    internal string Recognize(Mat pixels, CompanionWindowsOcrRecognizer engine)
    {
        if (!SingleLine) return HermesiaMessages.Recognize(pixels, engine);
        var text = engine.Recognize(pixels).Text;
        if (Parse(text).Count > 0) return text;
        using var gray = new Mat();
        using var enlarged = new Mat();
        Cv2.CvtColor(pixels, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Resize(gray, enlarged, new OpenCvSharp.Size(), 2, 2, InterpolationFlags.Cubic);
        return text + "\n" + engine.Recognize(enlarged).Text;
    }
    // Up to three stacked banners, centered: the widest (AFK) spans 37.9–62 % of the width,
    // the lines 54.7–63.5 % of the height. Measured on the reference video and a live session.
    internal static readonly RotationMessageProfile Hermesia = new(HermesiaMessages.Parse, (w, h) =>
        Rectangle.FromLTRB((int)Math.Floor(w * .365), (int)Math.Floor(h * .54),
            (int)Math.Ceiling(w * .635), (int)Math.Ceiling(h * .65)));

    // Resolve crop: left 0.40625, right 0.4072916667,
    // top 0.6166666667, bottom 0.3611111111. Verified on the source video.
    internal static readonly RotationMessageProfile Aphrodon = new(AphrodonMessages.Parse, (w, h) =>
        Rectangle.FromLTRB((int)Math.Floor(w * .40625), (int)Math.Floor(h * 37 / 60d),
            (int)Math.Ceiling(w * (1 - 391 / 960d)), (int)Math.Ceiling(h * 23 / 36d)), SingleLine: true);
}

internal static class AphrodonMessages
{
    internal static readonly (string Kind, string Label, string Phrase)[] Definitions = [
        ("setup", "Startup", "as the scarecrow falls"),
        ("small-scarecrow", "Kleine Vogelscheuche", "the harvest winds begin to blow"),
        ("restart", "Rotation aktiviert", "a golden fragrance rides the wind"),
        ("hog", "Hog", "you sense the energy of abundance"),
        ("agris", "Agris-Event", "you sense an intoxicating energy of abundance"),
        ("big-scarecrow", "Große Vogelscheuche", "a scarecrow blessed with abundance appears"),
        ("afk", "AFK-Phase", "blessing settles over the fields"),
        ("end", "AFK-Ende", "blessing fades from the fields"),
        ("failure", "Vogelscheuche erwacht", "the scarecrow awakens as the commotion continues")
    ];
    internal static IReadOnlyList<(string Kind, string Label)> Parse(string text)
    {
        var normalized = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        return Definitions.Where(d => normalized.Contains(d.Phrase, StringComparison.Ordinal))
            .Select(d => (d.Kind, d.Label)).ToArray();
    }
}
