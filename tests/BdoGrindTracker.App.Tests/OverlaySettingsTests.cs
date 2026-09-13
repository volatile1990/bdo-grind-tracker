using System.Text.Json;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlaySettingsTests
{
    [Fact]
    public async Task TemporaryReadFailureBlocksOverwriteUntilTheOriginalCollectionCanBeReloaded()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession(empty: true);
        using (var original = new OverlayService(tracker, new OverlaySettingsStore(folder.Path)))
        {
            await original.RenameOverlayAsync(original.SelectedOverlayId, "Mein Layout");
            await original.CreateOverlayAsync("Zweites Layout");
        }
        var path = System.IO.Path.Combine(folder.Path, "overlay.json");
        var before = File.ReadAllBytes(path);
        OverlayService recovered;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            recovered = new OverlayService(tracker, new OverlaySettingsStore(folder.Path));
            Assert.NotNull(recovered.LoadError);
            Assert.True(recovered.State.IsError);
            Assert.False(recovered.Hotkeys.Enabled);
            Assert.False((await recovered.ReloadAsync()).Succeeded);
        }
        using (recovered)
        {
            Assert.False((await recovered.ToggleAllOverlaysAsync()).Succeeded);
            Assert.False((await recovered.CreateOverlayAsync("Ungewollter Ersatz")).Succeeded);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.True((await recovered.ReloadAsync()).Succeeded);
            Assert.Null(recovered.LoadError);
            Assert.False(recovered.State.IsError);
            Assert.Equal(new[] { "Mein Layout", "Zweites Layout" }, recovered.Overlays.Select(overlay => overlay.Name));
            Assert.True((await recovered.ToggleAllOverlaysAsync()).Succeeded);
        }
        using var restored = new OverlayService(tracker, new OverlaySettingsStore(folder.Path));
        Assert.Equal(2, restored.Overlays.Count);
        Assert.All(restored.Overlays, overlay => Assert.True(overlay.Settings.Enabled));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Version\":99,\"Overlays\":[]}")]
    [InlineData("{\"Version\":2,\"Overlays\":null}")]
    public async Task InvalidExistingOverlayFilesRemainUntouchedAfterChangesOrReloads(string json)
    {
        using var folder = new TestFolder();
        var path = System.IO.Path.Combine(folder.Path, "overlay.json");
        File.WriteAllText(path, json);
        await using var tracker = new PreviewTrackerSession(empty: true);
        using var service = new OverlayService(tracker, new OverlaySettingsStore(folder.Path));
        Assert.NotNull(service.LoadError);
        Assert.False((await service.ToggleAllOverlaysAsync()).Succeeded);
        Assert.False((await service.ReloadAsync()).Succeeded);
        Assert.Equal(json, File.ReadAllText(path));
        var compatibilityStore = new OverlaySettingsStore(folder.Path);
        Assert.Throws<IOException>(() => compatibilityStore.Save(new()));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void CorruptOrOldConfigurationKeepsTheOverlayOptInAndBoundsEveryModule()
    {
        var id = Guid.NewGuid().ToString("N");
        var settings = OverlayLayout.Normalize(new()
        {
            Width = double.NaN, Height = -4, PositionX = double.PositiveInfinity,
            PositionY = -100, Scale = 90, BackgroundOpacity = 10,
            Interaction = "invalid", Visibility = "unknown",
            Widgets =
            [
                new() { Id = id, X = 900, Y = -100, Width = 800, Height = 400, FontScale = 9 },
                new() { Id = id, Kind = "trash" },
                new() { Kind = "unrecognized" },
            ],
        });
        Assert.False(settings.Enabled);
        Assert.Equal(360, settings.Width);
        Assert.Equal(64, settings.Height);
        Assert.InRange(settings.PositionX, 0, 1);
        Assert.Equal(0, settings.PositionY);
        Assert.Equal(2, settings.Scale);
        Assert.Equal(1, settings.BackgroundOpacity);
        Assert.Equal("move", settings.Interaction);
        Assert.Equal("game", settings.Visibility);
        var widget = Assert.Single(settings.Widgets);
        Assert.True(widget.X >= 0 && widget.Y >= 0);
        Assert.True(widget.X + widget.Width <= 1600);
        Assert.True(widget.Y + widget.Height <= 1200);
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("dashboard")]
    [InlineData("loot")]
    [InlineData("loot-strip")]
    public void PresetsAreImmediatelyUsableAndFitTheirCanvas(string name)
    {
        var settings = OverlayCatalog.Preset(name);
        var normalized = OverlayLayout.Normalize(settings);
        Assert.Equal(settings, normalized with { Widgets = settings.Widgets });
        Assert.Equal(settings.Widgets, normalized.Widgets);
        Assert.Contains(settings.Widgets, widget => widget.Kind == "controls");
        Assert.Equal(settings.Widgets.Count, settings.Widgets.Select(widget => widget.Id).Distinct().Count());
        Assert.All(settings.Widgets, widget =>
        {
            Assert.InRange(widget.X, 0, settings.Width - widget.Width);
            Assert.InRange(widget.Y, 0, settings.Height - widget.Height);
        });
    }

    [Fact]
    public void LootInventoryPresetStacksSessionMetricsAboveAFullWidthInventory()
    {
        var settings = OverlayCatalog.Preset("loot");
        Assert.Equal(336, settings.Width);
        Assert.Equal(640, settings.Height);
        Assert.Equal(new[] { "spot", "duration", "silver", "chart", "controls", "trash-hour", "drop-grid" },
            settings.Widgets.Select(widget => widget.Kind));

        var widgets = settings.Widgets.ToDictionary(widget => widget.Kind);
        Assert.All(new[] { "spot", "chart", "drop-grid" }, kind =>
        {
            Assert.Equal(8, widgets[kind].X);
            Assert.Equal(settings.Width - 16, widgets[kind].Width);
        });
        Assert.All(new[] { ("duration", "silver"), ("controls", "trash-hour") }, pair =>
        {
            var left = widgets[pair.Item1];
            var right = widgets[pair.Item2];
            Assert.Equal(left.Y, right.Y);
            Assert.Equal(left.Height, right.Height);
            Assert.Equal(left.X + left.Width + 8, right.X);
            Assert.Equal(settings.Width - 8, right.X + right.Width);
        });
        Assert.All(new[] { ("spot", "duration"), ("duration", "chart"), ("chart", "controls"), ("controls", "drop-grid") }, pair =>
        {
            var above = widgets[pair.Item1];
            Assert.Equal(above.Y + above.Height + 8, widgets[pair.Item2].Y);
        });

        var inventory = widgets["drop-grid"];
        Assert.Equal(settings.Height - 8, inventory.Y + inventory.Height);
        Assert.False(inventory.ShowLabel);
        Assert.Equal(24, inventory.ItemLimit);
        Assert.Equal(5, OverlayLootPresentation.Create(inventory, OverlaySnapshot.Demo).Columns);
        Assert.False(widgets["duration"].ShowLabel);
        Assert.False(widgets["chart"].ShowLabel);
        Assert.False(widgets["controls"].ShowLabel);
    }

    [Fact]
    public async Task OverlayChangesPersistIndependentlyOfCapturePreferencesAndNewServiceRestoresThem()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        var store = new OverlaySettingsStore(folder.Path);
        using (var service = new OverlayService(tracker, store))
        {
            var preferences = tracker.Preferences;
            var layout = OverlayCatalog.Preset("dashboard") with { Enabled = true, Interaction = "passthrough", Scale = 1.5 };
            Assert.True((await service.SaveAsync(layout)).Succeeded);
            Assert.True((await service.SavePositionAsync(.6, .4)).Succeeded);
            await service.SetPreviewAsync(true);
            Assert.Same(preferences, tracker.Preferences);
        }
        using var restored = new OverlayService(tracker, store);
        Assert.True(restored.Settings.Enabled);
        Assert.Equal("passthrough", restored.Settings.Interaction);
        Assert.Equal(1.5, restored.Settings.Scale);
        Assert.Equal(.6, restored.Settings.PositionX);
        Assert.Equal(.4, restored.Settings.PositionY);
        Assert.Equal(OverlayCatalog.Preset("dashboard").Widgets.Count, restored.Settings.Widgets.Count);
        Assert.False(restored.State.Previewing);
        Assert.False(File.Exists(System.IO.Path.Combine(folder.Path, "settings.json")));
    }

    [Fact]
    public async Task FailedAtomicSaveKeepsPreviousLayoutAndCanBeRetried()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new OverlaySettingsStore(folder.Path));
        Assert.True((await service.SaveAsync(service.Settings)).Succeeded);
        var path = System.IO.Path.Combine(folder.Path, "overlay.json");
        var before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await service.SaveAsync(service.Settings with { Enabled = true });
            Assert.False(result.Succeeded);
            Assert.False(service.Settings.Enabled);
            Assert.True(service.State.IsError);
            service.UpdateRuntime(new() { Status = "Runtime update" });
            Assert.Contains("nicht gespeichert", service.State.Status);
            Assert.Equal(before, File.ReadAllText(path));
        }
        Assert.True((await service.SaveAsync(service.Settings with { Enabled = true })).Succeeded);
        Assert.True(service.Settings.Enabled);
        Assert.False(service.State.IsError);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task DesktopPreviewIsTemporaryAndRuntimeDoesNotOverwriteIt()
    {
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker);
        await service.SetPreviewAsync(true);
        service.UpdateRuntime(new() { Previewing = false, IsVisible = true });
        Assert.True(service.State.Previewing);
        Assert.False(service.Settings.Enabled);
        await service.SetPreviewAsync(false);
        Assert.False(service.State.Previewing);
    }

    [Fact]
    public void BadJsonDoesNotEnableAnOverlayOrPreventStartup()
    {
        using var folder = new TestFolder();
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "overlay.json"), "{broken");
        var settings = new OverlaySettingsStore(folder.Path).Load();
        Assert.False(settings.Enabled);
        Assert.NotEmpty(settings.Widgets);
        Assert.True(settings.CaptureExcluded);
    }

    [Fact]
    public void LoadedCollectionsAreOwnedAndUnknownModulesAreDiscarded()
    {
        using var folder = new TestFolder();
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "overlay.json"), JsonSerializer.Serialize(new OverlaySettings
        {
            Enabled = true, Widgets = [new() { Kind = "future-widget" }, new() { Kind = "trash" }],
        }));
        var settings = new OverlaySettingsStore(folder.Path).Load();
        Assert.Equal("trash", Assert.Single(settings.Widgets).Kind);
        Assert.True(Assert.IsAssignableFrom<IList<OverlayWidget>>(settings.Widgets).IsReadOnly);
    }

    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Grindcrest.Overlay.Tests", Guid.NewGuid().ToString("N"));
        public TestFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
