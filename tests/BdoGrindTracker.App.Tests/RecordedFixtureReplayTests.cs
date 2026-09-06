using BdoGrindTracker.App.Diagnostics;

namespace BdoGrindTracker.App.Tests;

public sealed class RecordedFixtureReplayTests
{
    [Fact]
    public void FixedNativeFixtureCountsFollowingTrashAndKeepsRecordedSpotRejections()
    {
        var result = LootDiagnosticReplay.Run(Path.Combine(
            AppContext.BaseDirectory, "fixtures", "companion-counter-v1.jsonl"));
        Assert.True(result.TotalsMatch, result.ToDisplayText());
        Assert.True(result.EventTimelineMatches, result.ToDisplayText());
        Assert.Equal(1, result.Totals["BON Origin Shard"]);
        Assert.Equal(26, result.Totals["Black Crystal Fragment"]);
        Assert.False(result.Totals.ContainsKey("Black Gem Fragment"));
        Assert.True(result.HasFinalCompletion);
    }
}
