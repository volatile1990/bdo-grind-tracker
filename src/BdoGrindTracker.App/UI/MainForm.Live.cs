using System.Security.Cryptography;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core;
using Grindcrest.Live;

namespace BdoGrindTracker.App.UI;

internal sealed partial class MainForm
{
    private readonly LiveCredentialStore _liveCredentialStore = new();
    private readonly BdoButton _liveOptionsButton = new();
    private string _liveStatus = "Live-Freigabe aus";
    private LiveSessionPublisher? _livePublisher;
    private bool _liveOptionsOpen;

    private void InitializeLiveSharing()
    {
        SetLiveStatus("Live-Freigabe aus");
        if (!_settings.LiveSharingEnabled || !LiveEndpoint.TryParse(_settings.LiveApiUrl, out var endpoint)) return;
        try
        {
            var token = _liveCredentialStore.Load(endpoint);
            if (!LiveEndpoint.IsValidToken(token))
            {
                SetLiveStatus("Live-Freigabe · Schreibschlüssel fehlt");
                return;
            }
            _livePublisher = new LiveSessionPublisher(endpoint, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        { SetLiveStatus("Live-Freigabe · Schreibschlüssel erneut hinterlegen"); }
    }

    private void PublishLiveSession()
    {
        if (_shutdownStarted || IsDisposed || _livePublisher is null) return;
        _livePublisher.Offer(CreateLiveSnapshot());
        SetLiveStatus("Live-Freigabe · " + _livePublisher.Status);
    }

    internal LiveSessionUpdate? CreateLiveSnapshot()
    {
        if (!_hasSession || _sessionStartedAt is null || _sessionSubmitted) return null;
        var silver = SilverValuation.Calculate(_sessionSummary.Totals, _prices, _settings.GetSilverTaxOptions());
        return new LiveSessionUpdate
        {
            SessionId = _sessionId,
            DisplayName = _settings.LiveDisplayName,
            SpotName = _sessionSpotId is { } spotId ? LootSpotCatalog.GetRequired(spotId).DisplayName : null,
            CharacterClass = (_sessionClass ?? SelectedCharacterClass)?.DisplayName,
            Region = _settings.MarketRegion.ToUpperInvariant(),
            StartedAt = _sessionStartedAt.Value,
            ActiveSeconds = (long)_sessionClock.Elapsed.TotalSeconds,
            Paused = !_uiRunning,
            SilverAfterTax = silver.HasKnownValue ? silver.AfterTax : null,
            SilverIsPartial = !silver.IsComplete,
            PricesAreStale = silver.IsStale,
            Loot = new(_sessionSummary.Totals.Where(item => item.Value > 0)),
        };
    }

    private void SetLiveStatus(string status)
    {
        _liveStatus = status;
        _liveOptionsButton.AccessibleDescription = status;
        _priceDetails.SetToolTip(_liveOptionsButton, status);
        _liveOptionsButton.Text = !_settings.LiveSharingEnabled ? "Live-Freigabe"
            : _livePublisher is null ? "Live · prüfen"
            : _livePublisher.Status.Contains("verbunden", StringComparison.Ordinal) ? "Live · aktiv"
            : _livePublisher.Status.Contains("pausiert", StringComparison.Ordinal) ? "Live · Pause"
            : _livePublisher.Status.StartsWith("Wartet", StringComparison.Ordinal) ? "Live · bereit" : "Live · prüfen";
    }

    private async void LiveOptionsButton_Click(object? sender, EventArgs e)
    {
        if (_liveOptionsOpen || _shutdownStarted) return;
        _liveOptionsOpen = true;
        try
        {
            var token = "";
            if (LiveEndpoint.TryParse(_settings.LiveApiUrl, out var currentEndpoint))
            {
                try { token = _liveCredentialStore.Load(currentEndpoint); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException) { }
            }
            using var dialog = new LiveSessionOptionsDialog(_settings.LiveSharingEnabled,
                _settings.LiveApiUrl, _settings.LiveDisplayName, token, _liveStatus);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await StopLiveSharingAsync();
            if (_shutdownStarted || IsDisposed) return;
            LiveEndpoint.TryParse(dialog.Endpoint, out var endpoint);
            _liveCredentialStore.Save(endpoint, dialog.Token);
            _settings.LiveApiUrl = dialog.Endpoint;
            _settings.LiveDisplayName = dialog.DisplayName;
            _settings.LiveSharingEnabled = dialog.SharingEnabled;
            _settingsStore.Save(_settings);
            InitializeLiveSharing();
            PublishLiveSession();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _settings.LiveSharingEnabled = false;
            SetLiveStatus("Live-Freigabe aus · Einstellungen konnten nicht gespeichert werden");
        }
        finally { _liveOptionsOpen = false; }
    }

    private async Task StopLiveSharingAsync()
    {
        var publisher = _livePublisher;
        _livePublisher = null;
        if (publisher is null) return;
        try { await publisher.StopAsync(); }
        finally { publisher.Dispose(); }
    }
}
