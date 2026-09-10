using System.Net;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.App.Updates;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class BlazorFrontendTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("de")]
    public async Task GermanGameLanguageLocalizesLootAndEditorsWithoutChangingStoredKeys(string preference)
    {
        var session = new SnapshotSession { Preferences = new() { GameLanguage = preference }, State = ActiveState() with
        {
            DetectedGameLanguage = "de", GameLanguageStatus = "Erkannt: Deutsch",
        } };
        var markup = WebUtility.HtmlDecode(await RenderAsync<LootTable>(session, new Dictionary<string, object?>
        {
            [nameof(LootTable.Totals)] = new Dictionary<string, long> { ["Black Crystal Fragment"] = 2387 },
            [nameof(LootTable.EditableSessionId)] = session.State.SessionId,
        }));
        Assert.Contains("Schwarzkristallfragment", markup);
        Assert.Contains("Gesamtmenge für Schwarzkristallfragment bearbeiten", markup);
        Assert.Contains("quantity-edit-trigger", ButtonAttributes(markup, "2.387"));
        Assert.DoesNotContain("quantity-edit-button", markup);
        Assert.DoesNotContain("<svg", Regex.Match(markup,
            "<button[^>]*class=\"quantity-edit-trigger\"[^>]*>(.*?)</button>", RegexOptions.Singleline).Groups[1].Value);
        Assert.Contains("assets/icons/black-crystal-fragment.png", markup);
        Assert.DoesNotContain("Preis fehlt", markup);
        var settings = WebUtility.HtmlDecode(await RenderAsync<TrackerSettings>(session));
        Assert.Contains("Automatisch aus BDO-Einstellungen", settings);
        Assert.Contains("Deutsch", settings);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task SettingsPagesHaveNoAutosaveExplanationsOrSaveOrDiscardButtons()
    {
        var session = new SnapshotSession { State = ActiveState() };
        foreach (var markup in new[] { await RenderAsync<TrackerSettings>(session), await RenderAsync<GarmothDashboard>(session) })
        {
            var text = WebUtility.HtmlDecode(markup);
            Assert.DoesNotContain("automatisch lokal gespeichert", text);
            Assert.DoesNotContain("Einmal einrichten", text);
            Assert.DoesNotContain("Einstellungen speichern", text);
            Assert.DoesNotContain("Verwerfen", text);
            Assert.DoesNotContain("Änderungen verwerfen", text);
        }
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task MissingMainLogShowsAnAlertAndDisablesStart()
    {
        var session = new SnapshotSession { State = new()
        {
            AnalyzerAvailable = false, IsError = true,
            TrackingBlockedReason = "Die Position des Haupt-Droplogs konnte nicht erkannt werden.",
            Status = "Unrelated saved-settings status",
        } };
        var markup = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));
        Assert.Contains("error-notice tracking-blocked", markup);
        Assert.Contains("role=\"alert\"", markup);
        Assert.Contains("Tracking nicht möglich", markup);
        Assert.Contains("Die Position des Haupt-Droplogs", markup);
        Assert.DoesNotContain("Unrelated saved-settings status", markup);
        Assert.True(IsDisabled(ButtonAttributes(markup, "Tracking starten")));
        Assert.DoesNotContain("ocr-install-notice", markup);
        Assert.DoesNotContain("OCR-Sprachpaket installieren", markup);
    }

    [Theory]
    [InlineData("de-DE", "Deutsch (de-DE)")]
    [InlineData("en-US", "Englisch (en-US)")]
    public async Task MissingOcrLanguageOffersAnExplicitInstallAndRecheckWithoutStartingEither(
        string languageTag, string languageLabel)
    {
        var session = new SnapshotSession { State = new()
        {
            AnalyzerAvailable = true, IsError = true, MissingOcrLanguageTag = languageTag,
            TrackingBlockedReason = "Windows-OCR fehlt.",
        } };

        var markup = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));

        Assert.Contains("ocr-install-notice warning-notice", markup);
        Assert.Contains(languageLabel, markup);
        Assert.Contains("Administratorbestätigung", markup);
        Assert.Contains("Internet", markup);
        Assert.Contains("einige Minuten", markup);
        Assert.False(IsDisabled(ButtonAttributes(markup, "OCR-Sprachpaket installieren")));
        Assert.False(IsDisabled(ButtonAttributes(markup, "Erneut prüfen")));
        Assert.False(IsDisabled(ButtonAttributes(markup, "Einstellungen")));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Tracking starten")));
        Assert.DoesNotContain("tracking-blocked", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task OcrInstallationShowsIndeterminateProgressAndDisablesRepeatedActions()
    {
        var session = new SnapshotSession { State = new()
        {
            AnalyzerAvailable = false, MissingOcrLanguageTag = "en-US", IsInstallingOcrLanguage = true,
            OcrInstallationStatus = "Windows lädt die Texterkennung herunter …",
        } };

        var markup = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));

        Assert.Contains("Windows lädt die Texterkennung herunter …", markup);
        Assert.Contains("role=\"status\"", markup);
        Assert.Contains("aria-live=\"polite\"", markup);
        var progress = Regex.Match(markup, "<progress(?<attributes>[^>]*)>");
        Assert.True(progress.Success);
        Assert.Contains("aria-label=\"OCR-Sprachpaket wird installiert\"", progress.Groups["attributes"].Value);
        Assert.DoesNotContain("value=", progress.Groups["attributes"].Value);
        Assert.True(IsDisabled(ButtonAttributes(markup, "Wird installiert …")));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Erneut prüfen")));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Einstellungen")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OcrInstallActionsRequireAnIdlePausedTracker(bool busy, bool running)
    {
        var session = new SnapshotSession { State = new()
        {
            MissingOcrLanguageTag = "de-DE", IsBusy = busy, IsRunning = running,
        } };

        var markup = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));

        Assert.True(IsDisabled(ButtonAttributes(markup, "OCR-Sprachpaket installieren")));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Erneut prüfen")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task OcrRestartRequirementShowsTheResultWithoutOfferingAnotherInstallation()
    {
        var session = new SnapshotSession { State = new()
        {
            MissingOcrLanguageTag = "de-DE", OcrRestartRequired = true,
            OcrInstallationStatus = "Windows benötigt einen Neustart, um das Sprachpaket zu aktivieren.",
        } };

        var markup = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));

        Assert.Contains("Windows-Neustart erforderlich", markup);
        Assert.Contains(session.State.OcrInstallationStatus, markup);
        Assert.DoesNotContain("OCR-Sprachpaket installieren", markup);
        Assert.False(IsDisabled(ButtonAttributes(markup, "Erneut prüfen")));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Tracking starten")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task SuccessfulOcrInstallationKeepsItsResultVisibleAndEnablesTracking()
    {
        var session = new SnapshotSession { State = new()
        {
            AnalyzerAvailable = true,
            OcrInstallationStatus = "Die Windows-Texterkennung für Englisch ist verfügbar. Du kannst das Tracking starten.",
        } };

        var markup = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));

        Assert.Contains("ocr-install-notice success-notice", markup);
        Assert.Contains(session.State.OcrInstallationStatus, markup);
        Assert.DoesNotContain("OCR-Sprachpaket installieren", markup);
        Assert.DoesNotContain("<progress", markup);
        Assert.False(IsDisabled(ButtonAttributes(markup, "Tracking starten")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task GenericOcrRuntimeErrorsDoNotOfferALanguageInstallation()
    {
        var session = new SnapshotSession { State = new()
        {
            AnalyzerAvailable = true, IsError = true, Status = "Windows OCR konnte nicht erstellt werden.",
        } };

        var markup = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));

        Assert.Contains(session.State.Status, markup);
        Assert.DoesNotContain("ocr-install-notice", markup);
        Assert.DoesNotContain("OCR-Sprachpaket installieren", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task ItemQuantitiesStayEditableWhileRunningPausedOrSubmitted(bool running, bool submitted)
    {
        var session = new SnapshotSession { State = ActiveState() with { IsRunning = running, IsSubmitted = submitted } };
        var markup = await RenderAsync<LiveDashboard>(session);
        var attributes = AriaButtonAttributes(markup, "Gesamtmenge für Black Crystal Fragment bearbeiten");
        Assert.False(IsDisabled(attributes));
        Assert.Contains("quantity-edit-trigger", attributes);
        Assert.Contains("quantity-edit-trigger", ButtonAttributes(markup, "1.582"));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task SpotHistoryOffersAnEditorForBothExistingAndMissingLoot()
    {
        var entry = HistoryEntry(Guid.NewGuid());
        var session = new SnapshotSession { History = [entry] };
        var markup = await RenderAsync<HistoryDashboard>(session,
            new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = entry.SpotId });
        Assert.False(IsDisabled(AriaButtonAttributes(markup, "Gesamtmenge für Black Crystal Fragment bearbeiten")));
        Assert.False(IsDisabled(AriaButtonAttributes(markup, "Gesamtmenge für BON Wandering Origin Crystal bearbeiten")));
        Assert.DoesNotContain("quantity-edit-button", markup);
        Assert.Contains("quantity-edit-trigger", ButtonAttributes(markup, "1.582"));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task AggregatedLootAcrossSessionsCannotBeEditedAsOneSession()
    {
        var session = new SnapshotSession();
        var markup = await RenderAsync<LootTable>(session, new Dictionary<string, object?>
        {
            [nameof(LootTable.Totals)] = ActiveState().Loot.Totals,
        });
        Assert.DoesNotContain("quantity-edit-trigger", markup);
        Assert.DoesNotContain("loot-quantity-control", markup);
    }

    [Fact]
    public async Task SessionSilverUsesCurrentPricesInsteadOfSavedPartialDemoTotal()
    {
        var entry = HistoryEntry(Guid.NewGuid()) with
        {
            Totals = new Dictionary<string, long> { ["Black Stone"] = 100 },
            Duration = TimeSpan.FromHours(1), SilverAfterTax = 999999999, SilverIsComplete = false
        };
        var session = new SnapshotSession
        {
            History = [entry],
            Prices = new LootPriceSnapshot("eu", [new("Black Stone", 100, 0, LootPriceOrigin.LiveMarket, null)]),
            Preferences = new()
        };
        var markup = WebUtility.HtmlDecode(await RenderAsync<HistoryDashboard>(session,
            new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId }));
        Assert.Contains("numeric gold\">6.500</td>", markup);
        Assert.DoesNotContain("* Enthält Sessions", markup);
        Assert.Equal(999999999, entry.SilverAfterTax);
        Assert.False(entry.SilverIsComplete);
    }

    [Fact]
    public async Task RunningSessionOffersPauseAndShowsExactLootWithoutStartingAnyActionDuringRender()
    {
        var session = new SnapshotSession { State = ActiveState() };
        var markup = await RenderAsync<LiveDashboard>(session);
        Assert.False(IsDisabled(ButtonAttributes(markup, "Pausieren")));
        Assert.Contains("1.582", markup);
        Assert.Contains("00:30:00", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task PauseRemainsAvailableWhenTheServiceAllowsItDuringAnUpload()
    {
        var session = new SnapshotSession { State = ActiveState() with { IsBusy = true, CanPause = true } };
        var markup = await RenderAsync<LiveDashboard>(session);
        Assert.False(IsDisabled(ButtonAttributes(markup, "Pausieren")));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Neue Session")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task SubmittedSessionCannotResumeFromTheLiveScreen()
    {
        var session = new SnapshotSession
        {
            State = ActiveState() with { IsRunning = false, IsSubmitted = true, UploadBlocked = true }
        };
        var markup = await RenderAsync<LiveDashboard>(session);
        Assert.True(IsDisabled(ButtonAttributes(markup, "Fortsetzen")));
        Assert.False(IsDisabled(ButtonAttributes(markup, "Neue Session")));
        AssertNoGarmothControls(markup);
    }

    [Fact]
    public async Task LiveGeneralSettingsAndHistoryContainNoConnectionOrUploadControls()
    {
        var state = ActiveState() with { UploadBlocked = true };
        var session = new SnapshotSession { State = state, History = [HistoryEntry(Guid.NewGuid())] };
        var live = await RenderAsync<LiveDashboard>(session);
        Assert.False(IsDisabled(ButtonAttributes(live, "Pausieren")));
        AssertNoGarmothControls(live);
        AssertNoGarmothControls(await RenderAsync<TrackerSettings>(session));
        AssertNoGarmothControls(await RenderAsync<HistoryDashboard>(session, new Dictionary<string, object?>
        {
            [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId
        }));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task SpotHistoryRendersTheCompactLootMatrixWithPinnedSessionColumns()
    {
        var session = new SnapshotSession
        {
            History = [HistoryEntry(Guid.NewGuid())]
        };

        var markup = WebUtility.HtmlDecode(await RenderAsync<HistoryDashboard>(session,
            new Dictionary<string, object?>
            {
                [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId
            }));

        Assert.Contains("spot-session-table-scroll", markup);
        Assert.Contains("WIE LANG HER", markup);
        Assert.Contains("GRINDZEIT", markup);
        Assert.Contains("SILBER / H", markup);
        Assert.Contains("Black Crystal Fragment", markup);
        Assert.Contains("session-delete-button", AriaButtonAttributes(markup, "Session löschen"));
        Assert.DoesNotContain("session-action-menu", markup);
        Assert.DoesNotContain("Aktionen für diese Session", markup);
        Assert.Contains("Session löschen?", markup);
        Assert.Contains("dauerhaft aus deinem lokalen Verlauf entfernt", markup);
        Assert.Contains("Klasse dieser Session", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task ManualColumnOrderRemainsWithoutLootPool()
    {
        var session = new SnapshotSession
        {
            State = ActiveState() with { Loot = ActiveState().Loot with { Totals = new Dictionary<string, long> { ["Black Stone"] = 2 } } },
            History = [HistoryEntry(Guid.NewGuid())],
            Preferences = new() { LootColumnOrders = new Dictionary<string, string[]> { [LootSpotCatalog.HermesiaId] = ["Black Stone", "Black Crystal Fragment"] } }
        };
        var markup = WebUtility.HtmlDecode(await RenderAsync<HistoryDashboard>(session,
            new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId }));
        Assert.DoesNotContain("spot-loot-pool", markup);

        var headers = Regex.Matches(markup, "<th class=\"session-loot-column\"[^>]*title=\"([^\"]+)\"");
        Assert.StartsWith("Black Stone", headers[0].Groups[1].Value);
        Assert.StartsWith("Black Crystal Fragment", headers[1].Groups[1].Value);
        var live = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));
        Assert.DoesNotContain("favorite-loot-row", live);
        Assert.DoesNotContain("★ FAVORIT", live);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureSettingsStayLockedForBothRunningAndPausedSessions(bool running)
    {
        var session = new SnapshotSession { State = ActiveState() with { IsRunning = running } };
        var markup = await RenderAsync<TrackerSettings>(session);
        Assert.True(IsDisabled(FieldSelectAttributes(markup, "Spielbildschirm")));
        Assert.Equal(running, IsDisabled(FieldSelectAttributes(markup, "Charakterklasse")));
        Assert.DoesNotContain("Event-Loot mitzählen", markup);
        Assert.True(IsDisabled(ToggleAttributes(markup, "Diese Session aufzeichnen")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GarmothConnectionUsesAnEmptyMaskedInputForBothNewAndStoredKeys(bool hasSavedKey)
    {
        var session = new SnapshotSession { State = ActiveState() with { HasApiKey = hasSavedKey } };
        var markup = await RenderAsync<GarmothDashboard>(session);
        var input = Regex.Matches(WebUtility.HtmlDecode(markup), "<input(?<attributes>[^>]*)>")
            .Single(match => match.Groups["attributes"].Value.Contains("aria-label=\"Garmoth-API-Schlüssel\"", StringComparison.Ordinal));
        var attributes = input.Groups["attributes"].Value;
        Assert.Contains("type=\"password\"", attributes);
        var value = Regex.Match(attributes, "(?:^|\\s)value=\"(?<value>[^\"]*)\"");
        Assert.True(!value.Success || value.Groups["value"].Length == 0);
        var text = WebUtility.HtmlDecode(markup);
        Assert.Contains(hasSavedKey ? "Gespeichert" : "Nicht hinterlegt", text);
        Assert.Equal(0, session.CommandCalls);
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("busy")]
    [InlineData("blocked")]
    [InlineData("submitted")]
    public async Task GarmothCurrentUploadRespectsDemoBusyAndDuplicateGuards(string reason)
    {
        var state = ActiveState();
        state = reason switch
        {
            "demo" => state with { IsDemo = true },
            "busy" => state with { IsBusy = true },
            "blocked" => state with { UploadBlocked = true },
            _ => state with { IsRunning = false, IsSubmitted = true }
        };
        var session = new SnapshotSession { State = state };
        var markup = await RenderAsync<GarmothDashboard>(session);
        Assert.True(IsDisabled(AriaButtonAttributes(markup, "Aktuellen Session-Anteil hochladen")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task GarmothShowsAPartialSilverValueHonestlyWhileAllowingItsKnownAmount()
    {
        var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 1_582, ["Black Stone"] = 2 };
        var state = ActiveState() with
        {
            Loot = new(totals, 1_584, 102),
            Silver = SilverValuation.Calculate(totals, LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default)
        };
        state = state with { CurrentGarmothUpload = GarmothUploadPreview.Create(Guid.NewGuid(), state.SessionId,
            state.SpotId, state.CharacterLabel, state.Elapsed, totals, DateTimeOffset.UnixEpoch,
            LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default) };
        Assert.False(state.Silver.IsComplete);
        Assert.True(state.Silver.HasKnownValue);
        Assert.True(state.CurrentGarmothUpload.IsReady);
        Assert.False(state.CurrentGarmothUpload.SilverIsComplete);
        var session = new SnapshotSession { State = state };
        var markup = await RenderAsync<GarmothDashboard>(session);
        Assert.Contains("Teilbetrag", WebUtility.HtmlDecode(markup));
        Assert.False(IsDisabled(AriaButtonAttributes(markup, "Aktuellen Session-Anteil hochladen")));
    }

    [Fact]
    public async Task GarmothCurrentButtonUsesReadinessOfTheRemainderInsteadOfTheWholeSession()
    {
        var state = ActiveState();
        var remainder = GarmothUploadPreview.Create(Guid.NewGuid(), state.SessionId, state.SpotId,
            state.CharacterLabel, TimeSpan.FromSeconds(30), state.Loot.Totals, DateTimeOffset.UnixEpoch,
            LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default);
        var session = new SnapshotSession { State = state with { CurrentGarmothUpload = remainder } };
        var markup = WebUtility.HtmlDecode(await RenderAsync<GarmothDashboard>(session));

        Assert.True(state.Elapsed > TimeSpan.FromMinutes(1));
        Assert.True(IsDisabled(AriaButtonAttributes(markup, "Aktuellen Session-Anteil hochladen")));
        Assert.Contains("volle aktive Minute", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task GarmothHistoryExplainsKnownMissingRequirementsBeforeConfirmation()
    {
        var missingClass = HistoryEntry(Guid.NewGuid()) with { CharacterClass = null };
        var shortSession = HistoryEntry(Guid.NewGuid()) with { Duration = TimeSpan.FromSeconds(30) };
        var session = new SnapshotSession { State = ActiveState(), History = [missingClass, shortSession] };
        var markup = WebUtility.HtmlDecode(await RenderAsync<GarmothDashboard>(session));

        var buttons = AriaButtons(markup, "Gespeicherte Session hochladen").ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.All(buttons, attributes => Assert.True(IsDisabled(attributes)));
        Assert.Contains("Für diesen Grind fehlt die Charakterklasse", markup);
        Assert.Contains("volle aktive Minute", markup);
        Assert.Contains($"/history/spots/{missingClass.SpotId}/{missingClass.SessionId}?edit=1", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task GarmothListIncludesCurrentSessionOnceAndDisablesAlreadyTransferredRows()
    {
        var state = ActiveState() with { IsRunning = false };
        var session = new SnapshotSession
        {
            State = state,
            History =
            [
                HistoryEntry(state.SessionId) with { CharacterClass = "CURRENT-ROW-MUST-NOT-REAPPEAR" },
                HistoryEntry(Guid.NewGuid()),
                HistoryEntry(Guid.NewGuid()) with { GarmothUploadBlocked = true },
                HistoryEntry(Guid.NewGuid()) with { GarmothUploadedAt = DateTimeOffset.UnixEpoch.AddHours(1) }
            ]
        };
        var markup = await RenderAsync<GarmothDashboard>(session);
        Assert.DoesNotContain("CURRENT-ROW-MUST-NOT-REAPPEAR", markup);
        Assert.Single(Regex.Matches(markup, $"data-session-id=\"{state.SessionId}\""));
        Assert.Contains("Live-Session", WebUtility.HtmlDecode(markup));
        Assert.DoesNotContain("garmoth-current", markup);
        var uploadButtons = AriaButtons(markup, "Gespeicherte Session hochladen").ToArray();
        Assert.Equal(3, uploadButtons.Length);
        Assert.Equal(2, uploadButtons.Count(IsDisabled));
        Assert.False(IsDisabled(AriaButtonAttributes(markup, "Aktuellen Session-Anteil hochladen")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public async Task UpdateRestartRequiresPausedIdleSession(bool running, bool busy, bool disabled)
    {
        var session = new SnapshotSession { State = ActiveState() with { IsRunning = running, IsBusy = busy } };
        var updates = new StaticUpdates(new(true, true, "0.10.0-test.2", "0.10.0-test.3",
            UpdatePhase.ReadyToRestart, 100, "Update bereit."));
        var markup = await RenderAsync<AppUpdates>(session, updates: updates);
        Assert.Equal(disabled, IsDisabled(ButtonAttributes(markup, "Installieren und neu starten")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task StoreUpdatesExplainStoreManagementWithoutGitHubUpdateControls()
    {
        var session = new SnapshotSession { State = ActiveState() };
        var updates = new AppUpdateRuntime(AppPackageIdentity.Packaged).CreateUpdates(true,
            () => throw new InvalidOperationException("Store builds must not create GitHub updates."));

        var markup = WebUtility.HtmlDecode(await RenderAsync<AppUpdates>(session, updates: updates));

        Assert.Contains("Diese Version wird über den Microsoft Store aktualisiert.", markup);
        Assert.Contains("Updates im Microsoft Store suchen.", markup);
        Assert.DoesNotContain("<button", markup);
        Assert.DoesNotContain("Beta-Updates", markup);
        Assert.DoesNotContain("Grindcrest-Setup", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task StoreUpdatesDoNotDisplayAGitHubUpdateBanner()
    {
        var session = new SnapshotSession { State = ActiveState() };
        var updates = new AppUpdateRuntime(AppPackageIdentity.Packaged).CreateUpdates(true,
            () => throw new InvalidOperationException());

        var markup = await RenderAsync<AppUpdates>(session,
            new Dictionary<string, object?> { [nameof(AppUpdates.Compact)] = true }, updates);

        Assert.DoesNotContain("update-banner", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task ActiveStoreUpdatesOfferDownloadWithoutBetaOrExternalStoreNavigation()
    {
        var updates = new StaticUpdates(new(true, false, "1.0.1", null,
            UpdatePhase.Available, 0, "Eine neue Version von Grindcrest ist verfügbar.") { UsesStore = true });
        var session = new SnapshotSession { State = ActiveState() };
        var markup = WebUtility.HtmlDecode(await RenderAsync<AppUpdates>(session, updates: updates));
        Assert.Contains("Update herunterladen", markup);
        Assert.Contains("Nach Updates suchen", markup);
        Assert.DoesNotContain("Beta-Updates", markup);
        Assert.DoesNotContain("Öffne den Microsoft Store", markup);
        var banner = await RenderAsync<AppUpdates>(session,
            new Dictionary<string, object?> { [nameof(AppUpdates.Compact)] = true }, updates);
        Assert.Contains("update-banner", banner);
        Assert.Contains("Update ansehen", banner);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public async Task StoreInstallButtonRequiresPausedIdleSession(bool running, bool busy, bool disabled)
    {
        var session = new SnapshotSession { State = ActiveState() with { IsRunning = running, IsBusy = busy } };
        var updates = new StaticUpdates(new(true, false, "1.0.1", null,
            UpdatePhase.ReadyToRestart, 100, "Update bereit.") { UsesStore = true });
        var markup = await RenderAsync<AppUpdates>(session, updates: updates);
        Assert.Equal(disabled, IsDisabled(ButtonAttributes(markup, "Update installieren")));
        Assert.DoesNotContain("Beta-Updates", markup);
    }

    private static TrackerState ActiveState()
    {
        var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 1_582 };
        var sessionId = Guid.NewGuid();
        return new()
        {
            SessionId = sessionId, HasSession = true, IsRunning = true, CanPause = true, AnalyzerAvailable = true,
            HasApiKey = true, SpotId = LootSpotCatalog.HermesiaId, Elapsed = TimeSpan.FromMinutes(30),
            CharacterClassId = "warrior-awakening", CharacterLabel = "Warrior · Awakening",
            Loot = new(totals, 1_582, 100),
            Silver = SilverValuation.Calculate(totals, LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default),
            CurrentGarmothUpload = GarmothUploadPreview.Create(Guid.NewGuid(), sessionId, LootSpotCatalog.HermesiaId,
                "Warrior · Awakening", TimeSpan.FromMinutes(30), totals, DateTimeOffset.UnixEpoch,
                LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default),
        };
    }

    private static LootHistoryEntry HistoryEntry(Guid id) => new()
    {
        SessionId = id, StartedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch.AddHours(1),
        Duration = TimeSpan.FromHours(1), SpotId = LootSpotCatalog.HermesiaId, CharacterClass = "Warrior · Awakening",
        Totals = new() { ["Black Crystal Fragment"] = 1_582 }, SilverBeforeTax = 1_582 * 160_539m,
        SilverAfterTax = 1_582 * 160_539m, SilverIsComplete = true
    };

    private static async Task<string> RenderAsync<TComponent>(ITrackerSession session,
        IDictionary<string, object?>? parameters = null, IAppUpdates? updates = null) where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(session);
        services.AddSingleton<IAppUpdates>(updates ?? new DisabledAppUpdates("0.10.0-test.2", "Updates sind in der Vorschau deaktiviert."));
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, StaticNavigation>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TComponent>(parameters is null
                ? ParameterView.Empty : ParameterView.FromDictionary(parameters));
            return rendered.ToHtmlString();
        });
    }

    private sealed class StaticUpdates(UpdateState state) : IAppUpdates
    {
        public UpdateState State => state;
        public event Action? Changed { add { } remove { } }
        public Task CheckAsync() => Task.CompletedTask;
        public Task DownloadAsync() => Task.CompletedTask;
        public Task SetBetaAsync(bool enabled) => Task.CompletedTask;
        public Task RequestRestartAsync() => Task.CompletedTask;
    }

    private static string ButtonAttributes(string markup, string label)
    {
        var button = Regex.Matches(markup, "<button(?<attributes>[^>]*)>(?<content>.*?)</button>", RegexOptions.Singleline)
            .First(match => WebUtility.HtmlDecode(Regex.Replace(match.Groups["content"].Value, "<[^>]+>", "")).Trim() == label);
        return button.Groups["attributes"].Value;
    }

    private static IEnumerable<string> AriaButtons(string markup, string label) =>
        Regex.Matches(WebUtility.HtmlDecode(markup), "<button(?<attributes>[^>]*)>")
            .Where(match => match.Groups["attributes"].Value.Contains("aria-label=\"" + label + "\"", StringComparison.Ordinal))
            .Select(match => match.Groups["attributes"].Value);

    private static string AriaButtonAttributes(string markup, string label) => Assert.Single(AriaButtons(markup, label));

    private static void AssertNoGarmothControls(string markup)
    {
        Assert.DoesNotContain("type=\"password\"", markup);
        foreach (Match match in Regex.Matches(WebUtility.HtmlDecode(markup), "<button(?<attributes>[^>]*)>(?<content>.*?)</button>", RegexOptions.Singleline))
        {
            var control = match.Groups["attributes"].Value + " " + Regex.Replace(match.Groups["content"].Value, "<[^>]+>", "");
            Assert.DoesNotMatch("(?i)hochladen|API-Schlüssel|API-Key", control);
        }
        Assert.DoesNotContain("Stündlich automatisch hochladen", WebUtility.HtmlDecode(markup));
    }

    private static string FieldSelectAttributes(string markup, string label)
    {
        var match = Regex.Match(WebUtility.HtmlDecode(markup),
            "<span>" + Regex.Escape(label) + "</span>\\s*<select(?<attributes>[^>]*)>");
        Assert.True(match.Success, $"Missing settings select: {label}");
        return match.Groups["attributes"].Value;
    }

    private static string ToggleAttributes(string markup, string label)
    {
        var match = Regex.Match(WebUtility.HtmlDecode(markup),
            "<strong>" + Regex.Escape(label) + "</strong>(?:(?!</label>).)*?<input(?<attributes>[^>]*)>", RegexOptions.Singleline);
        Assert.True(match.Success, $"Missing settings switch: {label}");
        return match.Groups["attributes"].Value;
    }

    private static bool IsDisabled(string attributes) => Regex.IsMatch(attributes, @"\bdisabled(?:\s|=|$)");

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Static component rendering must not invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("https://0.0.0.1/", "https://0.0.0.1/");
        protected override void NavigateToCore(string uri, bool forceLoad) =>
            throw new InvalidOperationException("Static rendering must not navigate.");
    }

    private sealed class SnapshotSession : ITrackerSession
    {
        public event Action? Changed { add { } remove { } }
        public TrackerState State { get; init; } = new();
        public TrackerPreferences Preferences { get; init; } = new() { MonitorDeviceName = "synthetic" };
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [new("synthetic", "Testbildschirm", new(0, 0, 1920, 1080), true)];
        public IReadOnlyList<LootHistoryEntry> History { get; init; } = [];
        public LootPriceSnapshot Prices { get; init; } = LootPriceCatalog.FixedSnapshot("eu");
        public int CommandCalls { get; private set; }
        private Task<TrackerCommandResult> Command() { CommandCalls++; return Task.FromResult(TrackerCommandResult.Success); }
        public Task<TrackerCommandResult> ToggleTrackingAsync() => Command();
        public Task<TrackerCommandResult> PauseAsync() => Command();
        public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Command();
        public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Command();
        public Task<TrackerCommandResult> NewSessionAsync() => Command();
        public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Command();
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false) { CommandCalls++; return Task.FromResult(new PreferenceSaveResult()); }
        public Task<TrackerCommandResult> UploadAsync() => Command();
        public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => Command();
        public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals,
            string? characterClass = null) => Command();
        public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity) => Command();
        public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => Command();
        public Task RefreshPricesAsync() => Command();
        public Task TickAsync() => Command();
        public Task PrepareUpdateRestartAsync() => Task.CompletedTask;
        public Task RunPreparedUpdateAsync(Func<Task> install) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
