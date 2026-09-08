using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.App.Services;
using Microsoft.Web.WebView2.Core;
using Velopack;

namespace BdoGrindTracker.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Hooks must exit before Windows, WebView2, OCR or user data are touched.
        // Downloading an update never authorizes an implicit restart of another session.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
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
        using var instance = new Mutex(false, @"Global\Grindcrest-" +
            System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value);
        if (!preview && !smokeTest)
        {
            bool acquired;
            try { acquired = instance.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                MessageBox.Show("Grindcrest läuft bereits. Bitte verwende das geöffnete Fenster.",
                    "Grindcrest", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
        }
        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            int? debugPort = null;
            var debugArgument = args.FirstOrDefault(arg => arg.StartsWith("--ui-debug-port=", StringComparison.Ordinal));
            if (debugArgument is not null)
            {
                if (!int.TryParse(debugArgument["--ui-debug-port=".Length..], out var port) || port is < 1024 or > 65535)
                    throw new ArgumentException("Ungültiger UI-Debug-Port.");
                debugPort = port;
            }
            ITrackerSession session;
            if (preview)
            {
                session = new PreviewTrackerSession(emptyPreview);
            }
            else
            {
                var analyzer = FrameAnalyzerFactory.Create();
                if (startupSmokeTest && !analyzer.IsAvailable)
                {
                    Console.Error.WriteLine(analyzer.Status);
                    analyzer.Dispose();
                    return 2;
                }
                var monitors = Screen.AllScreens.Select((screen, index) => new TrackerMonitor(
                    screen.DeviceName,
                    $"Bildschirm {index + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · Hauptbildschirm" : ""),
                    screen.Bounds, screen.Primary)).OrderByDescending(screen => screen.IsPrimary).ToArray();
                session = new TrackerSessionService(new PassiveCaptureSession(new PassiveScreenCapture()),
                    analyzer, new SettingsStore(), monitors);
            }
            var hidden = preview && args.Contains("--ui-hidden", StringComparer.OrdinalIgnoreCase);
            using var form = new HybridMainForm(session, smokeTest, debugPort, preview, hidden);
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
