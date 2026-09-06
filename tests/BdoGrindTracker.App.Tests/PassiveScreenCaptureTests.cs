using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class PassiveScreenCaptureTests
{
    [Theory]
    [InlineData(12, true)]
    [InlineData(13, true)]
    [InlineData(14, true)]
    [InlineData(15, false)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    [InlineData(18, true)]
    [InlineData(19, true)]
    [InlineData(0, false)]
    public void HdrPredicateMatchesCompanionsOutputColorSpaceMask(
        int colorSpace,
        bool expected)
    {
        Assert.Equal(expected, PassiveScreenCapture.IsHdrColorSpace(colorSpace));
    }
}
