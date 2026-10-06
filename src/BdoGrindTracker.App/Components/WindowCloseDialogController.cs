using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Components;

internal enum WindowCloseDialogKind { Choice, Error }

internal sealed record WindowCloseDialogState(long Id, WindowCloseDialogKind Kind,
    string? Title = null, string? Message = null, string? Error = null, bool Saving = false);

internal sealed class WindowCloseDialogController
{
    private readonly object _gate = new();
    private WindowCloseDialogState? _state;
    private TaskCompletionSource<bool?>? _completion;
    private Func<bool, Task<TrackerCommandResult>>? _saveChoice;
    private long _nextId;
    private bool _available;

    public event Action? Changed;
    public WindowCloseDialogState? State { get { lock (_gate) return _state; } }
    public bool IsAvailable { get { lock (_gate) return _available; } }

    public void Attach() { lock (_gate) _available = true; }

    public void Detach()
    {
        lock (_gate) _available = false;
        CancelPending();
    }

    public Task<bool?> AskAsync(Func<bool, Task<TrackerCommandResult>> saveChoice)
    {
        ArgumentNullException.ThrowIfNull(saveChoice);
        Task<bool?> task;
        lock (_gate)
        {
            if (!_available) return Task.FromResult<bool?>(null);
            if (_completion is not null) return _completion.Task;
            _state = new(++_nextId, WindowCloseDialogKind.Choice);
            _saveChoice = saveChoice;
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = _completion.Task;
        }
        Changed?.Invoke();
        return task;
    }

    public Task ShowErrorAsync(string title, string message, string? detail)
    {
        Task task;
        lock (_gate)
        {
            if (!_available || _completion is not null) return Task.CompletedTask;
            _state = new(++_nextId, WindowCloseDialogKind.Error, title, message, detail);
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = _completion.Task;
        }
        Changed?.Invoke();
        return task;
    }

    public async Task ChooseAsync(long id, bool closeToTray)
    {
        Func<bool, Task<TrackerCommandResult>> save;
        lock (_gate)
        {
            if (_state is not { Kind: WindowCloseDialogKind.Choice, Saving: false } state ||
                state.Id != id || _saveChoice is null) return;
            save = _saveChoice;
            _state = state with { Saving = true, Error = null };
        }
        Changed?.Invoke();
        TrackerCommandResult result;
        try { result = await save(closeToTray); }
        catch (Exception error) { result = new(error.Message); }
        TaskCompletionSource<bool?>? completion = null;
        lock (_gate)
        {
            // Shutdown or a detached renderer can cancel while the durable save is pending.
            if (_state?.Id != id) return;
            if (result.Succeeded) completion = ClearPending();
            else _state = _state with { Saving = false, Error = result.Error };
        }
        Changed?.Invoke();
        completion?.TrySetResult(closeToTray);
    }

    public void Dismiss(long id) => Complete(id, allowSaving: false);
    public void FailPresentation(long id) => Complete(id, allowSaving: true, unavailable: true);

    public void CancelPending()
    {
        TaskCompletionSource<bool?>? completion;
        lock (_gate) completion = ClearPending();
        completion?.TrySetResult(null);
        if (completion is not null) Changed?.Invoke();
    }

    private void Complete(long id, bool allowSaving, bool unavailable = false)
    {
        TaskCompletionSource<bool?>? completion;
        lock (_gate)
        {
            if (_state is null || _state.Id != id || !allowSaving && _state.Saving) return;
            if (unavailable) _available = false;
            completion = ClearPending();
        }
        Changed?.Invoke();
        completion?.TrySetResult(null);
    }

    private TaskCompletionSource<bool?>? ClearPending()
    {
        var completion = _completion;
        _state = null;
        _completion = null;
        _saveChoice = null;
        return completion;
    }
}
