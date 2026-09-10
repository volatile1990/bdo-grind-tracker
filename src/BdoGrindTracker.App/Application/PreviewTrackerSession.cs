using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Services;

/// <summary>Explicit --ui-preview mode: entirely in-memory, with no capture, disk writes or network.</summary>
internal sealed class PreviewTrackerSession : ITrackerSession
{
    private readonly List<LootHistoryEntry> _history = [];
    private readonly SessionSilverHistory _silverHistory = new();
    public event Action? Changed;
    public TrackerState State { get; private set; } = new() { AnalyzerAvailable = true, IsDemo = true,
        DetectedGameLanguage = "en", GameLanguageStatus = "Vorschau: Englisch · keine BDO-Konfiguration gelesen" };
    public TrackerPreferences Preferences { get; private set; } = new() { MonitorDeviceName = "preview", ValuePack = true };
    public IReadOnlyList<TrackerMonitor> Monitors { get; } =
        [new("preview", "Bildschirm 1 · 3840 × 2160 · Hauptbildschirm", new(0, 0, 3840, 2160), true),
         new("preview-secondary", "Bildschirm 2 · 2560 × 1440", new(3840, 0, 2560, 1440), false)];
    public IReadOnlyList<LootHistoryEntry> History => _history.ToArray();
    public LootPriceSnapshot Prices { get; private set; } = LootPriceCatalog.FixedSnapshot("eu");

    public PreviewTrackerSession(bool empty = false)
    {
        if (empty) { Change(State with { Status = "Vorschau · Änderungen bleiben ausschließlich im Arbeitsspeicher." }); return; }
        foreach (var profile in LootSpotPresentationCatalog.Profiles)
        {
            for (var i = 0; i < 3; i++)
            {
                var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
                {
                    [profile.TrashItemName] = 18_420 + i * 630,
                    ["Ancient Spirit Dust"] = 76 + i * 9,
                    ["Black Stone"] = 51 + i * 11,
                    ["Nev's Fragment"] = 8 + i
                };
                var value = SilverValuation.Calculate(totals, Prices, Preferences.Tax);
                _history.Add(new()
                {
                    SessionId = Guid.NewGuid(), SpotId = profile.SpotId, CharacterClass = "Warrior · Awakening",
                    StartedAt = DateTimeOffset.Now.AddDays(-i).AddHours(-2), UpdatedAt = DateTimeOffset.Now.AddDays(-i).AddHours(-1),
                    Duration = TimeSpan.FromMinutes(60 + i * 15), Totals = totals,
                    SilverBeforeTax = value.BeforeTax, SilverAfterTax = value.AfterTax, SilverIsComplete = value.IsComplete
                });
            }
        }
        ShowSample();
    }

