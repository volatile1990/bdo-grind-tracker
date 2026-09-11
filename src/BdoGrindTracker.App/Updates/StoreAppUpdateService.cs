namespace BdoGrindTracker.App.Updates;

/// <summary>Keeps Store operations serialized and installation behind a durable session save.</summary>
internal sealed class StoreAppUpdateService(
    IStoreUpdateBackend backend,
    Func<bool> sessionBlocked,
    Func<Func<Task>, Task<bool>> requestInstall,
    string installedVersion) : IAppUpdates
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private volatile UpdateState _state = new(true, false, installedVersion, null, UpdatePhase.Idle, 0,
        "Noch nicht geprüft.") { UsesStore = true };
    public UpdateState State => _state;
    public event Action? Changed;

    public async Task CheckAsync()
    {
        if (!await _operation.WaitAsync(0)) return;
        try
        {
            if (State.Phase is UpdatePhase.ReadyToRestart or UpdatePhase.Installed) return;
            Publish(State with { Phase = UpdatePhase.Checking, DownloadPercent = 0, Message = "Suche nach neuen Versionen …" });
            try
            {
                var available = await backend.CheckAsync();
                Publish(State with { Phase = available ? UpdatePhase.Available : UpdatePhase.Idle,
                    Message = available ? "Eine neue Version von Grindcrest ist verfügbar." : "Grindcrest ist auf dem neuesten für dich verfügbaren Stand." });
            }
            catch (Exception)
            {
                Publish(State with { Phase = UpdatePhase.Error,
                    Message = "Updates können gerade nicht geprüft werden. Bitte Verbindung prüfen und später erneut versuchen." });
            }
        }
        finally { _operation.Release(); }
    }

    public async Task DownloadAsync()
    {
        if (!await _operation.WaitAsync(0)) return;
        try
        {
            if (State.Phase != UpdatePhase.Available) return;
            Publish(State with { Phase = UpdatePhase.Downloading, DownloadPercent = 0, Message = "Update wird heruntergeladen …" });
            try
            {
                var result = await backend.DownloadAsync(ProgressFor(UpdatePhase.Downloading));
                Publish(State with { Phase = result == StoreUpdateResult.Completed ? UpdatePhase.ReadyToRestart : UpdatePhase.Available,
                    DownloadPercent = result == StoreUpdateResult.Completed ? 100 : 0,
                    Message = result == StoreUpdateResult.Completed ? "Update bereit. Pausiere deine Session und wähle Update installieren." : FailureMessage(result) });
            }
            catch (Exception)
            {
                Publish(State with { Phase = UpdatePhase.Available, DownloadPercent = 0,
                    Message = "Das Update konnte nicht heruntergeladen werden. Bitte erneut versuchen." });
            }
        }
        finally { _operation.Release(); }
    }

    public async Task RequestRestartAsync()
    {
        if (!await _operation.WaitAsync(0)) return;
        try
        {
            if (State.Phase is not (UpdatePhase.Available or UpdatePhase.ReadyToRestart)) return;
            // Store can download and install in one operation. Retain whether a
            // package was already staged so cancellation never claims a download.
            var retryPhase = State.Phase;
            var retryPercent = retryPhase == UpdatePhase.ReadyToRestart ? 100 : 0;
            if (sessionBlocked())
            {
                Publish(State with { Message = "Bitte zuerst die Session pausieren und laufende Vorgänge abwarten." });
                return;
            }
            Publish(State with { Phase = UpdatePhase.Restarting, DownloadPercent = 0,
                Message = "Session wird gespeichert. Anschließend wird das Update heruntergeladen und installiert …" });
            try
            {
                StoreUpdateResult? result = null;
                var started = false;
                var accepted = await requestInstall(async () =>
                {
                    if (started) return;
                    started = true;
                    result = await backend.InstallAsync(ProgressFor(UpdatePhase.Restarting));
                });
                Publish(State with { Phase = accepted && result == StoreUpdateResult.Completed ? UpdatePhase.Installed : retryPhase,
                    DownloadPercent = accepted && result == StoreUpdateResult.Completed ? 100 : retryPercent,
                    Message = accepted && result == StoreUpdateResult.Completed
                        ? "Die Installation ist abgeschlossen. Falls Grindcrest nicht automatisch neu startet, öffne die App erneut."
                        : result is null ? "Die Installation wurde nicht gestartet. Du kannst das Update erneut starten." : FailureMessage(result.Value) });
            }
            catch (Exception)
            {
                Publish(State with { Phase = retryPhase, DownloadPercent = retryPercent,
                    Message = "Speichern oder Installation fehlgeschlagen. Grindcrest bleibt geöffnet. Bitte erneut versuchen." });
            }
        }
        finally { _operation.Release(); }
    }

    // Microsoft controls Store distribution; never read or write GitHub channel preferences.
    public Task SetBetaAsync(bool enabled) => Task.CompletedTask;

    private Action<int> ProgressFor(UpdatePhase phase) => percent =>
    {
        if (State.Phase == phase) Publish(State with { DownloadPercent = Math.Clamp(percent, 0, 100) });
    };

    private static string FailureMessage(StoreUpdateResult result) => result switch
    {
        StoreUpdateResult.Canceled => "Update abgebrochen. Du kannst es später erneut starten.",
        StoreUpdateResult.LowBattery => "Bitte das Gerät mit Strom versorgen und das Update erneut starten.",
        StoreUpdateResult.NetworkRequired => "Bitte eine geeignete Internetverbindung herstellen und das Update erneut starten.",
        _ => "Das Update konnte nicht abgeschlossen werden. Bitte später erneut versuchen."
    };

    private void Publish(UpdateState next)
    {
        _state = next;
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception) { /* Closing UI must not turn an update into an error. */ }
        }
    }
}
