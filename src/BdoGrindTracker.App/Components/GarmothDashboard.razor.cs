using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using static BdoGrindTracker.App.Components.Presentation;

namespace BdoGrindTracker.App.Components;

public partial class GarmothDashboard
{
    private const int PageSize = 8;
    private string _apiKey = "", _search = "", _statusFilter = "all";
    private bool _uploadFailed, _confirmCurrent, _preparing, _batchUploading, _disposed;
    private string? _saveFeedback, _uploadFeedback, _dialogError;
    private int _page = 1, _batchSkipped, _batchCompleted, _batchTotal;
    private Guid? _uploadingSession;
    private GarmothUploadPreview? _confirmedPreview;
    private UploadTarget? _singleTarget;
    private IReadOnlyList<UploadTarget> _batchTargets = [];

    private sealed record UploadTarget(Guid SessionId, bool IsCurrent, GarmothUploadPreview Preview);
    private sealed record UploadRow(Guid SessionId, bool IsCurrent, string? SpotId, DateTimeOffset? StartedAt,
        string? CharacterClass, TimeSpan Duration, SilverValuationResult Valuation, GarmothUploadPreview Preview,
        bool Blocked, bool Uploaded, bool LocallyModified);

    private bool InteractionBlocked => Acting || _preparing || _batchUploading || State.IsBusy;
    private bool UploadsAvailable => !State.IsDemo && !State.IsBusy && State.HasApiKey && State.PersistenceError is null;
    private void FormChanged() => _saveFeedback = null;
    private void ResetPage() => _page = 1;

    private IReadOnlyList<UploadRow> SessionRows
    {
        get
        {
            var rows = new List<UploadRow>();
            if (State.HasSession)
            {
                var saved = Tracker.History.FirstOrDefault(entry => entry.SessionId == State.SessionId);
                rows.Add(new(State.SessionId, true, State.SpotId, saved?.StartedAt ?? State.CurrentGarmothUpload.Draft?.StartedAt,
                    State.CharacterLabel, State.Elapsed, State.Silver, State.CurrentGarmothUpload,
                    State.UploadBlocked || State.IsSubmitted, saved?.GarmothUploadedAt is not null,
                    saved?.GarmothLocallyModified ?? false));
            }
            rows.AddRange(Tracker.History.Where(entry => !State.HasSession || entry.SessionId != State.SessionId)
                .Select(entry => new UploadRow(entry.SessionId, false, entry.SpotId, entry.StartedAt, entry.CharacterClass,
                    entry.Duration, SilverValuation.Calculate(entry.Totals, Tracker.Prices, Tracker.Preferences.Tax),
                    State.PersistenceError is { } error ? GarmothUploadPreview.Unavailable(error)
                        : GarmothUploadPreview.ForHistory(entry, Tracker.Prices, Tracker.Preferences.Tax),
                    entry.GarmothUploadBlocked, entry.GarmothUploadedAt is not null, entry.GarmothLocallyModified)));
            return rows;
        }
    }

    private IEnumerable<UploadRow> FilterRows(IEnumerable<UploadRow> rows) => rows
        .Where(row => SpotName(row.SpotId).Contains(_search, StringComparison.OrdinalIgnoreCase) && (_statusFilter switch
        {
            "pending" => !row.Blocked && (!row.Uploaded || row.IsCurrent),
            "transferred" => row.Uploaded,
            "blocked" => row.Blocked && !row.Uploaded,
            _ => true
        })).OrderByDescending(row => row.IsCurrent && !State.IsSubmitted).ThenByDescending(row => row.StartedAt);

