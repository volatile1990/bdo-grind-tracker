using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;

namespace BdoGrindTracker.App.Components;

public abstract class TrackerComponentBase : ComponentBase, IDisposable
{
    [Inject] private protected ITrackerSession Tracker { get; set; } = default!;
    private protected TrackerState State => Tracker.State;
    protected string ItemLabel(string canonicalName) => BdoGrindTracker.Core.ItemLocalizationCatalog.DisplayName(
        canonicalName, Tracker.Preferences.GameLanguage == "auto" ? State.DetectedGameLanguage ?? "en" : Tracker.Preferences.GameLanguage);
    protected string? ActionError { get; private set; }
    protected bool Acting { get; private set; }
    private bool _disposed;
    protected void ClearActionError() => ActionError = null;

    protected override void OnInitialized() => Tracker.Changed += OnChanged;

    private void OnChanged()
    {
        if (!_disposed)
            _ = InvokeAsync(() => { if (!_disposed) StateHasChanged(); });
    }

    private protected Task<bool> Act(Func<Task<TrackerCommandResult>> action) => Act(async () =>
    {
        var result = await action();
        if (!result.Succeeded) throw new InvalidOperationException(result.Error);
    });

    protected async Task<bool> Act(Func<Task> action)
    {
        if (Acting) return false;
        Acting = true;
        ActionError = null;
        try { await action(); return true; }
        catch (Exception exception) { ActionError = exception.Message; return false; }
        finally { Acting = false; }
    }

    private protected Task<bool> SavePreferenceChange(
        Func<TrackerPreferences, TrackerPreferences> change, string? apiKey = null, bool resumeAutomaticUpload = false) =>
        Act(async () =>
        {
            var result = await Tracker.SavePreferencesAsync(change(Tracker.Preferences), apiKey, resumeAutomaticUpload);
            if (!result.Succeeded) throw new InvalidOperationException(result.Error);
        });

    public virtual void Dispose()
    {
        _disposed = true;
        Tracker.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }
}
