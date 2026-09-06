using System.Runtime.ExceptionServices;
using System.Reflection;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class MainFormStartupTests
{
    [Fact]
    public void BundledAnalyzerCanBeInitialized()
    {
        using var analyzer = FrameAnalyzerFactory.Create();

        Assert.True(analyzer.IsAvailable, analyzer.Status);
    }

    [Fact]
    public void MainFormCanBeConstructedBeforeItsFirstLayout()
    {
        using var settings = new IsolatedSettingsStore();
        RunInSta(() =>
        {
            using var form = CreateForm(settings.Store);

            Assert.Equal(new Size(860, 640), form.MinimumSize);
            Assert.Equal(AppBranding.WindowTitle, form.Text);
            Assert.True(form.ShowIcon);
            Assert.NotNull(form.Icon);
            Assert.NotNull(FindByAccessibleName<PictureBox>(form, "Grindcrest-Logo").Image);
            BdoWindowChromeTests.AssertScreenshotCaptureEnabled(form);
            form.Size = form.MinimumSize;
            form.CreateControl();
            form.PerformLayout();

            Assert.False(form.IsDisposed);
            Assert.Empty(FindDescendants<DataGridView>(form));
            Assert.Equal(3m, Assert.Single(FindDescendants<NumericUpDown>(form)).Value);
            Assert.DoesNotContain(FindDescendants<Label>(form), label => label.Text == "GESAMTMENGE");
            Assert.Empty(FindDescendants<SplitContainer>(form));
            Assert.Single(FindDescendants<LootTotalsView>(form));
            Assert.Empty(FindDescendants<LiveDetectionDebugView>(form));
            Assert.Empty(FindDescendants<DetectionRegionPreview>(form));
            Assert.All(FindDescendants<TextBox>(form), textBox => Assert.IsAssignableFrom<UpDownBase>(textBox.Parent));
            Assert.Equal(7, FindDescendants<BdoButton>(form).Count);
            Assert.DoesNotContain(FindDescendants<Label>(form), label => label.Text is "DROPS" or "ITEMARTEN");
            Assert.Equal("0", FindByAccessibleName<Label>(form, "Silberwert vor Steuer").Text);
            Assert.Equal("0", FindByAccessibleName<Label>(form, "Silberwert nach Steuer").Text);
            Assert.Equal("00:00:00", FindByAccessibleName<Label>(
                form, "Dauer der aktuellen Grindsession").Text);
        });
    }

    [Fact]
    public void StartupDoesNotRequireAManualSpotSelection()
    {
        using var settings = new IsolatedSettingsStore();
        RunInSta(() =>
        {
            using var form = CreateForm(settings.Store);
            var trackingButton = FindByAccessibleName<BdoButton>(
                form,
                "Tracking starten oder pausieren");

            Assert.DoesNotContain(FindDescendants<ComboBox>(form),
                comboBox => comboBox.AccessibleName == "Grindspot");
            Assert.Equal("Spot: wird aus Trashloot erkannt",
                FindByAccessibleName<Label>(form, "Automatisch erkannter Grindspot").Text);
            Assert.NotEmpty(FindByAccessibleName<ComboBox>(form, "Spielmonitor").Items);
            Assert.True(trackingButton.Enabled);
        });
    }

    [Fact]
    public void LiveSnapshotUsesContractRegionAndPreservesUnknownPricesWithoutStartingPublisher()
    {
        using var settings = new IsolatedSettingsStore();
        RunInSta(() =>
        {
            using var form = CreateForm(settings.Store);
            Assert.Null(form.CreateLiveSnapshot());
            Assert.Null(typeof(MainForm).GetField("_livePublisher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form));
            SetPrivateField(form, "_hasSession", true);
            SetPrivateField(form, "_sessionStartedAt", (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1));
            var snapshot = Assert.IsType<Grindcrest.Live.LiveSessionUpdate>(form.CreateLiveSnapshot());
            Assert.Equal("EU", snapshot.Region);
            Assert.Null(snapshot.SilverAfterTax);
            Assert.True(snapshot.Paused);
            SetPrivateField(form, "_sessionSubmitted", true);
            Assert.Null(form.CreateLiveSnapshot());
        });
    }

    [Fact]
    public void ASavedManualSpotDoesNotOverrideAutomaticDetection()
    {
        using var settings = new IsolatedSettingsStore();
        settings.Store.Save(new AppSettings { SpotId = LootSpotCatalog.AphrodonId });
        RunInSta(() =>
        {
            using var form = CreateForm(settings.Store);

            Assert.Equal("Spot: wird aus Trashloot erkannt",
                FindByAccessibleName<Label>(form, "Automatisch erkannter Grindspot").Text);
            Assert.True(FindByAccessibleName<BdoButton>(
                form, "Tracking starten oder pausieren").Enabled);
        });
    }

    [Fact]
    public void DiagnosticRecordingStartsOffAndSessionLocksAllOptions()
    {
        using var settings = new IsolatedSettingsStore();
        RunInSta(() =>
        {
            using var form = CreateForm(settings.Store);
            var monitorPicker = FindByAccessibleName<ComboBox>(form, "Spielmonitor");
            var eventLoot = Assert.Single(
                FindDescendants<CheckBox>(form),
                checkBox => checkBox.Text == "Event-Loot zulassen");
            var recording = Assert.Single(
                FindDescendants<CheckBox>(form),
                checkBox => checkBox.Text == "Loot-Diagnose lokal aufzeichnen");
            var options = FindByAccessibleName<Control>(form, "Tracking-Optionen");
            var optionsButton = FindByAccessibleName<BdoButton>(
                form, "Tracking-Optionen öffnen oder schließen");

            Assert.False(recording.Checked);
            Assert.False(eventLoot.Checked);
            Assert.Contains(monitorPicker, FindDescendants<ComboBox>(options));
            Assert.Contains(eventLoot, FindDescendants<CheckBox>(options));
            Assert.Contains(recording, FindDescendants<CheckBox>(options));
            Assert.DoesNotContain(FindDescendants<CheckBox>(form),
                checkBox => checkBox.AccessibleName == "Live-Log anzeigen");

            SetPrivateField(form, "_hasSession", true);
            InvokePrivateMethod(form, "UpdateControlState");

            Assert.False(monitorPicker.Enabled);
            Assert.False(eventLoot.Enabled);
            Assert.False(recording.Enabled);
            Assert.True(optionsButton.Enabled);
        });
    }

    [Fact]
    public void AutomaticSpotIsDisplayedFromTheAnalysisWithoutRequiringLiveLog()
    {
        using var settings = new IsolatedSettingsStore();
        RunInSta(() =>
        {
            using var form = CreateForm(settings.Store);
            var field = typeof(MainForm).GetField("_uiMailbox", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var mailbox = Assert.IsType<FrameUiMailbox>(field.GetValue(form));
            mailbox.Publish(new FrameAnalysisResult([], [], 0, "test", 0, 0, 0, 0, null)
            {
                SpotId = LootSpotCatalog.HermesiaId,
            });

            InvokePrivateMethod(form, "RefreshPendingUi");

            Assert.Equal("Spot: Hermesia Inner Castle",
                FindByAccessibleName<Label>(form, "Automatisch erkannter Grindspot").Text);
            Assert.All(FindDescendants<TextBox>(form), textBox => Assert.IsAssignableFrom<UpDownBase>(textBox.Parent));
        });
    }

    [Fact]
    public void DashboardDoesNotEnumerateRemovedDebugLogDecisions()
    {
        using var settings = new IsolatedSettingsStore();
        RunInSta(() =>
        {
            using var form = CreateForm(settings.Store);
            var field = typeof(MainForm).GetField("_uiMailbox", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var mailbox = Assert.IsType<FrameUiMailbox>(field.GetValue(form));
            mailbox.Publish(new FrameAnalysisResult([], [], 0, "test", 0, 0, 0, 0, null)
            {
                TrackingResult = new TrackerFrameResult([], new UnexpectedDecisionAccess()),
            });

            InvokePrivateMethod(form, "RefreshPendingUi");

            Assert.All(FindDescendants<TextBox>(form), textBox => Assert.IsAssignableFrom<UpDownBase>(textBox.Parent));
        });
    }

    private static MainForm CreateForm(SettingsStore settingsStore) =>
        new(
            new PassiveScreenCapture(),
            new StubFrameAnalyzer(),
            settingsStore,
            garmothKeyStore: new GarmothApiKeyStore(Path.Combine(
                Path.GetTempPath(), "BdoGrindTracker.Tests", "absent-" + Guid.NewGuid().ToString("N"))));

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI construction did not finish.");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static TControl FindByAccessibleName<TControl>(
        Control root,
        string accessibleName)
        where TControl : Control =>
        Assert.Single(
            FindDescendants<TControl>(root),
            control => control.AccessibleName == accessibleName);

    private static void SetPrivateField<TValue>(
        object target,
        string fieldName,
        TValue value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static void InvokePrivateMethod(object target, string methodName)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, null);
    }

    private static IReadOnlyList<TControl> FindDescendants<TControl>(Control root)
        where TControl : Control
    {
        var results = new List<TControl>();
        foreach (Control child in root.Controls)
        {
            if (child is TControl match)
            {
                results.Add(match);
            }

            results.AddRange(FindDescendants<TControl>(child));
        }

        return results;
    }

    private sealed class StubFrameAnalyzer : ILootFrameAnalyzer
    {
        public bool IsAvailable => true;

        public string Status => "Test";

        public Task<FrameAnalysisResult> AnalyzeAsync(
            Bitmap frame,
            DateTimeOffset capturedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnexpectedDecisionAccess : IReadOnlyList<LootTrackingDecision>
    {
        public int Count => throw new InvalidOperationException("Disabled log accessed decisions.");
        public LootTrackingDecision this[int index] => throw new InvalidOperationException("Disabled log accessed decisions.");
        public IEnumerator<LootTrackingDecision> GetEnumerator() => throw new InvalidOperationException("Disabled log enumerated decisions.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class IsolatedSettingsStore : IDisposable
    {
        private readonly string directory;
        private readonly string settingsPath;

        public IsolatedSettingsStore()
        {
            directory = Path.Combine(
                Path.GetTempPath(),
                "BdoGrindTracker.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            settingsPath = Path.Combine(directory, "settings.json");
            Store = new SettingsStore();
            SetPrivateField(Store, "_settingsPath", settingsPath);
        }

        public SettingsStore Store { get; }

        public void Dispose()
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }
}
