using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RareDropPresentationTests
{
    private const string ItemName = "Embers of Ynix - Shoes";
    private const string SourceUrl = "https://garmoth.com/grind-tracker/best-grind-spots/215";

    [Theory]
    [InlineData("0", "0.5", "0.25")]
    [InlineData("100", "1", "0.5")]
    [InlineData("320", "2.1", "1.05")]
    [InlineData("320.5", "2.1025", "1.05125")]
    [InlineData("1000", "5.5", "2.75")]
    public void UserBonusScalesThePublicHundredPercentReference(string bonus, string hourly, string quantity)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var percent = decimal.Parse(bonus, culture);
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 0,
            TimeSpan.FromMinutes(30), LootSpotCatalog.MagaiaId, Benchmark(1m), percent, 5000));

        Assert.Equal(decimal.Parse(hourly, culture), result.ExpectedPerHour);
        Assert.Equal(decimal.Parse(quantity, culture), result.ExpectedCount);
        Assert.Equal(RareDropRelation.BelowAverage, result.Relation);
        Assert.Contains("100 % Dropratenbonus", result.Description);
        Assert.Contains($"Dein Bonus: {percent.ToString("0.##", AppText.Culture("de"))} %", result.Description);
        Assert.Contains("/ 200", result.Description);
    }

    [Fact]
    public void NoScrollReferenceIsNotScaledByTheUserBonus()
    {
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 0,
            TimeSpan.FromMinutes(30), LootSpotCatalog.MagaiaId,
            Benchmark(0.2m) with { RareDropRateScalingApplies = false }, 320m, 5000, "en"));

        Assert.Equal(0.2m, result.ExpectedPerHour);
        Assert.Equal(0.1m, result.ExpectedCount);
        Assert.Contains("Your bonus: 320%", result.Description);
        Assert.Contains("drop rate does not affect the reference", result.Description);
        Assert.DoesNotContain("/ 200", result.Description);
    }

    [Fact]
    public void RareDropProvenanceDoesNotInheritAnOlderTrashDateOrSource()
    {
        const string rareSource = SourceUrl + "?startDate=2026-10-01&endDate=2026-10-08";
        var benchmark = Benchmark(1m) with
        {
            SourceUrl = "https://garmoth.com/grind-tracker/best-grind-spots/214",
            RareDropSourceUrl = rareSource,
            RareDropUpdatedAt = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
        };
        var result = Assert.IsType<RareDropPresentation>(Create(1, TimeSpan.FromHours(1), benchmark));

        Assert.Contains("Stand 02.10.2026", result.Description);
        Assert.Contains(rareSource, result.Description);
        Assert.DoesNotContain("Stand 12.09.2026", result.Description);
    }

    [Fact]
    public void InvalidRareSourceDoesNotFallBackToAValidTrashSource()
    {
        var benchmark = Benchmark(1m) with { RareDropSourceUrl = "https://example.invalid/215" };

        Assert.Null(Create(1, TimeSpan.FromHours(1), benchmark));
    }

    [Fact]
    public void ExpectedQuantityUsesExactActiveTicksIncludingFractionalSeconds()
    {
        var elapsed = TimeSpan.FromSeconds(20 * 60 + 44.5);
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 2, elapsed,
            LootSpotCatalog.MagaiaId, Benchmark(1.25m), 100m, 5000));

        Assert.Equal(0.625m / ((decimal)elapsed.Ticks / TimeSpan.TicksPerHour), result.ExpectedPerHour);
        Assert.Equal(0.625m, result.ExpectedCount);
        Assert.Equal(RareDropRelation.AboveAverage, result.Relation);
        Assert.Equal("Über Durchschnitt · Ø 0,63 erwartet", result.Label);
        Assert.Equal("is-above", result.ToneClass);
        Assert.Contains("00:20:44,5 aktive Grindzeit (ohne Pausen)", result.Description);
        Assert.Contains("1,25 Drops / h", result.Description);
        Assert.Contains("100 %", result.Description);
        Assert.Contains("Stand 12.09.2026", result.Description);
        Assert.Contains("Quelle: " + SourceUrl, result.Description);
    }

    [Theory]
    [InlineData(2500, "0.525", "BelowAverage")]
    [InlineData(5000, "1.05", "BelowAverage")]
    [InlineData(6000, "1.26", "BelowAverage")]
    [InlineData(10000, "2.1", "BelowAverage")]
    public void OwnTrashPerformanceScalesTheBonusAdjustedExpectation(long trash, string expected, string relation)
    {
        var count = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 0,
            TimeSpan.FromMinutes(30), LootSpotCatalog.MagaiaId, Benchmark(1m), 320m, trash));

        Assert.Equal(count, result.ExpectedCount);
        Assert.Equal(count * 2m, result.ExpectedPerHour);
        Assert.Equal(Enum.Parse<RareDropRelation>(relation), result.Relation);
        Assert.Contains("10.000,00 Trash / h bei Garmoth", result.Description);
        Assert.Contains("Trash-Abgleich:", result.Description);
    }

    [Theory]
    [InlineData(4000, "AboveAverage")]
    [InlineData(10000, "Average")]
    [InlineData(12000, "BelowAverage")]
    public void TheSameRareCountIsComparedAgainstTheSessionsTrashPerformance(long trash, string relation)
    {
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 1,
            TimeSpan.FromHours(1), LootSpotCatalog.MagaiaId, Benchmark(1m), 100m, trash));

        Assert.Equal(Enum.Parse<RareDropRelation>(relation), result.Relation);
        Assert.Equal(trash / 10000m, result.ExpectedCount);
    }

    [Fact]
    public void WaitingWithoutAdditionalTrashCannotChangeExpectedDrops()
    {
        var first = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 1,
            TimeSpan.FromMinutes(30), LootSpotCatalog.MagaiaId, Benchmark(1.23456789m), 320m, 8765));
        var later = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 1,
            TimeSpan.FromMinutes(90), LootSpotCatalog.MagaiaId, Benchmark(1.23456789m), 320m, 8765));

        Assert.Equal(first.ExpectedCount, later.ExpectedCount);
        Assert.Equal(first.Relation, later.Relation);
        Assert.Equal(first.ExpectedPerHour / 3m, later.ExpectedPerHour);
    }

    [Fact]
    public void RareReferenceTrashIsUsedEvenWhenTheRatingTrashHasAnotherDateAndValue()
    {
        var benchmark = Benchmark(1m) with { AverageTrashPerHour = 20000m, RareDropReferenceTrashPerHour = 10000m };
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 1,
            TimeSpan.FromHours(1), LootSpotCatalog.MagaiaId, benchmark, 100m, 10000));

        Assert.Equal(1m, result.ExpectedCount);
        Assert.Contains("10.000,00 Trash / h bei Garmoth", result.Description);
        Assert.DoesNotContain("20.000,00", result.Description);
    }

    [Fact]
    public void NoScrollSpotsStillAdjustForOwnTrashPerformance()
    {
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 0,
            TimeSpan.FromMinutes(30), LootSpotCatalog.MagaiaId,
            Benchmark(0.2m) with { RareDropRateScalingApplies = false }, 320m, 10000));

        Assert.Equal(0.2m, result.ExpectedCount);
        Assert.Equal(0.4m, result.ExpectedPerHour);
    }

    [Theory]
    [InlineData(0, "BelowAverage", "is-below", "Unter Durchschnitt · Ø 1,00 erwartet")]
    [InlineData(1, "Average", "is-average", "Im Durchschnitt · Ø 1,00 erwartet")]
    [InlineData(2, "AboveAverage", "is-above", "Über Durchschnitt · Ø 1,00 erwartet")]
    public void ActualDropsAreComparedWithExpectedQuantity(long actual, string relation, string tone,
        string label)
    {
        var result = Assert.IsType<RareDropPresentation>(Create(actual, TimeSpan.FromHours(1), Benchmark(1m)));

        Assert.Equal(Enum.Parse<RareDropRelation>(relation), result.Relation);
        Assert.Equal(tone, result.ToneClass);
        Assert.Equal(label, result.Label);
    }

    [Theory]
    [InlineData("en", "0.76", "AboveAverage", "is-above", "Above average · Ø 0.76 expected")]
    [InlineData("en", "1.76", "BelowAverage", "is-below", "Below average · Ø 1.76 expected")]
    [InlineData("en", "1", "Average", "is-average", "At average · Ø 1.00 expected")]
    [InlineData("de", "0.76", "AboveAverage", "is-above", "Über Durchschnitt · Ø 0,76 erwartet")]
    [InlineData("de", "1.76", "BelowAverage", "is-below", "Unter Durchschnitt · Ø 1,76 erwartet")]
    [InlineData("de", "1", "Average", "is-average", "Im Durchschnitt · Ø 1,00 erwartet")]
    public void OneActualDropUsesPlainLocalizedRelationAndExactExpectedQuantity(
        string language, string expected, string relation, string tone, string label)
    {
        var count = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 1,
            TimeSpan.FromHours(1), LootSpotCatalog.MagaiaId, Benchmark(count), 100m, 10000, language));

        Assert.Equal(count, result.ExpectedCount);
        Assert.Equal(count, result.ExpectedPerHour);
        Assert.Equal(Enum.Parse<RareDropRelation>(relation), result.Relation);
        Assert.Equal(tone, result.ToneClass);
        Assert.Equal(label, result.Label);
        Assert.DoesNotContain("↑", result.Label);
        Assert.DoesNotContain("↓", result.Label);
        Assert.DoesNotContain("1 above", result.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1 below", result.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0.9999", "AboveAverage")]
    [InlineData("1.0001", "BelowAverage")]
    public void RoundedDisplayDoesNotChangeComparison(string hourly, string relation)
    {
        var baseline = decimal.Parse(hourly, System.Globalization.CultureInfo.InvariantCulture);
        var result = Assert.IsType<RareDropPresentation>(Create(1, TimeSpan.FromHours(1), Benchmark(baseline)));

        Assert.Equal(Enum.Parse<RareDropRelation>(relation), result.Relation);
        Assert.EndsWith("Ø 1,00 erwartet", result.Label);
        Assert.Equal(baseline, result.ExpectedCount);
    }

    [Fact]
    public void SmallPositiveRareRatesRemainBelowAverageWithNoActualDrops()
    {
        var result = Assert.IsType<RareDropPresentation>(Create(0, TimeSpan.FromMinutes(30), Benchmark(0.0001m)));

        Assert.Equal(0.00005m, result.ExpectedCount);
        Assert.Equal(RareDropRelation.BelowAverage, result.Relation);
        Assert.EndsWith("Ø 0,00 erwartet", result.Label);
    }

    [Theory]
    [InlineData("benchmark")]
    [InlineData("spot")]
    [InlineData("unknown-spot")]
    [InlineData("different-spot")]
    [InlineData("rates")]
    [InlineData("item")]
    [InlineData("unknown-item")]
    [InlineData("other-spot-item")]
    [InlineData("nonrare-item")]
    [InlineData("zero-rate")]
    [InlineData("negative-rate")]
    [InlineData("source")]
    [InlineData("relative-source")]
    [InlineData("file-source")]
    [InlineData("http-source")]
    [InlineData("other-source-host")]
    [InlineData("other-source-spot")]
    [InlineData("source-userinfo")]
    [InlineData("source-port")]
    [InlineData("source-fragment")]
    [InlineData("duration")]
    [InlineData("negative-duration")]
    [InlineData("negative-actual")]
    [InlineData("negative-percent")]
    [InlineData("large-percent")]
    [InlineData("zero-trash")]
    [InlineData("negative-trash")]
    [InlineData("missing-reference-trash")]
    [InlineData("zero-reference-trash")]
    [InlineData("negative-reference-trash")]
    public void IncompleteOrInvalidReferenceNeverInventsAnExpectation(string scenario)
    {
        GrindBenchmark? benchmark = Benchmark(1m);
        string? spotId = LootSpotCatalog.MagaiaId;
        var itemName = ItemName;
        var actual = 1L;
        var elapsed = TimeSpan.FromHours(1);
        var percent = 100m;
        var trash = 10000L;
        switch (scenario)
        {
            case "benchmark": benchmark = null; break;
            case "spot": spotId = null; break;
            case "unknown-spot": spotId = "unknown"; benchmark = benchmark with { SpotId = "unknown" }; break;
            case "different-spot": benchmark = benchmark with { SpotId = LootSpotCatalog.HermesiaId }; break;
            case "rates": benchmark = benchmark with { RareDropHourlyRates = null }; break;
            case "item": benchmark = benchmark with { RareDropHourlyRates = new Dictionary<string, decimal>() }; break;
            case "unknown-item": itemName = "Unknown Rare Drop"; benchmark = Benchmark(1m, itemName); break;
            case "other-spot-item": itemName = "Embers of Ynix - Armor"; benchmark = Benchmark(1m, itemName); break;
            case "nonrare-item": itemName = "Black Stone"; benchmark = Benchmark(1m, itemName); break;
            case "zero-rate": benchmark = Benchmark(0m); break;
            case "negative-rate": benchmark = Benchmark(-1m); break;
            case "source": benchmark = benchmark with { SourceUrl = "" }; break;
            case "relative-source": benchmark = benchmark with { SourceUrl = "/garmoth/215" }; break;
            case "file-source": benchmark = benchmark with { SourceUrl = "file:///C:/215.json" }; break;
            case "http-source": benchmark = benchmark with { SourceUrl = SourceUrl.Replace("https:", "http:") }; break;
            case "other-source-host": benchmark = benchmark with { SourceUrl = "https://garmoth.com.evil.invalid/grind-tracker/best-grind-spots/215" }; break;
            case "other-source-spot": benchmark = benchmark with { SourceUrl = "https://garmoth.com/grind-tracker/best-grind-spots/214" }; break;
            case "source-userinfo": benchmark = benchmark with { SourceUrl = "https://user@garmoth.com/grind-tracker/best-grind-spots/215" }; break;
            case "source-port": benchmark = benchmark with { SourceUrl = "https://garmoth.com:444/grind-tracker/best-grind-spots/215" }; break;
            case "source-fragment": benchmark = benchmark with { SourceUrl = SourceUrl + "#external" }; break;
            case "duration": elapsed = TimeSpan.Zero; break;
            case "negative-duration": elapsed = TimeSpan.FromTicks(-1); break;
            case "negative-actual": actual = -1; break;
            case "negative-percent": percent = -0.01m; break;
            case "large-percent": percent = 1000.01m; break;
            case "zero-trash": trash = 0; break;
            case "negative-trash": trash = -1; break;
            case "missing-reference-trash": benchmark = benchmark with { RareDropReferenceTrashPerHour = null }; break;
            case "zero-reference-trash": benchmark = benchmark with { RareDropReferenceTrashPerHour = 0 }; break;
            case "negative-reference-trash": benchmark = benchmark with { RareDropReferenceTrashPerHour = -1 }; break;
        }

        Assert.Null(RareDropPresentation.Create(itemName, actual, elapsed, spotId, benchmark, percent, trash));
    }

    [Theory]
    [InlineData("hourly")]
    [InlineData("quantity")]
    public void UnrepresentableExpectationIsUnavailableInsteadOfCrashing(string scenario)
    {
        var benchmark = Benchmark(scenario == "hourly" ? decimal.MaxValue : decimal.MaxValue / 100m);
        var elapsed = scenario == "hourly" ? TimeSpan.FromHours(1) : TimeSpan.MaxValue;
        var percent = scenario == "hourly" ? 1000m : 100m;

        Assert.Null(RareDropPresentation.Create(ItemName, 1, elapsed, LootSpotCatalog.MagaiaId, benchmark, percent,
            scenario == "hourly" ? 10000 : long.MaxValue));
    }

    [Theory]
    [InlineData("hourly")]
    [InlineData("quantity")]
    public void PositiveRateThatUnderflowsDecimalPrecisionDoesNotBecomeAnAverageOfZero(string scenario)
    {
        var elapsed = scenario == "hourly" ? TimeSpan.FromHours(1) : TimeSpan.FromTicks(1);
        var percent = scenario == "hourly" ? 0m : 100m;
        var benchmark = Benchmark(0.0000000000000000000000000001m);

        Assert.Null(RareDropPresentation.Create(ItemName, 0, elapsed, LootSpotCatalog.MagaiaId, benchmark, percent,
            scenario == "hourly" ? 10000 : 1));
    }

    [Theory]
    [InlineData("de", "Unter Durchschnitt · Ø 1,25 erwartet", "Stand 12.09.2026", "00:30:00 aktive Grindzeit")]
    [InlineData("en", "Below average · Ø 1.25 expected", "updated 9/12/2026", "00:30:00 active grind time")]
    [InlineData(null, "Below average · Ø 1.25 expected", "updated 9/12/2026", "00:30:00 active grind time")]
    [InlineData("unsupported", "Below average · Ø 1.25 expected", "updated 9/12/2026", "00:30:00 active grind time")]
    public void LabelsNumbersAndSourceDetailsUseSelectedLanguage(string? language, string label, string date,
        string duration)
    {
        var result = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(ItemName, 1,
            TimeSpan.FromMinutes(30), LootSpotCatalog.MagaiaId, Benchmark(2.5m), 100m, 5000, language));

        Assert.Equal(label, result.Label);
        Assert.Contains(date, result.Description);
        Assert.Contains(duration, result.Description);
        Assert.Contains(SourceUrl, result.Description);
    }

    [Theory]
    [InlineData("Droprate", "Drop rate")]
    [InlineData("Droprate für den Rare-Drop-Vergleich", "Drop rate for the rare drop comparison")]
    [InlineData("Garmoth Ø nicht verfügbar", "Garmoth average unavailable")]
    [InlineData("Vergleich ab erstem Drop", "Comparison from the first drop")]
    [InlineData("Vergleich ab erstem Trash-Drop", "Comparison from the first trash drop")]
    public void ComparisonControlsHaveEnglishResources(string key, string english)
    {
        Assert.Equal(english, AppText.Translate(key, "en"));
        Assert.Equal(key, AppText.Translate(key, "de"));
    }

    private static RareDropPresentation? Create(long actual, TimeSpan elapsed, GrindBenchmark? benchmark) =>
        RareDropPresentation.Create(ItemName, actual, elapsed, LootSpotCatalog.MagaiaId, benchmark, 100m,
            (long)(Presentation.Hours(elapsed) * 10000m));

    private static GrindBenchmark Benchmark(decimal hourly, string itemName = ItemName) =>
        new(LootSpotCatalog.MagaiaId, 12803m, null, null,
            new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), SourceUrl, "Loot-Scroll Lv.2 · ohne Agris")
        {
            RareDropHourlyRates = new Dictionary<string, decimal>(StringComparer.Ordinal) { [itemName] = hourly },
            RareDropReferenceTrashPerHour = 10000m,
        };
}
