using System.Globalization;
using BdoGrindTracker.App.Overlay;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Components;

public partial class OverlayEditor
{
    [CascadingParameter(Name = "IsBrowserPreview")] public bool IsBrowserPreview { get; set; }
    private OverlaySettings _settings = new();
    private string? _selectedId, _error;
    private string _itemSearch = "";
    private bool _demo, _saving, _disposed;
    private readonly System.Diagnostics.Stopwatch _rotationDemoClock = System.Diagnostics.Stopwatch.StartNew();
    private System.Threading.Timer? _rotationDemoTimer;
    private ElementReference _viewport;
    private DotNetObjectReference<OverlayEditor>? _reference;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private long _revision;
    private sealed record LayoutChange(string? Preset, OverlayTemplate? Template = null);
    private LayoutChange? _pendingLayoutChange;
    private bool _confirmingLayout;
    private bool PendingClearLayout => _pendingLayoutChange?.Preset is null && _pendingLayoutChange?.Template is null;
    private string LayoutConfirmationTitle => PendingClearLayout ? T("Layout leeren?") : T("Vorlage anwenden?");
    private string LayoutConfirmationAction => PendingClearLayout ? T("Layout leeren") : T("Vorlage anwenden");
    private string PendingPresetLabel => _pendingLayoutChange?.Template?.Name ?? (_pendingLayoutChange?.Preset switch
    {
        "loot" => T("Loot-Inventar"), "loot-strip" => T("Loot-Leiste"), "compact" => T("Kompakt"), "dashboard" => "Dashboard", "rotation-monitor" => "Rotation Monitor", _ => T("Vorlage")
    });
    private static IReadOnlyList<OverlayWidgetDefinition> Modules => OverlayCatalog.Widgets;
    private OverlayWidget? SelectedWidget => _settings.Widgets.FirstOrDefault(w => w.Id == _selectedId);
    private OverlaySnapshot PreviewSnapshot => _demo ? OverlayMetrics.DemoFor(UiLanguage) with { ThemeId = Overlay.Snapshot.ThemeId, UiLanguage = UiLanguage,
        Rotation = Overlay.Snapshot.Rotation.SpotId switch
        {
            BdoGrindTracker.Core.LootSpotCatalog.AphrodonId =>
                AphrodonRotationDemo.At((350 + _rotationDemoClock.Elapsed.TotalSeconds) % AphrodonRotationDemo.Reference.Duration),
            BdoGrindTracker.Core.LootSpotCatalog.EventHorizonId =>
                EventHorizonRotationDemo.At((350 + _rotationDemoClock.Elapsed.TotalSeconds) % EventHorizonRotationDemo.Reference.Duration),
            _ => HermesiaRotationDemo.At((350 + _rotationDemoClock.Elapsed.TotalSeconds) % HermesiaRotationDemo.Reference.Duration),
        } } : Overlay.Snapshot;
    private OverlayWindowChrome Chrome => OverlayWindowChrome.For(PreviewSnapshot.ThemeId, _settings.ShowBorder);
    private string StageStyle => $"width:{Css(Chrome.OuterWidth(_settings.Width))}px;height:{Css(Chrome.OuterHeight(_settings.Height))}px;--overlay-opacity:{Css(_settings.BackgroundOpacity)};background:rgba(var(--overlay-surface-rgb,17,23,30),{Css(_settings.BackgroundOpacity)})";
    private string ContentStyle => $"inset:{Css(Chrome.Top)}px {Css(Chrome.Right)}px {Css(Chrome.Bottom)}px {Css(Chrome.Left)}px";
    private static string WidgetStyle(OverlayWidget widget) => $"left:{Css(widget.X)}px;top:{Css(widget.Y)}px;width:{Css(widget.Width)}px;height:{Css(widget.Height)}px";
    private static string Css(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private string Percent(double value) => value.ToString("P0", UiCulture);
    private static string Text(ChangeEventArgs e) => e.Value?.ToString() ?? "";
    private static bool Checked(ChangeEventArgs e) => e.Value is true;
    private static int WholeNumber(ChangeEventArgs e, int current) =>
        int.TryParse(Text(e), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : current;
    private string PeakHelp(string mode) => T(mode switch
    {
        OverlayChartSections.ExcludePeaks => "Ihr Silber fließt nicht in die Kurve ein; so bleibt nur der übrige Loot sichtbar.",
        OverlayChartSections.LogarithmicPeaks => "Große Werte werden gestaucht: ein Hundertstel des höchsten Abschnitts erreicht noch die halbe Höhe.",
        _ => "Die Höhe richtet sich nach den Abschnitten ohne wertvolle Drops; höhere Abschnitte werden oben gekappt und mit zwei Strichen markiert.",
    });
    private string Label(string kind) => T(OverlayCatalog.Find(kind)?.Label ?? "Modul");
    private string InteractionLabel => _settings.Interaction switch { "passthrough" => T("Klicks gehen ans Spiel"), "locked" => T("Position gesperrt"), _ => T("Verschiebbar") };
    private string VisibilityLabel => _settings.Visibility switch { "session" => T("Während der Session"), "always" => T("Immer sichtbar"), _ => T("Im Spiel sichtbar") };
    private string InteractionHint => _settings.Interaction switch
    {
        "passthrough" => T("Das Overlay reagiert nicht auf die Maus. Spiele auch durch das Overlay hindurch."),
        "locked" => T("Die Position bleibt fest. Tracking-Buttons lassen sich weiterhin anklicken."),
        _ => T("Zum Verschieben den Overlay-Hintergrund ziehen."),
    };

    protected override void OnInitialized()
    {
        base.OnInitialized();
        LoadSelectedOverlay();
        Overlay.Changed += OverlayChanged;
        _rotationDemoTimer = new System.Threading.Timer(_ =>
        {
            if (_demo && !_disposed && _settings.Widgets.Any(w => w.Kind == "rotation-monitor"))
                _ = InvokeAsync(() => { if (!_disposed) StateHasChanged(); });
        }, null, 500, 500);
    }

    private void OverlayChanged()
    {
        if (!_disposed) _ = InvokeAsync(() =>
        {
            if (_disposed) return;
            // Native resize or a global interaction shortcut may update the
            // same settings while the editor is visible.
            if (!_saving)
            {
                if (_editingOverlayId != Overlay.SelectedOverlayId) LoadSelectedOverlay();
                else if (_error is null) _settings = OverlayLayout.Normalize(Overlay.Settings);
                else _settings = _settings with
                {
                    Enabled = Overlay.Settings.Enabled,
                    Interaction = Overlay.Settings.Interaction,
                };
            }
            StateHasChanged();
        });
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed) return;
        _reference = DotNetObjectReference.Create(this);
        try { await JS.InvokeVoidAsync("grindcrestOverlayEditor.mount", "overlay-editor", _reference); }
        catch (JSException exception)
        {
            _error = "Die Mausbedienung konnte nicht geladen werden. Position und Größe können weiterhin über die Felder geändert werden. " + exception.Message;
            StateHasChanged();
        }
    }

    private async Task Change(Func<OverlaySettings, OverlaySettings> update)
    {
        if (_disposed || Overlay.LoadError is not null) return;
        var overlayId = _editingOverlayId;
        var original = _settings;
        _settings = OverlayLayout.Normalize(update(_settings));
        var revision = ++_revision;
        var draft = _settings;
        _saving = true;
        _error = null;
        await _saveGate.WaitAsync();
        try
        {
            // Moving the native window can happen while the editor is open.
            // Layout edits must retain that newly saved screen position.
            var current = Overlay.Overlays.FirstOrDefault(window => window.Id == overlayId)?.Settings;
            if (current is null)
            {
                if (revision == _revision) _error = "Das Overlay-Fenster ist nicht mehr vorhanden.";
                return;
            }
            draft = draft with
            {
                PositionX = current.PositionX, PositionY = current.PositionY,
                // Global hotkeys can fire while a layout save is queued. Keep
                // their result unless this edit explicitly changed that field.
                Enabled = draft.Enabled == original.Enabled ? current.Enabled : draft.Enabled,
                Interaction = draft.Interaction == original.Interaction ? current.Interaction : draft.Interaction,
            };
            var result = await Overlay.SaveAsync(overlayId, draft);
            if (revision == _revision && _editingOverlayId == overlayId)
            {
                _error = result.Error;
                if (result.Succeeded) _settings = OverlayLayout.Normalize(Overlay.Settings);
            }
        }
        catch (Exception exception) { if (revision == _revision) _error = "Das Overlay konnte nicht gespeichert werden: " + exception.Message; }
        finally
        {
            _saveGate.Release();
            if (revision == _revision) _saving = false;
        }
    }

    private Task ChangeNumber(ChangeEventArgs e, double minimum, double maximum, Func<OverlaySettings, double, OverlaySettings> update)
    {
        if (!TryNumber(e, minimum, maximum, out var value)) return Task.CompletedTask;
        return Change(settings => update(settings, value));
    }

    private bool TryNumber(ChangeEventArgs e, double minimum, double maximum, out double value)
    {
        if (double.TryParse(Text(e), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value) && value >= minimum && value <= maximum) return true;
        _error = $"Bitte einen Wert zwischen {Css(minimum)} und {Css(maximum)} eingeben. Die Änderung wurde nicht gespeichert.";
        return false;
    }

    private Task CanvasDimension(ChangeEventArgs e, bool width) => width
        ? ChangeNumber(e, 160, 1600, (s, value) => OverlayLayout.ResizeCanvas(s, value, s.Height))
        : ChangeNumber(e, 64, 1200, (s, value) => OverlayLayout.ResizeCanvas(s, s.Width, value));

    private Task ChangeWidget(Func<OverlayWidget, OverlayWidget> update)
    {
        var id = _selectedId;
        return Change(settings => settings with { Widgets = settings.Widgets.Select(w => w.Id == id ? update(w) : w).ToArray() });
    }

    private Task WidgetNumber(ChangeEventArgs e, string field)
    {
        if (SelectedWidget is not { } widget) return Task.CompletedTask;
        var (minimum, maximum) = field switch
        {
            "x" => (0d, 1600 - widget.Width), "y" => (0d, 1200 - widget.Height),
            "width" => (Math.Min(80, widget.Width), 1600 - widget.X), "height" => (Math.Min(40, widget.Height), 1200 - widget.Y),
            "fontScale" => (.7, 2d), "itemLimit" => (1d, 24d), "itemSize" => (32d, 112d),
            "clockOffset" => (-240d, 240d), _ => (0d, 0d),
        };
        if (!TryNumber(e, minimum, maximum, out var value)) return Task.CompletedTask;
        if (field == "itemLimit" && value != Math.Truncate(value))
        {
            _error = "Bitte eine ganze Anzahl an Items eingeben.";
            return Task.CompletedTask;
        }
        if (field == "clockOffset" && value != Math.Truncate(value))
        {
            _error = "Bitte ganze Minuten für die BDO-Zeitkorrektur eingeben.";
            return Task.CompletedTask;
        }
        return ChangeWidget(w => field switch
        {
            "x" => w with { X = value }, "y" => w with { Y = value },
            "width" => OverlayLayout.ResizeWidget(w, value, w.Height), "height" => OverlayLayout.ResizeWidget(w, w.Width, value),
            "fontScale" => w with { FontScale = value }, "itemLimit" => w with { ItemLimit = (int)value },
            "itemSize" => w with { ItemSize = value },
            "clockOffset" => w with { ClockOffsetMinutes = (int)value }, _ => w,
        });
    }

    [JSInvokable]
    public Task SelectWidget(string id)
    {
        if (_selectedId != id) _itemSearch = "";
        _selectedId = _settings.Widgets.Any(w => w.Id == id) ? id : null;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task ClearSelection()
    {
        if (_disposed || _selectedId is null) return Task.CompletedTask;
        _selectedId = null;
        _itemSearch = "";
        StateHasChanged();
        return Task.CompletedTask;
    }

    private IReadOnlyList<OverlayLootItem> ItemCatalog => PreviewSnapshot.ItemCatalog;

    private OverlayLootItem CatalogItem(string canonicalName) =>
        ItemCatalog.FirstOrDefault(item => string.Equals(item.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase))
        ?? new OverlayLootItem(canonicalName, canonicalName, "0");

    private IEnumerable<OverlayLootItem> AvailableItems(OverlayWidget widget)
    {
        var search = _itemSearch.Trim();
        return ItemCatalog.Where(item => !widget.ItemNames.Contains(item.CanonicalName, StringComparer.OrdinalIgnoreCase)
                && (search.Length == 0 || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.CanonicalName.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private Task AddItem(string canonicalName) => ChangeWidget(widget => widget.Kind != "drop-item" && widget.ItemNames.Count >= 24 ? widget : widget with
    {
        ItemFilter = "selected",
        ItemNames = widget.Kind == "drop-item" ? [canonicalName]
            : widget.ItemNames.Append(canonicalName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
    });

    private Task RemoveItem(string canonicalName) => ChangeWidget(widget => widget with
    {
        ItemNames = widget.ItemNames.Where(name => !string.Equals(name, canonicalName, StringComparison.OrdinalIgnoreCase)).ToArray(),
    });

    private Task MoveItem(string canonicalName, int direction) => ChangeWidget(widget =>
    {
        var names = widget.ItemNames.ToList();
        var index = names.FindIndex(name => string.Equals(name, canonicalName, StringComparison.OrdinalIgnoreCase));
        var target = index + direction;
        if (index < 0 || target < 0 || target >= names.Count) return widget;
        (names[index], names[target]) = (names[target], names[index]);
        return widget with { ItemNames = names.ToArray(), ItemSort = "default" };
    });

    private async Task AddModule(string kind)
    {
        if (_settings.Widgets.Count >= OverlayCatalog.MaximumWidgets || OverlayCatalog.Find(kind) is not { } definition) return;
        var width = Math.Min(definition.Width, _settings.Width - 16);
        var height = Math.Min(definition.Height, 1200 - 16);
        for (var y = 8d; y <= 1200 - height - 8; y += 8)
        for (var x = 8d; x <= _settings.Width - width - 8; x += 8)
        {
            if (_settings.Widgets.Any(w => x < w.X + w.Width + 4 && x + width + 4 > w.X && y < w.Y + w.Height + 4 && y + height + 4 > w.Y)) continue;
            var widget = OverlayCatalog.CreateWidget(kind, x, y) with { Width = width, Height = height };
            _selectedId = widget.Id;
            await Change(s => s with { Height = Math.Max(s.Height, y + height + 8), Widgets = s.Widgets.Append(widget).ToArray() });
            return;
        }
        _error = "Für dieses Modul ist kein freier Platz vorhanden. Vergrößere das Layout oder ziehe es direkt an die gewünschte Stelle.";
    }

    [JSInvokable]
    public async Task AddModuleAt(string kind, double x, double y)
    {
        if (_disposed || _settings.Widgets.Count >= OverlayCatalog.MaximumWidgets || OverlayCatalog.Find(kind) is null || !double.IsFinite(x) || !double.IsFinite(y)) return;
        var widget = OverlayCatalog.CreateWidget(kind, x, y);
        _selectedId = widget.Id;
        await Change(s => s with { Widgets = s.Widgets.Append(widget).ToArray() });
        StateHasChanged();
    }

    [JSInvokable]
    public async Task CommitWidgetGeometry(string id, double x, double y, double width, double height)
    {
        if (_disposed || !new[] { x, y, width, height }.All(double.IsFinite)) return;
        _selectedId = id;
        await ChangeWidget(w => width == w.Width && height == w.Height ? w with { X = x, Y = y } :
            OverlayLayout.ResizeWidget(w, width, height) with { X = x, Y = y });
        StateHasChanged();
    }

    [JSInvokable]
    public async Task CommitCanvasSize(double width, double height)
    {
        if (_disposed || !double.IsFinite(width) || !double.IsFinite(height)) return;
        await Change(s => OverlayLayout.ResizeCanvas(s, width, height));
        StateHasChanged();
    }

    [JSInvokable]
    public async Task CommitCanvasCorner(double width, double height, string corner)
    {
        if (_disposed || !double.IsFinite(width) || !double.IsFinite(height)) return;
        await Change(s => {
            var resized = OverlayLayout.ResizeCanvas(s, width, height);
            var west = corner is "nw" or "sw";
            var north = corner is "nw" or "ne";
            if (s.Widgets.Count > 0)
            {
                // Translate the complete layout together; clamping individual
                // origins would collapse modules onto each other when shrinking.
                if (west) resized = resized with { Width = Math.Clamp(resized.Width,
                    Math.Max(160, s.Width - s.Widgets.Min(w => w.X)),
                    Math.Min(1600, s.Width + 1600 - s.Widgets.Max(w => w.X + w.Width))) };
                if (north) resized = resized with { Height = Math.Clamp(resized.Height,
                    Math.Max(64, s.Height - s.Widgets.Min(w => w.Y)),
                    Math.Min(1200, s.Height + 1200 - s.Widgets.Max(w => w.Y + w.Height))) };
            }
            var dx = west ? resized.Width - s.Width : 0;
            var dy = north ? resized.Height - s.Height : 0;
            return resized with { Widgets = s.Widgets.Select(w => w with { X = w.X + dx, Y = w.Y + dy }).ToArray() };
        });
        StateHasChanged();
    }

    [JSInvokable]
    public async Task NudgeWidget(string id, double dx, double dy)
    {
        if (_disposed || !double.IsFinite(dx) || !double.IsFinite(dy)) return;
        _selectedId = id;
        await ChangeWidget(w => w with { X = w.X + Math.Clamp(dx, -8, 8), Y = w.Y + Math.Clamp(dy, -8, 8) });
        StateHasChanged();
    }

    private Task RemoveSelected() => RemoveWidget(_selectedId);

    private async Task RemoveWidget(string? id)
    {
        if (id is null || !_settings.Widgets.Any(widget => widget.Id == id)) return;
        if (_selectedId == id) _selectedId = null;
        await Change(s => s with { Widgets = s.Widgets.Where(w => w.Id != id).ToArray() });
    }

    private async Task ApplyPreset(string name)
    {
        if (_disposed || _saving || _confirmingLayout || _pendingLayoutChange is not null) return;
        if (_settings.Widgets.Count > 0)
        {
            await RequestLayoutConfirmation(new(name));
            return;
        }
        var preset = OverlayCatalog.Preset(name);
        _selectedId = null;
        await Change(s => name == "rotation-monitor" ? new OverlayTemplate { Layout = preset }.ApplyTo(s) : s with { Width = preset.Width, Height = preset.Height, Widgets = preset.Widgets });
    }

    private async Task ClearLayout()
    {
        if (_disposed || _saving || _confirmingLayout || _pendingLayoutChange is not null || _settings.Widgets.Count == 0) return;
        await RequestLayoutConfirmation(new(null));
    }

    private async Task RequestLayoutConfirmation(LayoutChange change)
    {
        _pendingLayoutChange = change;
        StateHasChanged();
        try { await JS.InvokeVoidAsync("grindcrest.showDialog", "overlay-layout-confirm"); }
        catch (JSException exception)
        {
            _pendingLayoutChange = null;
            _error = "Die Bestätigung konnte nicht geöffnet werden. " + exception.Message;
        }
    }

    private async Task CloseLayoutConfirmation()
    {
        if (_confirmingLayout) return;
        _pendingLayoutChange = null;
        await JS.InvokeVoidAsync("grindcrest.closeDialog", "overlay-layout-confirm");
    }

    private async Task ConfirmLayoutChange()
    {
        if (_disposed || _saving || _confirmingLayout || _pendingLayoutChange is not { } change) return;
        _confirmingLayout = true;
        try
        {
            _selectedId = null;
            _itemSearch = "";
            if (change.Template is { } template)
                await Change(template.ApplyTo);
            else if (change.Preset is { } name)
            {
                var preset = OverlayCatalog.Preset(name);
                await Change(s => name == "rotation-monitor" ? new OverlayTemplate { Layout = preset }.ApplyTo(s) : s with { Width = preset.Width, Height = preset.Height, Widgets = preset.Widgets });
            }
            else await Change(s => s with { Widgets = [] });
            if (_error is null)
            {
                _pendingLayoutChange = null;
                await JS.InvokeVoidAsync("grindcrest.closeDialog", "overlay-layout-confirm");
            }
        }
        finally { _confirmingLayout = false; }
    }

    private async Task TogglePreview()
    {
        try { await Overlay.SetPreviewAsync(!Overlay.State.Previewing); }
        catch (Exception exception) { _error = "Die Bildschirmvorschau konnte nicht geändert werden: " + exception.Message; }
    }

    private async Task ResetPosition()
    {
        try { await Overlay.ResetPositionAsync(); }
        catch (Exception exception) { _error = "Die Position konnte nicht zurückgesetzt werden: " + exception.Message; }
    }

    public async ValueTask DisposeAsync()
    {
        base.Dispose();
        _disposed = true;
        _rotationDemoTimer?.Dispose();
        Overlay.Changed -= OverlayChanged;
        try { await JS.InvokeVoidAsync("grindcrestOverlayEditor.unmount", "overlay-editor"); }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException) { }
        _reference?.Dispose();
        await _saveGate.WaitAsync();
        _saveGate.Release();
        // Preview is temporary setup, separate from the saved Enabled choice.
        foreach (var window in Overlay.Overlays)
            if (Overlay.GetState(window.Id).Previewing) await Overlay.SetPreviewAsync(window.Id, false);
        GC.SuppressFinalize(this);
    }
}