    private void ShowSample()
    {
        var totals = new Dictionary<string, long> { ["Branch of Abundance"] = 18_420, ["Ancient Spirit Dust"] = 76,
            ["Black Stone"] = 51, ["WON Origin Shard"] = 4, ["WON Wandering Origin Crystal"] = 1,
            ["Broken Vestige of Goldroot"] = 1, ["Nev's Fragment"] = 9 };
        Change(new()
        {
            SessionId = Guid.NewGuid(), AnalyzerAvailable = true, IsDemo = true, HasApiKey = State.HasApiKey,
            SpotId = LootSpotCatalog.AphrodonId,
            CharacterLabel = "Warrior · Awakening", CharacterClassId = "warrior-awakening", Elapsed = TimeSpan.FromMinutes(60),
            Loot = new(totals, totals.Values.Sum(), 147), Silver = SilverValuation.Calculate(totals, Prices, Preferences.Tax),
            Status = "Vorschau · Beispieldaten werden weder aufgezeichnet noch hochgeladen.", PriceStatus = "EU · NPC- und Festwerte"
        });
    }
    private void Change(TrackerState state)
    {
        State = state with { CanPause = state.IsRunning, DetectedGameLanguage = "en",
            GameLanguageStatus = "Vorschau: Englisch · keine BDO-Konfiguration gelesen" };
        State = State with { SilverHistory = _silverHistory.Update(State) };
        Changed?.Invoke();
    }
    public Task<TrackerCommandResult> ToggleTrackingAsync() { Change(State with { IsRunning = !State.IsRunning, HasSession = true, Status = "Vorschau · Tracking wird nur simuliert." }); return Task.FromResult(TrackerCommandResult.Success); }
    public Task<TrackerCommandResult> PauseAsync() { Change(State with { IsRunning = false }); return Task.FromResult(TrackerCommandResult.Success); }
    public Task<TrackerCommandResult> InstallOcrLanguageAsync() { Change(State with { OcrInstallationStatus = "Vorschau · Es wird kein Windows-Sprachpaket installiert." }); return Task.FromResult(TrackerCommandResult.Success); }
    public Task<TrackerCommandResult> RecheckOcrLanguageAsync() { Change(State with { OcrInstallationStatus = "Vorschau · Windows-Sprachpakete werden nicht geprüft." }); return Task.FromResult(TrackerCommandResult.Success); }
    public Task<TrackerCommandResult> NewSessionAsync() { Change(new() { SessionId = Guid.NewGuid(), AnalyzerAvailable = true, IsDemo = true, HasApiKey = State.HasApiKey, Status = "Vorschau · Neue Session bereit." }); return Task.FromResult(TrackerCommandResult.Success); }
    public Task<TrackerCommandResult> SetDemoAsync(bool enabled) { if (enabled) ShowSample(); else return NewSessionAsync(); return Task.FromResult(TrackerCommandResult.Success); }
    public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null,
        bool resumeAutomaticUpload = false)
    {
        var hasApiKey = apiKey is null ? State.HasApiKey : !string.IsNullOrWhiteSpace(apiKey);
        Preferences = preferences with { AutoUpload = preferences.AutoUpload && hasApiKey };
        Prices = LootPriceCatalog.FixedSnapshot(preferences.MarketRegion);
        Change(State with { HasApiKey = hasApiKey,
            Silver = SilverValuation.Calculate(State.Loot.Totals, Prices, Preferences.Tax), Status = "Vorschau · Einstellungen nur im Arbeitsspeicher gespeichert." });
        return Task.FromResult(new PreferenceSaveResult());
    }
    public Task<TrackerCommandResult> UploadAsync() { Change(State with { Status = "Vorschau · Es wird nichts an Garmoth gesendet." }); return Task.FromResult(TrackerCommandResult.Success); }
    public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => UploadAsync();
    public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals, string? characterClass = null)
    {
        var index = _history.FindIndex(entry => entry.SessionId == sessionId);
        if (index >= 0)
        {
            var values = totals.Where(p => p.Value > 0 || p.Value == 0 && _history[index].Totals.ContainsKey(p.Key))
                .ToDictionary(p => p.Key, p => p.Value);
            var silver = SilverValuation.Calculate(values, Prices, Preferences.Tax);
            _history[index] = _history[index] with { CharacterClass = characterClass is null ? _history[index].CharacterClass : string.IsNullOrWhiteSpace(characterClass) ? null : characterClass.Trim(), Totals = values, SilverBeforeTax = silver.BeforeTax, SilverAfterTax = silver.AfterTax, SilverIsComplete = silver.IsComplete };
        }
        Changed?.Invoke(); return Task.FromResult(TrackerCommandResult.Success);
    }
    public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        var current = sessionId == State.SessionId;
        var entry = _history.FirstOrDefault(entry => entry.SessionId == sessionId);
        var source = current ? State.Loot.Totals : entry?.Totals
            ?? throw new InvalidOperationException("Diese Session ist nicht mehr verfügbar.");
        var correction = checked(quantity - originalQuantity);
        var totals = new Dictionary<string, long>(source, StringComparer.OrdinalIgnoreCase)
        {
            [itemName] = Math.Max(0, checked(source.GetValueOrDefault(itemName) + correction)),
        };
        var totalQuantity = totals.Values.Sum();
        if (!current) return UpdateHistoryLootAsync(sessionId, totals);
        Change(State with
        {
            Loot = new(totals, totalQuantity, State.Loot.ConfirmedEventCount),
            ManualLootItems = Array.AsReadOnly(State.ManualLootItems.Append(itemName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
            Silver = SilverValuation.Calculate(totals, Prices, Preferences.Tax),
            Status = "Vorschau · Lootmenge nur im Arbeitsspeicher korrigiert.",
        });
        return Task.FromResult(TrackerCommandResult.Success);
    }
    public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) { _history.RemoveAll(entry => entry.SessionId == sessionId); Changed?.Invoke(); return Task.FromResult(TrackerCommandResult.Success); }
    public Task RefreshPricesAsync() { Change(State with { Status = "Vorschau · Kein Netzwerkabruf." }); return Task.CompletedTask; }
    public Task TickAsync() => Task.CompletedTask;
    public Task PrepareUpdateRestartAsync() => Task.CompletedTask;
    public Task RunPreparedUpdateAsync(Func<Task> install) => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
