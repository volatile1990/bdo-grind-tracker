using System.Drawing.Imaging;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class MainFormDashboardTests(ITestOutputHelper output)
{
    [Fact]
    public void SessionDurationRefreshesEvenWhenNoNewAnalysisHasArrived()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            using var form = CreateForm(settings.Store, clock);
            var duration = FindByAccessibleName<Label>(form, "Dauer der aktuellen Grindsession");
            Assert.Equal("00:00:00", duration.Text);

            clock.Start();
            time.Advance(TimeSpan.FromSeconds(3723));
            Invoke(form, "RefreshPendingUi");
            Assert.Equal("01:02:03", duration.Text);

            time.Advance(TimeSpan.FromSeconds(1));
            Invoke(form, "RefreshPendingUi");
            Assert.Equal("01:02:04", duration.Text);

            clock.Pause();
            time.Advance(TimeSpan.FromHours(1));
            Invoke(form, "RefreshPendingUi");
            Assert.Equal("01:02:04", duration.Text);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void NewSessionClearsDurationLootAndAutomaticallyDetectedSpot()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            using var form = CreateForm(settings.Store, clock);
            clock.Start();
            time.Advance(TimeSpan.FromMinutes(20));
            clock.Pause();
            PublishSyntheticLoot(form);
            SetField(form, "_hasSession", true);
            Invoke(form, "RefreshPendingUi");
            Assert.Equal(12, Find<LootTotalsView>(form).Single().EntryCount);

            Invoke(form, "ResetButton_Click", null, EventArgs.Empty);

            Assert.Equal("00:00:00", FindByAccessibleName<Label>(
                form, "Dauer der aktuellen Grindsession").Text);
            Assert.Equal("Spot: wird aus Trashloot erkannt", FindByAccessibleName<Label>(
                form, "Automatisch erkannter Grindspot").Text);
            Assert.False(clock.IsRunning);
            Assert.Equal(TimeSpan.Zero, clock.Elapsed);
            Assert.Equal(0, Find<LootTotalsView>(form).Single().EntryCount);
            Assert.True(FindByAccessibleName<ComboBox>(form, "Spielmonitor").Enabled);
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(1160, 840, false, false, "default-empty")]
    [InlineData(1160, 840, true, false, "default-loot")]
    [InlineData(860, 640, true, false, "minimum-loot")]
    [InlineData(1160, 840, true, true, "default-options")]
    [InlineData(860, 640, true, true, "minimum-options")]
    public void DashboardKeepsLootProminentAndRendersWithoutShowingAWindow(
        int width,
        int height,
        bool hasLoot,
        bool showOptions,
        string previewName)
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            using var form = CreateForm(settings.Store, clock);
            form.Size = new Size(width, height);

            if (hasLoot)
            {
                GetField<ComboBox>(form, "_classOverrideComboBox").SelectedItem =
                    CompanionCharacterClassCatalog.FindById("maegu-awakening");
                clock.Start();
                time.Advance(TimeSpan.FromSeconds(3723));
                PublishSyntheticLoot(form);
                SetField(form, "_hasSession", true);
                SetField(form, "_uiRunning", true);
                Invoke(form, "UpdateControlState");
                Invoke(form, "RefreshPendingUi");
                SetActiveStatus(form);
            }

            if (showOptions)
            {
                Invoke(form, "ToggleOptions");
            }

            LayoutRecursively(form);
            using var bitmap = new Bitmap(form.Width, form.Height);
            // Draw only this test-owned form into memory; no Show(), focus,
            // desktop screenshot, capture session, or game interaction occurs.
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            Assert.False(form.Visible);
            Assert.True(CountBodyColors(bitmap) >= 6, "The offscreen dashboard is blank or its child controls were not rendered.");
            SavePreviewWhenRequested(bitmap, previewName);

            var totals = Assert.Single(Find<LootTotalsView>(form));
            output.WriteLine($"{previewName}: form={form.Size}, client={form.ClientSize}, DPI={form.DeviceDpi}, loot={BoundsInForm(totals, form)}");
            foreach (var layout in Find<TableLayoutPanel>(form))
            {
                output.WriteLine($"Table layout {layout.AccessibleName}: {BoundsInForm(layout, form)}, rows=[{string.Join(", ", layout.GetRowHeights())}]");
            }
            Assert.Equal(hasLoot ? 12 : 0, totals.EntryCount);
            Assert.True(totals.Width >= form.ClientSize.Width * 0.85,
                $"Loot viewport is unexpectedly narrow: {totals.Size} within {form.ClientSize}.");
            var minimumHeight = showOptions ? 100 : width == 860 ? 220 : 400;
            Assert.True(totals.Height >= minimumHeight,
                $"Loot viewport needs at least {minimumHeight}px: actual {totals.Size}.");
            Assert.True(form.ClientRectangle.Contains(BoundsInForm(totals, form)),
                $"Loot viewport extends outside the dashboard: {BoundsInForm(totals, form)}.");
            Assert.Empty(Find<DetectionRegionPreview>(form));
            Assert.Empty(Find<LiveDetectionDebugView>(form));
            Assert.All(Find<TextBox>(form), textBox => Assert.IsAssignableFrom<UpDownBase>(textBox.Parent));
            var duration = FindByAccessibleName<Label>(form, "Dauer der aktuellen Grindsession");
            var metricCard = Assert.IsType<BdoSurfacePanel>(duration.Parent!.Parent);
            Assert.True(metricCard.Parent!.ClientRectangle.Contains(metricCard.Bounds),
                "Session card is clipped by the summary row.");

        });
    }

    [Fact]
    public void OptionsCanBeOpenedAndClosedWithoutChangingTheSession()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            using var form = CreateForm(settings.Store);
            PublishSyntheticLoot(form);
            Invoke(form, "RefreshPendingUi");
            LayoutRecursively(form);
            var totals = Assert.Single(Find<LootTotalsView>(form));
            var collapsedHeight = totals.Height;

            Invoke(form, "ToggleOptions");
            LayoutRecursively(form);
            Assert.True(totals.Height < collapsedHeight);
            Assert.Equal(12, totals.EntryCount);

            Invoke(form, "ToggleOptions");
            LayoutRecursively(form);
            Assert.Equal(collapsedHeight, totals.Height);
            Assert.Equal(12, totals.EntryCount);
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(860, 1f)]
    [InlineData(860, 1.25f)]
    [InlineData(860, 1.5f)]
    [InlineData(860, 2f)]
    [InlineData(1160, 1f)]
    [InlineData(1160, 1.25f)]
    [InlineData(1160, 1.5f)]
    [InlineData(1160, 2f)]
    public void EveryMetricFitsLongValuesAndLargerTextWithoutClipping(int width, float textScale)
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            using var form = CreateForm(settings.Store);
            form.Size = new Size(width, width == 860 ? 640 : 840);
            PublishSyntheticLoot(form);
            Invoke(form, "RefreshPendingUi");
            var values = new Dictionary<string, string>
            {
                ["_sessionDurationValue"] = "123:45:56",
                ["_silverBeforeTaxValue"] = "≥ 12.345.678.901",
                ["_silverAfterTaxValue"] = "≥ 10.432.098.765"
            };
            using var largerFont = new Font("Segoe UI Semibold", 25f * textScale, FontStyle.Bold);
            foreach (var (field, value) in values)
            {
                var label = GetField<MetricValueLabel>(form, field);
                // Exercise independent text growth, including an unscaled layout:
                // measuring only the outer card previously missed this regression.
                label.Font = largerFont;
                label.Text = value;
            }
            LayoutRecursively(form);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var graphics = Graphics.FromImage(bitmap);
            foreach (var (field, value) in values)
            {
                var label = GetField<MetricValueLabel>(form, field);
                var measured = label.MeasureRenderedText(graphics);
                Assert.Equal(value, label.Text);
                Assert.False(label.AutoEllipsis);
                Assert.True(measured.Width + label.Padding.Horizontal <= label.ClientSize.Width,
                    $"{field} width: {measured} exceeds {label.ClientSize} at text scale {textScale}.");
                Assert.True(measured.Height + label.Padding.Vertical <= label.ClientSize.Height,
                    $"{field} height: {measured} exceeds {label.ClientSize} at text scale {textScale}.");
                for (Control child = label; child.Parent is { } parent; child = parent)
                {
                    Assert.True(parent.ClientRectangle.Contains(child.Bounds),
                        $"{child.GetType().Name} clipped by {parent.GetType().Name}: {child.Bounds} in {parent.ClientRectangle}.");
                }
            }
            SavePreviewWhenRequested(bitmap, $"metrics-{width}-{textScale * 100:0}");
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void AutomaticPauseStopsTheSessionAfterThreeIdleMinutesAndPreservesLoot()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            var activity = new GrindInactivityTimer(time);
            using var form = CreateForm(settings.Store, clock, activity);
            PublishSyntheticLoot(form);
            Invoke(form, "RefreshPendingUi");
            SetField(form, "_hasSession", true);
            SetField(form, "_uiRunning", true);
            clock.Start();
            activity.Start();
            time.Advance(TimeSpan.FromSeconds(179));
            InvokeTask(form, "PauseIfInactiveAsync");
            Assert.True(clock.IsRunning);
            activity.RecordDrop();
            time.Advance(TimeSpan.FromSeconds(179));
            InvokeTask(form, "PauseIfInactiveAsync");
            Assert.True(clock.IsRunning);
            time.Advance(TimeSpan.FromSeconds(1));
            InvokeTask(form, "PauseIfInactiveAsync");
            Assert.False(clock.IsRunning);
            Assert.False(activity.IsRunning);
            Assert.False(GetField<bool>(form, "_uiRunning"));
            Assert.Equal(12, Find<LootTotalsView>(form).Single().EntryCount);
            Assert.Contains("Automatisch pausiert", GetField<Label>(form, "_statusLabel").Text);
            Assert.Equal("Fortsetzen", GetField<BdoButton>(form, "_trackingButton").Text);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void AutoPauseSettingPersistsAndCanBeChangedDuringTheSession()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            settings.Store.Save(new AppSettings { AutoPauseMinutes = 7 });
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            var activity = new GrindInactivityTimer(time);
            using var form = CreateForm(settings.Store, clock, activity);
            var input = GetField<NumericUpDown>(form, "_autoPauseMinutes");
            Assert.Equal(7m, input.Value);
            SetField(form, "_hasSession", true);
            SetField(form, "_uiRunning", true);
            Invoke(form, "UpdateControlState");
            Assert.True(input.Enabled);
            clock.Start();
            activity.Start();
            time.Advance(TimeSpan.FromMinutes(2));
            input.Value = 1;
            Assert.Equal(1, settings.Store.Load().AutoPauseMinutes);
            InvokeTask(form, "PauseIfInactiveAsync");
            Assert.False(clock.IsRunning);
            Assert.Contains("Seit 1 Minute", GetField<Label>(form, "_statusLabel").Text);
        });
    }

    private static void InvokeTask(object target, string name)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(target, null));
        PumpUntilCompleted(task);
    }

    private static void PumpUntilCompleted(Task task)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            // Pump only this test-owned STA's messages so async UI continuations run.
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Assert.True(task.IsCompleted, "Async dashboard operation did not finish.");
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void AutomaticClassDetectionIsDisplayedAndManualCorrectionRemainsPossible()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var detected = CompanionCharacterClassCatalog.FindById("warrior-awakening")!;
            using var form = CreateForm(settings.Store, detector: () =>
                new CharacterClassDetection(detected, CharacterClassDetectionStatus.Detected, 4));
            InvokeTask(form, "RefreshClassDetectionAsync");
            var label = GetField<Label>(form, "_characterClassLabel");
            Assert.Equal($"Klasse: {detected.DisplayName}", label.Text);
            var selection = GetField<ComboBox>(form, "_classOverrideComboBox");
            var correction = CompanionCharacterClassCatalog.FindById("ranger-succession")!;
            selection.SelectedItem = correction;
            Assert.Equal($"Klasse: {correction.DisplayName}", label.Text);
            InvokeTask(form, "RefreshClassDetectionAsync");
            Assert.Equal($"Klasse: {correction.DisplayName}", label.Text);
            SetField(form, "_hasSession", true);
            SetField(form, "_uiRunning", true);
            Invoke(form, "UpdateControlState");
            Assert.False(selection.Enabled);
            SetField(form, "_uiRunning", false);
            Invoke(form, "UpdateControlState");
            Assert.True(selection.Enabled);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void AmbiguousClassDoesNotBecomeAGuessedClass()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            using var form = CreateForm(settings.Store, detector: () =>
                new CharacterClassDetection(null, CharacterClassDetectionStatus.Ambiguous, 2));
            InvokeTask(form, "RefreshClassDetectionAsync");
            Assert.Contains("mehrdeutig", GetField<Label>(form, "_characterClassLabel").Text);
            Assert.True(GetField<BdoButton>(form, "_trackingButton").Enabled);
            Assert.False(form.Visible);
        });
    }

    private static MainForm CreateForm(SettingsStore settings, GrindSessionClock? clock = null,
        GrindInactivityTimer? activity = null, Func<CharacterClassDetection>? detector = null,
        ILootPriceProvider? prices = null, GarmothUploadClient? uploadClient = null,
        GarmothApiKeyStore? keyStore = null) =>
        new(new PassiveScreenCapture(), new NoCaptureAnalyzer(), settings, clock, activity, detector,
            prices ?? new SyntheticPriceProvider(), uploadClient,
            keyStore ?? new GarmothApiKeyStore(Path.Combine(Path.GetTempPath(),
                "BdoGrindTracker.Tests", "absent-" + Guid.NewGuid().ToString("N"))));

    [Fact]
    public void SilverCardsValueExistingTotalsAndTaxChangesDoNotChangeLoot()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var prices = new SyntheticPriceProvider
            {
                Cached = region => new LootPriceSnapshot(region,
                [new("Black Crystal Fragment", 0, 160539, LootPriceOrigin.FixedCatalog, null),
                 new("Black Stone", 1001, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.UtcNow)])
            };
            using var form = CreateForm(settings.Store, prices: prices);
            Assert.Equal(0, prices.Calls);
            PublishItems(form, ("Black Crystal Fragment", 2), ("Black Stone", 3));
            Assert.Equal("324.081", GetField<MetricValueLabel>(form, "_silverBeforeTaxValue").Text);
            Assert.Equal("323.028", GetField<MetricValueLabel>(form, "_silverAfterTaxValue").Text);
            var before = GetField<LootSessionSnapshot>(form, "_sessionSummary");

            Invoke(form, "ApplySilverPreferences", "eu", new SilverTaxOptions(ValuePack: true));
            Assert.Equal("323.613", GetField<MetricValueLabel>(form, "_silverAfterTaxValue").Text);
            Assert.Same(before, GetField<LootSessionSnapshot>(form, "_sessionSummary"));
            Assert.True(settings.Store.Load().SilverValuePack);
            PublishItems(form, ("Black Stone", -1));
            Assert.Equal("323.080", GetField<MetricValueLabel>(form, "_silverBeforeTaxValue").Text);
            Assert.Equal("322.768", GetField<MetricValueLabel>(form, "_silverAfterTaxValue").Text);
            Invoke(form, "RefreshPendingUi");
            Assert.Equal("322.768", GetField<MetricValueLabel>(form, "_silverAfterTaxValue").Text);
            Assert.Equal(0, prices.Calls);
        });
    }

    [Fact]
    public void UnknownPricesAreNotDisplayedAsAnExactZeroAndKnownZeroRemainsValid()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            using var form = CreateForm(settings.Store);
            PublishItems(form, ("Unknown item", 1));
            Assert.Equal("—", GetField<MetricValueLabel>(form, "_silverBeforeTaxValue").Text);
            Assert.Contains("Preise fehlen", GetField<Label>(form, "_priceStatusLabel").Text);
            PublishItems(form, ("Black Crystal Fragment", 1));
            Assert.Equal("≥ 160.539", GetField<MetricValueLabel>(form, "_silverAfterTaxValue").Text);
            Assert.Contains("Unknown item", GetField<Label>(form, "_priceStatusLabel").AccessibleDescription);
            PublishItems(form, ("Unknown item", -1), ("Black Crystal Fragment", -1), ("Embers of Ynix - Armor", 1));
            Assert.Equal("0", GetField<MetricValueLabel>(form, "_silverAfterTaxValue").Text);
            Assert.DoesNotContain("Preise fehlen", GetField<Label>(form, "_priceStatusLabel").Text);
        });
    }

    [Fact]
    public void StaleMarketPricesAreMarkedWithoutAffectingTrashOnlySessions()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var prices = new SyntheticPriceProvider
            {
                Cached = region => new LootPriceSnapshot(region,
                [new("Black Crystal Fragment", 0, 160539, LootPriceOrigin.FixedCatalog, null),
                 new("Black Stone", 1001, 0, LootPriceOrigin.CachedMarket, DateTimeOffset.UnixEpoch, true)],
                    DateTimeOffset.UnixEpoch)
            };
            using var form = CreateForm(settings.Store, prices: prices);
            PublishItems(form, ("Black Crystal Fragment", 1));
            Assert.DoesNotContain("letzter Preisstand", GetField<Label>(form, "_priceStatusLabel").Text);
            PublishItems(form, ("Black Stone", 1));
            Assert.Contains("letzter Preisstand", GetField<Label>(form, "_priceStatusLabel").Text);
            Assert.Equal("161.189", GetField<MetricValueLabel>(form, "_silverAfterTaxValue").Text);
        });
    }

    [Fact]
    public void SlowPricesDoNotBlockLootAndAnOldRegionResponseCannotReplaceNewPrices()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var prices = new SyntheticPriceProvider
            {
                Pending = new(TaskCreationOptions.RunContinuationsAsynchronously),
                Cached = region => new LootPriceSnapshot(region,
                    [new("Black Stone", region == "eu" ? 1000 : 2000, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.UtcNow)])
            };
            using var form = CreateForm(settings.Store, prices: prices);
            var refresh = Assert.IsAssignableFrom<Task>(typeof(MainForm)
                .GetMethod("RefreshPricesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null));
            Assert.False(refresh.IsCompleted);
            var secondRefresh = Assert.IsAssignableFrom<Task>(typeof(MainForm)
                .GetMethod("RefreshPricesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null));
            Assert.Same(refresh, secondRefresh);
            Assert.Equal(1, prices.Calls);
            PublishItems(form, ("Black Stone", 3));
            Assert.Equal("3.000", GetField<MetricValueLabel>(form, "_silverBeforeTaxValue").Text);
            Invoke(form, "ApplySilverPreferences", "na", new SilverTaxOptions());
            Assert.Equal("6.000", GetField<MetricValueLabel>(form, "_silverBeforeTaxValue").Text);
            prices.Pending.SetResult(new LootPriceSnapshot("eu",
                [new("Black Stone", 9999, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.UtcNow)]));
            PumpUntilCompleted(refresh);
            Assert.Equal("6.000", GetField<MetricValueLabel>(form, "_silverBeforeTaxValue").Text);
            Assert.StartsWith("NA", GetField<Label>(form, "_priceStatusLabel").Text);
            Assert.Equal(3, GetField<LootSessionSnapshot>(form, "_sessionSummary").TotalQuantity);
            Assert.False(form.Visible);
        });
    }

    private static void PublishItems(MainForm form, params (string Name, int Quantity)[] items)
    {
        GetField<FrameUiMailbox>(form, "_uiMailbox").Publish(new FrameAnalysisResult(items.Select(item =>
            new LootEventView(Guid.NewGuid(), DateTimeOffset.UtcNow, item.Name, item.Quantity)).ToArray(),
            [], 1, "synthetic-pricing-test", 0, 0, 0, 0, null));
        Invoke(form, "RefreshPendingUi");
    }

    [Fact]
    public void OneClickUploadUsesSavedKeyAndNetValuePausesAndOmitsUnsupportedItems()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            settings.KeyStore.Save("synthetic-api-key-not-a-real-secret");
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            var requests = 0;
            using var client = new GarmothUploadClient(new MockUploadHandler(async request =>
            {
                requests++;
                Assert.Equal("synthetic-api-key-not-a-real-secret", request.Headers.GetValues("apiKey").Single());
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal(321078L, json.RootElement.GetProperty("total").GetInt64());
                Assert.Equal(9632340L, json.RootElement.GetProperty("hourly").GetInt64());
                Assert.Equal(2L, json.RootElement.GetProperty("minutes").GetInt64());
                Assert.Equal(214, json.RootElement.GetProperty("grindspot_id").GetInt32());
                var drops = json.RootElement.GetProperty("drops");
                Assert.Equal(2, drops.GetProperty("980128_0").GetInt64());
                Assert.Single(drops.EnumerateObject());
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            using var form = CreateForm(settings.Store, clock, uploadClient: client, keyStore: settings.KeyStore);
            PublishItems(form, ("Black Crystal Fragment", 2), ("Pure Black Stone", 1));
            SetField(form, "_sessionSpotId", LootSpotCatalog.HermesiaId);
            SetField(form, "_sessionClass", CompanionCharacterClassCatalog.FindById("warrior-awakening")!);
            SetField(form, "_hasSession", true);
            SetField(form, "_uiRunning", true);
            clock.Start();
            time.Advance(TimeSpan.FromMinutes(2));
            Invoke(form, "UpdateControlState");
            Assert.True(GetField<BdoButton>(form, "_garmothButton").Enabled);
            InvokeTask(form, "UploadToGarmothAsync");
            Assert.Equal(1, requests);
            Assert.False(clock.IsRunning);
            Assert.True(GetField<bool>(form, "_sessionSubmitted"));
            Assert.Equal(2, Find<LootTotalsView>(form).Single().EntryCount);
            Assert.Contains("Pure Black Stone", GetField<Label>(form, "_statusLabel").Text);
            Assert.Contains("Teilsumme", GetField<Label>(form, "_statusLabel").Text);
            InvokeTask(form, "UploadToGarmothAsync");
            Assert.Equal(1, requests);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void MissingKeyDoesNotUploadOrPauseAndSavingItPersistsWithoutPlaintextSettings()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            var requests = 0;
            using var client = new GarmothUploadClient(new MockUploadHandler(_ =>
            {
                requests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }));
            using var form = CreateForm(settings.Store, clock, uploadClient: client, keyStore: settings.KeyStore);
            PublishItems(form, ("Black Crystal Fragment", 1));
            SetField(form, "_uiRunning", true);
            clock.Start();
            InvokeTask(form, "UploadToGarmothAsync");
            Assert.Equal(0, requests);
            Assert.True(clock.IsRunning);
            Assert.Contains("API-Key einmal", GetField<Label>(form, "_statusLabel").Text);
            Invoke(form, "SaveGarmothApiKey", "  synthetic-saved-test-key  ");
            Assert.Equal("synthetic-saved-test-key", settings.KeyStore.Load());
            Assert.Equal("Garmoth-Key ✓", GetField<BdoButton>(form, "_garmothOptionsButton").Text);
            Invoke(form, "SaveGarmothApiKey", "");
            Assert.Equal("", settings.KeyStore.Load());
            Assert.Equal(0, requests);
        });
    }

    private sealed class MockUploadHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }

    [Fact]
    public void UploadWaitsForNewRegionAndCaptureErrorCannotUnlockAnInFlightUpload()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            settings.KeyStore.Save("synthetic-race-test-key");
            var eu = new TaskCompletionSource<LootPriceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var na = new TaskCompletionSource<LootPriceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestedRegions = new List<string>();
            var prices = new SyntheticPriceProvider { Fetch = region =>
            {
                requestedRegions.Add(region);
                return region == "eu" ? eu.Task : na.Task;
            } };
            var requests = 0;
            using var client = new GarmothUploadClient(new MockUploadHandler(async request =>
            {
                requests++;
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal(1300, json.RootElement.GetProperty("total").GetInt64());
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            clock.Start();
            time.Advance(TimeSpan.FromMinutes(2));
            clock.Pause();
            using var form = CreateForm(settings.Store, clock, prices: prices,
                uploadClient: client, keyStore: settings.KeyStore);
            LayoutRecursively(form);
            PublishItems(form, ("Black Stone", 1));
            SetField(form, "_hasSession", true);
            SetField(form, "_sessionSpotId", LootSpotCatalog.HermesiaId);
            SetField(form, "_sessionClass", CompanionCharacterClassCatalog.FindById("warrior-awakening")!);
            var initialPrices = InvokeAsync(form, "RefreshPricesAsync");
            Invoke(form, "ApplySilverPreferences", "na", new SilverTaxOptions());
            var upload = InvokeAsync(form, "UploadToGarmothAsync");
            Assert.False(upload.IsCompleted);
            Invoke(form, "CaptureSession_Stopped", null, new CaptureSessionStoppedEventArgs(new IOException("Synthetic error")));
            Application.DoEvents();
            Assert.False(GetField<BdoButton>(form, "_resetButton").Enabled);
            Assert.False(GetField<BdoButton>(form, "_trackingButton").Enabled);
            Assert.False(GetField<BdoButton>(form, "_garmothButton").Enabled);
            var session = GetField<Guid>(form, "_sessionId");
            Invoke(form, "ResetButton_Click", null, EventArgs.Empty);
            Assert.Equal(session, GetField<Guid>(form, "_sessionId"));
            InvokeTask(form, "UploadToGarmothAsync");
            eu.SetResult(new LootPriceSnapshot("eu", [new("Black Stone", 9999, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.UtcNow)]));
            PumpUntilCompleted(initialPrices);
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (requestedRegions.Count < 2 && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.Equal(new[] { "eu", "na" }, requestedRegions);
            Assert.Equal(0, requests);
            Assert.False(upload.IsCompleted);
            na.SetResult(new LootPriceSnapshot("na", [new("Black Stone", 2000, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.UtcNow)]));
            PumpUntilCompleted(upload);
            Assert.Equal(1, requests);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void CompletelyUnknownSilverDoesNotSendAnInventedZero()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            settings.KeyStore.Save("synthetic-unknown-price-key");
            var requests = 0;
            using var client = new GarmothUploadClient(new MockUploadHandler(_ =>
            {
                requests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }));
            var time = new ManualTimeProvider();
            var clock = new GrindSessionClock(time);
            clock.Start();
            time.Advance(TimeSpan.FromMinutes(2));
            clock.Pause();
            using var form = CreateForm(settings.Store, clock, uploadClient: client, keyStore: settings.KeyStore);
            PublishItems(form, ("Black Stone", 1));
            SetField(form, "_sessionSpotId", LootSpotCatalog.HermesiaId);
            SetField(form, "_sessionClass", CompanionCharacterClassCatalog.FindById("warrior-awakening")!);
            InvokeTask(form, "UploadToGarmothAsync");
            Assert.Equal(0, requests);
            Assert.Equal("—", GetField<MetricValueLabel>(form, "_silverAfterTaxValue").Text);
            Assert.Contains("Noch kein Silberpreis", GetField<Label>(form, "_statusLabel").Text);
            Assert.False(GetField<bool>(form, "_sessionSubmitted"));
        });
    }

    private static Task InvokeAsync(MainForm form, string method) => Assert.IsAssignableFrom<Task>(
        typeof(MainForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null));

    private sealed class SyntheticPriceProvider : ILootPriceProvider
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<LootPriceSnapshot>? Pending { get; set; }
        public Func<string, LootPriceSnapshot> Cached { get; set; } = LootPriceCatalog.FixedSnapshot;
        public Func<string, Task<LootPriceSnapshot>>? Fetch { get; set; }
        public LootPriceSnapshot GetCachedSnapshot(string region) => Cached(region);
        public Task<LootPriceSnapshot> GetSnapshotAsync(string region, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fetch?.Invoke(region) ?? Pending?.Task ?? Task.FromResult(Cached(region));
        }
        public void Dispose() { }
    }

    [Theory]
    [InlineData(nameof(GarmothUploadStatus.Succeeded))]
    [InlineData(nameof(GarmothUploadStatus.OutcomeUnknown))]
    [InlineData(nameof(GarmothUploadStatus.AlreadySubmitted))]
    public void SubmittedOrUncertainSessionCannotResumeOrUploadAgainUntilReset(string statusName)
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            using var form = CreateForm(settings.Store);
            PublishSyntheticLoot(form);
            Invoke(form, "RefreshPendingUi");
            SetField(form, "_hasSession", true);
            Invoke(form, "UpdateControlState");
            var previousId = GetField<Guid>(form, "_sessionId");
            Assert.True(GetField<BdoButton>(form, "_garmothButton").Enabled);
            Invoke(form, "ApplyGarmothResult", new GarmothUploadResult(Enum.Parse<GarmothUploadStatus>(statusName), "Synthetic result"));
            Assert.False(GetField<BdoButton>(form, "_garmothButton").Enabled);
            Assert.False(GetField<BdoButton>(form, "_trackingButton").Enabled);
            Assert.True(GetField<BdoButton>(form, "_resetButton").Enabled);
            Assert.False(GetField<ComboBox>(form, "_classOverrideComboBox").Enabled);
            Invoke(form, "ResetButton_Click", null, EventArgs.Empty);
            Assert.True(GetField<BdoButton>(form, "_trackingButton").Enabled);
            Assert.False(GetField<BdoButton>(form, "_garmothButton").Enabled);
            Assert.Equal(0, Find<LootTotalsView>(form).Single().EntryCount);
            Assert.NotEqual(previousId, GetField<Guid>(form, "_sessionId"));
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void RejectedUploadDoesNotLockTheLocalSession()
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            using var form = CreateForm(settings.Store);
            PublishSyntheticLoot(form);
            Invoke(form, "RefreshPendingUi");
            SetField(form, "_hasSession", true);
            Invoke(form, "ApplyGarmothResult", new GarmothUploadResult(GarmothUploadStatus.Rejected, "Synthetic rejection"));
            Assert.True(GetField<BdoButton>(form, "_trackingButton").Enabled);
            Assert.True(GetField<BdoButton>(form, "_garmothButton").Enabled);
            SetField(form, "_uiRunning", true);
            Invoke(form, "UpdateControlState");
            Assert.True(GetField<BdoButton>(form, "_garmothButton").Enabled);
        });
    }

    [Theory]
    [InlineData(LootSpotCatalog.AphrodonId, "Branch of Abundance")]
    [InlineData(LootSpotCatalog.HermesiaId, "Black Crystal Fragment")]
    [InlineData(LootSpotCatalog.MagaiaId, "Elion Follower's Helmet")]
    public void EverySpotRendersItsOwnTrashIconWithCompactLootCards(string spotId, string trashName)
    {
        RunInSta(() =>
        {
            using var settings = new IsolatedSettingsStore();
            using var form = CreateForm(settings.Store);
            form.Size = new Size(860, 640);
            GetField<ComboBox>(form, "_classOverrideComboBox").SelectedItem =
                CompanionCharacterClassCatalog.FindById("maegu-awakening");
            var items = new (string Name, int Quantity)[]
            {
                (trashName, 1582), ("Ancient Spirit Dust", 71), ("Black Stone", 28),
                ("Caphras Stone", 9), ("Refined Essence of Devouring", 3),
                ("Violet Primordial Pigment - Sovereign", 1)
            };
            var events = items.Select(item => new LootEventView(Guid.NewGuid(),
                DateTimeOffset.UnixEpoch, item.Name, item.Quantity)).ToArray();
            GetField<FrameUiMailbox>(form, "_uiMailbox").Publish(
                new FrameAnalysisResult(events, [], 1, "synthetic-spot-ui", 0, 0, 0, 0, null)
                    { SpotId = spotId });
            Invoke(form, "RefreshPendingUi");
            Assert.Equal($"Spot: {LootSpotCatalog.GetRequired(spotId).DisplayName}",
                GetField<Label>(form, "_activeSpotLabel").Text);
            using var icons = new LootIconRepository(Path.Combine(AppContext.BaseDirectory, "data", "icons"));
            foreach (var (name, _) in items)
                Assert.NotNull(icons.GetIcon(name));
            LayoutRecursively(form);
            BdoWindowChromeTests.AssertScreenshotCaptureEnabled(form);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            SavePreviewWhenRequested(bitmap, $"{spotId}-icons");
            Assert.Equal(6, Find<LootTotalsView>(form).Single().EntryCount);
            Assert.False(form.Visible);
        });
    }

    private static void PublishSyntheticLoot(MainForm form)
    {
        var entries = new (string Name, int Quantity)[]
        {
            ("Black Crystal Fragment", 1582),
            ("Ancient Spirit Dust", 71),
            ("Black Stone", 28),
            ("Caphras Stone", 9),
            ("BON Origin Shard", 3),
            ("Nev's Fragment", 4),
            ("Refined Essence of Devouring", 2),
            ("Refined Origin of Hunger", 1),
            ("Embers of Ynix - Helmet", 2),
            ("Corrupt Oil of Immortality", 1),
            ("Laila's Petal", 2),
            ("Pure Black Stone", 1)
        };
        var events = entries.Select(entry => new LootEventView(
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, entry.Name, entry.Quantity)).ToArray();
        GetField<FrameUiMailbox>(form, "_uiMailbox").Publish(
            new FrameAnalysisResult(events, [], 1, "synthetic-ui-test", 0, 0, 0, 0, null)
            {
                SpotId = LootSpotCatalog.HermesiaId
            });
    }

    private static void SetActiveStatus(MainForm form)
    {
        var method = typeof(MainForm).GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var statusType = method.GetParameters()[0].ParameterType;
        method.Invoke(form, [Enum.Parse(statusType, "Active"), "Tracking aktiv", "Drops werden automatisch erkannt und gezählt."]);
    }

    private static Rectangle BoundsInForm(Control control, Form form)
    {
        var location = Point.Empty;
        for (Control? current = control; current is not null && current != form; current = current.Parent)
        {
            location.Offset(current.Location);
        }

        return new Rectangle(location, control.Size);
    }

    private static void LayoutRecursively(Control root)
    {
        // CreateControl() skips descendants of an unshown form. Reading each
        // handle creates only the test-owned HWNDs without showing the form.
        _ = root.Handle;
        root.PerformLayout();
        foreach (Control child in root.Controls)
        {
            LayoutRecursively(child);
        }
        root.PerformLayout();
    }

    private static void SavePreviewWhenRequested(Bitmap bitmap, string previewName)
    {
        var directory = Environment.GetEnvironmentVariable("BDO_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, $"dashboard-{previewName}.png"), ImageFormat.Png);
    }

    private static int CountBodyColors(Bitmap bitmap)
    {
        var colors = new HashSet<int>();
        for (var y = 60; y < bitmap.Height - 60; y += 13)
        {
            for (var x = 32; x < bitmap.Width - 32; x += 13)
            {
                colors.Add(bitmap.GetPixel(x, y).ToArgb());
            }
        }
        return colors.Count;
    }

    private static IReadOnlyList<T> Find<T>(Control root) where T : Control
    {
        var found = new List<T>();
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                found.Add(match);
            }
            found.AddRange(Find<T>(child));
        }
        return found;
    }

    private static T FindByAccessibleName<T>(Control root, string name) where T : Control =>
        Assert.Single(Find<T>(root), control => control.AccessibleName == name);

    private static T GetField<T>(object target, string name) =>
        Assert.IsType<T>(GetFieldInfo(target, name).GetValue(target));

    private static void SetField(object target, string name, object value) =>
        GetFieldInfo(target, name).SetValue(target, value);

    private static FieldInfo GetFieldInfo(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field;
    }

    private static void Invoke(object target, string name, params object?[]? args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, args);
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Dashboard test did not finish.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class NoCaptureAnalyzer : ILootFrameAnalyzer
    {
        public bool IsAvailable => true;
        public string Status => "Synthetic UI test";
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt,
            CancellationToken cancellationToken) => throw new InvalidOperationException("UI tests must never start capture.");
        public void Reset() { }
        public void Dispose() { }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private sealed class IsolatedSettingsStore : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        private readonly string _settingsPath;

        public IsolatedSettingsStore()
        {
            Directory.CreateDirectory(_directory);
            _settingsPath = Path.Combine(_directory, "settings.json");
            Store = new SettingsStore();
            SetField(Store, "_settingsPath", _settingsPath);
            KeyStore = new GarmothApiKeyStore(Path.Combine(_directory, "test-key.dpapi"));
        }

        public SettingsStore Store { get; }
        public GarmothApiKeyStore KeyStore { get; }

        public void Dispose()
        {
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
            KeyStore.Save("");
            Directory.Delete(_directory);
        }
    }
}
