using System.Drawing;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// Full-frame normal-loot panel and slot geometry used by BDO Companion 0.7.4.
/// Rectangles use exclusive right and bottom edges, matching D3D11_BOX and OpenCV.
/// </summary>
public static class CompanionNormalLootGeometry
{
    private const float NormalLeftExtent = 165f;
    private const float RareLeftExtent = 125f;
    private const float RightExtent = 260f;
    private const float VerticalExtent = 150f;
    private const float SlotHeight = 50f;
    private const float SlotLeftInset = 40f;

    public static Rectangle CalculatePanelBounds(CompanionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ValidateScale(calibration.UiScale);
        if (calibration.ScreenWidth <= 0 || calibration.ScreenHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(calibration),
                "The calibrated screen dimensions must be positive.");
        }

        return CalculatePanelBounds(
            calibration.LootAnchorX,
            calibration.LootAnchorY,
            NormalLeftExtent,
            calibration);
    }

    /// <summary>
    /// Calculates Companion's mode-1 panel from the optional visible UIData 161
    /// anchor. Mode 1 uses a 125-scale-pixel left extent instead of mode 0's 165.
    /// </summary>
    public static Rectangle CalculateRarePanelBounds(CompanionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ValidateCalibration(calibration);
        if (!calibration.HasRareLootAnchor)
        {
            throw new InvalidOperationException(
                "The calibration has no visible UIData Index 161 position.");
        }

        return CalculatePanelBounds(
            calibration.RareLootAnchorX,
            calibration.RareLootAnchorY,
            RareLeftExtent,
            calibration);
    }

    /// <summary>
    /// Calculates Companion's single mode-1 recognition band. Unlike normal
    /// loot, mode 1 does not split the panel into six rows: it takes the middle
    /// 20 percent after skipping the first 40 percent of the rare panel.
    /// </summary>
    public static Rectangle CalculateRareBandCrop(CompanionCalibration calibration)
    {
        var panel = CalculateRarePanelBounds(calibration);
        var relativeY = RoundAwayFromZero(panel.Height * 0.4f);
        var height = RoundAwayFromZero(panel.Height * 0.2f);

        return new Rectangle(
            panel.Left,
            checked(panel.Top + relativeY),
            panel.Width,
            height);
    }

    private static Rectangle CalculatePanelBounds(
        int anchorX,
        int anchorY,
        float leftExtent,
        CompanionCalibration calibration)
    {
        var left = Math.Max(
            anchorX - FloorPositive(leftExtent * calibration.UiScale),
            0);
        var top = Math.Max(
            anchorY - FloorPositive(VerticalExtent * calibration.UiScale),
            0);
        var right = Math.Min(
            anchorX + CeilingPositive(RightExtent * calibration.UiScale),
            calibration.ScreenWidth);
        var bottom = Math.Min(
            anchorY + CeilingPositive(VerticalExtent * calibration.UiScale),
            calibration.ScreenHeight);

        if (right < left || bottom < top)
        {
            throw new ArgumentOutOfRangeException(
                nameof(calibration),
                "The loot anchor lies outside the calibrated screen.");
        }

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static void ValidateCalibration(CompanionCalibration calibration)
    {
        ValidateScale(calibration.UiScale);
        if (calibration.ScreenWidth <= 0 || calibration.ScreenHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(calibration),
                "The calibrated screen dimensions must be positive.");
        }
    }

    /// <summary>
    /// Returns Companion's scheduled normal-loot crops in bottom-to-top order.
    /// The one-pixel overlaps produced by independently rounded y coordinates and
    /// heights are retained.
    /// </summary>
    public static IReadOnlyList<Rectangle> CalculateSlotCrops(CompanionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        return CalculateSlotCrops(CalculatePanelBounds(calibration), calibration.UiScale);
    }

    internal static IReadOnlyList<Rectangle> CalculateSlotCrops(
        Rectangle panelBounds,
        float uiScale)
    {
        ValidateScale(uiScale);
        if (panelBounds.Width <= 0 || panelBounds.Height <= 0)
        {
            return Array.Empty<Rectangle>();
        }

        var leftInset = RoundAwayFromZero(SlotLeftInset * uiScale);
        var width = panelBounds.Width - leftInset;
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(panelBounds),
                "The panel is narrower than Companion's normal-loot left inset.");
        }

        var step = SlotHeight * uiScale;
        var nominalHeight = RoundAwayFromZero(step);
        var remaining = (float)panelBounds.Height;
        var result = new List<Rectangle>(6);
        while (step <= remaining)
        {
            remaining -= step;
            var relativeY = RoundAwayFromZero(remaining);
            var height = nominalHeight;
            if (relativeY + height > panelBounds.Height)
            {
                height = panelBounds.Height - relativeY;
            }

            result.Add(new Rectangle(
                checked(panelBounds.Left + leftInset),
                checked(panelBounds.Top + relativeY),
                width,
                height));
        }

        return result;
    }

    private static void ValidateScale(float uiScale)
    {
        if (!float.IsFinite(uiScale) || uiScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(uiScale));
        }
    }

    private static int FloorPositive(float value) => checked((int)MathF.Floor(value));

    private static int CeilingPositive(float value) => checked((int)MathF.Ceiling(value));

    private static int RoundAwayFromZero(float value) =>
        checked((int)MathF.Round(value, MidpointRounding.AwayFromZero));
}
