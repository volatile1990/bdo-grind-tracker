using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Services;

/// <summary>Explicit --ui-preview mode: entirely in-memory, with no capture, disk writes or network.</summary>
internal sealed class PreviewTrackerSession : ITrackerSession
{
    private readonly List<LootHistoryEntry> _history = [];
    public event Action? Changed;
    public TrackerState State { get; private set; } = new() { AnalyzerAvailable = true, IsDemo = true };
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
    private void Change(TrackerState state) { State = state; Changed?.Invoke(); }
    public Task ToggleTrackingAsync() { Change(State with { IsRunning = !State.IsRunning, HasSession = true, Status = "Vorschau · Tracking wird nur simuliert." }); return Task.CompletedTask; }
    public Task PauseAsync() { Change(State with { IsRunning = false }); return Task.CompletedTask; }
    public Task NewSessionAsync() { Change(new() { SessionId = Guid.NewGuid(), AnalyzerAvailable = true, IsDemo = true, HasApiKey = State.HasApiKey, Status = "Vorschau · Neue Session bereit." }); return Task.CompletedTask; }
    public Task SetDemoAsync(bool enabled) { if (enabled) ShowSample(); else return NewSessionAsync(); return Task.CompletedTask; }
    public Task SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null,
        bool resumeAutomaticUpload = false)
    {
        var hasApiKey = apiKey is null ? State.HasApiKey : !string.IsNullOrWhiteSpace(apiKey);
        Preferences = preferences with { AutoUpload = preferences.AutoUpload && hasApiKey };
        Prices = LootPriceCatalog.FixedSnapshot(preferences.MarketRegion);
        Change(State with { HasApiKey = hasApiKey,
            Silver = SilverValuation.Calculate(State.Loot.Totals, Prices, Preferences.Tax), Status = "Vorschau · Einstellungen nur im Arbeitsspeicher gespeichert." });
        return Task.CompletedTask;
    }
    public Task UploadAsync() { Change(State with { Status = "Vorschau · Es wird nichts an Garmoth gesendet." }); return Task.CompletedTask; }
    public Task UploadHistoryAsync(Guid sessionId) => UploadAsync();
    public Task UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals)
    {
        var index = _history.FindIndex(entry => entry.SessionId == sessionId);
        if (index >= 0)
        {
            var values = totals.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value);
            var silver = SilverValuation.Calculate(values, Prices, Preferences.Tax);
            _history[index] = _history[index] with { Totals = values, SilverBeforeTax = silver.BeforeTax, SilverAfterTax = silver.AfterTax, SilverIsComplete = silver.IsComplete };
        }
        Changed?.Invoke(); return Task.CompletedTask;
    }
    public Task DeleteHistoryAsync(Guid sessionId) { _history.RemoveAll(entry => entry.SessionId == sessionId); Changed?.Invoke(); return Task.CompletedTask; }
    public Task RefreshPricesAsync() { Change(State with { Status = "Vorschau · Kein Netzwerkabruf." }); return Task.CompletedTask; }
    public Task TickAsync() => Task.CompletedTask;
    public Task PrepareUpdateRestartAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
