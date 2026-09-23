using Microsoft.AspNetCore.Components;

namespace BdoGrindTracker.App.Tests;

internal sealed class OverlayTestNavigation : NavigationManager
{
    public string? LastPath { get; private set; }
    public OverlayTestNavigation() => Initialize("http://localhost/", "http://localhost/overlay");
    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Uri = ToAbsoluteUri(uri).AbsoluteUri;
        LastPath = ToAbsoluteUri(uri).PathAndQuery;
        NotifyLocationChanged(false);
    }
    protected override void NavigateToCore(string uri, NavigationOptions options) => NavigateToCore(uri, options.ForceLoad);
}
