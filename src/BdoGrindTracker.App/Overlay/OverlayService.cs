using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Overlay;

/// <summary>Overlay presentation and settings are independent of capture and its command gate.</summary>
internal sealed class OverlayService : IOverlayService
{
    private readonly ITrackerSession _tracker;
    private readonly Persistence.GrindGoalStore? _goals;
    private readonly OverlaySettingsStore? _store;
    private readonly OverlayTemplateStore? _templateStore;
    private readonly string? _templateLoadError;
    private readonly OverlayMetrics _metrics = new();
    private readonly Dictionary<string, OverlayRuntimeState> _runtime = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _saveErrors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _previewing = new(StringComparer.Ordinal);
    private OverlayCollectionSettings _collection;
    private string? _hotkeyStatus, _sharedSaveError;
    private bool _disposed;

    public OverlayService(ITrackerSession tracker, OverlaySettingsStore? store = null, OverlayTemplateStore? templateStore = null, Persistence.GrindGoalStore? goals = null)
    {
        _tracker = tracker;
        _goals = goals;
        if (_goals is not null) { _goals.Load(); _goals.Changed += TrackerChanged; }
        _store = store;
        _templateStore = templateStore;
        Templates = templateStore?.Load() ?? Array.Empty<OverlayTemplate>();
        TemplateError = _templateLoadError = templateStore?.LoadError;
        _collection = OverlaySettingsStore.NormalizeCollection(store?.LoadCollection() ?? new());
        Snapshot = _metrics.Update(tracker.State, tracker.Preferences, tracker.Prices);
        Snapshot = WithDailyGoal(Snapshot);
        tracker.Changed += TrackerChanged;
    }

    public event Action? Changed;
    public IReadOnlyList<OverlayInstance> Overlays => _collection.Overlays;
    public string SelectedOverlayId => _collection.SelectedOverlayId;
    public OverlaySettings Settings => Overlays.First(overlay => overlay.Id == SelectedOverlayId).Settings;
    public string? LoadError => _store?.LoadError;
    public OverlayHotkeySettings Hotkeys => LoadError is null ? _collection.Hotkeys! : _collection.Hotkeys! with { Enabled = false };
    public string? HotkeyStatus => _sharedSaveError ?? _hotkeyStatus;
    public OverlaySnapshot Snapshot { get; private set; }
    public IReadOnlyList<OverlayTemplate> Templates { get; private set; }
    public string? TemplateError { get; private set; }
    public OverlayRuntimeState State => GetState(SelectedOverlayId);
    public OverlayRuntimeState GetState(string id)
    {
        var runtime = _runtime.GetValueOrDefault(id) ?? new();
        var error = LoadError ?? _saveErrors.GetValueOrDefault(id);
        return runtime with
        {
            Previewing = _previewing.Contains(id),
            Status = error ?? runtime.Status,
            IsError = error is not null || runtime.IsError,
            HotkeyStatus = HotkeyStatus,
        };
    }

    public Task<OverlaySaveResult> ReloadAsync()
    {
        if (_disposed) return Failure("Das Overlay wurde bereits beendet.");
        if (_store is null) return Task.FromResult(new OverlaySaveResult());
        var loaded = _store.LoadCollection();
        if (_store.LoadError is not null)
        {
            Changed?.Invoke();
            return Failure(_store.LoadError);
        }
        _collection = OverlaySettingsStore.NormalizeCollection(loaded);
        _runtime.Clear();
        _saveErrors.Clear();
        _previewing.Clear();
        _sharedSaveError = null;
        Changed?.Invoke();
        return Task.FromResult(new OverlaySaveResult());
    }

    public Task<OverlaySaveResult> SaveAsync(OverlaySettings settings) => SaveAsync(SelectedOverlayId, settings);

    public Task<OverlaySaveResult> SaveAsync(string id, OverlaySettings settings)
    {
        if (_disposed) return Task.FromResult(new OverlaySaveResult("Das Overlay wurde bereits beendet."));
        ArgumentNullException.ThrowIfNull(settings);
        if (!Overlays.Any(overlay => overlay.Id == id)) return Failure("Das Overlay wurde nicht gefunden.");
        var normalized = Hotkeys.ApplyTo(OverlayLayout.Normalize(settings));
        return SaveCollection(_collection with { Overlays = Array.AsReadOnly(Overlays.Select(overlay =>
            overlay.Id == id ? overlay with { Settings = normalized } : overlay).ToArray()) }, id);
    }

