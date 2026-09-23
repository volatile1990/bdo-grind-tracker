using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class GarmothNavigationTests
{
    [Theory]
    [InlineData(null, "sessions")]
    [InlineData("sessions", "sessions")]
    [InlineData("connection", "connection")]
    [InlineData("unknown", "sessions")]
    public async Task SectionShowsTheSelectedAreaAndKeepsBothCategoriesReachable(string? section, string expected)
    {
        await Render(section, (_, _, markup) =>
        {
            AssertSection(markup(), expected);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SwitchingSectionsPreservesSessionFiltersPageAndPendingApiKey()
    {
        await Render(null, async (page, tracker, markup) =>
        {
            var entries = Enumerable.Range(0, 10).Select(index => Entry(LootSpotCatalog.HermesiaId, index)).ToList();
            entries.Add(Entry(LootSpotCatalog.HermesiaId, 11) with { GarmothUploadedAt = DateTimeOffset.Now });
            entries.Add(Entry(LootSpotCatalog.Spots.First(spot => spot.Id != LootSpotCatalog.HermesiaId).Id, 12));
            ((List<LootHistoryEntry>)typeof(PreviewTrackerSession)
                .GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracker)!).AddRange(entries);
            var search = Presentation.SpotName(LootSpotCatalog.HermesiaId);
            SetField(page, "_search", search);
            SetField(page, "_statusFilter", "pending");
            SetField(page, "_page", 2);
            SetField(page, "_apiKey", "pending-test-key");
            await page.SetParametersAsync(ParameterView.Empty);

            AssertSessionSelection(markup(), search);
            await ChangeSection(page, "connection");

            AssertSection(markup(), "connection");
            Assert.Contains("value=\"pending-test-key\"", markup());
            Assert.False(tracker.State.HasApiKey);

            await ChangeSection(page, "sessions");

            AssertSection(markup(), "sessions");
            AssertSessionSelection(markup(), search);
            Assert.Contains("value=\"pending-test-key\"", markup());
            Assert.False(tracker.State.HasApiKey);
        });
    }

    private static void AssertSessionSelection(string html, string search)
    {
        Assert.Contains($"value=\"{search}\"", html);
        Assert.Matches("<option[^>]*value=\"pending\"[^>]*selected", html);
        Assert.Contains("10 Treffer", html);
        Assert.Contains("9–10 von 10 Sessions", html);
        Assert.Contains("Seite 2 von 2", html);
        Assert.Equal(2, Regex.Matches(html, "data-session-id=").Count);
    }

    private static void AssertSection(string html, string expected)
    {
        var navigation = Regex.Match(html, "<nav[^>]*aria-label=\"Garmoth-Bereiche\"[^>]*>.*?</nav>", RegexOptions.Singleline);
        Assert.True(navigation.Success);
        var links = Regex.Matches(navigation.Value, "<a\\b[^>]*href=\"([^\"]+)\"[^>]*>");
        Assert.Equal(2, links.Count);
        Assert.Equal("garmoth", links[0].Groups[1].Value);
        Assert.Equal("garmoth?section=connection", links[1].Groups[1].Value);
        Assert.Equal(expected == "sessions", links[0].Value.Contains("aria-current=\"page\"", StringComparison.Ordinal));
        Assert.Equal(expected == "connection", links[1].Value.Contains("aria-current=\"page\"", StringComparison.Ordinal));
        foreach (var (section, label) in new[]
        {
            ("connection", "Garmoth-Verbindung und Automatik"), ("sessions", "Sessions für Garmoth")
        })
        {
            var area = Regex.Match(html, "<section[^>]*aria-label=\"" + Regex.Escape(label) + "\"[^>]*>");
            Assert.True(area.Success);
            Assert.Equal(section != expected, Regex.IsMatch(area.Value, @"\bhidden(?:\s|=|>)"));
        }
    }

    private static async Task ChangeSection(GarmothDashboard page, string section)
    {
        typeof(GarmothDashboard).GetProperty(nameof(GarmothDashboard.Section))!.SetValue(page, section);
        await page.SetParametersAsync(ParameterView.Empty);
    }

    private static LootHistoryEntry Entry(string spotId, int hoursAgo) => new()
    {
        SessionId = Guid.NewGuid(), SpotId = spotId, CharacterClass = "Warrior · Awakening",
        StartedAt = DateTimeOffset.Now.AddHours(-hoursAgo - 1), UpdatedAt = DateTimeOffset.Now.AddHours(-hoursAgo),
        Duration = TimeSpan.FromHours(1), Totals = new() { [Presentation.Profile(spotId)!.TrashItemName] = 100 },
        SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = true,
    };

    private static void SetField(GarmothDashboard page, string name, object value) =>
        typeof(GarmothDashboard).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private static async Task Render(string? section,
        Func<GarmothDashboard, PreviewTrackerSession, Func<string>, Task> test)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        var activator = new CapturingActivator(section);
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IComponentActivator>(activator).AddSingleton<NavigationManager, StaticNavigation>()
            .AddSingleton<IJSRuntime, NoJavaScript>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<GarmothDashboard>();
            await test(activator.Page!, tracker, () => WebUtility.HtmlDecode(rendered.ToHtmlString()));
        });
    }

    private sealed class CapturingActivator(string? section) : IComponentActivator
    {
        public GarmothDashboard? Page { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is GarmothDashboard page)
            {
                Page = page;
                typeof(GarmothDashboard).GetProperty(nameof(GarmothDashboard.Section))!.SetValue(page, section);
            }
            return component;
        }
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/garmoth");
        protected override void NavigateToCore(string uri, bool forceLoad) =>
            throw new InvalidOperationException("Rendering sections must not navigate.");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Rendering sections must not invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
