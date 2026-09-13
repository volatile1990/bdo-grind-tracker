using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// Keeps the special announcement's full text band after HDR-to-SDR conversion.
/// Local contrast preserves colored lettering without assuming its original HSV range.
/// </summary>
public static class ToneMappedRareRowProcessor
{
    public static CompanionRareRowResult Process(Mat sourceBand, int y)
    {
        using var images = RareLootRecoveryPreprocessor.Prepare(sourceBand, NormalLootRecoveryVariant.AdaptiveThreshold);
        using var gray = CompanionNormalRowProcessor.ConvertToGray(sourceBand);
        var luma = Cv2.Mean(gray).Val0;
        // Test only the actual band; the ownership border contains no evidence.
        using var band = new Mat(images.NameImage,
            new Rect(0, 0, images.RecognizedTextWidth, RareLootRecoveryPreprocessor.NormalizedHeight));
        var blank = CompanionNormalRowProcessor.IsBlank(band);
        return new(y, blank, luma, images.RecognizedTextWidth,
            blank ? null : images.NameImage.Clone());
    }
}
