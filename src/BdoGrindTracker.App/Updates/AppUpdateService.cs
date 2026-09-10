using System.Reflection;

namespace BdoGrindTracker.App.Updates;

/// <summary>Serializes release selection, downloads and explicit restarts independently of tracking.</summary>
internal sealed class AppUpdateService : IAppUpdates
{
    internal const string DefaultRepositoryUrl = "https://github.com/volatile1990/bdo-grind-tracker";
    private readonly Func<bool, IUpdateBackend> _createBackend;
    private readonly UpdatePreferencesStore _store;
    private readonly Func<bool> _isSessionRunning;
    private readonly Func<bool> _isSessionBusy;
    private readonly Func<Action, Task<bool>> _requestRestart;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private IUpdateBackend _backend;
    private UpdatePreferences _preferences;
    private UpdateCandidate? _candidate;
    private bool _downloaded;
    private volatile UpdateState _state;

    public UpdateState State => _state;
    public event Action? Changed;

    internal static string ApplicationVersion => typeof(AppUpdateService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppUpdateService).Assembly.GetName().Version?.ToString() ?? "unbekannt";

    public static IAppUpdates Create(bool enabled, Func<bool> isSessionRunning, Func<bool> isSessionBusy,
        Func<Action, Task<bool>> requestRestart, Func<IAppUpdates>? createStoreUpdates = null)
        => AppUpdateRuntime.Current.CreateUpdates(enabled,
            () => CreateUnpackaged(isSessionRunning, isSessionBusy, requestRestart), createStoreUpdates);

    private static IAppUpdates CreateUnpackaged(Func<bool> isSessionRunning, Func<bool> isSessionBusy,
        Func<Action, Task<bool>> requestRestart)
    {
        try
        {
            var repository = GetRepositoryUrl();
            var installed = new VelopackUpdateBackend(repository);
            if (!installed.IsInstalled)
                return new DisabledAppUpdates(ApplicationVersion, "Automatische Updates sind nach Installation über Grindcrest-Setup.exe verfügbar.");
            return new AppUpdateService(beta => new VelopackUpdateBackend(repository, beta),
                UpdatePreferencesStore.CreateDefault(), isSessionRunning, isSessionBusy, requestRestart, installed.IsBetaChannel);
        }
        catch (Exception)
        {
            // A missing or damaged updater must never prevent the tracker from opening.
            return new DisabledAppUpdates(ApplicationVersion, "Die Updatefunktion konnte nicht gestartet werden. Grindcrest kann weiter verwendet werden.");
        }
    }

    internal AppUpdateService(Func<bool, IUpdateBackend> createBackend, UpdatePreferencesStore store,
        Func<bool> isSessionRunning, Func<bool> isSessionBusy, Func<Action, Task<bool>> requestRestart,
        bool defaultBeta = false)
    {
        _createBackend = createBackend;
        _store = store;
        _isSessionRunning = isSessionRunning;
        _isSessionBusy = isSessionBusy;
        _requestRestart = requestRestart;
        _preferences = store.Load(defaultBeta);
        _backend = createBackend(_preferences.IsBeta);
        _state = NewState();
        RestorePending();
    }

    public async Task CheckAsync()
    {
        if (!State.Enabled || !await _operation.WaitAsync(0)) return;
        try
        {
            if (!_downloaded) await CheckCoreAsync();
        }
        finally { _operation.Release(); }
    }

    public async Task DownloadAsync()
    {
        if (!State.Enabled || !await _operation.WaitAsync(0)) return;
        try
        {
            if (_candidate is null || _downloaded) return;
            var selected = _candidate;
            Publish(State with { Phase = UpdatePhase.Downloading, DownloadPercent = 0, Message = "Update wird heruntergeladen …" });
            try
            {
                await _backend.DownloadAsync(selected, percent =>
                {
                    if (State.Phase == UpdatePhase.Downloading && ReferenceEquals(selected, _candidate))
                        Publish(State with { DownloadPercent = Math.Clamp(percent, 0, 100) });
                });
                // A durable marker ties the verified package to the selected channel.
                // A leftover beta package must not become ready after returning to stable.
                var next = _preferences with { PendingVersion = selected.Version, PendingIsBeta = _preferences.IsBeta };
                _store.Save(next);
                _preferences = next;
                _downloaded = true;
                Publish(State with { Phase = UpdatePhase.ReadyToRestart, DownloadPercent = 100, Message = "Update bereit. Zum Installieren die Session pausieren und Grindcrest neu starten." });
            }
            catch (Exception)
            {
                _downloaded = false;
                Publish(State with { Phase = UpdatePhase.Error, DownloadPercent = 0, Message = "Das Update konnte nicht vollständig heruntergeladen und gespeichert werden. Bitte erneut versuchen." });
            }
        }
        finally { _operation.Release(); }
    }

