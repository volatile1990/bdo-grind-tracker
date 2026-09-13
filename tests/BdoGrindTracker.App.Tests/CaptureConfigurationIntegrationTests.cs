using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Ocr;
using System.Text.Json;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public void CaptureCatalogUsesTheSelectedExternalInstallationAndItsOwnGameOptions()
    {
        using var files = new CaptureConfigurationFiles();
        var local = files.Configuration("default", "41", 1920, 1080);
        var external = files.Configuration("backup", Path.Combine("42", "character", "nested"), 2560, 1440);
        var catalog = new CaptureConfigurationCatalog(files.Installation("default"));
        var calibration = catalog.Read(external);
        Assert.Equal(external, calibration.GameVariablePath);
        Assert.Equal(Path.Combine(files.Installation("backup"), "GameOption.txt"), calibration.GameOptionPath);
        Assert.Equal(2560, calibration.ScreenWidth);
        Assert.Equal(1440, calibration.ScreenHeight);
        Assert.Null(calibration.ActiveCharacterGameVariablePath);
        var option = catalog.Inspect(external);
        Assert.True(option.IsValid);
        Assert.Equal(CompanionNormalLootGeometry.CalculatePanelBounds(calibration), option.NormalBounds);
        Assert.Equal(CompanionNormalLootGeometry.CalculateRareBandCrop(calibration), option.RareBounds);
        var scan = catalog.Scan(external);
        Assert.Equal(external, scan.ActivePath);
        Assert.Null(scan.Error);
        Assert.Contains(scan.Candidates, candidate => candidate.Path == local);
        Assert.Contains(scan.Candidates, candidate => candidate.Path == external);
    }

    [Fact]
    public void CaptureCatalogPreservesTheNormalGuardAndTheOptionalSpecialPanelBoundary()
    {
        using var files = new CaptureConfigurationFiles();
        var hiddenMain = files.Configuration("default", "41", xml:
            "<UIData><UIData Index='159' IsShow='false'/>" +
            "<UIData Index='161' IsShow='true' RelativePosX='0.6' RelativePosY='0.3'/></UIData>");
        var invalidSpecial = files.Configuration("default", "42", xml:
            "<UIData><UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.7'/>" +
            "<UIData Index='161' IsShow='true' RelativePosX='NaN' RelativePosY='0.3'/></UIData>");
        var catalog = new CaptureConfigurationCatalog(files.Installation("default"));
        Assert.Throws<LootPanelUnavailableException>(() => catalog.Read(hiddenMain));
        var rejected = catalog.Inspect(hiddenMain);
        Assert.False(rejected.IsValid);
        Assert.Null(rejected.NormalBounds);
        var allowed = catalog.Inspect(invalidSpecial);
        Assert.True(allowed.IsValid);
        Assert.NotNull(allowed.NormalBounds);
        Assert.Null(allowed.RareBounds);
        Assert.Contains("Ungültige", allowed.RareStatus);
        var invalidSelection = catalog.Scan(hiddenMain);
        Assert.Null(invalidSelection.ActivePath);
        Assert.NotNull(invalidSelection.Error);
        Assert.Contains(invalidSelection.Candidates, candidate => candidate.Path == invalidSpecial && candidate.IsValid);
    }

    [Fact]
    public async Task CaptureConfigurationSelectionPersistsThroughSettingsAndServiceReload()
    {
        using var files = new CaptureConfigurationFiles();
        var selected = files.Configuration("default", "41");
        await using var fixture = CreateCaptureConfigurationFixture(files);
        var saved = await fixture.Service.SelectCaptureConfigurationAsync(selected);
        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal(selected, fixture.Service.Preferences.CaptureConfigurationPath);
        var persisted = new SettingsStore(fixture.DirectoryPath).Load();
        Assert.Equal(selected, persisted.CaptureConfigurationPath);
        await using var reloaded = new Fixture(autoUpload: false, saveKey: false, initialSettings: persisted);
        Assert.Equal(selected, reloaded.Service.Preferences.CaptureConfigurationPath);
        Assert.Equal(selected, (await fixture.Service.ScanCaptureConfigurationsAsync()).ActivePath);
    }

    [Fact]
    public async Task InvalidCaptureConfigurationCannotChangeTheSavedOrActiveAnalyzer()
    {
        using var files = new CaptureConfigurationFiles();
        var selected = files.Configuration("default", "41");
        var invalid = files.Configuration("default", "42", xml: "<incomplete");
        var replacements = 0;
        await using var fixture = CreateCaptureConfigurationFixture(files, selected, createAnalyzer: (_, _) =>
        {
            replacements++;
            return new SyntheticAnalyzer();
        });
        var before = File.ReadAllText(fixture.SettingsPath);
        var result = await fixture.Service.SelectCaptureConfigurationAsync(invalid);
        Assert.False(result.Succeeded);
        Assert.Equal(selected, fixture.Service.Preferences.CaptureConfigurationPath);
        Assert.Equal(before, File.ReadAllText(fixture.SettingsPath));
        Assert.False(fixture.Analyzer.Disposed);
        Assert.Equal(0, replacements);
        Assert.Equal(selected, (await fixture.Service.ScanCaptureConfigurationsAsync()).ActivePath);
    }

    [Fact]
    public async Task CaptureConfigurationCannotChangeDuringAnExistingPausedSession()
    {
        using var files = new CaptureConfigurationFiles();
        var selected = files.Configuration("default", "41");
        var alternate = files.Configuration("default", "42");
        await using var fixture = CreateCaptureConfigurationFixture(files, selected);
        fixture.Begin();
        await fixture.Service.PauseAsync();
        Assert.True(fixture.Service.State.HasSession);
        Assert.False(fixture.Service.State.IsRunning);
        var result = await fixture.Service.SelectCaptureConfigurationAsync(alternate);
        Assert.False(result.Succeeded);
        Assert.Equal(selected, fixture.Service.Preferences.CaptureConfigurationPath);
        Assert.Equal(selected, fixture.Settings.Load().CaptureConfigurationPath);
        Assert.False(fixture.Analyzer.Disposed);
        var directPreferenceChange = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
            { CaptureConfigurationPath = alternate });
        Assert.False(directPreferenceChange.Succeeded);
        Assert.Equal(selected, fixture.Service.Preferences.CaptureConfigurationPath);
    }

    [Fact]
    public async Task CapturePreviewOfAnotherConfigurationDoesNotAnalyzeCountOrSelectIt()
    {
        using var files = new CaptureConfigurationFiles();
        var selected = files.Configuration("default", "41");
        var previewed = files.Configuration("default", "42", normalX: "0.6");
        var captures = 0;
        await using var fixture = CreateCaptureConfigurationFixture(files, selected, onCapture: () => captures++);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Stone", 3));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var beforeTotals = fixture.Service.State.Loot.Totals.OrderBy(pair => pair.Key).ToArray();
        var beforeSettings = File.ReadAllText(fixture.SettingsPath);
        var analysisCalls = fixture.Analyzer.Calls;
        var completionCalls = fixture.Analyzer.CompletionCalls;
        var preview = await fixture.Service.PreviewCaptureConfigurationAsync(previewed);
        Assert.Null(preview.Error);
        Assert.Equal(previewed, preview.Configuration!.Path);
        Assert.StartsWith("data:image/jpeg;base64,", preview.ImageDataUrl);
        Assert.StartsWith("data:image/png;base64,", preview.NormalImageDataUrl);
        Assert.StartsWith("data:image/png;base64,", preview.RareImageDataUrl);
        Assert.Equal(1, captures);
        Assert.Equal(analysisCalls, fixture.Analyzer.Calls);
        Assert.Equal(completionCalls, fixture.Analyzer.CompletionCalls);
        Assert.Equal(beforeTotals, fixture.Service.State.Loot.Totals.OrderBy(pair => pair.Key));
        Assert.Equal(selected, fixture.Service.Preferences.CaptureConfigurationPath);
        Assert.Equal(beforeSettings, File.ReadAllText(fixture.SettingsPath));
        Assert.False(fixture.Service.State.IsRunning);
    }

    [Fact]
    public async Task CapturePreviewReportsResolutionMismatchWithoutChangingConfigurationOrCounts()
    {
        using var files = new CaptureConfigurationFiles();
        var selected = files.Configuration("default", "41");
        await using var fixture = CreateCaptureConfigurationFixture(files, selected, frameSize: new(1280, 720));
        var preview = await fixture.Service.PreviewCaptureConfigurationAsync(selected);
        Assert.Contains("1280 × 720", preview.Error);
        Assert.Contains("1920 × 1080", preview.Error);
        Assert.Null(preview.ImageDataUrl);
        Assert.Null(preview.NormalImageDataUrl);
        Assert.Null(preview.RareImageDataUrl);
        Assert.Equal(0, fixture.Analyzer.Calls);
        Assert.Empty(fixture.Service.State.Loot.Totals);
        Assert.Equal(selected, fixture.Service.Preferences.CaptureConfigurationPath);
    }

    [Fact]
    public async Task CapturePreviewRefusesRunningTrackingBeforeCapturingAnyPixels()
    {
        using var files = new CaptureConfigurationFiles();
        var selected = files.Configuration("default", "41");
        var captures = 0;
        await using var fixture = CreateCaptureConfigurationFixture(files, selected, onCapture: () => captures++);
        fixture.Begin();
        var preview = await fixture.Service.PreviewCaptureConfigurationAsync(selected);
        Assert.NotNull(preview.Error);
        Assert.Equal(0, captures);
        Assert.Equal(0, fixture.Analyzer.Calls);
        Assert.Equal(selected, fixture.Service.Preferences.CaptureConfigurationPath);
        Assert.True(fixture.Service.State.IsRunning);
    }

    [Fact]
    public async Task CaptureAnalyzerFactoryReceivesNewSavedSelectionAndReloadsTheSameChangedFile()
    {
        using var files = new CaptureConfigurationFiles();
        var initial = files.Configuration("default", "41");
        var selected = files.Configuration("default", "42", normalX: "0.6");
        var calibrations = new List<CompanionCalibration>();
        await using var fixture = CreateCaptureConfigurationFixture(files, initial, createAnalyzer: (current, _) =>
        {
            calibrations.Add(new CaptureConfigurationCatalog(files.Installation("default"))
                .Read(current.Service.Preferences.CaptureConfigurationPath));
            return new SyntheticAnalyzer();
        });
        var changed = await fixture.Service.SelectCaptureConfigurationAsync(selected);
        Assert.True(changed.Succeeded, changed.Error);
        Assert.True(fixture.Analyzer.Disposed);
        Assert.Equal(selected, Assert.Single(calibrations).GameVariablePath);
        Assert.Equal(1152, calibrations[0].LootAnchorX);
        files.Configuration("default", "42", normalX: "0.7");
        var reloaded = await fixture.Service.SelectCaptureConfigurationAsync(selected);
        Assert.True(reloaded.Succeeded, reloaded.Error);
        Assert.Equal(2, calibrations.Count);
        Assert.Equal(selected, calibrations[1].GameVariablePath);
        Assert.Equal(1344, calibrations[1].LootAnchorX);
        Assert.Equal(selected, fixture.Settings.Load().CaptureConfigurationPath);
    }

    [Fact]
    public async Task RecoveredSettingsRebuildTheAnalyzerForTheirChangedCaptureConfiguration()
    {
        using var files = new CaptureConfigurationFiles();
        var initial = files.Configuration("default", "41");
        var recovered = files.Configuration("default", "42", normalX: "0.6");
        var paths = new List<string>();
        await using var fixture = CreateCaptureConfigurationFixture(files, initial, createAnalyzer: (current, _) =>
        {
            paths.Add(current.Service.Preferences.CaptureConfigurationPath!);
            return new SyntheticAnalyzer();
        });
        PrepareRecoveredCaptureSettings(fixture, recovered);
        Assert.Equal(initial, fixture.Service.Preferences.CaptureConfigurationPath);
        var result = await fixture.Service.SaveSessionAsync();
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(recovered, fixture.Service.Preferences.CaptureConfigurationPath);
        Assert.Equal(recovered, fixture.Settings.Load().CaptureConfigurationPath);
        Assert.Equal(new[] { recovered }, paths);
        Assert.True(fixture.Analyzer.Disposed);
    }

    [Fact]
    public async Task RecoveredCaptureConfigurationWaitsForOldAnalysisBeforeReplacingItsAnalyzer()
    {
        using var files = new CaptureConfigurationFiles();
        var initial = files.Configuration("default", "41");
        var recovered = files.Configuration("default", "42");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new PassiveCaptureSession(_ => new Bitmap(1920, 1080), frameInterval: TimeSpan.FromDays(1));
        var replacements = 0;
        await using var fixture = CreateCaptureConfigurationFixture(files, initial, createAnalyzer: (_, _) =>
        {
            replacements++;
            return new SyntheticAnalyzer();
        }, suppliedCapture: capture);
        SetField(capture, "_pendingAnalysis", pending.Task);
        try
        {
            PrepareRecoveredCaptureSettings(fixture, recovered);
            Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
            Assert.Equal(recovered, fixture.Service.Preferences.CaptureConfigurationPath);
            Assert.Equal(0, replacements);
            Assert.False(fixture.Analyzer.Disposed);
            var blocked = await fixture.Service.ToggleTrackingAsync();
            Assert.False(blocked.Succeeded);
            Assert.False(fixture.Service.State.HasSession);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.Equal(0, replacements);
        }
        finally { pending.TrySetResult(); }
        var resumed = await fixture.Service.ToggleTrackingAsync();
        Assert.True(resumed.Succeeded, resumed.Error);
        Assert.Equal(1, replacements);
        Assert.True(fixture.Analyzer.Disposed);
        Assert.Equal(recovered, fixture.Service.Preferences.CaptureConfigurationPath);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
    }

    [Fact]
    public async Task FailedCaptureSettingsSaveKeepsThePreviousSelectionAndAnalyzerUntilRetry()
    {
        using var files = new CaptureConfigurationFiles();
        var initial = files.Configuration("default", "41");
        var selected = files.Configuration("default", "42");
        var replacements = 0;
        await using var fixture = CreateCaptureConfigurationFixture(files, initial, createAnalyzer: (_, _) =>
        {
            replacements++;
            return new SyntheticAnalyzer();
        });
        using (var locked = new FileStream(fixture.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = await fixture.Service.SelectCaptureConfigurationAsync(selected);
            Assert.False(failed.Succeeded);
            Assert.Contains("nicht gespeichert", failed.Error);
            Assert.Equal(initial, fixture.Service.Preferences.CaptureConfigurationPath);
            Assert.Equal(0, replacements);
            Assert.False(fixture.Analyzer.Disposed);
        }
        Assert.Equal(initial, fixture.Settings.Load().CaptureConfigurationPath);
        var retried = await fixture.Service.SelectCaptureConfigurationAsync(selected);
        Assert.True(retried.Succeeded, retried.Error);
        Assert.Equal(selected, fixture.Service.Preferences.CaptureConfigurationPath);
        Assert.Equal(selected, fixture.Settings.Load().CaptureConfigurationPath);
        Assert.Equal(1, replacements);
    }

    [Fact]
    public async Task ActiveCapturePathAndPausedPreviewReflectTheBoundAnalyzerGeometry()
    {
        using var files = new CaptureConfigurationFiles();
        var boundPath = files.Configuration("default", "41");
        var catalog = new CaptureConfigurationCatalog(files.Installation("default"));
        var calibration = catalog.Read(boundPath);
        var newlyAutomatic = files.Configuration("default", "42", normalX: "0.7");
        files.Configuration("default", "41", normalX: "0.6");
        File.SetLastWriteTimeUtc(newlyAutomatic, DateTime.UtcNow.AddMinutes(1));
        await using var fixture = CreateCaptureConfigurationFixture(files);
        using var bound = new BoundConfigurationAnalyzer(calibration);
        SetField(fixture.Service, "_analyzer", bound);
        Assert.Equal(newlyAutomatic, catalog.Scan(null).ActivePath);
        Assert.Equal(boundPath, (await fixture.Service.ScanCaptureConfigurationsAsync()).ActivePath);
        fixture.Begin();
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var preview = await fixture.Service.PreviewCaptureConfigurationAsync(boundPath);
        Assert.Null(preview.Error);
        Assert.True(preview.UsesSessionGeometry);
        Assert.Equal(CompanionNormalLootGeometry.CalculatePanelBounds(calibration), preview.Configuration!.NormalBounds);
        Assert.NotEqual(catalog.Inspect(boundPath).NormalBounds, preview.Configuration.NormalBounds);
        Assert.Equal(0, bound.AnalysisCalls);
        Assert.Empty(fixture.Service.State.Loot.Totals);
        Assert.Null(fixture.Service.Preferences.CaptureConfigurationPath);
    }

    private static void PrepareRecoveredCaptureSettings(Fixture fixture, string path)
    {
        var settings = fixture.Settings.Load();
        settings.CaptureConfigurationPath = path;
        File.WriteAllText(fixture.SettingsPath, "{incomplete");
        fixture.Settings.Load();
        Assert.NotNull(fixture.Settings.LoadError);
        File.WriteAllText(fixture.SettingsPath, JsonSerializer.Serialize(settings));
    }

    private static Fixture CreateCaptureConfigurationFixture(CaptureConfigurationFiles files, string? selected = null,
        Size? frameSize = null, Action? onCapture = null, Func<Fixture, string, ILootFrameAnalyzer>? createAnalyzer = null,
        PassiveCaptureSession? suppliedCapture = null)
    {
        var size = frameSize ?? new Size(1920, 1080);
        var fixture = new Fixture(autoUpload: false, saveKey: false,
            initialSettings: new AppSettings { CaptureConfigurationPath = selected },
            suppliedCapture: suppliedCapture ?? new PassiveCaptureSession(_ =>
            {
                onCapture?.Invoke();
                return new Bitmap(size.Width, size.Height);
            }, frameInterval: TimeSpan.FromDays(1)));
        SetField(fixture.Service, "_captureConfigurations", new CaptureConfigurationCatalog(files.Installation("default")));
        SetField(fixture.Service, "_analyzerFactory", (Func<string, ILootFrameAnalyzer>)(language =>
            createAnalyzer?.Invoke(fixture, language) ?? new SyntheticAnalyzer()));
        return fixture;
    }

    private sealed class BoundConfigurationAnalyzer(CompanionCalibration calibration) : ILootFrameAnalyzer
    {
        public bool IsAvailable => true;
        public string Status => "Synthetic bound configuration";
        public CompanionCalibration CaptureCalibration => calibration;
        public int AnalysisCalls { get; private set; }
        public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt, CancellationToken cancellationToken)
        {
            AnalysisCalls++;
            return Task.FromResult(new FrameAnalysisResult([], [], 1, "synthetic-bound-config", 0, 0, 0, 0, null));
        }
        public void Reset() { }
        public void Dispose() { }
    }

    private sealed class CaptureConfigurationFiles : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "grindcrest-config-integration-" + Guid.NewGuid().ToString("N"));
        public string Installation(string name) => Path.Combine(root, name);

        public string Configuration(string installation, string profile, int width = 1920, int height = 1080,
            string? xml = null, string normalX = "0.5")
        {
            var directory = Installation(installation);
            var path = Path.Combine(directory, "UserCache", profile, "gameVariable.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(Path.Combine(directory, "GameOption.txt"),
                $"width = {width}\nheight = {height}\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");
            File.WriteAllText(path, xml ??
                $"<UIData><UIData Index='159' IsShow='true' RelativePosX='{normalX}' RelativePosY='0.7'/>" +
                "<UIData Index='161' IsShow='true' RelativePosX='0.6' RelativePosY='0.3'/></UIData>");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
