using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed class PrivateItemChatQuantityRecoveryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExistingHistoryNeverSuppliesAQuantity()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        var panel = Panel(("Helmet", null), ("Stone", 1));
        var chat = Chat(("Stone", 1), ("Helmet", 6));

        Assert.Same(panel, recovery.Recover(panel, chat, Start));
        Assert.Same(panel, recovery.Recover(panel, chat, Start.AddSeconds(1)));
        Assert.Equal(0, recovery.LastRecoveredCount);
    }

    [Fact]
    public void PairedNewTailsFillOnlyMissingQuantityAndPreserveMetadataAndInputOrder()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Stone", 1));
        panel[0] = panel[0] with { RawText = "Helmet", VisualFingerprint = 42 };
        Array.Reverse(panel);

        var result = recovery.Recover(panel, Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1));

        Assert.Same(panel[0], result[0]);
        Assert.Equal(panel[1] with { Quantity = 6 }, result[1]);
        Assert.Equal(1, recovery.LastRecoveredCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void ExistingQuantityIsAuthoritativeEvenWhenChatHasAnotherNumber(int quantity)
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", quantity), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
        Assert.Equal(0, recovery.LastRecoveredCount);
    }

    [Fact]
    public void MultipleNewRowsMatchTheChatTailInItsExactOrder()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Dust", 2), ("Stone", 1));

        var result = recovery.Recover(panel,
            Chat(("Stone", 1), ("Dust", 2), ("Helmet", 6)), Start.AddSeconds(1));

        Assert.Equal(new int?[] { 6, 2, 1 }, result.Select(row => row.Quantity));
        Assert.Same(panel[1], result[1]);
    }

    [Fact]
    public void ConflictingKnownQuantityInTheNewTailInvalidatesThePairing()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Dust", 3), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Dust", 2), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void IndependentlyMovingSurfacesMustAlreadyAgreeOnTheirPreviousAnchor()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Helmet", 4)), Chat(("Helmet", 6)), Start);
        // The chat can still be one drop behind the panel. Its new x4 must not
        // become the quantity of the actual next, incompletely read panel drop.
        var panel = Panel(("Helmet", null), ("Helmet", 4));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Helmet", 6), ("Helmet", 4)), Start.AddSeconds(1)));
        Assert.Equal(0, recovery.LastRecoveredCount);
    }

    [Fact]
    public void EveryPreviousAnchorMustAgreeAcrossTheTwoSurfaces()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Dust", 2), ("Helmet", 4)),
            Chat(("Helmet", 6), ("Dust", 2)), Start);
        var panel = Panel(("Helmet", null), ("Dust", 2), ("Helmet", 4));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Helmet", 6), ("Dust", 2), ("Helmet", 8)), Start.AddSeconds(1)));
    }

    [Fact]
    public void DifferentItemOrOrderCannotBorrowTheNewestChatQuantity()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Dust", 2), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6), ("Dust", 2)), Start.AddSeconds(1)));
    }

    [Fact]
    public void ConsecutiveIdenticalMessagesRemainSeparatePositions()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Helmet", null), ("Stone", 1));

        var result = recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6), ("Helmet", 6)), Start.AddSeconds(1));

        Assert.Equal(new int?[] { 6, 6, 1 }, result.Select(row => row.Quantity));
        Assert.Equal(2, recovery.LastRecoveredCount);
    }

    [Fact]
    public void DifferentRepeatedQuantitiesKeepTheirPositions()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Helmet", null), ("Stone", 1));

        var result = recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 4), ("Helmet", 6)), Start.AddSeconds(1));

        Assert.Equal(new int?[] { 6, 4, 1 }, result.Select(row => row.Quantity));
    }

    [Fact]
    public void OneChatMessageCannotSupplyTwoNewPanelRows()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Helmet", null), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void ExtraChatTailCannotBeSkippedToFindAnOlderMatchingItem()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6), ("Dust", 2)), Start.AddSeconds(1)));
    }

    [Fact]
    public void UnambiguousChatScrollAndPanelPushMatchNewTail()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Dust", 2), ("Stone", 1)),
            Chat(("Stone", 1), ("Dust", 2)), Start);
        var panel = Panel(("Helmet", null), ("Dust", 2), ("Stone", 1));

        var result = recovery.Recover(panel,
            Chat(("Dust", 2), ("Helmet", 6)), Start.AddSeconds(1));

        Assert.Equal(6, result[0].Quantity);
    }

    [Fact]
    public void AmbiguousRepeatedChatOverlapDoesNotInventAnArrival()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Stone", 1)),
            Chat(("Stone", 1), ("Stone", 1)), Start);
        var panel = Panel(("Helmet", null), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void AmbiguousRepeatedPanelAnchorsDoNotGuessTheNumberOfNewRows()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Stone", 1), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void AStationaryOrUncertainPanelHasNoProvenNewTail()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        var panel = Panel(("Helmet", null), ("Stone", 1));
        recovery.Recover(panel, Chat(("Stone", 1)), Start);

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void MissingPreviousPanelQuantityCannotServeAsAMovementAnchor()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Stone", null)), Chat(("Stone", 1)), Start);
        var panel = Panel(("Helmet", null), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void RejectedNewestRowIsABarrierAndOlderMissingRowCannotBorrowItsQuantity()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Helmet", null), ("Stone", 1)), Chat(("Stone", 1)), Start);
        var panel = Panel(("Helmet", 6), ("Helmet", null), ("Stone", 1));
        panel[0] = panel[0] with { ItemName = null, RejectionReason = "ocr-geometry" };

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
        Assert.Null(panel[1].Quantity);
    }

    [Fact]
    public void MissingNewestSlotCannotMakeAnOlderRowTheNewestDrop()
    {
        var recovery = Warm();
        var oldRow = Panel(("Helmet", null), ("Helmet", null), ("Stone", 1))[1];
        LootObservation[] panel = [oldRow];

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void SlotShiftWithoutNativeUpwardMovementIsNotAnAnchor()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Stone", 1));
        panel[1] = panel[1] with { NativeY = 250 };

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void StationaryChatRediscoveryIsNotANewMessage()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Stone", 1));
        var stationary = Chat(("Stone", 1), ("Helmet", 6));
        stationary[0] = stationary[0] with { Y = 420 };
        stationary[1] = stationary[1] with { Y = 450 };

        Assert.Same(panel, recovery.Recover(panel, stationary, Start.AddSeconds(1)));
    }

    [Fact]
    public void ATemporaryUnreadableBottomChatLineCannotBecomeFreshOnItsReturn()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        var original = Chat(("Stone", 1), ("Helmet", 6));
        recovery.Recover(Panel(("Stone", 1)), original, Start);
        recovery.Recover(Panel(("Stone", 1)), [original[0]], Start.AddSeconds(1));
        var panel = Panel(("Helmet", null), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel, original, Start.AddSeconds(2)));
    }

    [Fact]
    public void DownwardChatScrollCannotMakeTheNewTailFresh()
    {
        var recovery = Warm();
        var downward = Chat(("Stone", 1), ("Helmet", 6));
        downward[0] = downward[0] with { Y = 440 };
        downward[1] = downward[1] with { Y = 470 };
        var panel = Panel(("Helmet", null), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel, downward, Start.AddSeconds(1)));
    }

    [Fact]
    public void EveryOverlappingChatLineMustMoveByTheSameTailDistance()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Dust", 2), ("Stone", 1)),
            Chat(("Stone", 1), ("Dust", 2)), Start);
        var inconsistent = Chat(("Stone", 1), ("Dust", 2), ("Helmet", 6));
        inconsistent[0] = inconsistent[0] with { Y = inconsistent[0].Y + 10 };
        var panel = Panel(("Helmet", null), ("Dust", 2), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel, inconsistent, Start.AddSeconds(1)));
    }

    [Fact]
    public void SmallChatOcrPositionJitterStillAllowsAConsistentAppend()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Dust", 2), ("Stone", 1)),
            Chat(("Stone", 1), ("Dust", 2)), Start);
        var jitter = Chat(("Stone", 1), ("Dust", 2), ("Helmet", 6));
        jitter[0] = jitter[0] with { Y = jitter[0].Y + 2 };
        jitter[2] = jitter[2] with { Y = jitter[2].Y + 1 };

        var result = recovery.Recover(Panel(("Helmet", null), ("Dust", 2), ("Stone", 1)),
            jitter, Start.AddSeconds(1));

        Assert.Equal(6, result[0].Quantity);
    }

    [Fact]
    public void AnUnchangedChatCannotBeReusedForALaterDifferentDrop()
    {
        var recovery = Warm();
        var chat = Chat(("Stone", 1), ("Helmet", 6));
        var first = Panel(("Helmet", null), ("Stone", 1));
        Assert.Equal(6, recovery.Recover(first, chat, Start.AddSeconds(1))[0].Quantity);
        recovery.Recover([], chat, Start.AddSeconds(2));
        var differentDrop = Panel(("Helmet", null), ("Stone", 1));

        Assert.Same(differentDrop, recovery.Recover(differentDrop, chat, Start.AddSeconds(3)));
        Assert.Equal(0, recovery.LastRecoveredCount);
    }

    [Fact]
    public void RepeatedSnapshotDoesNotApplyTheSameChatMessageAgain()
    {
        var recovery = Warm();
        var chat = Chat(("Stone", 1), ("Helmet", 6));
        var panel = Panel(("Helmet", null), ("Stone", 1));
        Assert.Equal(6, recovery.Recover(panel, chat, Start.AddSeconds(1))[0].Quantity);

        Assert.Same(panel, recovery.Recover(panel, chat, Start.AddSeconds(2)));
        Assert.Equal(0, recovery.LastRecoveredCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(7)]
    public void NonIncreasingTimeOrLongCaptureGapWarmsANewBaseline(int second)
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Stone", 1));

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(second)));
    }

    [Fact]
    public void ResetOrMissingChatRequiresANewHistoryBaseline()
    {
        var recovery = Warm();
        recovery.Reset();
        var panel = Panel(("Helmet", null), ("Stone", 1));
        var chat = Chat(("Stone", 1), ("Helmet", 6));
        Assert.Same(panel, recovery.Recover(panel, chat, Start.AddSeconds(1)));
        recovery.Recover(Panel(("Stone", 1)), [], Start.AddSeconds(2));

        Assert.Same(panel, recovery.Recover(panel, chat, Start.AddSeconds(3)));
    }

    [Fact]
    public void RareRowsAreNeverChanged()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Stone", 1));
        panel[0] = panel[0] with { Source = LootSource.Rare };

        Assert.Same(panel, recovery.Recover(panel,
            Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
    }

    [Fact]
    public void ExcessiveInputFailsClosed()
    {
        var recovery = Warm();
        var panel = Panel(("Helmet", null), ("Stone", 1));
        var chat = Enumerable.Range(0, PrivateItemChatQuantityRecovery.MaximumChatLines + 1)
            .Select(index => new PrivateItemChatLine("Helmet", 6, index, "Helmet x6")).ToArray();

        Assert.Same(panel, recovery.Recover(panel, chat, Start.AddSeconds(1)));
    }

    [Fact]
    public void OneChatFillFollowedByMissingQuantityMatchesTheFullyRecognizedCounter()
    {
        var recovery = Warm();
        var chat = Chat(("Stone", 1), ("Helmet", 6));
        var uncertain = Panel(("Helmet", null), ("Stone", 1));
        var filled = recovery.Recover(uncertain, chat, Start.AddSeconds(1));
        var unchanged = recovery.Recover(uncertain, chat, Start.AddSeconds(2));
        IReadOnlyList<LootObservation>[] frames = [Panel(("Stone", 1)), filled, unchanged, []];
        IReadOnlyList<LootObservation>[] reference =
        [
            Panel(("Stone", 1)),
            Panel(("Helmet", 6), ("Stone", 1)),
            Panel(("Helmet", 6), ("Stone", 1)),
            [],
        ];

        Assert.Equal(6, filled[0].Quantity);
        Assert.Null(unchanged[0].Quantity);
        Assert.Equal(Count(reference), Count(frames));
        Assert.Equal(new uint[] { 6 }, Count(frames).Where(entry => entry.Name == "Helmet").Select(entry => entry.Count));
    }

    [Fact]
    public void RealCounterRepairsAMissingQuantityBeforeAndAfterOneGoodFrame()
    {
        IReadOnlyList<LootObservation>[] frames =
        [
            Panel(("Helmet", null)),
            Panel(("Helmet", 6)),
            Panel(("Helmet", null)),
            [],
        ];

        var counted = Assert.Single(Count(frames));
        Assert.Equal("Helmet", counted.Name);
        Assert.Equal(6u, counted.Count);
    }

    [Fact]
    public void NoPanelRowsMeansChatCannotCreateLootEvents()
    {
        var recovery = Warm();

        Assert.Empty(recovery.Recover([], Chat(("Stone", 1), ("Helmet", 6)), Start.AddSeconds(1)));
        Assert.Equal(0, recovery.LastRecoveredCount);
    }

    private static PrivateItemChatQuantityRecovery Warm()
    {
        var recovery = new PrivateItemChatQuantityRecovery();
        recovery.Recover(Panel(("Stone", 1)), Chat(("Stone", 1)), Start);
        return recovery;
    }

    private static LootObservation[] Panel(params (string Name, int? Quantity)[] items) =>
        items.Select((item, index) => new LootObservation(LootSource.Normal, index, item.Name,
            item.Name, item.Quantity, 0.95, 0, null, null) { NativeY = 250 - index * 50 }).ToArray();

    private static PrivateItemChatLine[] Chat(params (string Name, int Quantity)[] items) =>
        items.Select((item, index) => new PrivateItemChatLine(item.Name, item.Quantity,
            420 - ((items.Length - 1 - index) * 30),
            $"You have obtained [{item.Name}] x{item.Quantity}. (12:00)")).ToArray();

    private static CompanionRecognizedEntry[] Count(IEnumerable<IReadOnlyList<LootObservation>> frames)
    {
        var counter = new CompanionFrameReconciler();
        var events = new List<CompanionRecognizedEntry>();
        foreach (var frame in frames)
            events.AddRange(counter.ProcessFrame(frame.Select(row => new CompanionRecognizedEntry(
                row.ItemName!, unchecked((uint)(row.Quantity ?? -1)), row.NativeY!.Value)).ToArray()));
        events.AddRange(counter.Complete());
        return events.ToArray();
    }
}
