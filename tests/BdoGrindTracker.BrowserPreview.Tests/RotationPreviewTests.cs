using System.Net;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class RotationPreviewTests
{
    [Theory]
    [InlineData("best", "2:00")]
    [InlineData("ideal", "1:50")]
    [InlineData("sectors", "2:00")]
    public async Task PortableOverlayRendersTheSelectedReferenceAndCurrentRotation(string comparison, string duration)
    {
        RotationEvent[] events = [new("start", "Rotationsstart", 0), new("drakania", "Drakania-Spawn", 30)];
        var state = new TrackerState { SpotId = LootSpotCatalog.HermesiaId, Rotation = new()
        {
            SpotId = LootSpotCatalog.HermesiaId, Elapsed = 60, Events = events, Synchronized = true,
            Best = new(120, events), Ideal = new(110, events),
        } };
        var snapshot = new OverlayMetrics().Update(state, new());
        Assert.True(snapshot.Rotation.HasProfile);
        Assert.Contains("Hermesia", snapshot.Rotation.SpotName);
        var widget = OverlayCatalog.CreateWidget("rotation-monitor") with { RotationComparison = comparison };
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var markup = await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<OverlayWidgetPreview>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Widget"] = widget, ["Snapshot"] = snapshot }))).ToHtmlString()));

        Assert.Contains("Rotation Monitor: Referenz oben, aktuelle Rotation unten", markup);
        Assert.Contains("Gesamtzeit der Referenzrotation</title>" + duration, markup);
        Assert.Contains("Gesamtzeit der aktuellen Rotation</title>1:00", markup);
        Assert.Contains("data-phase=\"drakania\"", markup);
        Assert.Contains("class=\"rotation-playhead\"", markup);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(LootSpotCatalog.AphrodonId)]
    [InlineData("unknown-spot")]
    public void OtherSpotsDoNotInheritTheDemoRotation(string? spotId)
    {
        var snapshot = new OverlayMetrics().Update(new TrackerState
        {
            SpotId = spotId, Rotation = HermesiaRotationDemo.At(350),
        }, new());

        Assert.Equal(spotId, snapshot.Rotation.SpotId);
        Assert.False(snapshot.Rotation.HasProfile);
        Assert.False(snapshot.Rotation.Synchronized);
        Assert.Null(snapshot.Rotation.Best);
        Assert.Null(snapshot.Rotation.Ideal);
        Assert.Empty(snapshot.Rotation.Events);
        Assert.Empty(snapshot.Rotation.SectorBests);
        Assert.DoesNotContain("Hermesia", snapshot.Rotation.SpotName);
    }
}
