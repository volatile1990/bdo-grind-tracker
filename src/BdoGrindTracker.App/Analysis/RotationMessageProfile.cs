using System.Text.RegularExpressions;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

/// <param name="GapSamples">Unreadable samples a backward search may bridge inside one banner sighting.</param>
internal sealed record RotationMessageProfile(
    Func<string, IReadOnlyList<(string Kind, string Label)>> Parse,
    Func<int, int, Rectangle> Crop, bool SingleLine = false, int GapSamples = 2, double DuplicateSeconds = 8)
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
    // The centered stack of up to three system banners. Its place is a fixed share of the screen at any
    // resolution: the widest line (Hermesia AFK) spans 37.9–62 % of the width, the lines 54.7–63.5 % of the height.
    // Measured on own Hermesia recordings and a live session.
    // Include the lower banner position in the supplied 2560x1080 ultrawide capture (center y ~= .67).
    private static Rectangle BannerStack(int w, int h) => Rectangle.FromLTRB((int)Math.Floor(w * .365), (int)Math.Floor(h * .52),
        (int)Math.Ceiling(w * .635), (int)Math.Ceiling(h * .71));

    internal static readonly RotationMessageProfile Hermesia = new(HermesiaMessages.Parse, BannerStack);

    // Same banner stack as Hermesia. Teleport black screens hide a banner for about 2.5 seconds,
    // so up to seven unreadable samples keep one sighting together.
    internal static readonly RotationMessageProfile EventHorizon = new(EventHorizonMessages.Parse, BannerStack, GapSamples: 7, DuplicateSeconds: 15);

    // Resolve crop: left 0.40625, right 0.4072916667,
    // top 0.6166666667, bottom 0.3611111111. Verified on the source video.
    internal static readonly RotationMessageProfile Aphrodon = new(AphrodonMessages.Parse, (w, h) =>
        Rectangle.FromLTRB((int)Math.Floor(w * .40625), (int)Math.Floor(h * 37 / 60d),
            (int)Math.Ceiling(w * (1 - 391 / 960d)), (int)Math.Ceiling(h * 23 / 36d)), SingleLine: true);
}

/// <summary>
/// Event Horizon banners. Several are deliberately glitched in the game ("acti○ted", "Warn■g",
/// "Spacetim○ distorti■n"), so each phrase avoids the garbled words.
/// </summary>
internal static class EventHorizonMessages
{
    internal static readonly (string Kind, string Label, string Phrase)[] Definitions = [
        ("anomaly", "Wurmloch gestartet", "flow violation"),
        ("halted", "Wurmloch geräumt", "anomaly removal halted"),
        ("reception", "Will_Reception erhältlich", "will reception"),
        ("debris", "Trümmer-AFK", "debris falling due"),
        ("distortion", "Trümmer-AFK · Hälfte", "distortion escalated"),
        // "Loading from the destroyed timeline" also follows Will_Reception; only the glitched first line marks the end.
        ("spacetime", "Trümmer-AFK beendet", "spacetim"),
        ("expansion", "Wurmloch aktiviert", "expansion create"),
        ("boss", "Boss-Spawn", "hadum vuhura"),
        ("boss-kill", "AFK-Beginn", "temporary damage to dimensional"),
        ("end", "AFK-Ende", "reconstruct space"),
    ];

    internal static IReadOnlyList<(string Kind, string Label)> Parse(string text)
    {
        var normalized = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        return Definitions.Where(d => normalized.Contains(d.Phrase, StringComparison.Ordinal))
            .Select(d => (d.Kind, d.Label)).Distinct().ToArray();
    }
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
