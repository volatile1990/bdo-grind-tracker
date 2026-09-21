using System.Net;
using BdoGrindTracker.App.Components;
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
        Assert.Contains("Verbrauchsprotokoll (1)", html);
        Assert.Contains("nicht addiert", html);
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

    private static async Task<string> Render(BuffLedgerSnapshot? snapshot, bool saved = false)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<BuffReadout>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BuffReadout.Value)] = snapshot,
                [nameof(BuffReadout.Saved)] = saved,
            }))).ToHtmlString()));
    }
}
