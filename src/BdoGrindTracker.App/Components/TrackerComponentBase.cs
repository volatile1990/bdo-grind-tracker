using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;

namespace BdoGrindTracker.App.Components;

public abstract class TrackerComponentBase : ComponentBase, IDisposable
{
    [Inject] private protected ITrackerSession Tracker { get; set; } = default!;
    private protected TrackerState State => Tracker.State;
    protected string? ActionError { get; private set; }
    protected bool Acting { get; private set; }
    private bool _disposed;

    protected override void OnInitialized() => Tracker.Changed += OnChanged;

    private void OnChanged()
    {
        if (!_disposed)
            _ = InvokeAsync(() => { if (!_disposed) StateHasChanged(); });
    }

    protected async Task<bool> Act(Func<Task> action)
    {
        if (Acting) return false;
        Acting = true;
        ActionError = null;
        try { await action(); return !State.IsError; }
        catch (Exception exception) { ActionError = exception.Message; return false; }
        finally { Acting = false; }
    }

    public virtual void Dispose()
    {
        _disposed = true;
        Tracker.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }
}
