using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Services;
using Microsoft.Web.WebView2.Core;

namespace BdoGrindTracker.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
#if DEBUG
        if (args.Any(a => a.StartsWith("--demo-sessions=", StringComparison.Ordinal)))
            return Development.DemoSessionGenerator.Run(args);
#endif
        if (args.Length > 0 && string.Equals(args[0], "--replay", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Aufruf: BdoGrindTracker.exe --replay <observations.jsonl>");
                return 2;
            }
            try
            {
                // Offline only: no capture, BDO configuration read, OCR engine or UI.
                var result = LootDiagnosticReplay.Run(args[1]);
                var report = result.ToDisplayText();
                Console.WriteLine(report);
                // WinExe usually has no attached console. Retain the requested result
                // beside the recording without overwriting any existing report.
                var reportPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,
                    $"replay-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
                File.WriteAllText(reportPath, report);
                return result.TotalsMatch && result.EventTimelineMatches ? 0 : 3;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }
        var startupSmokeTest = args.Contains("--startup-smoke-test", StringComparer.OrdinalIgnoreCase);
        var uiSmokeTest = args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase);
        var emptyPreview = args.Contains("--ui-preview-empty", StringComparer.OrdinalIgnoreCase);
        var preview = uiSmokeTest || emptyPreview || args.Contains("--ui-preview", StringComparer.OrdinalIgnoreCase);
        var smokeTest = startupSmokeTest || uiSmokeTest;
        var takeOverStoreSession = !preview && !smokeTest && args.Contains(
            StoreSessionViewerLaunch.TakeoverArgument, StringComparer.OrdinalIgnoreCase);
        string? viewerDirectory;
        try
        {
            viewerDirectory = preview || smokeTest ? null : StoreSessionViewerLaunch.Resolve(
                AppContext.BaseDirectory, Updates.AppUpdateRuntime.Current.PackageIdentity, args);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Startfehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        var instanceMutexName = @"Global\Grindcrest-" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        using var instance = new Mutex(false, instanceMutexName);
        if (!preview && !smokeTest && (viewerDirectory is null || takeOverStoreSession))
        {
            bool acquired;
            try { acquired = instance.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                MessageBox.Show(takeOverStoreSession ? StoreSessionTakeover.BusyReason
                    : "Grindcrest läuft bereits. Du kannst es über das Grindcrest-Symbol im Infobereich der Taskleiste öffnen.",
                    "Grindcrest", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
        }
        try
        {
            // Lower only this tracker process before OCR/native workers and
            // WebView2 are created. Validation and read-only viewers retain
            // their normal scheduling; no game process is queried or changed.
            if (!preview && !smokeTest && (viewerDirectory is null || takeOverStoreSession))
                CpuScheduling.ApplyBackgroundProcessPriority();
            if (takeOverStoreSession)
            {
                // Resolve shared paths only in the new writer process, after it owns the same lock as the Store app.
                Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryVariable, viewerDirectory);
                viewerDirectory = null;
            }
            AppDataPaths.PrepareForStartup(useUserData: !preview && !smokeTest && viewerDirectory is null);
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            int? debugPort = null;
            var debugArgument = args.FirstOrDefault(arg => arg.StartsWith("--ui-debug-port=", StringComparison.Ordinal));
            if (debugArgument is not null)
            {
                if (!int.TryParse(debugArgument["--ui-debug-port=".Length..], out var port) || port is < 1024 or > 65535)
                    throw new ArgumentException("Ungültiger UI-Debug-Port.");
                debugPort = port;
            }
            HybridMainForm? capturePromptOwner = null;
            ITrackerSession session;
            if (viewerDirectory is not null)
            {
                var sourceDirectory = viewerDirectory;
                session = new StoreSessionViewerSession(sourceDirectory, takeOver: async () =>
                {
                    var result = await Task.Run(() => StoreSessionTakeover.Start(
                        Environment.ProcessPath!, sourceDirectory, instanceMutexName));
                    if (result.Succeeded && capturePromptOwner is { } owner)
                        owner.BeginInvoke((Action)(() => owner.Close()));
                    return result;
                });
            }
            else if (preview)
            {
                session = new PreviewTrackerSession(emptyPreview);
            }
            else
            {
                var settingsStore = new SettingsStore();
                var analyzer = FrameAnalyzerFactory.Create(captureConfigurationPath: settingsStore.Load().CaptureConfigurationPath);
                if (startupSmokeTest)
                {
                    var available = analyzer.IsAvailable;
                    var status = analyzer.Status;
                    analyzer.Dispose();
                    if (!available)
                    {
                        Console.Error.WriteLine(status);
                        return 2;
                    }
                    session = new PreviewTrackerSession(empty: true);
                }
                else
                {
                    var monitors = Screen.AllScreens.Select((screen, index) => new TrackerMonitor(
                        screen.DeviceName,
                        $"Bildschirm {index + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · Hauptbildschirm" : ""),
                        screen.Bounds, screen.Primary)).OrderByDescending(screen => screen.IsPrimary).ToArray();
                    session = new TrackerSessionService(new PassiveCaptureSession(new PassiveWindowCapture()),
                        analyzer, settingsStore, monitors, benchmarkProvider: new GarmothGrindBenchmarkProvider(
                            Path.Combine(settingsStore.BaseDirectory, GarmothGrindBenchmarkProvider.CacheFileName),
                            token => capturePromptOwner?.ReadGarmothBenchmarksAsync(token) ??
                                Task.FromException<GarmothBenchmarkPayload>(new IOException("Das App-Fenster ist noch nicht bereit."))),
                        prepareWindowCapture: () => capturePromptOwner?.PrepareWindowCaptureAsync() ?? Task.FromResult(false));
                }
            }
            var hidden = (preview || viewerDirectory is not null) && args.Contains("--ui-hidden", StringComparer.OrdinalIgnoreCase);
            using var form = new HybridMainForm(session, smokeTest, debugPort, preview, hidden);
            capturePromptOwner = form;
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception exception)
        {
            var message = exception is WebView2RuntimeNotFoundException
                ? "Die Microsoft Edge WebView2-Laufzeit fehlt. Bitte die WebView2 Evergreen Runtime von Microsoft installieren und Grindcrest erneut starten.\nhttps://developer.microsoft.com/microsoft-edge/webview2/"
                : exception.Message;
            if (smokeTest) Console.Error.WriteLine(message);
            else MessageBox.Show($"{AppBranding.Name} konnte nicht gestartet werden.\n\n{message}",
                "Startfehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
