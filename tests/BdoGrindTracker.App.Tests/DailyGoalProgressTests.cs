using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.Tests;

public sealed class DailyGoalProgressTests
{
    [Theory]
    [InlineData(650,1000,.65)]
    [InlineData(1200,1000,1)]
    [InlineData(-20,1000,0)]
    public void ProgressFitsBarWithoutLosingActualNet(decimal earned, decimal target, double fraction)
    {
        var goal = new DailyGoalProgress(earned,target);
        Assert.Equal(fraction,goal.Fraction,5);
        Assert.Equal(earned,goal.Earned);
        if (earned >= target) Assert.Equal("Tagesziel erreicht",goal.Detail);
    }
    [Fact]
    public void MissingGoalDoesNotAppearAsAchieved()
    {
        var goal = new DailyGoalProgress(1000);
        Assert.Equal(0,goal.Fraction);
        Assert.Equal("Kein Tagesziel",goal.Value);
    }
}
