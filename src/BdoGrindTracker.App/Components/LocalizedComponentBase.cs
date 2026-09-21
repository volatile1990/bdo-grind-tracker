using System.Globalization;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using Microsoft.AspNetCore.Components;

namespace BdoGrindTracker.App.Components;

public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    // Small presentation components can also be rendered without a tracker (e.g.
    // exported previews); in that case the default UI language applies.
    [Inject] private IServiceProvider LocalizationServices { get; set; } = default!;
    private ITrackerSession? _localizationTracker;
    private string? _lastLanguage;
    private bool _localizationDisposed;
    protected virtual string UiLanguage => _localizationTracker?.Preferences.UiLanguage ?? AppText.DefaultLanguage;
    protected CultureInfo UiCulture => AppText.Culture(UiLanguage);
    protected string T(string? source) => AppText.Translate(source, UiLanguage);
    protected string F(string source, params object?[] arguments) => AppText.Format(source, UiLanguage, arguments);
    protected string Number(decimal value) => Presentation.Number(value, UiLanguage);
    protected string Silver(decimal value) => Presentation.Silver(value, UiLanguage);
    protected string ShortDuration(TimeSpan value) => Presentation.ShortDuration(value, UiLanguage);
    protected string CompactDuration(TimeSpan value) => Presentation.CompactDuration(value, UiLanguage);
    protected string SpotName(string? id) => Presentation.SpotName(id, UiLanguage);
    private protected string SpotGuidanceSummary(LootSpotPresentation profile) => Presentation.SpotGuidanceSummary(profile, UiLanguage);
    protected string Kind(string name) => Presentation.Kind(name, UiLanguage);
    protected string Trait(string trait) => Presentation.Trait(trait, UiLanguage);

    protected override void OnInitialized()
    {
        _localizationTracker = LocalizationServices.GetService(typeof(ITrackerSession)) as ITrackerSession;
        _lastLanguage = _localizationTracker?.Preferences.UiLanguage;
        if (_localizationTracker is not null) _localizationTracker.Changed += LanguageChanged;
    }

    private void LanguageChanged()
    {
        var language = _localizationTracker?.Preferences.UiLanguage;
        if (_lastLanguage == language) return;
        _lastLanguage = language;
        if (!_localizationDisposed)
            _ = InvokeAsync(() => { if (!_localizationDisposed) StateHasChanged(); });
    }

    public virtual void Dispose()
    {
        _localizationDisposed = true;
        if (_localizationTracker is not null) _localizationTracker.Changed -= LanguageChanged;
        GC.SuppressFinalize(this);
    }
}
