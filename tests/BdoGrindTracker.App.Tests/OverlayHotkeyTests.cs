using System.Text.Json;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayHotkeyTests
{
    [Fact]
    public void FreshSettingsEnableBothShortcutsWithoutOpeningTheOverlay()
    {
        var settings = new OverlaySettings();
        Assert.True(settings.HotkeysEnabled);
        Assert.False(settings.Enabled);
        Assert.Equal("Strg+Alt+O", settings.ToggleOverlayHotkey.DisplayText);
        Assert.Equal("Strg+Alt+L", settings.ToggleInteractionHotkey.DisplayText);
        Assert.True(settings.ToggleOverlayHotkey.IsValid);
        Assert.True(settings.ToggleInteractionHotkey.IsValid);
        Assert.Equal(OverlaySettings.CurrentHotkeySettingsVersion, settings.HotkeySettingsVersion);
    }

    [Theory]
    [InlineData(" q ", OverlayHotkeyModifiers.Control | OverlayHotkeyModifiers.Shift, "Q", "Strg+Umschalt+Q", 0x51u)]
    [InlineData("f11", OverlayHotkeyModifiers.None, "F11", "F11", 0x7au)]
    [InlineData("7", OverlayHotkeyModifiers.Alt, "7", "Alt+7", 0x37u)]
    [InlineData("pageup", OverlayHotkeyModifiers.Control, "PageUp", "Strg+Bild ↑", 0x21u)]
    [InlineData("numpad0", OverlayHotkeyModifiers.Control, "Numpad0", "Strg+Num 0", 0x60u)]
    [InlineData("Space", OverlayHotkeyModifiers.Windows, "Space", "Win+Leertaste", 0x20u)]
    public void KeysNormalizeToTheirCanonicalNameAndWindowsCode(string key, OverlayHotkeyModifiers modifiers,
        string canonical, string display, uint virtualKey)
    {
        var shortcut = OverlayHotkey.Normalize(new() { Key = key, Modifiers = modifiers }, OverlayHotkey.DefaultToggleOverlay);
        Assert.True(shortcut.IsValid);
        Assert.Equal(canonical, shortcut.Key);
        Assert.Equal(modifiers, shortcut.Modifiers);
        Assert.Equal(display, shortcut.DisplayText);
        Assert.Equal(virtualKey, shortcut.VirtualKey);
    }

    [Theory]
    [InlineData("A", OverlayHotkeyModifiers.None)]
    [InlineData("1", OverlayHotkeyModifiers.None)]
    [InlineData("Space", OverlayHotkeyModifiers.None)]
    [InlineData("Shift", OverlayHotkeyModifiers.Control)]
    [InlineData("F12", OverlayHotkeyModifiers.Control)]
    [InlineData("F13", OverlayHotkeyModifiers.None)]
    [InlineData("O", (OverlayHotkeyModifiers)0x4003)]
    [InlineData("", OverlayHotkeyModifiers.Control)]
    [InlineData(null, OverlayHotkeyModifiers.Control)]
    public void InvalidOrReservedBindingsFallBackToTheActionDefault(string? key, OverlayHotkeyModifiers modifiers)
    {
        var shortcut = new OverlayHotkey { Key = key!, Modifiers = modifiers };
        Assert.False(shortcut.IsValid);
        Assert.Equal(OverlayHotkey.DefaultToggleInteraction,
            OverlayHotkey.Normalize(shortcut, OverlayHotkey.DefaultToggleInteraction));
    }

    [Fact]
    public void SupportedChoicesAreUniqueImmutableAndHaveDisplayLabels()
    {
        Assert.DoesNotContain("F12", OverlayHotkey.SupportedKeys);
        Assert.Equal(OverlayHotkey.SupportedKeys.Count,
            OverlayHotkey.SupportedKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(Assert.IsAssignableFrom<IList<string>>(OverlayHotkey.SupportedKeys).IsReadOnly);
        Assert.All(OverlayHotkey.SupportedKeys, key =>
        {
            Assert.True(new OverlayHotkey { Key = key }.IsValid);
            Assert.NotEqual("–", OverlayHotkey.KeyDisplayText(key));
        });
    }

    [Fact]
    public void CorruptDuplicateBindingsRecoverToDistinctDefaultsWithoutEnablingAnExplicitDisable()
    {
        var same = new OverlayHotkey { Key = "F8", Modifiers = OverlayHotkeyModifiers.None };
        var normalized = OverlayLayout.Normalize(new()
        {
            HotkeysEnabled = false, ToggleOverlayHotkey = same, ToggleInteractionHotkey = same,
        });
        Assert.False(normalized.HotkeysEnabled);
        Assert.Equal(OverlayHotkey.DefaultToggleOverlay, normalized.ToggleOverlayHotkey);
        Assert.Equal(OverlayHotkey.DefaultToggleInteraction, normalized.ToggleInteractionHotkey);
    }

    [Fact]
    public void SerializedBindingsContainOnlyEditableData()
    {
        var settings = new OverlaySettings
        {
            ToggleOverlayHotkey = new() { Key = "F8", Modifiers = OverlayHotkeyModifiers.None },
            ToggleInteractionHotkey = new() { Key = "Home", Modifiers = OverlayHotkeyModifiers.Control },
        };
        var json = JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("DisplayText", json);
        Assert.DoesNotContain("IsValid", json);
        Assert.DoesNotContain("VirtualKey", json);
        var loaded = JsonSerializer.Deserialize<OverlaySettings>(json)!;
        Assert.Equal(settings.ToggleOverlayHotkey, loaded.ToggleOverlayHotkey);
        Assert.Equal(settings.ToggleInteractionHotkey, loaded.ToggleInteractionHotkey);
    }
}

public sealed class OverlayHotkeyMigrationTests
{
    [Theory]
    [InlineData("{\"HotkeysEnabled\":false,\"Width\":540,\"Interaction\":\"passthrough\"}")]
    [InlineData("{\"HotkeysEnabled\":false,\"HotkeySettingsVersion\":0,\"Width\":540,\"Interaction\":\"passthrough\"}")]
    [InlineData("{\"Width\":540,\"Interaction\":\"passthrough\"}")]
    public void LegacySettingsEnableShortcutsExactlyOnceAndPreserveTheLayout(string legacy)
    {
        using var folder = new HotkeyTestFolder();
        File.WriteAllText(folder.SettingsPath, legacy);
        var store = new OverlaySettingsStore(folder.Path);

        var migrated = store.Load();

        Assert.True(migrated.HotkeysEnabled);
        Assert.Equal(540, migrated.Width);
        Assert.Equal("passthrough", migrated.Interaction);
        using (var saved = JsonDocument.Parse(File.ReadAllText(folder.SettingsPath)))
        {
            Assert.True(saved.RootElement.GetProperty("HotkeysEnabled").GetBoolean());
            Assert.Equal(OverlaySettings.CurrentHotkeySettingsVersion,
                saved.RootElement.GetProperty("HotkeySettingsVersion").GetInt32());
        }
        var custom = new OverlayHotkey { Key = "F8", Modifiers = OverlayHotkeyModifiers.None };
        store.Save(migrated with { HotkeysEnabled = false, ToggleOverlayHotkey = custom });
        var disabled = new OverlaySettingsStore(folder.Path).Load();
        Assert.False(disabled.HotkeysEnabled);
        Assert.Equal(custom, disabled.ToggleOverlayHotkey);
        Assert.False(store.Load().HotkeysEnabled);
    }

    [Fact]
    public void CaseInsensitiveCurrentVersionKeepsAConsciousDisable()
    {
        using var folder = new HotkeyTestFolder();
        File.WriteAllText(folder.SettingsPath, "{\"hotkeysEnabled\":false,\"hotkeySettingsVersion\":1}");
        Assert.False(new OverlaySettingsStore(folder.Path).Load().HotkeysEnabled);
    }

    [Fact]
    public void FailedMigrationWriteStillReturnsTheExistingLayout()
    {
        using var folder = new HotkeyTestFolder();
        const string legacy = "{\"HotkeysEnabled\":false,\"Width\":540}";
        File.WriteAllText(folder.SettingsPath, legacy);
        using (var held = new FileStream(folder.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var settings = new OverlaySettingsStore(folder.Path).Load();
            Assert.True(settings.HotkeysEnabled);
            Assert.Equal(540, settings.Width);
            Assert.Equal(legacy, File.ReadAllText(folder.SettingsPath));
        }
        Assert.False(File.Exists(folder.SettingsPath + ".tmp"));
    }

    private sealed class HotkeyTestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Grindcrest.Hotkey.Tests", Guid.NewGuid().ToString("N"));
        public string SettingsPath => System.IO.Path.Combine(Path, "overlay.json");
        public HotkeyTestFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

public sealed class NativeOverlayHotkeyRegistrationTests
{
    private static readonly OverlayHotkey Toggle = OverlayHotkey.DefaultToggleOverlay;
    private static readonly OverlayHotkey Interaction = OverlayHotkey.DefaultToggleInteraction;

    [Fact]
    public void RegistersBothActionsOnceWithNoRepeat()
    {
        var api = new RegistrationApi();
        var registrations = new NativeOverlayHotkeyRegistration(api.Register, api.Unregister);
        Assert.Null(registrations.Apply(true, Toggle, Interaction));
        Assert.Equal([(1, 0x4003u, 0x4fu), (2, 0x4003u, 0x4cu)], api.Registrations);
        Assert.Null(registrations.Apply(true, Toggle with { }, Interaction with { }));
        Assert.Equal(2, api.Registrations.Count);
        Assert.True(registrations.Matches(1, 3, 0x4f));
        Assert.True(registrations.Matches(2, 3, 0x4c));
    }

    [Fact]
    public void SwappingKeysReleasesBothOldBindingsBeforeRegisteringTheNewPair()
    {
        var api = new RegistrationApi();
        var registrations = new NativeOverlayHotkeyRegistration(api.Register, api.Unregister);
        registrations.Apply(true, Toggle, Interaction);
        api.Operations.Clear();

        Assert.Null(registrations.Apply(true, Interaction, Toggle));

        Assert.Equal(["unregister:1", "unregister:2", "register:1", "register:2"], api.Operations);
        Assert.False(registrations.Matches(1, 3, 0x4f));
        Assert.True(registrations.Matches(1, 3, 0x4c));
        Assert.True(registrations.Matches(2, 3, 0x4f));
    }

    [Fact]
    public void DisableReleasesShortcutsAndEnableUsesTheCurrentPair()
    {
        var api = new RegistrationApi();
        var registrations = new NativeOverlayHotkeyRegistration(api.Register, api.Unregister);
        registrations.Apply(true, Toggle, Interaction);
        var custom = new OverlayHotkey { Key = "F8", Modifiers = OverlayHotkeyModifiers.None };
        Assert.Null(registrations.Apply(false, custom, Interaction));
        Assert.Empty(api.Owned);
        Assert.False(registrations.Matches(1, 3, 0x4f));
        Assert.False(registrations.Matches(2, 3, 0x4c));

        Assert.Null(registrations.Apply(true, custom, Interaction));

        Assert.True(registrations.Matches(1, 0, 0x77));
        Assert.Equal(4, api.Registrations.Count);
    }

    [Fact]
    public void ConflictingBindingReportsItsLabelAndDoesNotKeepTheOldShortcut()
    {
        var api = new RegistrationApi();
        var registrations = new NativeOverlayHotkeyRegistration(api.Register, api.Unregister);
        registrations.Apply(true, Toggle, Interaction);
        var custom = new OverlayHotkey { Key = "F2", Modifiers = OverlayHotkeyModifiers.Shift };
        api.Blocked.Add((4, 0x71));

        var error = registrations.Apply(true, Toggle, custom);

        Assert.Contains("Umschalt+F2", error);
        Assert.True(registrations.Matches(1, 3, 0x4f));
        Assert.False(registrations.Matches(2, 3, 0x4c));
        Assert.False(registrations.Matches(2, 4, 0x71));
        Assert.Equal(error, registrations.Apply(true, Toggle, custom));
        Assert.Equal(4, api.Registrations.Count);

        registrations.Apply(false, Toggle, custom);
        api.Blocked.Clear();
        Assert.Null(registrations.Apply(true, Toggle, custom));
        Assert.True(registrations.Matches(2, 4, 0x71));
    }

    [Fact]
    public void DestroyedHandleReleasesAndRecreatesTheSameBindings()
    {
        var api = new RegistrationApi();
        var registrations = new NativeOverlayHotkeyRegistration(api.Register, api.Unregister);
        registrations.Apply(true, Toggle, Interaction);
        registrations.Clear();
        registrations.Clear();
        Assert.Equal([1, 2], api.Unregistrations);
        Assert.Empty(api.Owned);
        Assert.False(registrations.Matches(1, 3, 0x4f));
        Assert.Null(registrations.Apply(true, Toggle, Interaction));
        Assert.Equal(4, api.Registrations.Count);
    }

    [Fact]
    public void QueuedMessagesMustMatchCurrentIdKeyAndModifiers()
    {
        var api = new RegistrationApi();
        var registrations = new NativeOverlayHotkeyRegistration(api.Register, api.Unregister);
        registrations.Apply(true, Toggle, Interaction);
        Assert.False(registrations.Matches(3, 3, 0x4f));
        Assert.False(registrations.Matches(1, 2, 0x4f));
        Assert.False(registrations.Matches(1, 3, 0x4c));
        registrations.Apply(true, Toggle with { Modifiers = OverlayHotkeyModifiers.Control }, Interaction);
        Assert.False(registrations.Matches(1, 3, 0x4f));
        Assert.True(registrations.Matches(1, 2, 0x4f));
    }

    private sealed class RegistrationApi
    {
        public List<(int Id, uint Modifiers, uint Key)> Registrations { get; } = [];
        public List<int> Unregistrations { get; } = [];
        public List<string> Operations { get; } = [];
        public Dictionary<int, (uint Modifiers, uint Key)> Owned { get; } = [];
        public HashSet<(uint Modifiers, uint Key)> Blocked { get; } = [];
        public bool Register(int id, uint modifiers, uint key)
        {
            Operations.Add($"register:{id}");
            Registrations.Add((id, modifiers, key));
            var binding = (modifiers & ~0x4000u, key);
            if (Blocked.Contains(binding) || Owned.Values.Contains(binding)) return false;
            Owned.Add(id, binding);
            return true;
        }
        public void Unregister(int id)
        {
            Operations.Add($"unregister:{id}");
            Unregistrations.Add(id);
            Assert.True(Owned.Remove(id));
        }
    }
}