    public Task<OverlaySaveResult> SaveHotkeysAsync(OverlayHotkeySettings hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        var normalized = OverlayHotkeySettings.Normalize(hotkeys);
        return SaveCollection(_collection with
        {
            Hotkeys = normalized,
            Overlays = Array.AsReadOnly(Overlays.Select(overlay => overlay with
            {
                Settings = normalized.ApplyTo(overlay.Settings),
            }).ToArray()),
        }, SelectedOverlayId, sharedChange: true);
    }

    public Task<OverlaySaveResult> ToggleAllOverlaysAsync()
    {
        var enabled = !Overlays.Any(overlay => overlay.Settings.Enabled) && _previewing.Count == 0;
        return SaveCollection(_collection with
        {
            Overlays = Array.AsReadOnly(Overlays.Select(overlay => overlay with
            {
                Settings = overlay.Settings with { Enabled = enabled },
            }).ToArray()),
        }, SelectedOverlayId, sharedChange: true, clearPreviews: !enabled);
    }

    public Task<OverlaySaveResult> ToggleAllInteractionAsync()
    {
        var interaction = Overlays.All(overlay => overlay.Settings.Interaction == "passthrough") ? "move" : "passthrough";
        return SaveCollection(_collection with
        {
            Overlays = Array.AsReadOnly(Overlays.Select(overlay => overlay with
            {
                Settings = overlay.Settings with { Interaction = interaction },
            }).ToArray()),
        }, SelectedOverlayId, sharedChange: true);
    }

    public Task<OverlaySaveResult> SelectOverlayAsync(string id)
    {
        if (!Overlays.Any(overlay => overlay.Id == id)) return Failure("Das Overlay wurde nicht gefunden.");
        return SaveCollection(_collection with { SelectedOverlayId = id }, SelectedOverlayId);
    }

    public Task<OverlaySaveResult> CreateOverlayAsync(string name, string? duplicateId = null)
    {
        OverlayInstance? source = null;
        if (duplicateId is not null)
        {
            source = Overlays.FirstOrDefault(overlay => overlay.Id == duplicateId);
            if (source is null) return Failure("Das zu kopierende Overlay wurde nicht gefunden.");
        }
        var nameError = ValidateName(name, null, out var normalizedName);
        if (nameError is not null) return Failure(nameError);
        var settings = Hotkeys.ApplyTo((source?.Settings ?? new OverlaySettings { Widgets = [] }) with
        {
            Enabled = false,
            Widgets = source?.Settings.Widgets.Select(widget => widget with { Id = Guid.NewGuid().ToString("N") }).ToArray() ?? [],
        });
        var created = new OverlayInstance
        {
            Id = Guid.NewGuid().ToString("N"), Name = normalizedName!, Settings = OverlayLayout.Normalize(settings),
        };
        return SaveCollection(_collection with
        {
            SelectedOverlayId = created.Id,
            Overlays = Array.AsReadOnly(Overlays.Append(created).ToArray()),
        }, SelectedOverlayId);
    }

    public Task<OverlaySaveResult> RenameOverlayAsync(string id, string name)
    {
        if (!Overlays.Any(overlay => overlay.Id == id)) return Failure("Das Overlay wurde nicht gefunden.");
        var nameError = ValidateName(name, id, out var normalizedName);
        if (nameError is not null) return Failure(nameError);
        return SaveCollection(_collection with { Overlays = Array.AsReadOnly(Overlays.Select(overlay =>
            overlay.Id == id ? overlay with { Name = normalizedName! } : overlay).ToArray()) }, id);
    }

    public Task<OverlaySaveResult> DeleteOverlayAsync(string id)
    {
        if (!Overlays.Any(overlay => overlay.Id == id)) return Failure("Das Overlay wurde nicht gefunden.");
        if (Overlays.Count <= 1) return Failure("Mindestens ein Overlay muss erhalten bleiben.");
        var next = Overlays.Where(overlay => overlay.Id != id).ToArray();
        return SaveCollection(_collection with
        {
            Overlays = Array.AsReadOnly(next),
            SelectedOverlayId = SelectedOverlayId == id ? next[0].Id : SelectedOverlayId,
        }, id);
    }

    private string? ValidateName(string name, string? excludedId, out string? normalizedName)
    {
        normalizedName = null;
        try { normalizedName = OverlayInstance.NormalizeName(name); }
        catch (ArgumentException exception) { return exception.Message; }
        var candidate = normalizedName;
        return Overlays.Any(overlay => overlay.Id != excludedId &&
            string.Equals(overlay.Name, candidate, StringComparison.OrdinalIgnoreCase))
            ? "Ein Overlay mit diesem Namen existiert bereits." : null;
    }

    private static Task<OverlaySaveResult> Failure(string message) => Task.FromResult(new OverlaySaveResult(message));

