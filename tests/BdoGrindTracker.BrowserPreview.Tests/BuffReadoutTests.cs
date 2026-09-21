using System.Net;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core.Buffs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class BuffReadoutTests
{
    [Fact]
    public async Task UnknownObservationsNeverPresentZeroCostAsMeasured()
    {
        var html = await Render(null);
        Assert.Contains("Noch keine Buff-Beobachtungen", html);
        Assert.DoesNotContain("0 Silber", html);
    }

    [Fact]
    public async Task UnpricedConsumptionAndCachedRunningTimeStayExplicit()
    {
        var snapshot = new BuffLedgerSnapshot(
            [new("frenzy-draught", "Frenzy Draught", 795, DateTimeOffset.UtcNow, null)],
            [new("frenzy-draught", "Frenzy Draught", 795, TimeSpan.FromMinutes(5), 123, TimeSpan.Zero, true)], []);

        var html = await Render(snapshot);

        Assert.Contains("123 Silber", html);
        Assert.Contains("Teilbetrag", html);
        Assert.Contains("Preis unbekannt", html);
        Assert.Contains("gespeicherten Marktpreisen", html);
        Assert.Contains("Buffprotokoll (1)", html);
    }

    [Fact]
    public async Task HistoryDoesNotClaimThatPersistedTimersAreCurrentlyActive()
    {
        var snapshot = new BuffLedgerSnapshot([], [],
            [new("frenzy-draught", "Frenzy Draught", 795, TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow, null, true)]);
        Assert.Contains("Aktive Buffs", await Render(snapshot));
        Assert.DoesNotContain("Aktive Buffs", await Render(snapshot, saved: true));
    }

    [Fact]
    public async Task TentConsumptionShowsNpcPriceInsteadOfAMarketQuote()
    {
        var price = new BuffPrice(50_000_000m, "eu", null, false) { Source = BuffPriceSource.FixedNpc };
        var snapshot = new BuffLedgerSnapshot(
            [new("tent-adventurers-luck-v", "Adventurer's Luck V", null, DateTimeOffset.UtcNow, price)], [], []);
        var html = await Render(snapshot);
        Assert.Contains("NPC-Festpreis", html);
        Assert.DoesNotContain("Zentralmarkt · EU", html);
        Assert.DoesNotContain("Preis unbekannt", html);
    }

    [Fact]
    public async Task RepeatedUsesShowCountsHistoricalUnitPriceRangeAndKnownSubtotal()
    {
        var at = DateTimeOffset.UtcNow;
        var snapshot = new BuffLedgerSnapshot(
            [new("harmony-draught", "Harmony Draught", 1399, at, new(100, "eu", at, false)),
             new("harmony-draught", "Harmony Draught", 1399, at.AddMinutes(20), new(150, "eu", at, true)),
             new("harmony-draught", "Harmony Draught", 1399, at.AddMinutes(40), null),
             new("perfume-of-courage", "Perfume of Courage", 734, at, new(200, "eu", at, false))], [], []);

        var html = await Render(snapshot);

        Assert.Contains("Arznei der Harmonie", html);
        Assert.Contains("class=\"buff-used-count\">3 ×", html);
        Assert.Contains("class=\"buff-unit-price\">100–150 Silber", html);
        Assert.Contains("250 Silber *", html);
        Assert.Contains("450 Silber *", html);
        Assert.Contains("Teilbetrag: 1 von 4 gezählten Buffs ohne Preis.", html);
        Assert.Contains("Buffprotokoll (4)", html);
    }

    [Fact]
    public async Task EntirelyUnpricedConsumptionNeverPresentsFreeBuffs()
    {
        var snapshot = new BuffLedgerSnapshot(
            [new("harmony-draught", "Harmony Draught", 1399, DateTimeOffset.UtcNow, null)], [], []);

        var html = await Render(snapshot);

        Assert.Contains("class=\"buff-used-count\">1 ×", html);
        Assert.Contains("class=\"buff-used-cost\">Preis unbekannt", html);
        Assert.DoesNotContain("0 Silber", html);
    }

    [Fact]
    public async Task UnconfirmedActiveBaselineIsNotCountedWithoutABooking()
    {
        var at = DateTimeOffset.UtcNow;
        var snapshot = new BuffLedgerSnapshot([], [],
            [new("simple-cron-meal", "Simple Cron Meal", 9692, TimeSpan.FromMinutes(60), at,
                new(500, "eu", at, false), true)]);

        var html = await Render(snapshot);

        Assert.Contains("Cron-Mahlzeit: Einfach", html);
        Assert.Contains("Bereits aktiv", html);
        Assert.Contains("Noch keine verbrauchten Items", html);
        Assert.DoesNotContain("class=\"buff-used-count\"", html);
        Assert.DoesNotContain("500 Silber", html);
    }

    [Theory]
    [InlineData("de", "Bei erster Erkennung aktiv: 1", "Bei erster Erkennung aktiv", "Erneuerung bestätigt", "500 Silber", "1.200 Silber")]
    [InlineData("en", "Active when first detected: 1", "Active when first detected", "Refresh confirmed", "500 silver", "1,200 silver")]
    public async Task ConfirmedInitialObservationCountsOnceWithItsPriceAndLaterRefreshAddsOne(
        string language, string startingCount, string startEntry, string refreshEntry, string initialCost, string refreshedCost)
    {
        var at = DateTimeOffset.UtcNow;
        var price = new BuffPrice(500, "eu", at, false);
        var start = new BuffConsumption("simple-cron-meal", "Simple Cron Meal", 9692, at, price) { IsSessionStart = true };
        var snapshot = new BuffLedgerSnapshot([start], [],
            [new(start.BuffId, start.Name, start.MarketItemId, TimeSpan.FromMinutes(60), at, price, true)]);

        foreach (var html in new[] { await Render(snapshot, language: language), await Render(snapshot, saved: true, language: language) })
        {
            Assert.Contains("class=\"buff-used-count\">1 ×", html);
            Assert.Contains(startingCount, html);
            Assert.Contains(startEntry, html);
            Assert.Contains(initialCost, html);
            Assert.DoesNotContain("Grindstart", html);
            Assert.DoesNotContain("grind start", html);
        }

        var renewed = snapshot with
        {
            Consumptions = [start, new(start.BuffId, start.Name, start.MarketItemId, at.AddHours(1), new(700, "eu", at, false))],
            Active = [],
        };
        var afterRefresh = await Render(renewed, language: language);
        Assert.Contains("class=\"buff-used-count\">2 ×", afterRefresh);
        Assert.Contains(startingCount, afterRefresh);
        Assert.Contains(refreshEntry, afterRefresh);
        Assert.Contains(refreshedCost, afterRefresh);
    }

    [Fact]
    public async Task HistoryRetainsGroupedConsumptionWithoutPresentingAnActiveTimer()
    {
        var at = DateTimeOffset.UtcNow;
        var price = new BuffPrice(250, "eu", at, false);
        var snapshot = new BuffLedgerSnapshot(
            [new("simple-cron-meal", "Simple Cron Meal", 9692, at, price),
             new("simple-cron-meal", "Simple Cron Meal", 9692, at.AddHours(2), price)], [],
            [new("simple-cron-meal", "Simple Cron Meal", 9692, TimeSpan.FromMinutes(60), at, price, false)]);

        var html = await Render(snapshot, saved: true);

        Assert.Contains("class=\"buff-used-count\">2 ×", html);
        Assert.Contains("500 Silber", html);
        Assert.DoesNotContain("Aktive Buffs", html);
        Assert.DoesNotContain("01:00:00", html);
        Assert.DoesNotContain("Bei erster Erkennung aktiv", html);
    }

    [Fact]
    public async Task EnglishReadoutUsesLocalizedLabelsNamesAndNumbers()
    {
        var at = DateTimeOffset.UtcNow;
        var snapshot = new BuffLedgerSnapshot(
            [new("simple-cron-meal", "Cron-Mahlzeit: Einfach", 9692, at, new(1234, "eu", at, false))], [], []);

        var html = await Render(snapshot, language: "en");

        Assert.Contains("Used this session", html);
        Assert.Contains("Unit price", html);
        Assert.Contains("Simple Cron Meal", html);
        Assert.Contains("1,234 silver", html);
        Assert.DoesNotContain("Cron-Mahlzeit", html);
        Assert.DoesNotContain("Verbrauchskosten", html);
    }

    [Fact]
    public void GroupingKeepsDistinctVariantsSeparateEvenWhenTheirSavedNamesMatch()
    {
        var at = DateTimeOffset.UtcNow;
        var groups = BuffConsumptionPresentation.Group(
            [new("harmony-draught", "Harmony", 1399, at, new(100, "eu", at, false)),
             new("immortal-harmony-draught", "Harmony", 1400, at, new(500, "eu", at, false))]);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal(1, group.Count));
        Assert.Equal(600, groups.Sum(group => group.KnownCost));
    }

    [Theory]
    [InlineData("de", "Unsterblich: Arznei der Harmonie", "1.234 Silber", "5.678 Silber")]
    [InlineData("en", "Immortal: Harmony Draught", "1,234 silver", "5,678 silver")]
    public async Task LiveHarmonyKeepsNormalAndImmortalPricesSeparateWithoutRewritingHistory(
        string language, string immortalName, string normalPrice, string immortalPrice)
    {
        var at = DateTimeOffset.UtcNow;
        var snapshot = new BuffLedgerSnapshot(
            [new("harmony-draught-human", "[Party] Harmony Draught - Human", 1401, at, new(1234, "eu", at, false)),
             new("immortal-harmony-draught-human", "[Party] Immortal: Harmony Draught - Human", 1402, at,
                new(5678, "eu", at, false))], [], []);

        var live = await Render(snapshot, language: language);
        var saved = await Render(snapshot, saved: true, language: language);

        foreach (var html in new[] { live, saved })
        {
            Assert.Contains(immortalName, html);
            Assert.Contains(normalPrice, html);
            Assert.Contains(immortalPrice, html);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "class=\"buff-used-count\">1 ×").Count);
        }
        Assert.DoesNotContain("assigned by your profile", live);
    }

    [Theory]
    [InlineData("de", "Arznei der Harmonie (Variante unbekannt)", "Preis unbekannt")]
    [InlineData("en", "Harmony Draught (variant unknown)", "Price unknown")]
    public async Task IndistinguishableAutomaticGroupIsNamedWithoutClaimingAFreeOrSpecificVariant(
        string language, string expectedName, string expectedPrice)
    {
        var snapshot = new BuffLedgerSnapshot(
            [new("automatic-harmony", "Harmony Draught (variant unknown)", null, DateTimeOffset.UtcNow, null)], [], []);

        var html = await Render(snapshot, language: language);

        Assert.Contains(expectedName, html);
        Assert.Contains(expectedPrice, html);
        Assert.Contains("class=\"buff-used-count\">1 ×", html);
        Assert.DoesNotContain("0 Silber", html);
        Assert.DoesNotContain("0 silver", html);
        Assert.DoesNotContain("Immortal:", html);
    }

    [Theory]
    [InlineData("de", "Parfüm des Mutes (Variante unbekannt)")]
    [InlineData("en", "Perfume of Courage (variant unknown)")]
    public async Task SharedPerfumeSymbolDoesNotClaimTheImmortalCandidate(string language, string expectedName)
    {
        var snapshot = new BuffLedgerSnapshot(
            [new("automatic-perfume-of-courage", "Immortal: Perfume of Courage (variant unknown)",
                null, DateTimeOffset.UtcNow, null)], [], []);

        var html = await Render(snapshot, language: language);

        Assert.Contains(expectedName, html);
        Assert.DoesNotContain("Immortal:", html);
        Assert.DoesNotContain("Unsterblich:", html);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task TentDurationVariantShowsItsTimerWithoutCountingALaterBaseline(string language)
    {
        var at = DateTimeOffset.UtcNow;
        var price = new BuffPrice(10_000_000, "eu", null, false) { Source = BuffPriceSource.FixedNpc };
        var snapshot = new BuffLedgerSnapshot([], [],
            [new("tent-body-enhancement-300", "Body Enhancement (300 min)", null, TimeSpan.FromMinutes(280), at, price, true)]);

        var html = await Render(snapshot, language: language);

        Assert.Contains("04:40:00", html);
        Assert.DoesNotContain("class=\"buff-used-count\"", html);
    }

    private static async Task<string> Render(BuffLedgerSnapshot? snapshot, bool saved = false, string language = "de")
    {
        var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = language });
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<BuffReadout>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BuffReadout.Value)] = snapshot,
                [nameof(BuffReadout.Saved)] = saved,
            }))).ToHtmlString()));
    }
}
