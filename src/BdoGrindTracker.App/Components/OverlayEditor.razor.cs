using System.Globalization;
using BdoGrindTracker.App.Overlay;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Components;

public partial class OverlayEditor
{
    private OverlaySettings _settings = new();
    private string? _selectedId, _error;
    private string _itemSearch = "";
    private bool _demo = true, _saving, _disposed;
    private ElementReference _viewport;
    private DotNetObjectReference<OverlayEditor>? _reference;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private long _revision;
    private static IReadOnlyList<OverlayWidgetDefinition> Modules => OverlayCatalog.Widgets;
    private OverlayWidget? SelectedWidget => _settings.Widgets.FirstOrDefault(w => w.Id == _selectedId);
    private OverlaySnapshot PreviewSnapshot => _demo ? OverlaySnapshot.Demo : Overlay.Snapshot;
    private string StageStyle => $"width:{Css(_settings.Width)}px;height:{Css(_settings.Height)}px;--overlay-opacity:{Css(_settings.BackgroundOpacity)};background:rgba(17,23,30,{Css(_settings.BackgroundOpacity)})";
    private static string WidgetStyle(OverlayWidget widget) => $"left:{Css(widget.X)}px;top:{Css(widget.Y)}px;width:{Css(widget.Width)}px;height:{Css(widget.Height)}px";
    private static string Css(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Percent(double value) => value.ToString("P0", CultureInfo.GetCultureInfo("de-DE"));
    private static string Text(ChangeEventArgs e) => e.Value?.ToString() ?? "";
    private static bool Checked(ChangeEventArgs e) => e.Value is true;
    private static string Label(string kind) => OverlayCatalog.Find(kind)?.Label ?? "Modul";
    private string InteractionLabel => _settings.Interaction switch { "passthrough" => "Klicks gehen ans Spiel", "locked" => "Position gesperrt", _ => "Verschiebbar" };
    private string VisibilityLabel => _settings.Visibility switch { "session" => "Während der Session", "always" => "Immer sichtbar", _ => "Im Spiel sichtbar" };
    private string InteractionHint => _settings.Interaction switch
    {
        "passthrough" => "Das Overlay reagiert nicht auf die Maus. Spiele auch durch das Overlay hindurch.",
        "locked" => "Die Position bleibt fest. Tracking-Buttons lassen sich weiterhin anklicken.",
        _ => "Im Spiel den Overlay-Hintergrund anklicken und ziehen. Die Position wird automatisch gespeichert.",
    };

    protected override void OnInitialized()
    {
        _settings = OverlayLayout.Normalize(Overlay.Settings);
        Overlay.Changed += OverlayChanged;
    }

    private void OverlayChanged()
    {
        if (!_disposed) _ = InvokeAsync(() =>
        {
            if (_disposed) return;
            // Native resize or a global interaction shortcut may update the
            // same settings while the editor is visible.
            if (!_saving && _error is null) _settings = OverlayLayout.Normalize(Overlay.Settings);
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
        if (_disposed) return;
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
            draft = draft with { PositionX = Overlay.Settings.PositionX, PositionY = Overlay.Settings.PositionY };
            var result = await Overlay.SaveAsync(draft);
            if (revision == _revision)
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
        ? ChangeNumber(e, 160, 1600, (s, value) => s with { Width = value })
        : ChangeNumber(e, 64, 1200, (s, value) => s with { Height = value });

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
            "x" => (0d, _settings.Width - widget.Width), "y" => (0d, _settings.Height - widget.Height),
            "width" => (80d, _settings.Width - widget.X), "height" => (40d, _settings.Height - widget.Y),
            "fontScale" => (.7, 2d), "itemLimit" => (1d, 24d), "itemSize" => (32d, 112d), _ => (0d, 0d),
        };
        if (!TryNumber(e, minimum, maximum, out var value)) return Task.CompletedTask;
        if (field == "itemLimit" && value != Math.Truncate(value))
        {
            _error = "Bitte eine ganze Anzahl an Items eingeben.";
            return Task.CompletedTask;
        }
        return ChangeWidget(w => field switch
        {
            "x" => w with { X = value }, "y" => w with { Y = value },
            "width" => w with { Width = value }, "height" => w with { Height = value },
            "fontScale" => w with { FontScale = value }, "itemLimit" => w with { ItemLimit = (int)value },
            "itemSize" => w with { ItemSize = value }, _ => w,
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
        await ChangeWidget(w => w with { X = x, Y = y, Width = width, Height = height });
        StateHasChanged();
    }

    [JSInvokable]
    public async Task CommitCanvasSize(double width, double height)
    {
        if (_disposed || !double.IsFinite(width) || !double.IsFinite(height)) return;
        await Change(s => s with { Width = width, Height = height });
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
        if (_settings.Widgets.Count > 0 && !await JS.InvokeAsync<bool>("confirm", "Aktuelles Layout durch die Vorlage ersetzen? Anzeige- und Verhaltenseinstellungen bleiben erhalten.")) return;
        var preset = OverlayCatalog.Preset(name);
        _selectedId = null;
        await Change(s => s with { Width = preset.Width, Height = preset.Height, Widgets = preset.Widgets });
    }

    private async Task ClearLayout()
    {
        if (_settings.Widgets.Count > 0 && !await JS.InvokeAsync<bool>("confirm", "Alle Module aus dem Overlay entfernen?")) return;
        _selectedId = null;
        await Change(s => s with { Widgets = [] });
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
        _disposed = true;
        Overlay.Changed -= OverlayChanged;
        try { await JS.InvokeVoidAsync("grindcrestOverlayEditor.unmount", "overlay-editor"); }
        catch (Exception exception) when (exception is JSException or TaskCanceledException or InvalidOperationException) { }
        _reference?.Dispose();
        await _saveGate.WaitAsync();
        _saveGate.Release();
        // Preview is temporary setup, separate from the saved Enabled choice.
        if (Overlay.State.Previewing) await Overlay.SetPreviewAsync(false);
        GC.SuppressFinalize(this);
    }
}