    private static bool Eligible(UploadRow row) => !row.Blocked && row.Preview.IsReady && (!row.Uploaded || row.IsCurrent);
    private bool CanUpload(UploadRow row) => !InteractionBlocked && UploadsAvailable && Eligible(row);
    private bool CanUploadAll => !InteractionBlocked && UploadsAvailable && SessionRows.Any(Eligible);
    private static UploadTarget Freeze(UploadRow row) => new(row.SessionId, row.IsCurrent, row.Preview);
    private bool Matches(UploadTarget target, IEnumerable<UploadRow> rows) =>
        rows.FirstOrDefault(row => row.SessionId == target.SessionId && row.IsCurrent == target.IsCurrent) is { } row
        && Eligible(row) && (!target.IsCurrent || !State.IsRunning) && target.Preview.HasSameSessionData(row.Preview);
    private bool CanConfirmUpload => !InteractionBlocked && UploadsAvailable && _singleTarget is { } target
        && Matches(target, SessionRows);
    private bool CanConfirmAllUploads => !InteractionBlocked && UploadsAvailable && _batchTargets.Count > 0
        && BatchMatches();

    private bool BatchMatches()
    {
        var rows = SessionRows;
        return _batchTargets.All(target => Matches(target, rows));
    }

    private string RowStatus(UploadRow row) => row.Uploaded
        ? row.IsCurrent && !State.IsSubmitted && !State.UploadBlocked ? "Teilweise hochgeladen" : "Hochgeladen"
        : row.Blocked ? "Upload gesperrt" : "Noch offen";
    private string UploadHint(UploadRow row) => State.IsDemo ? "Demo-Sessions werden nicht hochgeladen."
        : !State.HasApiKey ? "Zuerst einen Garmoth-API-Schlüssel hinterlegen."
        : InteractionBlocked ? "Ein Vorgang wird gerade ausgeführt."
        : State.PersistenceError ?? row.Preview.Error ?? (row.IsCurrent ? "Rest hochladen und Session abschließen" : "Session hochladen");

    private Task SaveKey() => string.IsNullOrWhiteSpace(_apiKey) ? Task.CompletedTask
        : SaveConnection(p => p, _apiKey.Trim(), resume: true);
    private Task RemoveKey() => SaveConnection(p => p with { AutoUpload = false }, "");
    private Task ResumeAutomatic() => SaveConnection(p => p, resume: true);
    private Task AutoUploadChanged(ChangeEventArgs args)
    {
        var enabled = args.Value is true;
        if (enabled && !State.HasApiKey)
        {
            _saveFeedback = "Hinterlege zuerst einen API-Schlüssel.";
            return Task.CompletedTask;
        }
        return SaveConnection(p => p with { AutoUpload = enabled }, resume: enabled);
    }

    private async Task SaveConnection(Func<TrackerPreferences, TrackerPreferences> change, string? key = null, bool resume = false)
    {
        if (InteractionBlocked) return;
        _saveFeedback = null;
        if (await SavePreferenceChange(change, key, resume))
        {
            if (key is not null) _apiKey = "";
        }
        else _saveFeedback = ActionError ?? "Die Änderung wurde nicht gespeichert.";
    }

    private async Task PauseForPreview()
    {
        if (!State.IsRunning) return;
        var result = await Tracker.PauseAsync();
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
    }

    private async Task AskUpload(UploadRow row)
    {
        if (!CanUpload(row)) return;
        _preparing = true;
        _uploadFeedback = null;
        try
        {
            await Tracker.RefreshPricesAsync();
            if (row.IsCurrent) await PauseForPreview();
            var fresh = SessionRows.FirstOrDefault(candidate => candidate.SessionId == row.SessionId && candidate.IsCurrent == row.IsCurrent);
            if (!UploadsAvailable || fresh is null || !Eligible(fresh))
                throw new InvalidOperationException(fresh?.Preview.Error ?? "Diese Session ist derzeit nicht uploadfähig.");
            _singleTarget = Freeze(fresh);
            _confirmedPreview = fresh.Preview;
            _confirmCurrent = fresh.IsCurrent;
            _dialogError = null;
            if (!_disposed) await JS.InvokeVoidAsync("grindcrest.showDialog", "garmoth-upload-confirm");
        }
        catch (Exception exception) { _uploadFailed = true; _uploadFeedback = exception.Message; }
        finally { _preparing = false; }
    }

