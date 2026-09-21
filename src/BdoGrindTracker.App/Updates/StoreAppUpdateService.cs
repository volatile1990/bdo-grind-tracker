namespace BdoGrindTracker.App.Updates;

/// <summary>Keeps Store operations serialized and installation behind a durable session save.</summary>
internal sealed class StoreAppUpdateService(
    IStoreUpdateBackend backend,
    Func<bool> sessionBlocked,
    Func<Func<Task>, Task<bool>> requestInstall,
    string installedVersion) : IAppUpdates
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private volatile UpdateState _state = new(true, installedVersion, null, UpdatePhase.Idle, 0,
        "Noch nicht geprüft.") { UsesStore = true };
    public UpdateState State => _state;
    public event Action? Changed;

    public async Task CheckAsync()
    {
        if (!await _operation.WaitAsync(0)) return;
        try
        {
            if (State.Phase is UpdatePhase.ReadyToRestart or UpdatePhase.Installed) return;
            Publish(ResetProgress(State) with { Phase = UpdatePhase.Checking, DownloadPercent = 0, Message = "Suche nach neuen Versionen …" });
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
            Publish(ResetProgress(State) with { Phase = UpdatePhase.Downloading, DownloadPercent = 0,
                IsDownloadIndeterminate = true, OperationStartedAt = DateTimeOffset.UtcNow,
                Message = "Microsoft Store bereitet das Update vor …" });
            try
            {
                var result = await TransferAsync(install: false);
                Publish(ResetProgress(State) with { Phase = result == StoreUpdateResult.Completed ? UpdatePhase.ReadyToRestart : UpdatePhase.Available,
                    DownloadPercent = result == StoreUpdateResult.Completed ? 100 : 0,
                    Message = result == StoreUpdateResult.Completed ? "Update bereit. Pausiere deine Session und wähle Update installieren." : FailureMessage(result) });
            }
            catch (Exception)
            {
                Publish(ResetProgress(State) with { Phase = UpdatePhase.Available, DownloadPercent = 0,
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
            Publish(ResetProgress(State) with { Phase = UpdatePhase.Restarting, DownloadPercent = 0,
                IsDownloadIndeterminate = true, OperationStartedAt = DateTimeOffset.UtcNow,
                Message = "Session wird gespeichert …" });
            try
            {
                StoreUpdateResult? result = null;
                var started = false;
                var accepted = await requestInstall(async () =>
                {
                    if (started) return;
                    started = true;
                    Publish(State with { Message = "Microsoft Store bereitet das Update vor …" });
                    result = await TransferAsync(install: true);
                });
                Publish(ResetProgress(State) with { Phase = accepted && result == StoreUpdateResult.Completed ? UpdatePhase.Installed : retryPhase,
                    DownloadPercent = accepted && result == StoreUpdateResult.Completed ? 100 : retryPercent,
                    Message = accepted && result == StoreUpdateResult.Completed
                        ? "Die Installation ist abgeschlossen. Falls Grindcrest nicht automatisch neu startet, öffne die App erneut."
                        : result is null ? "Die Installation wurde nicht gestartet. Du kannst das Update erneut starten." : FailureMessage(result.Value) });
            }
            catch (Exception)
            {
                Publish(ResetProgress(State) with { Phase = retryPhase, DownloadPercent = retryPercent,
                    Message = "Speichern oder Installation fehlgeschlagen. Grindcrest bleibt geöffnet. Bitte erneut versuchen." });
            }
        }
        finally { _operation.Release(); }
    }

    private async Task<StoreUpdateResult> TransferAsync(bool install)
    {
        var progressGate = new object();
        var active = true;
        void Report(StoreUpdateProgress progress)
        {
            // Store callbacks can arrive from another thread. Ignore callbacks
            // after this operation finishes, including during a later retry.
            lock (progressGate)
            {
                if (!active) return;
                var percent = Math.Clamp(progress.DownloadPercent, 0, 100);
                Publish(State with
                {
                    DownloadPercent = percent,
                    DownloadedBytes = progress.DownloadedBytes,
                    TotalDownloadBytes = progress.TotalDownloadBytes,
                    IsDownloadIndeterminate = progress.Stage != StoreUpdateStage.Downloading ||
                        (percent == 0 && progress.TotalDownloadBytes is not > 0),
                    Message = progress.Stage switch
                    {
                        StoreUpdateStage.Downloading => "Update wird heruntergeladen …",
                        StoreUpdateStage.Installing => "Update wird installiert. Windows kann Grindcrest gleich neu starten …",
                        StoreUpdateStage.Completed => "Update wird abgeschlossen …",
                        _ => "Microsoft Store bereitet das Update vor …"
                    }
                });
            }
        }

        try
        {
            return install ? await backend.InstallAsync(Report) : await backend.DownloadAsync(Report);
        }
        finally
        {
            lock (progressGate) active = false;
        }
    }

    private static UpdateState ResetProgress(UpdateState state) => state with
    {
        DownloadedBytes = null, TotalDownloadBytes = null,
        IsDownloadIndeterminate = false, OperationStartedAt = null
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