    private Task<OverlaySaveResult> SaveCollection(OverlayCollectionSettings collection, string errorId,
        bool sharedChange = false, bool clearPreviews = false)
    {
        if (_disposed) return Failure("Das Overlay wurde bereits beendet.");
        try
        {
            _store?.SaveCollection(collection);
            _collection = collection;
            if (sharedChange) _sharedSaveError = null;
            else _saveErrors.Remove(errorId);
            if (clearPreviews) _previewing.Clear();
            var retained = Overlays.Select(overlay => overlay.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in _runtime.Keys.Where(id => !retained.Contains(id)).ToArray()) _runtime.Remove(id);
            foreach (var id in _saveErrors.Keys.Where(id => !retained.Contains(id)).ToArray()) _saveErrors.Remove(id);
            _previewing.IntersectWith(retained);
            Changed?.Invoke();
            return Task.FromResult(new OverlaySaveResult());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var error = "Overlay nicht gespeichert: " + exception.Message;
            if (sharedChange) _sharedSaveError = error;
            else _saveErrors[errorId] = error;
            Changed?.Invoke();
            return Failure(error);
        }
    }

    public Task<OverlaySaveResult> SavePositionAsync(double x, double y, double? width = null, double? height = null) =>
        SavePositionAsync(SelectedOverlayId, x, y, width, height);

    public Task<OverlaySaveResult> SavePositionAsync(string id, double x, double y, double? width = null, double? height = null)
    {
        var settings = Overlays.FirstOrDefault(overlay => overlay.Id == id)?.Settings;
        return settings is null ? Failure("Das Overlay wurde nicht gefunden.") : SaveAsync(id, settings with
        {
            PositionX = x, PositionY = y,
            Width = width ?? settings.Width, Height = height ?? settings.Height,
        });
    }

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

    public Task SetPreviewAsync(bool enabled) => SetPreviewAsync(SelectedOverlayId, enabled);

    public Task SetPreviewAsync(string id, bool enabled)
    {
        if (_disposed || LoadError is not null || !Overlays.Any(overlay => overlay.Id == id)) return Task.CompletedTask;
        var changed = enabled ? _previewing.Add(id) : _previewing.Remove(id);
        if (!changed) return Task.CompletedTask;
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

    public async Task NewSessionAsync()
    {
        if (_disposed || !Snapshot.CanNewSession) return;
        var result = await _tracker.NewSessionAsync();
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
    }

    public void UpdateRuntime(OverlayRuntimeState state) => UpdateRuntime(SelectedOverlayId, state);

    public void UpdateRuntime(string id, OverlayRuntimeState state)
    {
        if (_disposed || !Overlays.Any(overlay => overlay.Id == id)) return;
        // Compatibility callers may still publish the shared registration error in runtime state.
        if (state.HotkeyStatus is not null) _hotkeyStatus = state.HotkeyStatus;
        state = state with { Previewing = _previewing.Contains(id) };
        if (_runtime.GetValueOrDefault(id) == state) return;
        _runtime[id] = state;
        Changed?.Invoke();
    }

    public void UpdateHotkeyRuntime(string? status)
    {
        if (_disposed || _hotkeyStatus == status) return;
        _hotkeyStatus = status;
        Changed?.Invoke();
    }

    private OverlaySnapshot WithDailyGoal(OverlaySnapshot snapshot)
    {
        var today = DateOnly.FromDateTime(snapshot.ClockUtcNow.LocalDateTime);
        var earned = Persistence.GrindGoalStore.DailyNet(_tracker.History).GetValueOrDefault(today);
        decimal? target = _goals?.Goals.TryGetValue(today, out var value) == true ? value : null;
        return snapshot with { DailyGoal = new(earned, target, _goals?.Error) };
    }

    private void TrackerChanged()
    {
        if (_disposed) return;
        Snapshot = _metrics.Update(_tracker.State, _tracker.Preferences, _tracker.Prices);
        Snapshot = WithDailyGoal(Snapshot);
        Changed?.Invoke();
    }

    public void RefreshClock()
    {
        if (_disposed || !Overlays.Any(overlay => overlay.Settings.Widgets.Any(widget => widget.Kind == "clock"))) return;
        var now = DateTimeOffset.UtcNow;
        if (now.ToUnixTimeSeconds() == Snapshot.ClockUtcNow.ToUnixTimeSeconds()) return;
        Snapshot = WithDailyGoal(Snapshot with { ClockUtcNow = now });
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.Changed -= TrackerChanged;
        if (_goals is not null) _goals.Changed -= TrackerChanged;
        Changed = null;
    }
}
