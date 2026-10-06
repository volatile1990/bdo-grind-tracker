using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class RareDropComparisonUiTests
{
    private const string RareItem = "Apeiron Earring";
    private const string TrashItem = "Black Crystal Fragment";
    private const string SourceUrl = "https://garmoth.com/grind-tracker/best-grind-spots/214";

    [Theory]
    [InlineData("de", "Droprate", "Unter Durchschnitt · Ø 1,05 erwartet", "2,10 Drops / h", "ohne Pausen")]
    [InlineData("en", "Drop rate", "Below average · Ø 1.05 expected", "2.10 drops / h", "excluding pauses")]
    public async Task LiveSessionShowsEditableDefaultAndLocalizedActiveTimeComparison(
        string language, string inputLabel, string expectedLabel, string hourlyReference, string pauseExplanation)
    {
        var session = new Session(language);
        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            var input = Input(markup());
            Assert.Contains("type=\"number\"", input);
            Assert.Contains("value=\"320\"", input);
            Assert.Contains("aria-label=\"" + inputLabel, input);
            Assert.DoesNotContain("disabled", input);
            Assert.Contains("live-drop-rate-input", markup());
            var ratingPanel = Regex.Match(markup(), "<section class=\"panel grind-rating-panel\"[^>]*>.*?</section>", RegexOptions.Singleline).Value;
            Assert.Contains("id=\"live-drop-rate\"", ratingPanel);
            Assert.Contains("<span>%</span>", ratingPanel);
            Assert.DoesNotContain("% Bonus", ratingPanel);
            Assert.Single(Regex.Matches(markup(), "id=\"live-drop-rate\""));
            Assert.DoesNotContain("live-session-options", markup());
            var comparison = Comparison(markup());
            Assert.Contains("is-below", comparison);
            Assert.Contains(expectedLabel, comparison);
            Assert.Contains(hourlyReference, comparison);
            Assert.Contains("00:30:00", comparison);
            Assert.Contains(pauseExplanation, comparison);
            Assert.Contains("href=\"" + SourceUrl + "\"", comparison);
            Assert.Contains("rel=\"noopener noreferrer\"", comparison);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("en", "0.76", "AboveAverage", "is-above", "Above average · Ø 0.76 expected")]
    [InlineData("en", "1.76", "BelowAverage", "is-below", "Below average · Ø 1.76 expected")]
    [InlineData("en", "1", "Average", "is-average", "At average · Ø 1.00 expected")]
    [InlineData("de", "0.76", "AboveAverage", "is-above", "Über Durchschnitt · Ø 0,76 erwartet")]
    [InlineData("de", "1.76", "BelowAverage", "is-below", "Unter Durchschnitt · Ø 1,76 erwartet")]
    [InlineData("de", "1", "Average", "is-average", "Im Durchschnitt · Ø 1,00 erwartet")]
    public async Task OneCollectedRareDropShowsPlainLocalizedRelationWithoutAnArrowOrDifferenceCount(
        string language, string expected, string relation, string tone, string label)
    {
        var count = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        var session = new Session(language);
        await session.SavePreferencesAsync(session.Preferences with { DropRatePercent = 100m });
        var benchmark = Benchmark() with { RareDropHourlyRates = new Dictionary<string, decimal> { [RareItem] = count } };
        var totals = new Dictionary<string, long> { [RareItem] = 1, [TrashItem] = 12_000 };
        session.ReplaceState(session.State with
        {
            Elapsed = TimeSpan.FromHours(1), GrindBenchmark = benchmark,
            Loot = new(totals, totals.Values.Sum(), 2),
        });

        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            var presentation = Assert.IsType<RareDropPresentation>(RareDropPresentation.Create(RareItem, 1,
                session.State.Elapsed, session.State.SpotId, benchmark, session.Preferences.DropRatePercent,
                totals[TrashItem], language));
            Assert.Equal(count, presentation.ExpectedCount);
            Assert.Equal(Enum.Parse<RareDropRelation>(relation), presentation.Relation);
            Assert.Equal(tone, presentation.ToneClass);
            var comparison = Comparison(markup());
            Assert.Equal(label, Regex.Replace(comparison, "<[^>]*>", ""));
            Assert.Contains("rare-drop-comparison " + tone, comparison);
            Assert.DoesNotContain("↑", comparison);
            Assert.DoesNotContain("↓", comparison);
            Assert.DoesNotContain("1 above", comparison, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("1 below", comparison, StringComparison.OrdinalIgnoreCase);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EditingDropRateUpdatesExpectationWithoutResettingTheRunningSession()
    {
        var session = new Session();
        var initial = session.State;
        await Render<LiveDashboard>(session, null, async (dashboard, markup, _) =>
        {
            Assert.Contains("Unter Durchschnitt · Ø 1,05 erwartet", Comparison(markup()));

            await Invoke(dashboard, "DropRateChanged", 100m);

            Assert.Equal(100m, session.Preferences.DropRatePercent);
            Assert.Equal(1, session.PreferenceSaves);
            Assert.Contains("value=\"100\"", Input(markup()));
            Assert.Contains("Über Durchschnitt · Ø 0,50 erwartet", Comparison(markup()));
            Assert.Contains("1,00 Drops / h", Comparison(markup()));
            Assert.Equal(initial.SessionId, session.State.SessionId);
            Assert.Equal(initial.Elapsed, session.State.Elapsed);
            Assert.Same(initial.Loot, session.State.Loot);
            Assert.True(session.State.IsRunning);

            // A fractional setting remains precise through classification even
            // when the expected count rounds to the actual collected quantity.
            await Invoke(dashboard, "DropRateChanged", 300.5m);
            Assert.Equal(300.5m, session.Preferences.DropRatePercent);
            Assert.Contains("value=\"300.5\"", Input(markup()));
            Assert.Contains("Unter Durchschnitt · Ø 1,00 erwartet", Comparison(markup()));
            Assert.Equal(initial.SessionId, session.State.SessionId);
            Assert.Equal(initial.Elapsed, session.State.Elapsed);
            Assert.Same(initial.Loot, session.State.Loot);
        });
    }

    [Fact]
    public async Task LootCorrectionReclassifiesTheRareDropAgainstTheSameActiveDuration()
    {
        var session = new Session();
        await Render<LiveDashboard>(session, null, async (_, markup, components) =>
        {
            Assert.Contains("Unter Durchschnitt · Ø 1,05 erwartet", Comparison(markup()));
            var editor = components.OfType<LootQuantityEditor>().Single(component => component.ItemName == RareItem);
            var sessionId = session.State.SessionId;
            await Invoke(editor, "Begin");
            SetField(editor, "_value", "3");

            await Invoke(editor, "Save");

            Assert.Equal(3, session.State.Loot.Totals[RareItem]);
            Assert.Equal(1, session.Corrections);
            Assert.Contains("Über Durchschnitt · Ø 1,05 erwartet", Comparison(markup()));
            Assert.Contains("Manuell korrigiert", markup());
            Assert.Equal(sessionId, session.State.SessionId);
            Assert.Equal(TimeSpan.FromMinutes(30), session.State.Elapsed);
        });
    }

    [Fact]
    public async Task HourlyQuantityToggleStillComparesCollectedDropsAgainstExpectedSessionDrops()
    {
        var session = new Session();
        var initial = session.State;
        await Render<LiveDashboard>(session, null, (_, markup, components) =>
        {
            var before = Comparison(markup());
            var table = components.OfType<LootTable>().Single();

            SetField(table, "_hourly", true);
            Rerender(table);

            Assert.Contains("Menge / h", markup());
            Assert.Contains(">2,00</button>", RareRow(markup()));
            Assert.Equal(before, Comparison(markup()));
            Assert.Contains("is-below", Comparison(markup()));
            Assert.Contains("Ø 1,05 erwartet", Comparison(markup()));
            Assert.Equal(initial.SessionId, session.State.SessionId);
            Assert.Equal(initial.Elapsed, session.State.Elapsed);
            Assert.Same(initial.Loot, session.State.Loot);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TrashCorrectionInstantlyUpdatesTheExpectedDropsAndAverageRelation()
    {
        var session = new Session();
        var initial = session.State;
        await Render<LiveDashboard>(session, null, async (_, markup, components) =>
        {
            Assert.Contains("Unter Durchschnitt · Ø 1,05 erwartet", Comparison(markup()));
            var editor = components.OfType<LootQuantityEditor>().Single(component => component.ItemName == TrashItem);

            await Invoke(editor, "Begin");
            SetField(editor, "_value", "3000");
            await Invoke(editor, "Save");

            Assert.Equal(3_000, session.State.Loot.Totals[TrashItem]);
            Assert.Contains("Über Durchschnitt · Ø 0,53 erwartet", Comparison(markup()));
            Assert.Equal(initial.SessionId, session.State.SessionId);
            Assert.Equal(initial.Elapsed, session.State.Elapsed);
            Assert.True(session.State.IsRunning);
            Assert.Equal(0, session.PreferenceSaves);
            Assert.Equal(1, session.Corrections);

            // A later trash correction immediately restores the original relation.
            var updatedEditor = components.OfType<LootQuantityEditor>().Last(component => component.ItemName == TrashItem);
            await Invoke(updatedEditor, "Begin");
            SetField(updatedEditor, "_value", "12000");
            await Invoke(updatedEditor, "Save");

            Assert.Equal(12_000, session.State.Loot.Totals[TrashItem]);
            Assert.Contains("Unter Durchschnitt · Ø 2,10 erwartet", Comparison(markup()));
            Assert.Equal(initial.SessionId, session.State.SessionId);
            Assert.Equal(initial.Elapsed, session.State.Elapsed);
        });
    }

    [Fact]
    public async Task SearchingForTheRareItemKeepsTheUnfilteredTrashBasis()
    {
        var session = new Session();
        await Render<LiveDashboard>(session, null, (_, markup, components) =>
        {
            var before = Comparison(markup());
            var table = components.OfType<LootTable>().Single();

            SetField(table, "_query", RareItem);
            Rerender(table);

            Assert.Single(Regex.Matches(markup(), "<tr(?: [^>]*)?>.*?</tr>", RegexOptions.Singleline)
                .Select(match => match.Value), row => row.Contains("<td>", StringComparison.Ordinal));
            Assert.DoesNotContain(TrashItem, RareRow(markup()));
            Assert.Equal(before, Comparison(markup()));
            Assert.Contains("Unter Durchschnitt · Ø 1,05 erwartet", Comparison(markup()));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("de", false, "Vergleich ab erstem Trash-Drop")]
    [InlineData("de", true, "Vergleich ab erstem Trash-Drop")]
    [InlineData("en", false, "Comparison from the first trash drop")]
    [InlineData("en", true, "Comparison from the first trash drop")]
    public async Task MissingOrZeroSessionTrashWaitsForTheFirstTrashDrop(string language, bool includeZero, string explanation)
    {
        var session = new Session(language);
        var totals = session.State.Loot.Totals.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (includeZero) totals[TrashItem] = 0;
        else totals.Remove(TrashItem);
        session.ReplaceState(session.State with { Loot = new(totals, totals.Values.Sum(), session.State.Loot.ConfirmedEventCount) });
        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            Assert.Contains("rare-drop-comparison is-unavailable", RareRow(markup()));
            Assert.Contains(explanation, RareRow(markup()));
            Assert.DoesNotContain("<a class=\"rare-drop-comparison", RareRow(markup()));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task NormalLootDoesNotReceiveARareDropComparisonEvenWhenTheSourceIncludesItsRate()
    {
        var session = new Session();
        session.ReplaceState(session.State with
        {
            GrindBenchmark = Benchmark() with
            {
                RareDropHourlyRates = new Dictionary<string, decimal> { [RareItem] = 1m, ["Black Stone"] = 50m },
            },
        });
        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            Assert.Single(Regex.Matches(markup(), "class=\"rare-drop-comparison "));
            var normalRow = Row(markup(), "Black Stone");
            Assert.DoesNotContain("rare-drop-comparison", normalRow);
            Assert.Contains("rare-drop-comparison is-below", RareRow(markup()));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("missing_benchmark")]
    [InlineData("missing_rates")]
    [InlineData("missing_item")]
    [InlineData("mismatched_spot")]
    [InlineData("missing_source")]
    [InlineData("missing_trash_reference")]
    [InlineData("zero_trash_reference")]
    public async Task MissingOrMismatchedReferenceShowsAnUnavailableComparison(string condition)
    {
        var session = new Session();
        var benchmark = condition switch
        {
            "missing_benchmark" => null,
            "missing_rates" => Benchmark() with { RareDropHourlyRates = null },
            "missing_item" => Benchmark() with { RareDropHourlyRates = new Dictionary<string, decimal>() },
            "mismatched_spot" => Benchmark() with { SpotId = LootSpotCatalog.MagaiaId },
            "missing_source" => Benchmark() with { SourceUrl = "" },
            "missing_trash_reference" => Benchmark() with { RareDropReferenceTrashPerHour = null },
            "zero_trash_reference" => Benchmark() with { RareDropReferenceTrashPerHour = 0m },
            _ => throw new ArgumentException(condition),
        };
        session.ReplaceState(session.State with { GrindBenchmark = benchmark });
        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            var row = RareRow(markup());
            Assert.Contains("rare-drop-comparison is-unavailable", row);
            Assert.Contains("Garmoth Ø nicht verfügbar", row);
            Assert.DoesNotContain("<a class=\"rare-drop-comparison", row);
            Assert.DoesNotContain("erwartet", row);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ZeroActiveTimeExplainsWhyThereIsNoAverageYet()
    {
        var session = new Session();
        session.ReplaceState(session.State with { Elapsed = TimeSpan.Zero });
        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            Assert.Contains("Vergleich ab erstem Drop", RareRow(markup()));
            Assert.DoesNotContain("<a class=\"rare-drop-comparison", RareRow(markup()));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RareDropLinkAndTooltipUseTheirOwnSourceWindowAndUpdateDate()
    {
        var rareSource = SourceUrl + "?startDate=2026-09-23&endDate=2026-09-30";
        var trashSource = SourceUrl + "?startDate=2026-09-25&endDate=2026-10-02";
        var session = new Session();
        session.ReplaceState(session.State with
        {
            GrindBenchmark = Benchmark() with
            {
                SourceUrl = trashSource,
                RareDropSourceUrl = rareSource,
                RareDropUpdatedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
            },
        });
        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            var comparison = Comparison(markup());
            Assert.Contains("href=\"" + rareSource + "\"", comparison);
            Assert.Contains("Stand 30.09.2026", comparison);
            Assert.Contains("Quelle: " + rareSource, comparison);
            Assert.DoesNotContain(trashSource, comparison);
            Assert.DoesNotContain("Stand 02.10.2026", comparison);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ReadOnlyStoreSessionShowsItsSnapshotStatusAndAllowsViewOnlyDropRateChanges()
    {
        const string sourceStatus = "Store-Zwischenstand: 02.10.2026 14:13:43";
        var session = new Session();
        session.ReplaceState(session.State with { IsReadOnly = true, CanEditLoot = false, DataSourceStatus = sourceStatus });
        var initial = session.State;
        await Render<LiveDashboard>(session, null, async (dashboard, markup, _) =>
        {
            Assert.Contains("Store-Session · Lesemodus", markup());
            Assert.Contains("Die Store-App kann geöffnet bleiben.", markup());
            Assert.Contains(sourceStatus, markup());
            Assert.Contains("Änderungen an der Droprate gelten nur für diese Ansicht.", markup());
            Assert.DoesNotContain("disabled", Input(markup()));
            var share = Regex.Match(markup(), "<button[^>]*class=\"[^\"]*session-share-trigger[^>]*>").Value;
            Assert.NotEmpty(share);
            Assert.DoesNotContain("disabled", share);
            Assert.Contains("Unter Durchschnitt · Ø 1,05 erwartet", Comparison(markup()));

            await Invoke(dashboard, "DropRateChanged", 100m);

            Assert.Equal(100m, session.Preferences.DropRatePercent);
            Assert.Contains("value=\"100\"", Input(markup()));
            Assert.Contains("Über Durchschnitt · Ø 0,50 erwartet", Comparison(markup()));
            Assert.Same(initial.Loot, session.State.Loot);
            Assert.Equal(initial.SessionId, session.State.SessionId);
            Assert.Equal(initial.Elapsed, session.State.Elapsed);
            Assert.Equal(sourceStatus, session.State.DataSourceStatus);
            Assert.True(session.State.IsReadOnly);
            Assert.Equal(0, session.Corrections);
        });
    }

    [Fact]
    public async Task ReadOnlyStoreSnapshotUsesItsUpdatedTrashWithoutChangingTheSession()
    {
        var session = new Session();
        session.ReplaceState(session.State with { IsReadOnly = true, CanEditLoot = false });
        var initial = session.State;
        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            Assert.Contains("Unter Durchschnitt · Ø 1,05 erwartet", Comparison(markup()));
            var totals = session.State.Loot.Totals.ToDictionary(pair => pair.Key, pair => pair.Value);
            totals[TrashItem] = 3_000;

            session.ReplaceState(session.State with { Loot = new(totals, totals.Values.Sum(), session.State.Loot.ConfirmedEventCount) });

            Assert.Contains("Über Durchschnitt · Ø 0,53 erwartet", Comparison(markup()));
            Assert.True(session.State.IsReadOnly);
            Assert.Equal(initial.SessionId, session.State.SessionId);
            Assert.Equal(initial.Elapsed, session.State.Elapsed);
            Assert.Equal(0, session.Corrections);
            Assert.Equal(0, session.PreferenceSaves);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyStoreSessionKeepsTrackingAndAutomaticStartControlsVisibleButDisabled(bool running)
    {
        var session = new Session();
        session.ReplaceState(session.State with { IsReadOnly = true, CanEditLoot = false, IsRunning = running });
        await Render<LiveDashboard>(session, null, async (dashboard, markup, _) =>
        {
            Assert.Contains("disabled", Button(markup(), running ? "Pausieren" : "Fortsetzen"));
            Assert.Contains("disabled", Button(markup(), "Neue Session"));
            var automaticStart = Regex.Match(markup(), "<input[^>]*id=\"auto-start-grinding\"[^>]*>").Value;
            Assert.NotEmpty(automaticStart);
            Assert.Contains("disabled", automaticStart);
            Assert.Contains("Grind automatisch erkennen", markup());
            Assert.DoesNotContain("disabled", Button(markup(), "Store-Session übernehmen"));
            Assert.DoesNotContain("id=\"live-confirm\"", markup());
            Assert.DoesNotContain("disabled", Input(markup()));

            await Invoke(dashboard, "RequestNew");
            await Invoke(dashboard, "ConfirmAction");

            Assert.True(session.State.IsReadOnly);
            Assert.Equal(0, session.Takeovers);
            Assert.DoesNotContain("id=\"live-confirm\"", markup());
        });
    }

    [Fact]
    public async Task TakeoverButtonCallsTheSessionAndRestoresNormalControlsAfterSuccess()
    {
        var session = new Session();
        session.ReplaceState(session.State with { IsReadOnly = true, CanEditLoot = false, IsRunning = false });
        var initial = session.State;
        await Render<LiveDashboard>(session, null, async (dashboard, markup, _) =>
        {
            Assert.DoesNotContain("disabled", Button(markup(), "Store-Session übernehmen"));
            Assert.Contains("disabled", Button(markup(), "Fortsetzen"));

            await Invoke(dashboard, "TakeOverSession");

            Assert.Equal(1, session.Takeovers);
            Assert.False(session.State.IsReadOnly);
            Assert.Equal(initial.SessionId, session.State.SessionId);
            Assert.Equal(initial.Elapsed, session.State.Elapsed);
            Assert.Same(initial.Loot, session.State.Loot);
            Assert.DoesNotContain("Store-Session übernehmen", markup());
            Assert.DoesNotContain("disabled", Button(markup(), "Fortsetzen"));
            Assert.DoesNotContain("disabled", Button(markup(), "Neue Session"));
            var automaticStart = Regex.Match(markup(), "<input[^>]*id=\"auto-start-grinding\"[^>]*>").Value;
            Assert.NotEmpty(automaticStart);
            Assert.DoesNotContain("disabled", automaticStart);
            Assert.Contains("id=\"live-confirm\"", markup());
        });
    }

    [Fact]
    public async Task FailedTakeoverDisplaysItsErrorAndKeepsTheStoreSnapshotReadOnly()
    {
        const string failure = "Die Store-App läuft noch. Bitte zuerst schließen.";
        var session = new Session { TakeoverResult = new(failure) };
        session.ReplaceState(session.State with { IsReadOnly = true, CanEditLoot = false, IsRunning = false });
        var initial = session.State;
        await Render<LiveDashboard>(session, null, async (dashboard, markup, _) =>
        {
            await Invoke(dashboard, "TakeOverSession");

            Assert.Equal(1, session.Takeovers);
            Assert.Same(initial, session.State);
            Assert.True(session.State.IsReadOnly);
            Assert.Contains(failure, markup());
            Assert.Contains("role=\"alert\"", markup());
            Assert.DoesNotContain("disabled", Button(markup(), "Store-Session übernehmen"));
            Assert.Contains("disabled", Button(markup(), "Fortsetzen"));
            Assert.Contains("disabled", Button(markup(), "Neue Session"));
            Assert.DoesNotContain("id=\"live-confirm\"", markup());
        });
    }

    [Fact]
    public async Task ReadOnlyStoreSessionPreventsOpeningOrSavingLootEdits()
    {
        var session = new Session();
        session.ReplaceState(session.State with { IsReadOnly = true, CanEditLoot = false });
        var originalLoot = session.State.Loot;
        await Render<LiveDashboard>(session, null, async (_, markup, components) =>
        {
            var quantityTriggers = Regex.Matches(markup(), "<button[^>]*class=\"quantity-edit-trigger\"[^>]*>")
                .Select(match => match.Value).ToArray();
            Assert.All(quantityTriggers, trigger => Assert.Contains("disabled", trigger));
            var editor = components.OfType<LootQuantityEditor>().SingleOrDefault(component => component.ItemName == RareItem);
            if (editor is not null)
            {
                await Invoke(editor, "Begin");
                SetField(editor, "_value", "3");
                await Invoke(editor, "Save");
                Assert.DoesNotContain("<form class=\"loot-quantity-editor\"", markup());
            }
            var adder = components.OfType<LootItemAdder>().SingleOrDefault();
            if (adder is not null)
            {
                var trigger = Regex.Match(markup(), "<div class=\"loot-add-control\">.*?<button[^>]*>", RegexOptions.Singleline).Value;
                Assert.Contains("disabled", trigger);
                await Invoke(adder, "Open");
                SetField(adder, "_selectedItem", "Apeiron Ring");
                SetField(adder, "_quantity", "3");
                await Invoke(adder, "Save");
            }
            Assert.Same(originalLoot, session.State.Loot);
            Assert.Equal(0, session.Corrections);
        });
    }

    [Fact]
    public async Task NormalLiveSessionStillHasTrackingAndEditableLootControls()
    {
        var session = new Session();
        await Render<LiveDashboard>(session, null, (_, markup, components) =>
        {
            Assert.DoesNotContain("Store-Session · Lesemodus", markup());
            Assert.Contains("Pausieren", markup());
            Assert.Contains("Neue Session", markup());
            Assert.Contains("auto-start-grinding", markup());
            Assert.DoesNotContain("disabled", Button(markup(), "Pausieren"));
            Assert.DoesNotContain("disabled", Button(markup(), "Neue Session"));
            Assert.DoesNotContain("Store-Session übernehmen", markup());
            var quantityTrigger = Regex.Match(RareRow(markup()), "<button[^>]*class=\"quantity-edit-trigger\"[^>]*>").Value;
            Assert.NotEmpty(quantityTrigger);
            Assert.DoesNotContain("disabled", quantityTrigger);
            Assert.Single(components.OfType<LootItemAdder>());
            Assert.Contains("Unter Durchschnitt · Ø 1,05 erwartet", Comparison(markup()));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HistoryStyleLootTableKeepsLiveDropRateComparisonDisabledByDefault()
    {
        var session = new Session();
        await Render<LootTable>(session, new Dictionary<string, object?>
        {
            [nameof(LootTable.Totals)] = session.State.Loot.Totals,
            [nameof(LootTable.DurationTicks)] = session.State.Elapsed.Ticks,
            [nameof(LootTable.SpotId)] = session.State.SpotId,
            [nameof(LootTable.EditableSessionId)] = Guid.NewGuid(),
        }, (_, markup, _) =>
        {
            Assert.Contains(RareItem, markup());
            Assert.DoesNotContain("rare-drop-comparison", markup());
            return Task.CompletedTask;
        });
    }

    private static GrindBenchmark Benchmark() => new(LootSpotCatalog.HermesiaId, 12_000m, null, null,
        new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), SourceUrl, "Public Garmoth 100%")
    {
        RareDropHourlyRates = new Dictionary<string, decimal> { [RareItem] = 1m },
        RareDropReferenceTrashPerHour = 12_000m,
    };

    private static string Input(string markup) => Regex.Match(markup, "<input[^>]*id=\"live-drop-rate\"[^>]*>").Value;
    private static string Button(string markup, string label) => Regex.Matches(markup,
        "<button\\b[^>]*>.*?</button>", RegexOptions.Singleline).Select(match => match.Value)
        .First(button => button.Contains(label, StringComparison.Ordinal));
    private static string Comparison(string markup) => Regex.Match(markup,
        "<a class=\"rare-drop-comparison [^>]*>.*?</a>", RegexOptions.Singleline).Value;
    private static string RareRow(string markup) => Row(markup, RareItem);
    private static string Row(string markup, string itemName) => Regex.Matches(markup, "<tr(?: [^>]*)?>.*?</tr>", RegexOptions.Singleline)
        .Select(match => match.Value).Single(row => row.Contains(itemName, StringComparison.Ordinal));

    private static async Task Render<T>(Session session, Dictionary<string, object?>? parameters,
        Func<T, Func<string>, IReadOnlyList<IComponent>, Task> test) where T : IComponent
    {
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(session)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<NavigationManager, StaticNavigation>()
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? []));
            var component = activator.Components.OfType<T>().Single();
            string Markup() => WebUtility.HtmlDecode(rendered.ToHtmlString());
            await test(component, Markup, activator.Components);
        });
    }

    private static async Task Invoke(object component, string method, params object[] args)
    {
        var result = component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
        if (result is Task task) await task;
        Rerender(component);
    }

    private static void SetField(object component, string field, object value) =>
        component.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
    private static void Rerender(object component) =>
        typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/live");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class Session : ITrackerSession
    {
        public event Action? Changed;
        public TrackerState State { get; private set; } = new()
        {
            SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true, CanPause = true, AnalyzerAvailable = true,
            SpotId = LootSpotCatalog.HermesiaId, Elapsed = TimeSpan.FromMinutes(30), GrindBenchmark = Benchmark(),
            Loot = new(new Dictionary<string, long> { [RareItem] = 1, ["Black Stone"] = 10, [TrashItem] = 6_000 }, 6_011, 3),
            // Wall time includes a long pause, while the average uses the active clock.
            Pauses = [new(TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1), SessionPause.Manual)],
            Silver = new(0, 0, 0, [], [], true),
        };
        public TrackerPreferences Preferences { get; private set; }
        public Session(string language = "de") => Preferences = new() { SetupCompleted = true, UiLanguage = language, GameLanguage = "en" };
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [];
        public IReadOnlyList<LootHistoryEntry> History { get; } = [];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        public int PreferenceSaves { get; private set; }
        public int Corrections { get; private set; }
        public int Takeovers { get; private set; }
        public TrackerCommandResult TakeoverResult { get; init; } = TrackerCommandResult.Success;
        public void ReplaceState(TrackerState state) { State = state; Changed?.Invoke(); }
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false)
        {
            PreferenceSaves++;
            if (preferences.DropRatePercent is < AppSettings.MinimumDropRatePercent or > AppSettings.MaximumDropRatePercent)
                return Task.FromResult(new PreferenceSaveResult("Die Droprate muss zwischen 0 und 1000 % liegen."));
            Preferences = preferences;
            Changed?.Invoke();
            return Task.FromResult(new PreferenceSaveResult());
        }
        public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity)
        {
            Assert.Equal(State.SessionId, sessionId);
            Corrections++;
            var totals = State.Loot.Totals.ToDictionary(pair => pair.Key, pair => pair.Value);
            totals[itemName] = totals.GetValueOrDefault(itemName) + quantity - originalQuantity;
            ReplaceState(State with { Loot = new(totals, totals.Values.Sum(), State.Loot.ConfirmedEventCount), ManualLootItems = [itemName] });
            return Task.FromResult(TrackerCommandResult.Success);
        }
        private static Task<TrackerCommandResult> Unexpected() => throw new InvalidOperationException("Unexpected session command.");
        public Task<TrackerCommandResult> TakeOverSessionAsync()
        {
            Takeovers++;
            if (TakeoverResult.Succeeded)
                ReplaceState(State with
                {
                    IsReadOnly = false, CanEditLoot = true, IsRunning = false, CanPause = true,
                    AnalyzerAvailable = true, TrackingBlockedReason = null, DataSourceStatus = null,
                });
            return Task.FromResult(TakeoverResult);
        }
        public Task<TrackerCommandResult> ToggleTrackingAsync() => Unexpected();
        public Task<TrackerCommandResult> PauseAsync() => Unexpected();
        public Task<TrackerCommandResult> NewSessionAsync() => Unexpected();
        public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Unexpected();
        public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Unexpected();
        public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Unexpected();
        public Task<TrackerCommandResult> UploadAsync() => Unexpected();
        public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => Unexpected();
        public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals, string? characterClass = null) => Unexpected();
        public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => Unexpected();
        public Task RefreshPricesAsync() => Task.CompletedTask;
        public Task TickAsync() => Task.CompletedTask;
        public Task RunPreparedUpdateAsync(Func<Task> install) => throw new InvalidOperationException("Unexpected shutdown.");
        public Task ShutdownAsync() => throw new InvalidOperationException("Unexpected shutdown.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