    public async Task SetBetaAsync(bool enabled)
    {
        if (!State.Enabled || !await _operation.WaitAsync(0)) return;
        try
        {
            if (_preferences.IsBeta == enabled) return;
            try
            {
                var nextBackend = _createBackend(enabled);
                var nextPreferences = new UpdatePreferences(enabled);
                _store.Save(nextPreferences);
                _backend = nextBackend;
                _preferences = nextPreferences;
                _candidate = null;
                _downloaded = false;
                Publish(NewState());
                await CheckCoreAsync();
            }
            catch (Exception)
            {
                Publish(State with { Message = "Der Updatekanal konnte nicht gespeichert werden. Bitte erneut versuchen." });
            }
        }
        finally { _operation.Release(); }
    }

    public async Task RequestRestartAsync()
    {
        if (!State.Enabled || !await _operation.WaitAsync(0)) return;
        try
        {
            if (!_downloaded || _candidate is null || State.Phase != UpdatePhase.ReadyToRestart) return;
            if (_isSessionRunning() || _isSessionBusy())
            {
                Publish(State with { Message = "Bitte zuerst die Session pausieren und laufende Vorgänge abwarten." });
                return;
            }
            Publish(State with { Phase = UpdatePhase.Restarting, Message = "Session wird gespeichert. Grindcrest startet anschließend neu …" });
            try
            {
                var scheduled = false;
                var accepted = await _requestRestart(() =>
                {
                    if (scheduled) return;
                    _backend.ScheduleApply(_candidate);
                    scheduled = true;
                });
                if (!accepted || !scheduled)
                    Publish(State with { Phase = UpdatePhase.ReadyToRestart, Message = "Der Neustart wurde nicht ausgeführt. Das Update bleibt bereit." });
            }
            catch (Exception)
            {
                Publish(State with { Phase = UpdatePhase.ReadyToRestart, Message = "Das Update konnte nicht gestartet werden. Bitte Grindcrest später erneut öffnen und nochmals versuchen." });
            }
        }
        finally { _operation.Release(); }
    }

    private async Task CheckCoreAsync()
    {
        _candidate = null;
        Publish(State with { Phase = UpdatePhase.Checking, AvailableVersion = null, DownloadPercent = 0, Message = "Suche nach neuen Versionen …" });
        try
        {
            var candidate = await _backend.CheckAsync();
            // Also reject prerelease versions in an accidentally mislabelled stable feed.
            _candidate = candidate is not null && (_preferences.IsBeta || !candidate.IsPrerelease) ? candidate : null;
            Publish(State with
            {
                Phase = _candidate is null ? UpdatePhase.Idle : UpdatePhase.Available,
                AvailableVersion = _candidate?.Version,
                Message = _candidate is null
                    ? "Keine neuere Version in diesem Kanal verfügbar."
                    : $"Version {_candidate.Version} ist verfügbar."
            });
        }
        catch (Exception)
        {
            Publish(State with { Phase = UpdatePhase.Error, Message = "Updates können gerade nicht geprüft werden. Bitte Verbindung prüfen oder später erneut versuchen." });
        }
    }

    private void RestorePending()
    {
        if (!State.Enabled) return;
        var pending = _backend.PendingUpdate;
        if (pending is null || pending.Version != _preferences.PendingVersion ||
            _preferences.PendingIsBeta != _preferences.IsBeta || (!_preferences.IsBeta && pending.IsPrerelease)) return;
        _candidate = pending;
        _downloaded = true;
        _state = State with
        {
            Phase = UpdatePhase.ReadyToRestart, AvailableVersion = pending.Version, DownloadPercent = 100,
            Message = "Ein heruntergeladenes Update ist bereit. Zum Installieren die Session pausieren und Grindcrest neu starten."
        };
    }

    private UpdateState NewState() => new(_backend.IsInstalled, _preferences.IsBeta, _backend.InstalledVersion,
        null, _backend.IsInstalled ? UpdatePhase.Idle : UpdatePhase.Disabled, 0,
        _backend.IsInstalled ? "Noch nicht geprüft." : "Updates sind nur in der installierten App verfügbar.");

    private void Publish(UpdateState next)
    {
        _state = next;
        var handlers = Changed;
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            // A UI which is closing must not turn a successful download into a failure.
            try { handler(); }
            catch (Exception) { }
        }
    }

    internal static string GetRepositoryUrl()
    {
        var configured = typeof(AppUpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "UpdateRepositoryUrl")?.Value;
        var url = string.IsNullOrWhiteSpace(configured) ? DefaultRepositoryUrl : configured.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2)
            throw new InvalidOperationException("Das öffentliche GitHub-Repository für Updates ist ungültig.");
        return url;
    }
}
