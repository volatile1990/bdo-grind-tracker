using System.Reflection;
using System.Text.Json;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class MultiOverlayServiceTests
{
    [Fact]
    public async Task LegacyFileBecomesOneStableOverlayAndPreservesLayoutAndExplicitHotkeys()
    {
        using var folder = new TestFolder();
        var legacy = OverlayCatalog.Preset("dashboard") with
        {
            Enabled = true, HotkeysEnabled = false, PositionX = .71, PositionY = .42, Scale = 1.5,
        };
        File.WriteAllText(folder.SettingsPath, JsonSerializer.Serialize(legacy));
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));

        var overlay = Assert.Single(service.Overlays);
        Assert.Equal(OverlayInstance.DefaultId, overlay.Id);
        Assert.Equal(overlay.Id, service.SelectedOverlayId);
        Assert.Equal(legacy.Width, overlay.Settings.Width);
        Assert.Equal(.71, overlay.Settings.PositionX);
        Assert.Equal(.42, overlay.Settings.PositionY);
        Assert.Equal(1.5, overlay.Settings.Scale);
        Assert.True(overlay.Settings.Enabled);
        Assert.False(overlay.Settings.HotkeysEnabled);
        Assert.Equal(legacy.Widgets, overlay.Settings.Widgets);
        using var json = JsonDocument.Parse(File.ReadAllText(folder.SettingsPath));
        Assert.Single(json.RootElement.GetProperty("Overlays").EnumerateArray());
        using var restored = new OverlayService(tracker, new(folder.Path));
        Assert.Equal(overlay.Id, restored.SelectedOverlayId);
        Assert.Equal(legacy.Widgets, restored.Settings.Widgets);
    }

    [Fact]
    public async Task CreateCloneAndScopedUpdatesStayIndependentAndRoundTripAllWindows()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));
        var firstId = service.SelectedOverlayId;
        Assert.True((await service.SaveAsync(service.Settings with { Enabled = true, Width = 480 })).Succeeded);
        Assert.True((await service.CreateOverlayAsync("Uhr")).Succeeded);
        var secondId = service.SelectedOverlayId;
        Assert.NotEqual(firstId, secondId);
        Assert.Empty(service.Settings.Widgets);
        Assert.False(service.Settings.Enabled);
        Assert.True(service.Settings.HotkeysEnabled);
        Assert.True((await service.SaveAsync(secondId, service.Settings with
        {
            Width = 600, Widgets = [OverlayCatalog.CreateWidget("duration")],
        })).Succeeded);
        Assert.True((await service.CreateOverlayAsync("Kopie", firstId)).Succeeded);
        var cloneId = service.SelectedOverlayId;
        Assert.Equal(480, service.Settings.Width);
        Assert.False(service.Settings.Enabled);
        Assert.True(service.Settings.HotkeysEnabled);
        var first = service.Overlays.Single(overlay => overlay.Id == firstId).Settings;
        Assert.Equal(first.Widgets.Select(widget => widget.Kind), service.Settings.Widgets.Select(widget => widget.Kind));
        Assert.Empty(first.Widgets.Select(widget => widget.Id).Intersect(service.Settings.Widgets.Select(widget => widget.Id)));
        Assert.NotSame(first.Widgets, service.Settings.Widgets);
        Assert.True(Assert.IsAssignableFrom<IList<OverlayWidget>>(service.Settings.Widgets).IsReadOnly);
        Assert.True((await service.SavePositionAsync(firstId, .8, .6, 700, 400)).Succeeded);
        Assert.Equal(480, service.Settings.Width);
        Assert.Equal(600, service.Overlays.Single(overlay => overlay.Id == secondId).Settings.Width);
        Assert.True((await service.RenameOverlayAsync(firstId, "  Grind  ")).Succeeded);
        Assert.True((await service.SelectOverlayAsync(secondId)).Succeeded);

        using var restored = new OverlayService(tracker, new(folder.Path));
        Assert.Equal(secondId, restored.SelectedOverlayId);
        Assert.Equal(new[] { firstId, secondId, cloneId }, restored.Overlays.Select(overlay => overlay.Id));
        var savedFirst = restored.Overlays.Single(overlay => overlay.Id == firstId);
        Assert.Equal("Grind", savedFirst.Name);
        Assert.Equal(.8, savedFirst.Settings.PositionX);
        Assert.Equal(.6, savedFirst.Settings.PositionY);
        Assert.Equal(700, savedFirst.Settings.Width);
        Assert.True(savedFirst.Settings.Enabled);
        Assert.True(savedFirst.Settings.HotkeysEnabled);
        Assert.Equal(600, restored.Settings.Width);
        Assert.Equal(480, restored.Overlays.Single(overlay => overlay.Id == cloneId).Settings.Width);
    }

    [Fact]
    public async Task RuntimePreviewVisibilityAndErrorsBelongToTheirWindowAndAreNeverPersisted()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));
        var firstId = service.SelectedOverlayId;
        await service.SetPreviewAsync(firstId, true);
        await service.CreateOverlayAsync("Zweites");
        var secondId = service.SelectedOverlayId;
        service.UpdateRuntime(firstId, new() { IsVisible = true, Status = "Erstes", HotkeyStatus = "Belegt" });
        service.UpdateRuntime(secondId, new() { IsError = true, Status = "Zweites" });
        Assert.False(service.State.Previewing);
        Assert.False(service.State.IsVisible);
        Assert.True(service.State.IsError);
        Assert.True(service.GetState(firstId).Previewing);
        Assert.True(service.GetState(firstId).IsVisible);
        Assert.False(service.GetState(firstId).IsError);
        Assert.Equal("Belegt", service.GetState(firstId).HotkeyStatus);
        await service.SetPreviewAsync(secondId, true);
        await service.SetPreviewAsync(firstId, false);
        Assert.True(service.State.Previewing);
        Assert.False(service.GetState(firstId).Previewing);
        await service.SelectOverlayAsync(firstId);
        Assert.Equal("Erstes", service.State.Status);
        using var restored = new OverlayService(tracker, new(folder.Path));
        Assert.All(restored.Overlays, overlay =>
        {
            Assert.False(restored.GetState(overlay.Id).Previewing);
            Assert.False(restored.GetState(overlay.Id).IsVisible);
            Assert.False(restored.GetState(overlay.Id).IsError);
            Assert.Null(restored.GetState(overlay.Id).HotkeyStatus);
        });
    }

    [Fact]
    public async Task DeletionKeepsSelectionValidAndRequiresOneRemainingWindow()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));
        var firstId = service.SelectedOverlayId;
        Assert.False((await service.DeleteOverlayAsync(firstId)).Succeeded);
        await service.CreateOverlayAsync("Zweites");
        var secondId = service.SelectedOverlayId;
        await service.SetPreviewAsync(secondId, true);
        Assert.True((await service.DeleteOverlayAsync(secondId)).Succeeded);
        Assert.Equal(firstId, service.SelectedOverlayId);
        Assert.False(service.GetState(secondId).Previewing);
        service.UpdateRuntime(secondId, new() { IsVisible = true });
        Assert.False(service.GetState(secondId).IsVisible);
        Assert.False((await service.SaveAsync(secondId, new())).Succeeded);
        Assert.False((await service.SelectOverlayAsync(secondId)).Succeeded);
        using var restored = new OverlayService(tracker, new(folder.Path));
        Assert.Equal(firstId, Assert.Single(restored.Overlays).Id);
    }

    [Fact]
    public async Task FailedCollectionChangesLeaveAllWindowsAndSelectionUntouched()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));
        var firstId = service.SelectedOverlayId;
        await service.CreateOverlayAsync("Zweites");
        var secondId = service.SelectedOverlayId;
        var before = File.ReadAllText(folder.SettingsPath);
        using (var held = new FileStream(folder.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False((await service.CreateOverlayAsync("Drittes")).Succeeded);
            Assert.False((await service.SelectOverlayAsync(firstId)).Succeeded);
            Assert.False((await service.DeleteOverlayAsync(firstId)).Succeeded);
            Assert.False((await service.SavePositionAsync(firstId, .8, .9)).Succeeded);
            Assert.Equal(secondId, service.SelectedOverlayId);
            Assert.Equal(2, service.Overlays.Count);
            Assert.True(service.GetState(firstId).IsError);
            Assert.Equal(before, File.ReadAllText(folder.SettingsPath));
        }
        Assert.True((await service.SavePositionAsync(firstId, .8, .9)).Succeeded);
        Assert.False(service.GetState(firstId).IsError);
        Assert.False(File.Exists(folder.SettingsPath + ".tmp"));
    }

    [Fact]
    public async Task InvalidAndDuplicateNamesCannotChangeTheCollection()
    {
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker);
        Assert.False((await service.CreateOverlayAsync("  ")).Succeeded);
        Assert.False((await service.CreateOverlayAsync(new string('a', 61))).Succeeded);
        Assert.False((await service.CreateOverlayAsync("Overlay\n2")).Succeeded);
        Assert.False((await service.CreateOverlayAsync("overlay 1")).Succeeded);
        Assert.False((await service.CreateOverlayAsync("Kopie", "missing")).Succeeded);
        Assert.Single(service.Overlays);
        await service.CreateOverlayAsync("Zweites");
        Assert.False((await service.RenameOverlayAsync(service.SelectedOverlayId, "Overlay 1")).Succeeded);
        Assert.Equal("Zweites", service.Overlays.Last().Name);
    }

    [Fact]
    public void CompatibilitySaveUpdatesOnlyTheSelectedWindowInAnExistingCollection()
    {
        using var folder = new TestFolder();
        var store = new OverlaySettingsStore(folder.Path);
        store.SaveCollection(new()
        {
            SelectedOverlayId = "second",
            Overlays = [new() { Id = "first", Settings = new() { Width = 410 } },
                new() { Id = "second", Settings = new() { Width = 600 } }],
        });
        Assert.Equal(600, store.Load().Width);
        store.Save(store.Load() with { Width = 720 });
        var restored = store.LoadCollection();
        Assert.Equal(2, restored.Overlays.Count);
        Assert.Equal(410, restored.Overlays[0].Settings.Width);
        Assert.Equal(720, restored.Overlays[1].Settings.Width);
        Assert.Equal("second", restored.SelectedOverlayId);
    }

    [Fact]
    public async Task ClockRefreshUpdatesIdleSnapshotsOnlyWhenAWindowContainsAClock()
    {
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker);
        var property = typeof(OverlayService).GetProperty(nameof(OverlayService.Snapshot))!;
        var old = service.Snapshot with { ClockUtcNow = DateTimeOffset.UtcNow.AddMinutes(-5) };
        property.SetValue(service, old);
        service.RefreshClock();
        Assert.Same(old, service.Snapshot);
        await service.SaveAsync(service.Settings with { Widgets = [OverlayCatalog.CreateWidget("clock")] });
        await service.CreateOverlayAsync("Ausgewählt ohne Uhr");
        var changed = 0;
        service.Changed += () => changed++;
        service.RefreshClock();
        Assert.True(service.Snapshot.ClockUtcNow > old.ClockUtcNow);
        Assert.Equal(1, changed);
        Assert.Same(old.Metrics, service.Snapshot.Metrics);
    }

    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Grindcrest.MultiOverlay.Tests", Guid.NewGuid().ToString("N"));
        public string SettingsPath => System.IO.Path.Combine(Path, "overlay.json");
        public TestFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
