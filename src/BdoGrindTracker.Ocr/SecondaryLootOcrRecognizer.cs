using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// A second opinion on one owned row image. Results are observations only; this
/// interface never emits loot events or remembers rows from previous captures.
/// </summary>
public interface ISecondaryLootOcrRecognizer : IDisposable
{
    string BackendName { get; }
    string LanguageTag { get; }
    SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default);
}

/// <param name="Confidence">Engine confidence from zero to one, not a calibrated probability.</param>
public sealed record SecondaryLootOcrResult(
    string Text,
    float Confidence,
    CompanionOcrWordGeometry FirstWord)
{
    // Optional for existing callers and other recognizers. Coordinates refer to
    // this exact prepared image, not the original capture or normalized row.
    public IReadOnlyList<SecondaryLootOcrWord> Words { get; init; } = [];
}

public sealed record SecondaryLootOcrWord(string Text, CompanionOcrWordGeometry Geometry);

