using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Overlay;

/// <summary>Theme decoration outside the saved content canvas. Widget coordinates stay unchanged.</summary>
public readonly record struct OverlayWindowChrome(double Left, double Top, double Right, double Bottom)
{
    public double Horizontal => Left + Right;
    public double Vertical => Top + Bottom;
    public bool HasTitleBar => Top > 0;
    public double OuterWidth(double contentWidth) => contentWidth + Horizontal;
    public double OuterHeight(double contentHeight) => contentHeight + Vertical;

    public static OverlayWindowChrome For(string? themeId, bool showBorder) =>
        showBorder && AppThemes.Normalize(themeId) is AppThemes.BlackDesert or AppThemes.Cats ? new(2, 32, 2, 2) : default;
}
