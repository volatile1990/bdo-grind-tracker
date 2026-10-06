using System.Collections;
using System.Text.Json;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class SessionSharePresentationTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-25T12:00:00Z");
    private static readonly TrackerPreferences German = new() { UiLanguage = "de" };

    [Fact]
    public void HistoryUsesSavedSilverAndRecordedBuffCostsDespiteCurrentTaxPreferences()
    {
        var history = History() with
        {
            SilverBeforeTax = 888_888_888m,
            SilverAfterTax = 123_000_000m,
            Buffs = new([new("custom", "Recorded buff", null, At, new(2_500_000m, "eu", At, false))], [], []),
        };
        var image = SessionSharePresentation.FromHistory(history, German with { ValuePack = true, MerchantRing = true, FamilyFame = 9999 });

        Assert.Equal("123,0 Mio.", Metric(image, "Silber netto").Value);
        Assert.Equal("246,0 Mio.", Metric(image, "Silber / h").Value);
        Assert.Equal("2,50 Mio. Silber", Detail(image, "Buffkosten").Value);
        Assert.Equal("Recorded buff", Assert.Single(image.Consumables).Name);
        Assert.Null(Metric(image, "Silber netto").Note);
        Assert.DoesNotContain(image.Details, detail => detail.Label is "Gespeichert am" or "Preisgrundlage");
        Assert.Null(Metric(image, "Silber / h").Note);
        Assert.Null(Detail(image, "Buffkosten").Note);
    }

    [Fact]
    public void LiveSnapshotFreezesAllPositiveLootAndContainsNoPrivateSettingsOrPaths()
    {
        var totals = new Dictionary<string, long>
        {
            ["Black Crystal Fragment"] = 12_000, ["Rare item"] = 3, ["Gone"] = 0, ["Correction"] = -1,
        };
        var state = Live() with
        {
            Loot = new(totals, 12_003, 2),
            RecordingPath = @"C:\private\recording", Status = "private status",
        };
        var preferences = German with
        {
            GameLanguage = "en",
            MonitorDeviceName = "private-monitor", CaptureConfigurationPath = @"C:\private\capture",
            BuffRecognitionProfilePath = @"C:\private\buffs",
        };
        var image = SessionSharePresentation.FromLive(state, preferences);
        var before = JsonSerializer.Serialize(image);
        totals["Black Crystal Fragment"] = 99_999;
        totals["Later drop"] = 100;

        Assert.Equal(before, JsonSerializer.Serialize(image));
        Assert.DoesNotContain("private", before, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Status\":", before, StringComparison.Ordinal);
        Assert.Equal(["Black Crystal Fragment", "Rare item"], image.Loot.Select(row => row.Name));
        Assert.Equal("12.000", image.Loot[0].Quantity);
        Assert.Equal("24.000,00", image.Loot[0].Hourly);
        Assert.Equal("6,00", image.Loot[1].Hourly);
        Assert.True(Assert.IsAssignableFrom<IList>(image.Loot).IsReadOnly);
        Assert.True(Assert.IsAssignableFrom<IList>(image.Details).IsReadOnly);
        Assert.StartsWith("assets/spot-backgrounds/", image.BackgroundUrl);
        Assert.Equal("assets/class-icons/warrior.png", image.IconUrl);
        Assert.EndsWith(".png", image.FileName);
    }

    [Fact]
    public void ShareLootShowsTrashThenRareDropsThenOtherItems()
    {
        var totals = new Dictionary<string, long>
        {
            ["Black Stone"] = 1_000,
            ["Apeiron Ring"] = 1,
            ["Black Crystal Fragment"] = 10,
            ["Nev's Fragment"] = 500,
            ["BON Wandering Origin Crystal"] = 2,
        };
        var preferences = new TrackerPreferences { UiLanguage = "en", GameLanguage = "en" };

        var live = SessionSharePresentation.FromLive(Live() with { Loot = new(totals, 1_513, 5) }, preferences);
        var history = SessionSharePresentation.FromHistory(History() with { Totals = totals }, preferences);
        string[] expected = ["Black Crystal Fragment", "BON Wandering Origin Crystal", "Apeiron Ring",
            "Black Stone", "Nev's Fragment"];

        Assert.Equal(expected, live.Loot.Select(row => row.Name));
        Assert.Equal(expected, history.Loot.Select(row => row.Name));
    }

    [Fact]
    public void ShareImageOmitsSnapshotDateAndOnlyIncludesKnownSessionStart()
    {
        var state = Live() with { ObservedAt = At, Elapsed = TimeSpan.FromHours(3) };
        var image = SessionSharePresentation.FromLive(state, German);

        Assert.DoesNotContain(image.Details, metric => metric.Label == "Momentaufnahme");
        Assert.DoesNotContain(image.Details, metric => metric.Label == "Sessionstart");
        Assert.Equal("03:00:00", Metric(image, "Dauer").Value);
        var knownStart = SessionSharePresentation.FromLive(state, German, At.AddHours(-7));
        Assert.Contains(knownStart.Details, metric => metric.Label == "Sessionstart");
    }

    [Fact]
    public void LegacyUnknownObservationsAndEntirelyMissingPricesStayUnknown()
    {
        var image = SessionSharePresentation.FromHistory(History() with
        {
            SilverAfterTax = 0, SilverIsComplete = false,
        }, German);

        Assert.Equal("—", Metric(image, "Silber netto").Value);
        Assert.Equal("—", Metric(image, "Silber / h").Value);
        Assert.Equal("—", Detail(image, "AP / DP").Value);
        Assert.Equal("—", Detail(image, "Agris aktiv").Value);
        Assert.Equal("—", Detail(image, "Erfahrung").Value);
        Assert.Equal("—", Detail(image, "Buffkosten").Value);
        Assert.All(image.Details, metric => Assert.Null(metric.Note));
        Assert.Null(Metric(image, "Silber netto").Note);
        Assert.DoesNotContain(image.Details, metric => metric.Label == "Loot-Scroll");
        var partial = SessionSharePresentation.FromHistory(History() with { SilverIsComplete = false }, German);
        Assert.Equal("≈ 123,0 Mio.", Metric(partial, "Silber netto").Value);
        Assert.Equal("≈ 246,0 Mio.", Metric(partial, "Silber / h").Value);
    }

    [Fact]
    public void ZeroDurationDoesNotInventHourlyRatesAndUnknownSpotDoesNotInventTrash()
    {
        var state = Live() with { SpotId = null, Elapsed = TimeSpan.Zero };
        var image = SessionSharePresentation.FromLive(state, German);

        Assert.Equal("—", Metric(image, "Trashloot").Value);
        Assert.Equal("—", Metric(image, "Silber / h").Value);
        Assert.All(image.Loot, row => Assert.Equal("—", row.Hourly));
        Assert.Null(image.BackgroundUrl);
        var tiny = SessionSharePresentation.FromLive(state with
        {
            Elapsed = TimeSpan.FromTicks(1), Silver = new(0, decimal.MaxValue, 1, [], [], false),
        }, German);
        Assert.Equal("—", Metric(tiny, "Silber / h").Value);
    }

    [Fact]
    public void EnglishSnapshotIncludesObservedMetadataAndOnlyCompleteSessionRotations()
    {
        var state = Live() with
        {
            CombatStats = new(2220, 830, CombatStatsCategory.Edania, At),
            AgrisActiveDuration = TimeSpan.FromMinutes(10), AgrisObservedDuration = TimeSpan.FromMinutes(30),
            ExperienceGainedPercentagePoints = .25m, ExperienceObservedDuration = TimeSpan.FromMinutes(30),
            ExperienceStartLevel = 61, ExperienceEndLevel = 62,
            Rotation = new() { SessionRotations = [new(90), new(150), new(1, Outcome: "aborted"), new(2, Outcome: "active")] },
        };
        var image = SessionSharePresentation.FromLive(state, new() { UiLanguage = "en" });

        Assert.Equal("30,000", Metric(image, "Trash loot").Value);
        Assert.Equal("2,220 / 830", Detail(image, "AP / DP").Value);
        Assert.Equal("≈ 10 min", Detail(image, "Agris active").Value);
        Assert.Equal("+0.250 %", Detail(image, "Experience").Value);
        Assert.Equal("+0.500 % / h", Detail(image, "Experience").Note);
        Assert.DoesNotContain(image.Details, detail => detail.Label is "Level" or "Snapshot" or "Price basis" or "Loot scroll");
        Assert.Equal("2", Detail(image, "Rotations").Value);
        Assert.Equal("00:02:00", Detail(image, "Avg. rotation").Value);
        Assert.Equal("00:01:30", Detail(image, "Fastest rotation").Value);
        Assert.Equal("Per hour", image.HourlyLabel);
        Assert.Equal("Count", image.QuantityLabel);
        Assert.Null(Metric(image, "Net silver").Note);
    }

    [Fact]
    public void HistoryRotationsAreRestrictedToTheSavedSpotAndExcludeFailedAttempts()
    {
        var entry = History() with
        {
            Rotations =
            [
                new(LootSpotCatalog.HermesiaId, At, new(120, [])),
                new(LootSpotCatalog.HermesiaId, At, new(1, []) { Outcome = "incomplete" }),
                new(LootSpotCatalog.AphrodonId, At, new(2, [])),
                new(LootSpotCatalog.HermesiaId, At, new(double.NaN, [])),
            ],
        };
        var image = SessionSharePresentation.FromHistory(entry, German);

        Assert.Equal("1", Detail(image, "Rotationen").Value);
        Assert.Equal("00:02:00", Detail(image, "Schnellste Rotation").Value);
    }

    [Theory]
    [InlineData("de", "en", "de")]
    [InlineData("en", "de", "en")]
    [InlineData("auto", "de", "de")]
    [InlineData("auto", "en", "en")]
    [InlineData("auto", null, "de")]
    public void LootNamesFollowTheConfiguredOrDetectedGameLanguage(string setting, string? detected, string expectedLanguage)
    {
        var image = SessionSharePresentation.FromLive(Live() with { DetectedGameLanguage = detected },
            German with { GameLanguage = setting });

        Assert.Equal(ItemLocalizationCatalog.DisplayName("Black Crystal Fragment", expectedLanguage), Assert.Single(image.Loot).Name);
    }

    [Fact]
    public void HistoryAutomaticItemLanguageFallsBackToTheSelectedUiLanguage()
    {
        var image = SessionSharePresentation.FromHistory(History(), German);

        Assert.Equal(ItemLocalizationCatalog.DisplayName("Black Crystal Fragment", "de"), Assert.Single(image.Loot).Name);
    }

    private static SessionShareMetric Metric(SessionShareImageData image, string label) =>
        Assert.Single(image.Metrics, metric => metric.Label == label);
    private static SessionShareMetric Detail(SessionShareImageData image, string label) =>
        Assert.Single(image.Details, metric => metric.Label == label);

    private static TrackerState Live() => new()
    {
        HasSession = true, IsRunning = true, SpotId = LootSpotCatalog.HermesiaId,
        CharacterClassId = "warrior-awakening", Elapsed = TimeSpan.FromMinutes(30), ObservedAt = At,
        Loot = new(new Dictionary<string, long> { ["Black Crystal Fragment"] = 30_000 }, 30_000, 1),
        Silver = new(123_000_000, 123_000_000, 1, [], [], false),
    };

    private static LootHistoryEntry History() => new()
    {
        SessionId = Guid.NewGuid(), SpotId = LootSpotCatalog.HermesiaId, StartedAt = At.AddHours(-2), UpdatedAt = At,
        Duration = TimeSpan.FromMinutes(30), CharacterClass = "Warrior · Awakening",
        Totals = new() { ["Black Crystal Fragment"] = 30_000 },
        SilverBeforeTax = 123_000_000, SilverAfterTax = 123_000_000, SilverIsComplete = true,
    };
}
