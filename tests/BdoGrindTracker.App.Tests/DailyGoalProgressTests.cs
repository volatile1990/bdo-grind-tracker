using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;

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

    [Theory]
    [InlineData("1000000000000", "0.0000000000000000000001")]
    [InlineData("79228162514264337593543950335", "0.0000000000000000000000000001")]
    [InlineData("79228162514264337593543950335", "1")]
    public void TinyPositiveGoalsAndLargeEarningsCannotOverflowTheDisplayedProgress(string earned, string target)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var goal = new DailyGoalProgress(decimal.Parse(earned, culture), decimal.Parse(target, culture));
        Assert.Equal(1, goal.Fraction);
        Assert.EndsWith(" %", goal.Percentage);
        Assert.DoesNotContain("∞", goal.Percentage);
        Assert.Equal("Tagesziel erreicht", goal.Detail);
    }

    [Fact]
    public async Task DailyGoalOnlyOverlayRefreshesItsTargetAtTheNextLocalDay()
    {
        await using var tracker = new PreviewTrackerSession();
        var goals = new GrindGoalStore(null);
        var today = DateOnly.FromDateTime(DateTime.Today);
        goals.Set([today.AddDays(-1)], 100);
        goals.Set([today], 200);
        using var service = new OverlayService(tracker, goals: goals);
        await service.SaveAsync(service.Settings with { Widgets = [OverlayCatalog.CreateWidget("daily-goal")] });
        typeof(OverlayService).GetProperty(nameof(OverlayService.Snapshot))!.SetValue(service, service.Snapshot with
        {
            ClockUtcNow = DateTimeOffset.Now.AddDays(-1), DailyGoal = new(0, 100),
        });

        service.RefreshClock();

        Assert.Equal(today, DateOnly.FromDateTime(service.Snapshot.ClockUtcNow.LocalDateTime));
        Assert.Equal(200, service.Snapshot.DailyGoal.Target);
    }
}