    private async Task AskAllUploads()
    {
        if (!CanUploadAll) return;
        _preparing = true;
        _uploadFeedback = null;
        _batchTargets = [];
        try
        {
            await Tracker.RefreshPricesAsync();
            if (SessionRows.FirstOrDefault(row => row.IsCurrent) is { } current && Eligible(current))
                await PauseForPreview();
            var rows = SessionRows;
            if (!UploadsAvailable) throw new InvalidOperationException("Uploads sind derzeit nicht verfügbar.");
            _batchTargets = rows.Where(Eligible).OrderByDescending(row => row.IsCurrent).ThenBy(row => row.StartedAt)
                .Select(Freeze).ToArray();
            if (_batchTargets.Count == 0) throw new InvalidOperationException("Keine uploadfähigen Sessions vorhanden.");
            _batchSkipped = rows.Count - _batchTargets.Count;
            _dialogError = null;
            if (!_disposed) await JS.InvokeVoidAsync("grindcrest.showDialog", "garmoth-all-upload-confirm");
        }
        catch (Exception exception) { _batchTargets = []; _uploadFailed = true; _uploadFeedback = exception.Message; }
        finally { _preparing = false; }
    }

    private async Task CloseUploadDialog()
    {
        _singleTarget = null;
        _confirmedPreview = null;
        _dialogError = null;
        await JS.InvokeVoidAsync("grindcrest.closeDialog", "garmoth-upload-confirm");
    }

    private async Task CloseAllUploadsDialog()
    {
        _batchTargets = [];
        _dialogError = null;
        await JS.InvokeVoidAsync("grindcrest.closeDialog", "garmoth-all-upload-confirm");
    }

    private async Task ConfirmUpload()
    {
        if (_singleTarget is null || InteractionBlocked) return;
        if (!CanConfirmUpload) { _dialogError = "Die Session hat sich geändert. Bitte öffne die Vorschau erneut."; return; }
        var target = _singleTarget;
        _singleTarget = null; // Consume confirmation before the first await.
        _uploadingSession = target.SessionId;
        try
        {
            var succeeded = await Act(async () =>
            {
                await CloseUploadDialog();
                var result = await Tracker.UploadConfirmedAsync(target.Preview);
                if (!result.Succeeded) throw new InvalidOperationException(result.Error);
            });
            _uploadFailed = !succeeded;
            _uploadFeedback = succeeded ? "Session hochgeladen." : ActionError ?? "Upload fehlgeschlagen.";
        }
        finally { _uploadingSession = null; }
    }

    private async Task ConfirmAllUploads()
    {
        if (_batchTargets.Count == 0 || InteractionBlocked) return;
        if (!CanConfirmAllUploads) { _dialogError = "Die Sessions haben sich geändert. Bitte öffne die Vorschau erneut."; return; }
        var targets = _batchTargets;
        _batchTargets = []; // A double click or an old dialog callback cannot dispatch again.
        _batchUploading = true;
        _batchCompleted = 0;
        _batchTotal = targets.Count;
        _uploadFeedback = null;
        try
        {
            var succeeded = await Act(async () =>
            {
                await CloseAllUploadsDialog();
                foreach (var target in targets)
                {
                    if (!UploadsAvailable || !Matches(target, SessionRows))
                        throw new InvalidOperationException("Die nächste Session hat sich geändert oder ist nicht mehr uploadfähig. Bitte öffne die Vorschau erneut.");
                    _uploadingSession = target.SessionId;
                    if (!_disposed) StateHasChanged();
                    var result = await Tracker.UploadConfirmedAsync(target.Preview);
                    if (!result.Succeeded) throw new InvalidOperationException(result.Error);
                    _batchCompleted++;
                }
            });
            _uploadFailed = !succeeded;
            _uploadFeedback = succeeded ? $"{_batchCompleted} Sessions hochgeladen."
                : $"{_batchCompleted} von {_batchTotal} Sessions erfolgreich hochgeladen. Vorgang gestoppt: {ActionError ?? "Upload fehlgeschlagen."}";
        }
        finally { _uploadingSession = null; _batchUploading = false; }
    }

    public override void Dispose()
    {
        _disposed = true;
        _apiKey = "";
        base.Dispose();
    }
}
