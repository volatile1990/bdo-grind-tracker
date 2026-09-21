using System.Net;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class ConsumableAssetTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task EveryBundledConsumableIconIsServedByTheBrowserHost()
    {
        var definitions = BuffPriceCatalog.Definitions.Concat(AutomaticBuffCatalog.Default.GroupDefinitions);
        var snapshot = new BuffLedgerSnapshot(definitions.Select(definition => new BuffConsumption(
            definition.Id, definition.Name, definition.MarketItemId, DateTimeOffset.UtcNow, null)).ToArray(), [], []);
        var items = ConsumablesPresentation.Create(snapshot, "en").Items;
        Assert.All(items, item => Assert.NotNull(item.IconPath));
        var paths = items.Select(item => item.IconPath!).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(37, paths.Length);
        using var client = factory.CreateClient();
        foreach (var path in paths)
        {
            using var response = await client.GetAsync("/" + path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        }
    }
}
