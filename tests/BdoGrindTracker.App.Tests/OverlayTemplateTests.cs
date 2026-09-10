using System.Text.Json;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayTemplateTests
{
    [Fact]
    public async Task SavedTemplateRestoresLayoutAndAppearanceWithoutChangingTheRunningOverlay()
    {
        using var folder = new TemplateFolder();
        await using var tracker = new PreviewTrackerSession();
        var source = OverlayCatalog.Preset("loot") with
        {
            Enabled = true, Interaction = "passthrough", Visibility = "always", CaptureExcluded = false,
            HotkeysEnabled = true, PositionX = .8, PositionY = .7,
            Scale = 1.25, BackgroundOpacity = .45, ShowBorder = false, SnapToGrid = false,
        };
        using (var service = new OverlayService(tracker, templateStore: new(folder.Path)))
        {
            var settings = service.Settings;
            var snapshot = service.Snapshot;
            var trackerState = tracker.State;
            Assert.True((await service.SaveTemplateAsync("  Mein Loot  ", source)).Succeeded);
            Assert.Equal("Mein Loot", Assert.Single(service.Templates).Name);
            Assert.Same(settings, service.Settings);
            Assert.Same(snapshot, service.Snapshot);
            Assert.Same(trackerState, tracker.State);
        }

        using var restored = new OverlayService(tracker, templateStore: new(folder.Path));
        var template = Assert.Single(restored.Templates);
        var current = new OverlaySettings
        {
            Enabled = false, Interaction = "locked", Visibility = "session", CaptureExcluded = true,
            HotkeysEnabled = false, PositionX = .1, PositionY = .2,
        };
        var applied = template.ApplyTo(current);
        Assert.Equal(source.Width, applied.Width);
        Assert.Equal(source.Height, applied.Height);
        Assert.Equal(source.Scale, applied.Scale);
        Assert.Equal(source.BackgroundOpacity, applied.BackgroundOpacity);
        Assert.Equal(source.ShowBorder, applied.ShowBorder);
        Assert.Equal(source.SnapToGrid, applied.SnapToGrid);
        Assert.Equal(source.Widgets.Select(widget => widget.Kind), applied.Widgets.Select(widget => widget.Kind));
        Assert.Equal(current.Enabled, applied.Enabled);
        Assert.Equal(current.Interaction, applied.Interaction);
        Assert.Equal(current.Visibility, applied.Visibility);
        Assert.Equal(current.CaptureExcluded, applied.CaptureExcluded);
        Assert.Equal(current.HotkeysEnabled, applied.HotkeysEnabled);
        Assert.Equal(current.PositionX, applied.PositionX);
        Assert.Equal(current.PositionY, applied.PositionY);
        Assert.False(template.Layout.Enabled);

        using var json = JsonDocument.Parse(File.ReadAllText(folder.FilePath));
        var layout = json.RootElement.GetProperty("Templates")[0].GetProperty("Layout");
        Assert.Equal(new[] { "BackgroundOpacity", "Height", "Scale", "ShowBorder", "SnapToGrid", "Widgets", "Width" },
            layout.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(System.IO.Path.Combine(folder.Path, "overlay.json")));
    }

    [Fact]
    public async Task SavedAndAppliedCollectionsCannotAliasCallerOwnedWidgetsOrSelectedItems()
    {
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker);
        var names = new[] { "Black Stone" };
        var sourceWidget = OverlayCatalog.CreateWidget("drop-grid") with { ItemFilter = "selected", ItemNames = Array.AsReadOnly(names) };
        var widgets = new List<OverlayWidget> { sourceWidget };
        var source = new OverlaySettings { Widgets = widgets };
        Assert.True((await service.SaveTemplateAsync("Unabhängig", source)).Succeeded);
        var template = Assert.Single(service.Templates);
        names[0] = "Caphras Stone";
        widgets.Clear();

        var savedWidget = Assert.Single(template.Layout.Widgets);
        Assert.Equal("Black Stone", Assert.Single(savedWidget.ItemNames));
        Assert.NotSame(sourceWidget, savedWidget);
        Assert.True(Assert.IsAssignableFrom<IList<OverlayWidget>>(template.Layout.Widgets).IsReadOnly);
        Assert.True(Assert.IsAssignableFrom<IList<string>>(savedWidget.ItemNames).IsReadOnly);
        var firstApply = template.ApplyTo(new());
        var secondApply = template.ApplyTo(new());
        Assert.NotSame(template.Layout.Widgets, firstApply.Widgets);
        Assert.NotSame(firstApply.Widgets, secondApply.Widgets);
        Assert.NotSame(savedWidget.ItemNames, firstApply.Widgets[0].ItemNames);
    }

    [Fact]
    public async Task DuplicateNamesNeedExplicitReplacementAndReplaceKeepsIdentity()
    {
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker);
        Assert.True((await service.SaveTemplateAsync("Mein Loot", new())).Succeeded);
        var original = Assert.Single(service.Templates);
        Assert.False((await service.SaveTemplateAsync("  MEIN LOOT ", OverlayCatalog.Preset("loot"))).Succeeded);
        Assert.Same(original, Assert.Single(service.Templates));
        Assert.False((await service.SaveTemplateAsync("Neu", new(), Guid.NewGuid().ToString("N"))).Succeeded);
        Assert.Same(original, Assert.Single(service.Templates));

        Assert.True((await service.SaveTemplateAsync("MEIN LOOT", OverlayCatalog.Preset("loot"), original.Id)).Succeeded);
        var replaced = Assert.Single(service.Templates);
        Assert.Equal(original.Id, replaced.Id);
        Assert.Equal("MEIN LOOT", replaced.Name);
        Assert.Equal(336, replaced.Layout.Width);
        Assert.Null(service.TemplateError);
        Assert.True((await service.SaveTemplateAsync("Andere Vorlage", new())).Succeeded);
        Assert.False((await service.SaveTemplateAsync("Andere Vorlage", new(), original.Id)).Succeeded);
        Assert.Equal(2, service.Templates.Count);
        Assert.Equal(replaced, service.Templates[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Zeile\nZwei")]
    public async Task InvalidNamesNeverCreateTemplates(string name)
    {
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker);
        Assert.False((await service.SaveTemplateAsync(name, new())).Succeeded);
        Assert.False((await service.SaveTemplateAsync(new string('a', 61), new())).Succeeded);
        Assert.Empty(service.Templates);
    }

    [Fact]
    public async Task TemplateLimitStillAllowsReplacementAndDeletion()
    {
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker);
        for (var index = 1; index <= OverlayTemplateStore.MaximumTemplates; index++)
            Assert.True((await service.SaveTemplateAsync("Vorlage " + index, new())).Succeeded);
        Assert.False((await service.SaveTemplateAsync("Eine zu viel", new())).Succeeded);
        var first = service.Templates[0];
        Assert.True((await service.SaveTemplateAsync(first.Name, OverlayCatalog.Preset("loot"), first.Id)).Succeeded);
        Assert.Equal(OverlayTemplateStore.MaximumTemplates, service.Templates.Count);
        Assert.True((await service.DeleteTemplateAsync(first.Id)).Succeeded);
        Assert.True((await service.SaveTemplateAsync("Passt wieder", new())).Succeeded);
        Assert.Equal(OverlayTemplateStore.MaximumTemplates, service.Templates.Count);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("replace")]
    [InlineData("delete")]
    public async Task FailedAtomicMutationKeepsTemplatesAndFileUntilSuccessfulRetry(string action)
    {
        using var folder = new TemplateFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, templateStore: new(folder.Path));
        Assert.True((await service.SaveTemplateAsync("Vorhanden", new())).Succeeded);
        var beforeTemplates = service.Templates;
        var before = File.ReadAllText(folder.FilePath);
        var id = beforeTemplates[0].Id;
        Task<OverlaySaveResult> Mutate() => action switch
        {
            "create" => service.SaveTemplateAsync("Neu", new()),
            "replace" => service.SaveTemplateAsync("Ersetzt", OverlayCatalog.Preset("loot"), id),
            _ => service.DeleteTemplateAsync(id),
        };
        using (var locked = new FileStream(folder.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False((await Mutate()).Succeeded);
            Assert.NotNull(service.TemplateError);
            Assert.Same(beforeTemplates, service.Templates);
            Assert.Equal(before, File.ReadAllText(folder.FilePath));
        }
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
        Assert.True((await Mutate()).Succeeded);
        Assert.Null(service.TemplateError);
        Assert.NotEqual(before, File.ReadAllText(folder.FilePath));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Version\":2,\"Templates\":[]}")]
    [InlineData("{\"Version\":1}")]
    [InlineData("{\"Version\":1,\"Templates\":null}")]
    [InlineData("{\"Version\":1,\"Templates\":[null]}")]
    public async Task UnreadableDocumentCannotBeSilentlyReplacedWithAnEmptyCollection(string contents)
    {
        using var folder = new TemplateFolder();
        File.WriteAllText(folder.FilePath, contents);
        await using var tracker = new PreviewTrackerSession();
        var store = new OverlayTemplateStore(folder.Path);
        using var service = new OverlayService(tracker, templateStore: store);
        Assert.NotNull(service.TemplateError);
        Assert.Empty(service.Templates);
        Assert.False((await service.SaveTemplateAsync("Neue Vorlage", new())).Succeeded);
        Assert.False((await service.DeleteTemplateAsync(Guid.NewGuid().ToString("N"))).Succeeded);
        Assert.Throws<InvalidDataException>(() => store.Save([]));
        Assert.Equal(contents, File.ReadAllText(folder.FilePath));
    }

    [Fact]
    public void MalformedEntryAndDuplicateIdentitiesProtectTheExistingDocument()
    {
        using var folder = new TemplateFolder();
        var store = new OverlayTemplateStore(folder.Path);
        store.Save([OverlayTemplate.Create("Vorlage", new())]);
        using var json = JsonDocument.Parse(File.ReadAllText(folder.FilePath));
        var entry = json.RootElement.GetProperty("Templates")[0].GetRawText();
        var duplicate = "{\"Version\":1,\"Templates\":[" + entry + "," + entry + "]}";
        File.WriteAllText(folder.FilePath, duplicate);
        Assert.Empty(store.Load());
        Assert.NotNull(store.LoadError);
        Assert.Throws<InvalidDataException>(() => store.Save([]));
        Assert.Equal(duplicate, File.ReadAllText(folder.FilePath));

        var missingLayout = "{\"Version\":1,\"Templates\":[{\"Id\":\"" + Guid.NewGuid().ToString("N") + "\",\"Name\":\"Vorlage\"}]}";
        File.WriteAllText(folder.FilePath, missingLayout);
        Assert.Empty(store.Load());
        Assert.NotNull(store.LoadError);
        Assert.Throws<InvalidDataException>(() => store.Save([]));
        Assert.Equal(missingLayout, File.ReadAllText(folder.FilePath));
    }

    [Fact]
    public async Task DeletedTemplateStaysDeletedAfterRestartAndMissingIdsCannotDeleteOthers()
    {
        using var folder = new TemplateFolder();
        await using var tracker = new PreviewTrackerSession();
        using (var service = new OverlayService(tracker, templateStore: new(folder.Path)))
        {
            Assert.True((await service.SaveTemplateAsync("Bleibt", new())).Succeeded);
            Assert.True((await service.SaveTemplateAsync("Entfernen", new())).Succeeded);
            var templates = service.Templates;
            Assert.False((await service.DeleteTemplateAsync(Guid.NewGuid().ToString("N"))).Succeeded);
            Assert.Same(templates, service.Templates);
            Assert.True((await service.DeleteTemplateAsync(templates[1].Id)).Succeeded);
        }
        using var restored = new OverlayService(tracker, templateStore: new(folder.Path));
        Assert.Equal("Bleibt", Assert.Single(restored.Templates).Name);
    }

    private sealed class TemplateFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Grindcrest.Template.Tests", Guid.NewGuid().ToString("N"));
        public string FilePath => System.IO.Path.Combine(Path, OverlayTemplateStore.FileName);
        public TemplateFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
