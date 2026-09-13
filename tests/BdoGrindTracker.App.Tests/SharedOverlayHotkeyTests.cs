using System.Text.Json;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class SharedOverlayHotkeyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MigrationChoosesFirstEnabledPairOrFirstWindowWhenAllDisabled(bool anyEnabled)
    {
        using var folder = new TestFolder();
        var first = Custom("F8", "F9") with { Enabled = false };
        var second = Custom("F6", "F7") with { Enabled = anyEnabled };
        var overlays = new[]
        {
            new OverlayInstance { Id = "first", Settings = first.ApplyTo(new() { Width = 450 }) },
            new OverlayInstance { Id = "second", Settings = second.ApplyTo(new() { Width = 600 }) },
            new OverlayInstance { Id = "selected", Settings = (Custom("F4", "F5") with { Enabled = anyEnabled }).ApplyTo(new()) },
        };
        File.WriteAllText(folder.SettingsPath, JsonSerializer.Serialize(new { Version = 1, SelectedOverlayId = "selected", Overlays = overlays }));
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));

        Assert.Equal(anyEnabled ? second : first, service.Hotkeys);
        Assert.All(service.Overlays, overlay => Assert.Equal(service.Hotkeys, OverlayHotkeySettings.FromLegacy(overlay.Settings)));
        Assert.Equal(450, service.Overlays[0].Settings.Width);
        Assert.Equal(600, service.Overlays[1].Settings.Width);
        using var persisted = JsonDocument.Parse(File.ReadAllText(folder.SettingsPath));
        Assert.Equal(2, persisted.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal(anyEnabled, persisted.RootElement.GetProperty("Hotkeys").GetProperty("Enabled").GetBoolean());
        Assert.All(persisted.RootElement.GetProperty("Overlays").EnumerateArray(), overlay =>
        {
            var settings = overlay.GetProperty("Settings");
            Assert.False(settings.TryGetProperty("HotkeysEnabled", out _));
            Assert.False(settings.TryGetProperty("ToggleOverlayHotkey", out _));
            Assert.False(settings.TryGetProperty("ToggleInteractionHotkey", out _));
        });
        using var restored = new OverlayService(tracker, new(folder.Path));
        Assert.Equal(service.Hotkeys, restored.Hotkeys);
        Assert.Equal("selected", restored.SelectedOverlayId);
    }

    [Fact]
    public async Task SharedBindingsSurviveSelectionDeletionNewWindowsAndStaleLayoutSaves()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));
        var firstId = service.SelectedOverlayId;
        var staleLayout = service.Settings;
        var custom = Custom("F8", "F9") with { Enabled = false };
        await service.SaveHotkeysAsync(custom);
        await service.CreateOverlayAsync("Zweites");
        Assert.Equal(custom, OverlayHotkeySettings.FromLegacy(service.Settings));
        await service.SaveAsync(firstId, staleLayout with { Width = 510 });
        Assert.Equal(custom, service.Hotkeys);
        Assert.Equal(510, service.Overlays.First().Settings.Width);
        service.UpdateHotkeyRuntime("Strg+F8 bereits belegt");
        await service.SelectOverlayAsync(firstId);
        await service.DeleteOverlayAsync(firstId);
        Assert.Equal(custom, service.Hotkeys);
        Assert.Equal("Strg+F8 bereits belegt", service.HotkeyStatus);
        Assert.Equal(service.HotkeyStatus, service.State.HotkeyStatus);
        using var restored = new OverlayService(tracker, new(folder.Path));
        Assert.Equal(custom, restored.Hotkeys);
        Assert.Null(restored.HotkeyStatus);
    }

    [Fact]
    public async Task GlobalVisibilityCommitsMixedWindowsAndClearsEveryPreviewInOneChange()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));
        var firstId = service.SelectedOverlayId;
        await service.SaveAsync(service.Settings with { Enabled = true, Width = 540 });
        await service.SetPreviewAsync(true);
        await service.CreateOverlayAsync("Zweites");
        var selected = service.SelectedOverlayId;
        await service.SetPreviewAsync(true);
        var changes = 0;
        service.Changed += () => changes++;

        Assert.True((await service.ToggleAllOverlaysAsync()).Succeeded);

        Assert.Equal(1, changes);
        Assert.Equal(selected, service.SelectedOverlayId);
        Assert.All(service.Overlays, overlay =>
        {
            Assert.False(overlay.Settings.Enabled);
            Assert.False(service.GetState(overlay.Id).Previewing);
        });
        Assert.Equal(540, service.Overlays.Single(overlay => overlay.Id == firstId).Settings.Width);
        using var restored = new OverlayService(tracker, new(folder.Path));
        Assert.All(restored.Overlays, overlay => Assert.False(overlay.Settings.Enabled));
        Assert.True((await service.ToggleAllOverlaysAsync()).Succeeded);
        Assert.Equal(2, changes);
        Assert.All(service.Overlays, overlay => Assert.True(overlay.Settings.Enabled));
    }

    [Fact]
    public async Task GlobalVisibilityHidesPreviewOnlyWindowsBeforeEnablingThemOnNextPress()
    {
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker);
        await service.SetPreviewAsync(true);
        await service.CreateOverlayAsync("Zweites");
        await service.SetPreviewAsync(true);
        await service.ToggleAllOverlaysAsync();
        Assert.All(service.Overlays, overlay =>
        {
            Assert.False(overlay.Settings.Enabled);
            Assert.False(service.GetState(overlay.Id).Previewing);
        });
        await service.ToggleAllOverlaysAsync();
        Assert.All(service.Overlays, overlay => Assert.True(overlay.Settings.Enabled));
    }

    [Fact]
    public async Task GlobalInteractionSynchronizesMixedWindowsWithoutChangingIndividualPreferences()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));
        await service.SaveAsync(service.Settings with { Enabled = true, Interaction = "passthrough", Visibility = "always", PositionX = .8 });
        await service.CreateOverlayAsync("Zweites");
        await service.SaveAsync(service.Settings with { Interaction = "locked", Width = 620, PositionY = .5 });
        var before = service.Overlays.Select(overlay => overlay.Settings).ToArray();
        var changes = 0;
        service.Changed += () => changes++;

        await service.ToggleAllInteractionAsync();

        Assert.Equal(1, changes);
        for (var index = 0; index < before.Length; index++)
            Assert.Equal(before[index] with { Interaction = "passthrough" }, service.Overlays[index].Settings);
        using var restored = new OverlayService(tracker, new(folder.Path));
        Assert.All(restored.Overlays, overlay => Assert.Equal("passthrough", overlay.Settings.Interaction));
        await service.ToggleAllInteractionAsync();
        Assert.Equal(2, changes);
        Assert.All(service.Overlays, overlay => Assert.Equal("move", overlay.Settings.Interaction));
    }

    [Fact]
    public async Task FailedSharedWriteKeepsEveryWindowPreviewAndBindingUnchanged()
    {
        using var folder = new TestFolder();
        await using var tracker = new PreviewTrackerSession();
        using var service = new OverlayService(tracker, new(folder.Path));
        await service.SaveAsync(service.Settings with { Enabled = true });
        await service.SetPreviewAsync(true);
        await service.CreateOverlayAsync("Zweites");
        var before = service.Overlays;
        var hotkeys = service.Hotkeys;
        var json = File.ReadAllText(folder.SettingsPath);
        using (var held = new FileStream(folder.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False((await service.ToggleAllOverlaysAsync()).Succeeded);
            Assert.False((await service.ToggleAllInteractionAsync()).Succeeded);
            Assert.False((await service.SaveHotkeysAsync(Custom("F8", "F9"))).Succeeded);
            Assert.Same(before, service.Overlays);
            Assert.Equal(hotkeys, service.Hotkeys);
            Assert.True(service.GetState(before[0].Id).Previewing);
            Assert.NotNull(service.HotkeyStatus);
            Assert.All(service.Overlays, overlay => Assert.Equal(service.HotkeyStatus, service.GetState(overlay.Id).HotkeyStatus));
            Assert.Equal(json, File.ReadAllText(folder.SettingsPath));
        }
        Assert.True((await service.ToggleAllOverlaysAsync()).Succeeded);
        Assert.Null(service.HotkeyStatus);
        Assert.False(File.Exists(folder.SettingsPath + ".tmp"));
    }

    private static OverlayHotkeySettings Custom(string toggle, string interaction) => new()
    {
        ToggleOverlay = new() { Key = toggle, Modifiers = OverlayHotkeyModifiers.Control },
        ToggleInteraction = new() { Key = interaction, Modifiers = OverlayHotkeyModifiers.Alt },
    };

    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Grindcrest.SharedHotkey.Tests", Guid.NewGuid().ToString("N"));
        public string SettingsPath => System.IO.Path.Combine(Path, "overlay.json");
        public TestFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
