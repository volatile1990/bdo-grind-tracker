using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RareEnhancementIdentityIntegrationTests
{
    private const string Earring = "Apeiron Earring";
    private const string Crystal = "HAN Wandering Origin Crystal";

    [Theory]
    [InlineData("lV Apeiron Earring x 1", false)]
    [InlineData("l Apeiron-Ohrring", true)]
    public void MisreadEnhancementCannotBecomeTwoBaseEarringsInTheEventHorizonInventory(
        string falseReading, bool acceptedByFirstPass)
    {
        var context = new LifetimeParsingContext(0,
            LootSpotCatalog.GetRequired(LootSpotCatalog.EventHorizonId).AllowedItems
                .Concat(LootSpotCatalog.EventItems).Distinct(StringComparer.Ordinal)
                .Select(name => new LifetimeParsingCatalogEntry(name,
                    ItemLocalizationCatalog.GermanNames.TryGetValue(name, out var german) ? [german] : [],
                    DropQuantityCatalog.GetBounds(LootSpotCatalog.EventHorizonId, name)?.IsFixedUnit == true,
                    LootSourceCatalog.GetRequired(name))).ToArray());
        var special = new LifetimeNormalReconciliationAdapter(context, false, LootSource.Rare, 1);
        var normal = new LifetimeLootReconciler().Complete(DateTimeOffset.UnixEpoch);
        var composer = new LifetimeLootProjectionComposer();
        using var mailbox = new FrameUiMailbox();
        var history = new SessionDropHistory();
        var metrics = new OverlayMetrics();
        var preferences = new TrackerPreferences { FavoriteItems = [Earring] };
        var state = new TrackerState { SessionId = Guid.NewGuid(), HasSession = true,
            SpotId = LootSpotCatalog.EventHorizonId };
        history.Update(state);
        var frame = 0;
        var genuineCrystal = new LootObservation(LootSource.Rare, 0, Crystal + " x 1",
            Crystal, 1, 1, 1, null, null);
        var enhanced = new LootObservation(LootSource.Rare, 0, falseReading,
            acceptedByFirstPass ? Earring : null, acceptedByFirstPass ? 1 : null,
            acceptedByFirstPass ? .94 : 0, 0, null, acceptedByFirstPass ? null : "native-catalog-miss");

        Observe(genuineCrystal, 30);
        // Two short misread notifications must not survive as two base items
        // when the following genuine loot outlasts the publication buffer.
        Observe(enhanced, 2);
        Observe(genuineCrystal, 30);
        Observe(enhanced, 2);
        Observe(genuineCrystal, 30);

        Assert.Equal(0, state.Loot.Totals.GetValueOrDefault(Earring));
        Assert.Equal(1, state.Loot.Totals[Crystal]);
        Assert.DoesNotContain(metrics.Update(state, preferences).Drops, drop => drop.CanonicalName == Earring);
        Assert.Empty(metrics.Update(state, preferences).DropMarkers);
        Assert.DoesNotContain(state.DropHistory, drop => drop.ItemName == Earring);

        // Rejecting a prefixed name must not suppress the next actual base drop.
        Observe(enhanced with { RawText = Earring, ItemName = Earring, Quantity = 1,
            NameConfidence = 1, RejectionReason = null }, 30);
        Assert.Equal(1, state.Loot.Totals[Earring]);
        Assert.Equal(1, Assert.Single(metrics.Update(state, preferences).Drops,
            drop => drop.CanonicalName == Earring).Quantity);
        Assert.Single(metrics.Update(state, preferences).DropMarkers);

        void Observe(LootObservation row, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var elapsed = TimeSpan.FromMilliseconds(++frame * 200);
                var at = DateTimeOffset.UnixEpoch + elapsed;
                special.ProcessObservations([row], at);
                var (projection, _) = composer.Combine(normal, special.Projection!, at);
                mailbox.Publish(new([], [row.RawText], 1, "synthetic-rare", 1, 1, 1, 1, null)
                    { LootProjection = projection }, capturedAt: at);
                using var update = mailbox.TakeLatest();
                state = state with { Elapsed = elapsed, Loot = update?.Totals ?? state.Loot };
                state = state with { DropHistory = history.Update(state) };
            }
        }
    }
}
