using System.Diagnostics;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class BoundedDecisionLogTests(ITestOutputHelper output)
{
    [Fact]
    public void RepeatedDecisionsDoNotProduceTextUpdates()
    {
        var log = new BoundedDecisionLog();
        var decision = Decision();
        Assert.True(log.Append([decision]).HasChanges);
        for (var index = 0; index < 10_000; index++)
            Assert.False(log.Append([decision]).HasChanges);
        Assert.Equal(1, log.LineCount);
    }

    [Fact]
    public void ChangingRowPositionDoesNotRepeatTheSameDecision()
    {
        var log = new BoundedDecisionLog();
        var decision = Decision();
        log.Append([decision]);
        var moved = decision with { Observation = decision.Observation with { Slot = 3 } };
        Assert.False(log.Append([moved]).HasChanges);
    }

    [Fact]
    public void LongSessionKeepsTextAndHistoryBounded()
    {
        var log = new BoundedDecisionLog();
        var displayed = string.Empty;
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < 100_000; index++)
        {
            var update = log.Append([Decision()]);
            displayed = displayed[update.RemovePrefixCharacters..] + update.AppendedText;
            Assert.InRange(log.LineCount, 0, BoundedDecisionLog.MaximumLines);
            Assert.InRange(displayed.Length, 0, BoundedDecisionLog.MaximumCharacters);
            Assert.Equal(displayed.Length, log.CharacterCount);
        }
        Assert.Equal(BoundedDecisionLog.MaximumLines, log.LineCount);
        output.WriteLine($"100,000 distinct decisions: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:N0} ms; {log.LineCount} lines / {displayed.Length:N0} characters retained.");
    }

    [Fact]
    public void AFullBurstAndOversizedOcrCannotExceedLimits()
    {
        var log = new BoundedDecisionLog();
        var first = log.Append([Decision()]);
        var displayed = first.AppendedText!;
        var burst = Enumerable.Range(0, 1_000)
            .Select(_ => Decision(new string('x', 10_000) + "\r\nnoise"))
            .ToArray();
        var update = log.Append(burst);
        displayed = displayed[update.RemovePrefixCharacters..] + update.AppendedText;

        Assert.InRange(displayed.Length, 1, BoundedDecisionLog.MaximumCharacters);
        Assert.Equal(displayed.Length, log.CharacterCount);
        Assert.Equal(log.LineCount, displayed.Split(Environment.NewLine).Length - 1);
        Assert.All(displayed.Split(Environment.NewLine), line =>
            Assert.True(line.Length <= BoundedDecisionLog.MaximumLineCharacters));
    }

    [Fact]
    public void NativeCorrectionAndPendingBatchAreLabeledWithoutConfirmationClaims()
    {
        var log = new BoundedDecisionLog();
        var correction = Decision() with
        {
            Observation = Decision().Observation with { Quantity = -1 },
        };
        var pending = Decision() with { Status = LootTrackingDecisionStatus.Pending };

        var update = log.Append([correction, pending]);

        Assert.Contains("Korrigiert", update.AppendedText);
        Assert.Contains("Im Frame-Abgleich", update.AppendedText);
        Assert.DoesNotContain("In Prüfung", update.AppendedText);
    }

    [Fact]
    public void ClearReleasesHistoryAndAllowsFreshDisplay()
    {
        var log = new BoundedDecisionLog();
        var decision = Decision();
        log.Append([decision]);
        log.Clear();
        Assert.Equal(0, log.LineCount);
        Assert.Equal(0, log.CharacterCount);
        Assert.True(log.Append([decision]).HasChanges);
    }

    private static LootTrackingDecision Decision(string itemName = "Trash") =>
        new(new LootObservation(LootSource.Normal, 0, itemName, itemName, 25, 1, 1, 0, null),
            Guid.NewGuid(), LootTrackingDecisionStatus.Counted, "new-row");
}
