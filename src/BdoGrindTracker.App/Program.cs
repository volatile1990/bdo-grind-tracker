using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.App.Diagnostics;

namespace BdoGrindTracker.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
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
        var startupSmokeTest = args.Contains(
            "--startup-smoke-test",
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var settingsStore = new SettingsStore();
            var capture = new PassiveScreenCapture();
            var analyzer = FrameAnalyzerFactory.Create();

            if (startupSmokeTest)
            {
                using (analyzer)
                {
                    if (!analyzer.IsAvailable)
                    {
                        Console.Error.WriteLine(analyzer.Status);
                        return 2;
                    }

                    using var form = new MainForm(capture, analyzer, settingsStore);
                    // Construct and lay out the real form without showing, focusing or
                    // capturing anything. This validates a published build safely.
                    LayoutHiddenControlTree(form);
                    ValidateMetricTextBounds(form);
                }

                return 0;
            }

            Application.Run(new MainForm(capture, analyzer, settingsStore));
            return 0;
        }
        catch (Exception exception)
        {
            if (!startupSmokeTest)
            {
                MessageBox.Show(
                    $"{AppBranding.Name} konnte nicht gestartet werden.\n\n{exception.Message}",
                    "Startfehler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return 1;
        }
    }

    private static void LayoutHiddenControlTree(Control control)
    {
        // Create test-owned HWNDs without showing a window. This exercises the
        // executable's real PerMonitorV2 initialization rather than a test host's DPI mode.
        _ = control.Handle;
        control.PerformLayout();
        foreach (Control child in control.Controls)
            LayoutHiddenControlTree(child);
        control.PerformLayout();
    }

    private static void ValidateMetricTextBounds(Control control)
    {
        if (control is MetricValueLabel metric)
        {
            using var graphics = metric.CreateGraphics();
            var measured = metric.MeasureRenderedText(graphics);
            if (measured.Width + metric.Padding.Horizontal > metric.ClientSize.Width ||
                measured.Height + metric.Padding.Vertical > metric.ClientSize.Height)
                throw new InvalidOperationException($"Kennzahl abgeschnitten bei {metric.DeviceDpi} DPI: {metric.Text}");
        }
        foreach (Control child in control.Controls)
            ValidateMetricTextBounds(child);
    }
}
