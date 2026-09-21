using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class MultiOverlayEditorInteractionTests
{
    [Fact]
    public async Task LoadErrorBlocksEditingAndRetryRecoversTheSavedLayout()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Grindcrest.OverlayRecovery.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "overlay.json");
        File.WriteAllText(path, "{broken");
        try
        {
            await Render(async (editor, overlay, markup, _) =>
            {
                Assert.Contains("Gespeicherte Overlays nicht verfügbar", markup());
                Assert.Matches("<fieldset[^>]*disabled", markup());
                Assert.Contains("Erneut laden", markup());
                await Invoke(editor, "AddModule", "clock");
                Assert.Equal("{broken", File.ReadAllText(path));
                File.WriteAllText(path, "{\"Version\":2,\"SelectedOverlayId\":\"recovered\",\"Overlays\":[{\"Id\":\"recovered\",\"Name\":\"Mein wiederhergestelltes Layout\",\"Settings\":{\"Width\":620}}]}");
                await Invoke(editor, "ReloadOverlays");
                Assert.Null(overlay.LoadError);
                Assert.Equal(620, overlay.Settings.Width);
                Assert.Contains("Mein wiederhergestelltes Layout", markup());
                Assert.DoesNotMatch("<fieldset[^>]*disabled", markup());
                await Invoke(editor, "CreateOverlay");
                Assert.Equal(2, overlay.Overlays.Count);
            }, store: new OverlaySettingsStore(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public Task WindowManagerOffersSelectionCreationDuplicationAndNaming() => Render(async (editor, overlay, markup, js) =>
    {
        var html = markup();
        Assert.Contains("oe-window-manager", html);
        Assert.Contains("aria-label=\"Overlay-Fenster auswählen\"", html);
        Assert.Contains("aria-label=\"Overlay-Name\"", html);
        Assert.Contains("Neues Overlay", html);
        Assert.Contains("Duplizieren", html);
        Assert.Contains("aria-label=\"Ausgewähltes Overlay löschen\"", html);
        Assert.Contains(Assert.Single(overlay.Overlays).Name, html);
        await Task.CompletedTask;
    });

    [Fact]
    public Task CreatingAnotherWindowSelectsItAndLeavesTheOriginalLayoutIntact() => Render(async (editor, overlay, markup, js) =>
    {
        var originalId = overlay.SelectedOverlayId;
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with
        {
            Enabled = true, BackgroundOpacity = .31, Widgets = [OverlayCatalog.CreateWidget("silver")],
        }));
        var original = overlay.Settings;

        await Invoke(editor, "CreateOverlay");

        Assert.Equal(2, overlay.Overlays.Count);
        Assert.NotEqual(originalId, overlay.SelectedOverlayId);
        Assert.Equal(original, overlay.Overlays.Single(window => window.Id == originalId).Settings);
        Assert.False(overlay.Settings.Enabled);
        Assert.Empty(overlay.Settings.Widgets);
        Assert.Contains(overlay.Overlays.Single(window => window.Id == overlay.SelectedOverlayId).Name, markup());

        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with
        {
            BackgroundOpacity = .72, Widgets = [OverlayCatalog.CreateWidget("duration")],
        }));
        Assert.Equal(original, overlay.Overlays.Single(window => window.Id == originalId).Settings);
        Assert.Equal("duration", Assert.Single(overlay.Settings.Widgets).Kind);
    });

    [Fact]
    public Task DuplicatingCopiesTheLayoutAndEditingTheCopyDoesNotChangeTheSource() => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with
        {
            Width = 540, Height = 280, BackgroundOpacity = .45, ShowBorder = false,
            Widgets = [OverlayCatalog.CreateWidget("silver", 32, 48) with { FontScale = 1.4, ShowLabel = false }],
        }));
        var sourceId = overlay.SelectedOverlayId;
        var source = overlay.Settings;

        await Invoke(editor, "DuplicateOverlay");

        Assert.Equal(2, overlay.Overlays.Count);
        Assert.NotEqual(sourceId, overlay.SelectedOverlayId);
        Assert.Equal(source.Width, overlay.Settings.Width);
        Assert.Equal(source.Height, overlay.Settings.Height);
        Assert.Equal(source.BackgroundOpacity, overlay.Settings.BackgroundOpacity);
        Assert.Equal(source.ShowBorder, overlay.Settings.ShowBorder);
        Assert.Equal(source.Widgets.Select(Layout), overlay.Settings.Widgets.Select(Layout));
        Assert.False(overlay.Settings.Enabled);
        Assert.Empty(source.Widgets.Select(widget => widget.Id).Intersect(overlay.Settings.Widgets.Select(widget => widget.Id)));

        var copiedWidget = Assert.Single(overlay.Settings.Widgets);
        await editor.SelectWidget(copiedWidget.Id);
        await Invoke(editor, "ChangeWidget", new Func<OverlayWidget, OverlayWidget>(widget => widget with
        {
            FontScale = .9, ShowLabel = true,
        }));

        Assert.Equal(.9, Assert.Single(overlay.Settings.Widgets).FontScale);
        Assert.Equal(source, overlay.Overlays.Single(window => window.Id == sourceId).Settings);
    });

    [Fact]
    public Task SwitchingWindowsKeepsTheirLayoutsBehaviorAndNativePositionsIndependent() => Render(async (editor, overlay, markup, js) =>
    {
        var firstId = overlay.SelectedOverlayId;
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with
        {
            Width = 400, Height = 200, Enabled = true, Visibility = "always", Interaction = "locked",
            Widgets = [OverlayCatalog.CreateWidget("silver")],
        }));
        await overlay.SavePositionAsync(.7, .2);
        var first = overlay.Settings;
        await editor.SelectWidget(Assert.Single(first.Widgets).Id);

        await Invoke(editor, "CreateOverlay");
        var secondId = overlay.SelectedOverlayId;
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with
        {
            Width = 520, Height = 300, Enabled = false, Visibility = "session", Interaction = "passthrough",
            Widgets = [OverlayCatalog.CreateWidget("duration")],
        }));
        await overlay.SavePositionAsync(.1, .8);
        var second = overlay.Settings;

        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = firstId });
        Assert.Equal(first, overlay.Settings);
        // Native movement can arrive while this window is selected in the editor.
        await overlay.SavePositionAsync(.6, .35);
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with { BackgroundOpacity = .25 }));
        Assert.Equal(.6, overlay.Settings.PositionX);
        Assert.Equal(.35, overlay.Settings.PositionY);
        Assert.Equal("silver", Assert.Single(overlay.Settings.Widgets).Kind);
        Assert.Equal(second, overlay.Overlays.Single(window => window.Id == secondId).Settings);

        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = secondId });
        Assert.Equal(second, overlay.Settings);
    });

    [Fact]
    public Task EditingShortcutsFromAnotherWindowUpdatesTheSharedPairAcrossSelections() => Render(async (editor, overlay, markup, js) =>
    {
        var firstId = overlay.SelectedOverlayId;
        var firstLayout = overlay.Settings.Widgets.ToArray();
        await Invoke(editor, "CreateOverlay");
        var secondId = overlay.SelectedOverlayId;
        await editor.AddModuleAt("clock", 16, 24);
        var secondLayout = overlay.Settings.Widgets.ToArray();
        var toggle = new OverlayHotkey { Modifiers = OverlayHotkeyModifiers.Control, Key = "F8" };
        var interaction = new OverlayHotkey { Modifiers = OverlayHotkeyModifiers.Alt, Key = "F9" };

        await Invoke(editor, "OpenHotkeyEditor");
        Set(editor, "_toggleOverlayDraft", toggle);
        Set(editor, "_toggleInteractionDraft", interaction);
        await Invoke(editor, "SaveHotkeys");

        Assert.Equal(secondId, overlay.SelectedOverlayId);
        Assert.True(overlay.Hotkeys.Enabled);
        Assert.Equal(toggle, overlay.Hotkeys.ToggleOverlay);
        Assert.Equal(interaction, overlay.Hotkeys.ToggleInteraction);
        Assert.Equal(secondLayout, overlay.Settings.Widgets);
        foreach (var id in new[] { firstId, secondId })
        {
            await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = id });
            Assert.Contains("<kbd>Strg+F8</kbd>", markup());
            Assert.Contains("<kbd>Alt+F9</kbd>", markup());
            await Invoke(editor, "OpenHotkeyEditor");
            Assert.Equal(toggle, Get<OverlayHotkey>(editor, "_toggleOverlayDraft"));
            Assert.Equal(interaction, Get<OverlayHotkey>(editor, "_toggleInteractionDraft"));
            Assert.True(Get<bool>(editor, "_hotkeysEnabledDraft"));
            await Invoke(editor, "CloseHotkeyEditor");
        }
        Assert.Equal(firstLayout, overlay.Overlays.Single(window => window.Id == firstId).Settings.Widgets);
        Assert.Equal(secondLayout, overlay.Settings.Widgets);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task CreatedAndDuplicatedWindowsUseTheCurrentSharedHotkeys(bool enabled) => Render(async (editor, overlay, markup, js) =>
    {
        var expected = new OverlayHotkeySettings
        {
            Enabled = enabled,
            ToggleOverlay = new() { Modifiers = OverlayHotkeyModifiers.Alt, Key = "F10" },
            ToggleInteraction = new() { Modifiers = OverlayHotkeyModifiers.Control, Key = "F11" },
        };
        Assert.True((await overlay.SaveHotkeysAsync(expected)).Succeeded);

        foreach (var action in new[] { "CreateOverlay", "DuplicateOverlay" })
        {
            await Invoke(editor, action);
            Assert.False(overlay.Settings.Enabled);
            Assert.Equal(expected, overlay.Hotkeys);
            Assert.Equal(enabled, overlay.Settings.HotkeysEnabled);
            Assert.Equal(expected.ToggleOverlay, overlay.Settings.ToggleOverlayHotkey);
            Assert.Equal(expected.ToggleInteraction, overlay.Settings.ToggleInteractionHotkey);
            Assert.Contains("<kbd>Alt+F10</kbd>", markup());
            Assert.Contains("<kbd>Strg+F11</kbd>", markup());
            await Invoke(editor, "OpenHotkeyEditor");
            Assert.Equal(enabled, Get<bool>(editor, "_hotkeysEnabledDraft"));
            await Invoke(editor, "CloseHotkeyEditor");
        }
        Assert.Equal(3, overlay.Overlays.Count);
        Assert.All(overlay.Overlays, window =>
        {
            Assert.Equal(enabled, window.Settings.HotkeysEnabled);
            Assert.Equal(expected.ToggleOverlay, window.Settings.ToggleOverlayHotkey);
            Assert.Equal(expected.ToggleInteraction, window.Settings.ToggleInteractionHotkey);
        });
    });

    [Fact]
    public Task GlobalVisibilityShortcutClearsAllPreviewsAndUpdatesTheSelectedEditor() => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "TogglePreview");
        await Invoke(editor, "CreateOverlay");
        var selectedId = overlay.SelectedOverlayId;
        await Invoke(editor, "TogglePreview");
        Assert.All(overlay.Overlays, window => Assert.True(overlay.GetState(window.Id).Previewing));
        Assert.True((await overlay.ToggleAllOverlaysAsync()).Succeeded);
        Assert.All(overlay.Overlays, window =>
        {
            Assert.False(window.Settings.Enabled);
            Assert.False(overlay.GetState(window.Id).Previewing);
        });
        Assert.False(Get<OverlaySettings>(editor, "_settings").Enabled);
        Assert.Contains("2 erstellt · 0 aktiv", markup());

        Assert.True((await overlay.ToggleAllOverlaysAsync()).Succeeded);
        Assert.All(overlay.Overlays, window => Assert.True(window.Settings.Enabled));
        Assert.True(Get<OverlaySettings>(editor, "_settings").Enabled);
        Assert.Contains("2 erstellt · 2 aktiv", markup());

        Assert.True((await overlay.ToggleAllOverlaysAsync()).Succeeded);

        Assert.Equal(selectedId, overlay.SelectedOverlayId);
        Assert.All(overlay.Overlays, window =>
        {
            Assert.False(window.Settings.Enabled);
            Assert.False(overlay.GetState(window.Id).Previewing);
        });
        Assert.False(Get<OverlaySettings>(editor, "_settings").Enabled);
        Assert.Contains("2 erstellt · 0 aktiv", markup());
        Assert.Contains("Auf Bildschirm testen", markup());
        Assert.DoesNotContain("Vorschau beenden", markup());

        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with { BackgroundOpacity = .45 }));
        Assert.All(overlay.Overlays, window => Assert.False(window.Settings.Enabled));
    });

    [Fact]
    public Task GlobalInteractionShortcutUpdatesEveryWindowAndSurvivesTheNextEditorChange() => Render(async (editor, overlay, markup, js) =>
    {
        var firstId = overlay.SelectedOverlayId;
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with { Interaction = "locked" }));
        await Invoke(editor, "CreateOverlay");
        var secondId = overlay.SelectedOverlayId;
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with { Interaction = "move" }));

        Assert.True((await overlay.ToggleAllInteractionAsync()).Succeeded);

        Assert.Equal(secondId, overlay.SelectedOverlayId);
        Assert.All(overlay.Overlays, window => Assert.Equal("passthrough", window.Settings.Interaction));
        Assert.Equal("passthrough", Get<OverlaySettings>(editor, "_settings").Interaction);
        Assert.Contains("Klicks gehen ans Spiel", markup());
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with { BackgroundOpacity = .4 }));
        Assert.All(overlay.Overlays, window => Assert.Equal("passthrough", window.Settings.Interaction));
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = firstId });
        Assert.Contains("Klicks gehen ans Spiel", markup());

        Assert.True((await overlay.ToggleAllInteractionAsync()).Succeeded);

        Assert.Equal(firstId, overlay.SelectedOverlayId);
        Assert.All(overlay.Overlays, window => Assert.Equal("move", window.Settings.Interaction));
        Assert.Equal("move", Get<OverlaySettings>(editor, "_settings").Interaction);
        Assert.Contains("Verschiebbar", markup());
    });

    [Fact]
    public Task PendingCanvasSavePreservesGlobalShortcutChangesMadeWhileItWaits() => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "CreateOverlay");
        await editor.AddModuleAt("clock", 16, 24);
        var selectedId = overlay.SelectedOverlayId;
        var modules = overlay.Settings.Widgets.ToArray();
        var gate = Get<SemaphoreSlim>(editor, "_saveGate");
        Task pendingSave = Task.CompletedTask;
        await gate.WaitAsync();
        try
        {
            pendingSave = editor.CommitCanvasSize(240, 128);
            Assert.False(pendingSave.IsCompleted);
            Assert.True((await overlay.ToggleAllOverlaysAsync()).Succeeded);
            Assert.True((await overlay.ToggleAllInteractionAsync()).Succeeded);
        }
        finally { gate.Release(); }

        await pendingSave;

        Assert.Equal(selectedId, overlay.SelectedOverlayId);
        Assert.Equal(240, overlay.Settings.Width);
        Assert.Equal(128, overlay.Settings.Height);
        Assert.Equal(modules, overlay.Settings.Widgets);
        Assert.All(overlay.Overlays, window =>
        {
            Assert.True(window.Settings.Enabled);
            Assert.Equal("passthrough", window.Settings.Interaction);
        });
        Assert.True(Get<OverlaySettings>(editor, "_settings").Enabled);
        Assert.Equal("passthrough", Get<OverlaySettings>(editor, "_settings").Interaction);
        Assert.Contains("2 erstellt · 2 aktiv", markup());
        Assert.Contains("Klicks gehen ans Spiel", markup());
    });

    [Fact]
    public Task GlobalShortcutsUpdateSelectedWindowWhileItsValidationErrorRemainsVisible() => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "CreateOverlay");
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with
        {
            Enabled = true, Interaction = "move",
        }));
        var before = overlay.Settings;
        await Invoke(editor, "CanvasDimension", new ChangeEventArgs { Value = "159" }, true);
        var error = Get<string>(editor, "_error");
        Assert.Contains("Die Änderung wurde nicht gespeichert.", error);
        Assert.Equal(before, overlay.Settings);

        Assert.True((await overlay.ToggleAllOverlaysAsync()).Succeeded);
        Assert.True((await overlay.ToggleAllInteractionAsync()).Succeeded);

        var displayed = Get<OverlaySettings>(editor, "_settings");
        Assert.False(displayed.Enabled);
        Assert.Equal("passthrough", displayed.Interaction);
        Assert.Equal(before.Width, displayed.Width);
        Assert.Equal(before.Widgets, displayed.Widgets);
        Assert.Equal(error, Get<string>(editor, "_error"));
        Assert.Contains(error, markup());
        Assert.Contains("2 erstellt · 0 aktiv", markup());
        Assert.Contains("Klicks gehen ans Spiel", markup());
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ShrinkingAndRestoringAWindowPreservesItsModulesAcrossWindowSelections(bool drag) => Render(async (editor, overlay, markup, js) =>
    {
        var firstId = overlay.SelectedOverlayId;
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with
        {
            Width = 600, Height = 400,
            Widgets =
            [
                OverlayCatalog.CreateWidget("duration", 24, 32) with { Width = 176, Height = 72, FontScale = 1.2 },
                OverlayCatalog.CreateWidget("drop-grid", 336, 192) with { Width = 240, Height = 160, ItemSize = 48 },
            ],
        }));
        var original = overlay.Settings;
        await Invoke(editor, "CreateOverlay");
        var secondId = overlay.SelectedOverlayId;
        await editor.AddModuleAt("clock", 16, 24);
        var second = overlay.Settings;
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = firstId });
        if (drag) await editor.CommitCanvasSize(160, 64);
        else
        {
            await Invoke(editor, "CanvasDimension", new ChangeEventArgs { Value = "160" }, true);
            await Invoke(editor, "CanvasDimension", new ChangeEventArgs { Value = "64" }, false);
        }

        Assert.Equal(160, overlay.Settings.Width);
        Assert.Equal(64, overlay.Settings.Height);
        Assert.Equal(original.Widgets, overlay.Settings.Widgets);
        Assert.Contains("oe-stage-content", markup());
        Assert.Contains("data-x=\"336\"", markup());
        Assert.Contains("data-y=\"192\"", markup());
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with { BackgroundOpacity = .52 }));
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = secondId });
        Assert.Equal(second, overlay.Settings);
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = firstId });
        Assert.Equal(160, overlay.Settings.Width);
        Assert.Equal(64, overlay.Settings.Height);
        Assert.Equal(original.Widgets, overlay.Settings.Widgets);

        await editor.CommitCanvasSize(original.Width, original.Height);

        Assert.Equal(original.Width, overlay.Settings.Width);
        Assert.Equal(original.Height, overlay.Settings.Height);
        Assert.Equal(original.Widgets, overlay.Settings.Widgets);
        Assert.Equal(second, overlay.Overlays.Single(window => window.Id == secondId).Settings);
    });

    [Fact]
    public Task RenamingOnlyChangesTheSelectedWindowsName() => Render(async (editor, overlay, markup, js) =>
    {
        var first = Assert.Single(overlay.Overlays);
        await Invoke(editor, "CreateOverlay");
        var selectedId = overlay.SelectedOverlayId;
        var before = overlay.Settings;

        await Invoke(editor, "RenameOverlay", new ChangeEventArgs { Value = "  Meine Uhr  " });

        Assert.Equal("Meine Uhr", overlay.Overlays.Single(window => window.Id == selectedId).Name);
        Assert.Equal(first.Name, overlay.Overlays.Single(window => window.Id == first.Id).Name);
        Assert.Equal(before, overlay.Settings);
        Assert.Contains("Meine Uhr · Aus</option>", markup());
    });

    [Fact]
    public Task PendingLayoutConfirmationPreventsSwitchingUntilItIsClosed() => Render(async (editor, overlay, markup, js) =>
    {
        var firstId = overlay.SelectedOverlayId;
        await Invoke(editor, "CreateOverlay");
        var secondId = overlay.SelectedOverlayId;
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = firstId });
        var first = overlay.Settings;

        await Invoke(editor, "ClearLayout");
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = secondId });

        Assert.Equal(firstId, overlay.SelectedOverlayId);
        Assert.Equal(first, overlay.Settings);
        await Invoke(editor, "CloseLayoutConfirmation");
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = secondId });
        await Invoke(editor, "ConfirmLayoutChange");

        Assert.Equal(secondId, overlay.SelectedOverlayId);
        Assert.Equal(first, overlay.Overlays.Single(window => window.Id == firstId).Settings);
    });

    [Fact]
    public Task CancelingWindowDeletionDiscardsThePendingAction() => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "CreateOverlay");
        var selectedId = overlay.SelectedOverlayId;
        var before = overlay.Settings;

        await Invoke(editor, "RequestOverlayDelete");

        Assert.Equal(2, overlay.Overlays.Count);
        var dialog = Regex.Match(markup(), "<dialog\\b[^>]*\\bid=\"overlay-window-delete\"[\\s\\S]*?</dialog>");
        Assert.True(dialog.Success, "Deleting an overlay window must use an app confirmation dialog.");
        Assert.Contains("Abbrechen</button>", dialog.Value);
        Assert.Contains(js.Calls, call => call == ("grindcrest.showDialog", "overlay-window-delete"));
        Assert.DoesNotContain(js.Calls, call => call.Identifier is "confirm" or "alert" or "prompt");

        await Invoke(editor, "CloseOverlayDelete");
        await Invoke(editor, "DeleteOverlay");

        Assert.Equal(2, overlay.Overlays.Count);
        Assert.Equal(selectedId, overlay.SelectedOverlayId);
        Assert.Equal(before, overlay.Settings);
    });

    [Fact]
    public Task ConfirmingWindowDeletionOnlyRemovesThatWindowAndSelectsASurvivor() => Render(async (editor, overlay, markup, js) =>
    {
        var survivor = Assert.Single(overlay.Overlays);
        await Invoke(editor, "CreateOverlay");
        var deletedId = overlay.SelectedOverlayId;

        await Invoke(editor, "RequestOverlayDelete");
        await Invoke(editor, "DeleteOverlay");
        await Invoke(editor, "DeleteOverlay");

        var remaining = Assert.Single(overlay.Overlays);
        Assert.Equal(survivor.Id, remaining.Id);
        Assert.Equal(survivor.Settings, remaining.Settings);
        Assert.Equal(survivor.Id, overlay.SelectedOverlayId);
        Assert.DoesNotContain(overlay.Overlays, window => window.Id == deletedId);
        Assert.Contains(js.Calls, call => call == ("grindcrest.closeDialog", "overlay-window-delete"));
    });

    [Fact]
    public Task ClosingTheEditorClearsEveryWindowsTemporaryPreview() => Render(async (editor, overlay, markup, js) =>
    {
        var firstId = overlay.SelectedOverlayId;
        await Invoke(editor, "TogglePreview");
        Assert.True(overlay.GetState(firstId).Previewing);
        await Invoke(editor, "CreateOverlay");
        var secondId = overlay.SelectedOverlayId;
        await Invoke(editor, "TogglePreview");
        Assert.True(overlay.GetState(secondId).Previewing);
        Assert.True(overlay.GetState(firstId).Previewing);
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = firstId });
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(settings => settings with { Enabled = true }));
    }, (overlay, js) =>
    {
        Assert.All(overlay.Overlays, window => Assert.False(overlay.GetState(window.Id).Previewing));
        Assert.True(overlay.Settings.Enabled);
        Assert.Contains(js.Calls, call => call.Identifier == "grindcrestOverlayEditor.unmount");
    });

    [Fact]
    public Task ClockCanBeAddedAndItsDisplaySettingsRemainWithItsWindow() => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "CreateOverlay");
        var clockWindowId = overlay.SelectedOverlayId;
        await editor.AddModuleAt("clock", 8, 8);
        var clock = Assert.Single(overlay.Settings.Widgets);
        Assert.Equal("clock", clock.Kind);
        var html = markup();
        Assert.Contains("oe-clock-settings", html);
        Assert.Contains("Echte Uhrzeit", html);
        Assert.Contains("BDO-Ingame-Zeit", html);
        Assert.Contains("Countdown bis Tag / Nacht", html);
        Assert.DoesNotContain("Sekunden anzeigen", html);
        Assert.Contains("aria-label=\"BDO-Zeitkorrektur\"", html);
        Assert.Contains("overlay-clock-row is-real", html);
        Assert.Contains("overlay-clock-row is-game", html);
        Assert.Contains("overlay-clock-row is-countdown", html);

        await Invoke(editor, "ChangeWidget", new Func<OverlayWidget, OverlayWidget>(widget => widget with
        {
            ShowRealTime = false, ShowGameTime = true, ShowDayNightCountdown = false,
        }));
        await Invoke(editor, "WidgetNumber", new ChangeEventArgs { Value = "20" }, "clockOffset");
        await Invoke(editor, "CreateOverlay");
        await Invoke(editor, "SelectOverlay", new ChangeEventArgs { Value = clockWindowId });

        var saved = Assert.Single(overlay.Settings.Widgets);
        Assert.False(saved.ShowRealTime);
        Assert.True(saved.ShowGameTime);
        Assert.False(saved.ShowDayNightCountdown);
        Assert.Equal(20, saved.ClockOffsetMinutes);
        html = markup();
        Assert.Contains("overlay-clock-row is-game", html);
        Assert.DoesNotContain("overlay-clock-row is-real", html);
        Assert.DoesNotContain("overlay-clock-row is-countdown", html);
    });

    [Theory]
    [InlineData("240.5")]
    [InlineData("-241")]
    [InlineData("0.5")]
    public Task InvalidClockCorrectionDoesNotReplaceTheSavedValue(string input) => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "CreateOverlay");
        await editor.AddModuleAt("clock", 8, 8);
        await Invoke(editor, "WidgetNumber", new ChangeEventArgs { Value = "20" }, "clockOffset");
        var before = overlay.Settings;

        await Invoke(editor, "WidgetNumber", new ChangeEventArgs { Value = input }, "clockOffset");

        Assert.Equal(before, overlay.Settings);
        Assert.Equal(20, Assert.Single(overlay.Settings.Widgets).ClockOffsetMinutes);
    });

    [Theory]
    [InlineData("overlay-hotkeys-edit")]
    [InlineData("overlay-template-save")]
    [InlineData("overlay-template-delete")]
    public Task FailedFeatureDialogDoesNotLeaveWindowManagementBlocked(string dialog) => Render(async (editor, overlay, markup, js) =>
    {
        js.FailNextDialog = dialog;
        if (dialog == "overlay-hotkeys-edit") await Invoke(editor, "OpenHotkeyEditor");
        else if (dialog == "overlay-template-save") await Invoke(editor, "OpenTemplateSave", new object?[] { null });
        else
        {
            await overlay.SaveTemplateAsync("Meine Vorlage", overlay.Settings);
            typeof(OverlayEditor).GetField("_selectedTemplateId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(editor, Assert.Single(overlay.Templates).Id);
            await Invoke(editor, "RequestTemplateDelete");
        }
        Assert.Contains("Der Dialog konnte nicht geöffnet werden.", markup());

        await Invoke(editor, "CreateOverlay");

        Assert.Equal(2, overlay.Overlays.Count);
    });

    private static object Layout(OverlayWidget widget) => new
    {
        widget.Kind, widget.X, widget.Y, widget.Width, widget.Height, widget.FontScale, widget.ShowLabel,
    };

    private static async Task Render(Func<OverlayEditor, OverlayService, Func<string>, RecordingJs, Task> test,
        Action<OverlayService, RecordingJs>? afterDispose = null, OverlaySettingsStore? store = null)
    {
        await using var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        using var overlay = new OverlayService(tracker, store);
        var activator = new CapturingActivator();
        var js = new RecordingJs();
        using var provider = new ServiceCollection().AddLogging().AddSingleton<IOverlayService>(overlay).AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime>(js).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using (var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<OverlayEditor>();
                var editor = activator.Components.OfType<OverlayEditor>().Single();
                string Markup()
                {
                    typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
                    return WebUtility.HtmlDecode(root.ToHtmlString());
                }
                await test(editor, overlay, Markup, js);
            });
        }
        afterDispose?.Invoke(overlay, js);
    }

    private static async Task Invoke(OverlayEditor editor, string name, params object?[] arguments)
    {
        var method = typeof(OverlayEditor).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        if (method.Invoke(editor, arguments) is Task task) await task;
    }

    private static void Set(OverlayEditor editor, string name, object value) =>
        typeof(OverlayEditor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, value);

    private static T Get<T>(OverlayEditor editor, string name) =>
        (T)typeof(OverlayEditor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class RecordingJs : IJSRuntime
    {
        public List<(string Identifier, string? Dialog)> Calls { get; } = [];
        public string? FailNextDialog { get; set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args?.FirstOrDefault()?.ToString()));
            if (FailNextDialog is not null && identifier == "grindcrest.showDialog" && args?.FirstOrDefault()?.ToString() == FailNextDialog)
            {
                FailNextDialog = null;
                throw new JSException("Der Dialog ist vorübergehend nicht verfügbar.");
            }
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
