using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly BuffMonitor _buffMonitor;
    private readonly BuffLedger _buffLedger = new(BuffPriceCatalog.HistoryDefinitions.Concat(AutomaticBuffCatalog.Default.HistoricalGroupDefinitions));
    private DateTimeOffset? _lastBuffObservation;
    private long _buffObservationGeneration;
    private bool _hasBuffObservation;
    private bool _buffHasUnknownObservations;
    private bool _buffCompletionPending;
    private DateTimeOffset? _buffCompletionCutoff;

    private string BuffStatus => !_uiRunning ? "Buff-Erkennung pausiert."
        : _sessionClock.IsWaitingForFirstDrop ? "Buff-Erkennung wartet auf den ersten Drop."
        : _buffHasUnknownObservations ? "Einige Buffs nicht eindeutig erkannt; bestätigte Buffs werden weiter erfasst."
        : _buffLedger.Snapshot.Active.Count > 0 ? "Buffs bestätigt · Prüfung alle 10 Sekunden."
        : _buffMonitor.LastDiagnostic is { Length: > 0 } diagnostic ? diagnostic
        : "Noch keine unterstützten Buffs bestätigt. Leiste und Restzeiten müssen sichtbar sein.";

    private bool CanObserveBuffSession()
    {
        if (_hasSession && !_demoMode && _uiRunning && _sessionClock.IsRunning &&
            !_sessionClock.IsWaitingForFirstDrop && _lastCaptureDesktopRegion is { } region)
        {
            try { return _captureSession.UsesWindowCapture ? _captureSession.IsRunning : _isLootScrollCaptureVisible(region); }
            catch (Exception) { /* Optional recognition never blocks loot tracking. */ }
        }
        return false;
    }

    private void BeginBuffCompletion()
    {
        if (_buffCompletionPending) return;
        _buffCompletionCutoff = CanObserveBuffSession() ? _captureSession.ObservationTime : null;
        // Publishing the paused clock must not reset an in-flight final HUD read.
        _buffCompletionPending = true;
    }

    private async Task CompleteBuffAnalysisAsync()
    {
        try
        {
            if (_buffCompletionCutoff is not { } cutoff) return;
            await _buffMonitor.CurrentAnalysis.WaitAsync(BuffMonitor.AnalysisTimeout);
            // Read only evidence captured before the pause request. The drain's
            // wall time never advances either the ledger or the session clock.
            UpdateBuffSession(cutoff);
        }
        catch (Exception) { /* Optional analysis cannot prevent pausing or shutdown. */ }
        finally
        {
            _buffLedger.BreakContinuity();
            _buffMonitor.Reset();
            _lastBuffObservation = null;
            _buffCompletionCutoff = null;
            _buffCompletionPending = false;
        }
    }

    private void UpdateBuffSession(DateTimeOffset? completionCutoff = null)
    {
        if (_buffCompletionPending && completionCutoff is null) return;
        var visible = completionCutoff is not null || CanObserveBuffSession();
        var snapshot = _buffMonitor.Snapshot(_captureSession.ObservationTime, out var generation);
        if (generation != _buffObservationGeneration)
        {
            _buffLedger.BreakContinuity();
            _lastBuffObservation = null;
            _buffObservationGeneration = generation;
        }
        if (!visible || !snapshot.IsKnown)
        {
            _buffHasUnknownObservations = false;
            _buffLedger.BreakContinuity();
            if (!visible) _buffMonitor.Reset();
            return;
        }
        if (snapshot.ObservedAt is not { } at || at == _lastBuffObservation ||
            completionCutoff is { } cutoff && at > cutoff) return;
        _lastBuffObservation = at;
        _buffHasUnknownObservations = snapshot.UnknownBuffIds.Count > 0;
        _buffLedger.Apply(snapshot.Observations, at, definition =>
            BuffPriceCatalog.GetPrice(definition, Prices), snapshot.UnknownBuffIds);
        _hasBuffObservation = true;
    }

}
