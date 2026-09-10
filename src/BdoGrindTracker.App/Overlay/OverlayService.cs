using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Overlay;

/// <summary>Overlay presentation and settings are independent of capture and its command gate.</summary>
internal sealed class OverlayService : IOverlayService
{
    private readonly ITrackerSession _tracker;
    private readonly OverlaySettingsStore? _store;
    private readonly OverlayTemplateStore? _templateStore;
    private readonly string? _templateLoadError;
    private readonly OverlayMetrics _metrics = new();
    private OverlayRuntimeState _runtime = new();
    private string? _saveError;
    private bool _previewing;
    private bool _disposed;

    public OverlayService(ITrackerSession tracker, OverlaySettingsStore? store = null, OverlayTemplateStore? templateStore = null)
    {
        _tracker = tracker;
        _store = store;
        _templateStore = templateStore;
        Templates = templateStore?.Load() ?? Array.Empty<OverlayTemplate>();
        TemplateError = _templateLoadError = templateStore?.LoadError;
        Settings = store?.Load() ?? new();
        Snapshot = _metrics.Update(tracker.State, tracker.Preferences);
        tracker.Changed += TrackerChanged;
    }

    public event Action? Changed;
    public OverlaySettings Settings { get; private set; }
    public OverlaySnapshot Snapshot { get; private set; }
    public IReadOnlyList<OverlayTemplate> Templates { get; private set; }
    public string? TemplateError { get; private set; }
    public OverlayRuntimeState State => _runtime with
    {
        Previewing = _previewing,
        Status = _saveError ?? _runtime.Status,
        IsError = _saveError is not null || _runtime.IsError,
    };

    public Task<OverlaySaveResult> SaveAsync(OverlaySettings settings)
    {
        if (_disposed) return Task.FromResult(new OverlaySaveResult("Das Overlay wurde bereits beendet."));
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = OverlayLayout.Normalize(settings);
        try
        {
            _store?.Save(normalized);
            Settings = normalized;
            _saveError = null;
            Changed?.Invoke();
            return Task.FromResult(new OverlaySaveResult());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _saveError = "Overlay nicht gespeichert: " + exception.Message;
            Changed?.Invoke();
            return Task.FromResult(new OverlaySaveResult(_saveError));
        }
    }

    public Task<OverlaySaveResult> SavePositionAsync(double x, double y, double? width = null, double? height = null) =>
        SaveAsync(Settings with
        {
            PositionX = x, PositionY = y,
            Width = width ?? Settings.Width, Height = height ?? Settings.Height,
        });

    public Task<OverlaySaveResult> SaveTemplateAsync(string name, OverlaySettings layout, string? replaceId = null)
    {
        if (_disposed) return Task.FromResult(new OverlaySaveResult("Das Overlay wurde bereits beendet."));
        if (_templateLoadError is not null) return TemplateFailure(_templateLoadError);
        try
        {
            var normalizedName = OverlayTemplate.NormalizeName(name);
            var existing = replaceId is null ? null : Templates.FirstOrDefault(template => template.Id == replaceId);
            if (replaceId is not null && existing is null)
                return TemplateFailure("Die Vorlage wurde nicht gefunden. Bitte erneut auswählen.");
            if (Templates.Any(template => template.Id != replaceId && string.Equals(template.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
                return TemplateFailure("Eine Vorlage mit diesem Namen existiert bereits. Bitte einen anderen Namen wählen oder die vorhandene Vorlage ersetzen.");
            if (existing is null && Templates.Count >= OverlayTemplateStore.MaximumTemplates)
                return TemplateFailure($"Es sind höchstens {OverlayTemplateStore.MaximumTemplates} eigene Vorlagen möglich.");
            var saved = OverlayTemplate.Create(normalizedName, layout, existing?.Id);
            var next = existing is null ? Templates.Append(saved).ToArray() :
                Templates.Select(template => template.Id == existing.Id ? saved : template).ToArray();
            _templateStore?.Save(next);
            Templates = Array.AsReadOnly(next);
            TemplateError = null;
            Changed?.Invoke();
            return Task.FromResult(new OverlaySaveResult());
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return TemplateFailure("Vorlage nicht gespeichert: " + exception.Message);
        }
    }

    public Task<OverlaySaveResult> DeleteTemplateAsync(string id)
    {
        if (_disposed) return Task.FromResult(new OverlaySaveResult("Das Overlay wurde bereits beendet."));
        if (_templateLoadError is not null) return TemplateFailure(_templateLoadError);
        if (!Templates.Any(template => template.Id == id)) return TemplateFailure("Die Vorlage wurde nicht gefunden. Bitte erneut auswählen.");
        try
        {
            var next = Templates.Where(template => template.Id != id).ToArray();
            _templateStore?.Save(next);
            Templates = Array.AsReadOnly(next);
            TemplateError = null;
            Changed?.Invoke();
            return Task.FromResult(new OverlaySaveResult());
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return TemplateFailure("Vorlage nicht gelöscht: " + exception.Message);
        }
    }

    private Task<OverlaySaveResult> TemplateFailure(string error)
    {
        TemplateError = error;
        Changed?.Invoke();
        return Task.FromResult(new OverlaySaveResult(error));
    }

    public Task SetPreviewAsync(bool enabled)
    {
        if (_disposed || _previewing == enabled) return Task.CompletedTask;
        _previewing = enabled;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public async Task ResetPositionAsync() => await SavePositionAsync(.02, .15);

    public async Task ToggleTrackingAsync()
    {
        if (_disposed || !Snapshot.CanToggleTracking) return;
        var result = _tracker.State.IsRunning ? await _tracker.PauseAsync() : await _tracker.ToggleTrackingAsync();
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
    }

    public void UpdateRuntime(OverlayRuntimeState state)
    {
        if (_disposed) return;
        state = state with { Previewing = _previewing };
        if (_runtime == state) return;
        _runtime = state;
        Changed?.Invoke();
    }

    private void TrackerChanged()
    {
        if (_disposed) return;
        Snapshot = _metrics.Update(_tracker.State, _tracker.Preferences);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.Changed -= TrackerChanged;
        Changed = null;
    }
}
