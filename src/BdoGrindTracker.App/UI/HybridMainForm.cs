using System.Diagnostics;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using BdoGrindTracker.App.Updates;

namespace BdoGrindTracker.App.UI;

/// <summary>Only window lifetime and the Blazor host live here; tracking is independent of rendering.</summary>
internal sealed class HybridMainForm : Form
{
    private readonly ITrackerSession _session;
    private readonly BlazorWebView _web = new() { Dock = DockStyle.Fill };
    private readonly ServiceProvider _services;
    private readonly IAppUpdates _updates;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly bool _smokeTest;
    private readonly bool _hidden;
    private readonly Stopwatch _startup = Stopwatch.StartNew();
    private bool _ticking;
    private bool _closing;
    private bool _closed;
    private bool _webReady;
    private bool _renderReady;
    private bool _resourcesDisposed;

    public int ExitCode { get; private set; }

    public HybridMainForm(ITrackerSession session, bool smokeTest = false, int? debugPort = null, bool preview = false, bool hidden = false)
    {
        _session = session;
        _smokeTest = smokeTest;
        _hidden = smokeTest || hidden;
        Text = AppBranding.WindowTitle + (preview ? " · Vorschau" : "");
        Icon = AppBranding.CreateWindowIcon();
        BackColor = Color.FromArgb(13, 17, 24);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(860, 640);
        Size = new Size(1320, 900);
        StartPosition = FormStartPosition.Manual;
        var screen = Screen.AllScreens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen;
        if (screen is not null)
        {
            var work = screen.WorkingArea;
            Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
        }
        if (_hidden) { Opacity = 0; ShowInTaskbar = false; }

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<ITrackerSession>(session);
        _updates = AppUpdateService.Create(!preview && !smokeTest,
            () => session.State.IsRunning, () => session.State.IsBusy, PrepareUpdateRestartAsync);
        services.AddSingleton<IAppUpdates>(_updates);
        _services = services.BuildServiceProvider();
        _web.Services = _services;
        _web.HostPage = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        _web.RootComponents.Add<TrackerApp>("#app");
        _web.BlazorWebViewInitializing += (_, args) =>
        {
            args.UserDataFolder = preview || smokeTest
                ? Path.Combine(Path.GetTempPath(), "Grindcrest.UiValidation", Environment.ProcessId.ToString())
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BdoGrindTracker", "webview2");
            if (debugPort is not null)
                args.EnvironmentOptions = new CoreWebView2EnvironmentOptions(
                    $"--remote-debugging-port={debugPort} --remote-debugging-address=127.0.0.1");
        };
        _web.BlazorWebViewInitialized += (_, args) =>
        {
            _webReady = true;
            args.WebView.DefaultBackgroundColor = BackColor;
            args.WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            args.WebView.CoreWebView2.Settings.AreDevToolsEnabled = debugPort is not null;
            args.WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            args.WebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            args.WebView.CoreWebView2.ProcessFailed += (_, failure) => FailStartup(
                "Die Oberfläche konnte nicht ausgeführt werden: " + failure.ProcessFailedKind);
        };
        Controls.Add(_web);
        _timer.Tick += Tick;
        Shown += (_, _) => _timer.Start();
        FormClosing += CloseAsync;
    }

    protected override bool ShowWithoutActivation => _hidden;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // WebView content uses logical pixels. Size the initial window after its
        // monitor DPI is known so 150% scaling does not halve the usable dashboard.
        var scale = DeviceDpi / 96f;
        var work = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min((int)(860 * scale), work.Width), Math.Min((int)(640 * scale), work.Height));
        Size = new Size(Math.Min((int)(1320 * scale), work.Width), Math.Min((int)(900 * scale), work.Height));
        Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BdoWindowChrome.Apply(this);
    }

    protected override void WndProc(ref Message message)
    {
        const int wmAppCommand = 0x0319;
        if (message.Msg == wmAppCommand)
        {
            var command = ((long)message.LParam >> 16) & 0x0fff;
            if (command is 1 or 2)
            {
                // WebView2 handles mouse X1/X2 itself. Only handle browser commands
                // that reach the parent unhandled (e.g. a mouse driver/keyboard).
                if (_webReady && !_closing)
                {
                    var browser = _web.WebView.CoreWebView2;
                    if (command == 1 && browser.CanGoBack) browser.GoBack();
                    if (command == 2 && browser.CanGoForward) browser.GoForward();
                }
                message.Result = (IntPtr)1;
                return;
            }
        }
        base.WndProc(ref message);
    }

    private async void Tick(object? sender, EventArgs args)
    {
        if (_ticking || _closing) return;
        _ticking = true;
        try
        {
            if (!_renderReady)
            {
                if (_startup.Elapsed > TimeSpan.FromSeconds(30))
                {
                    FailStartup("Blazor konnte innerhalb von 30 Sekunden nicht gestartet werden.");
                    return;
                }
                if (_webReady)
                {
                    var ready = await _web.WebView.ExecuteScriptAsync(
                        "!!document.querySelector('[data-app-ready=\"true\"]') && getComputedStyle(document.querySelector('#blazor-error-ui')).display === 'none'");
                    if (ready == "true")
                    {
                        _renderReady = true;
                        Console.WriteLine("Blazor Hybrid ready");
                        if (_smokeTest) { Close(); return; }
                        _ = _updates.CheckAsync();
                    }
                }
                if (!_renderReady) return;
            }
            await _session.TickAsync();
        }
        catch (Exception error)
        {
            FailStartup(error.Message);
        }
        finally { _ticking = false; }
    }

    private void FailStartup(string message)
    {
        if (_closing) return;
        ExitCode = 1;
        if (_hidden) Console.Error.WriteLine(message);
        else MessageBox.Show(this, message, "Grindcrest", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    private async void CloseAsync(object? sender, FormClosingEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        Enabled = false;
        try { await _session.ShutdownAsync(); await _session.DisposeAsync(); }
        catch (Exception error) { ExitCode = 1; Console.Error.WriteLine(error.Message); }
        finally { _closed = true; Close(); }
    }

    private Task<bool> PrepareUpdateRestartAsync(Action scheduleApply)
    {
        if (!InvokeRequired) return PrepareUpdateRestartCoreAsync(scheduleApply);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(async () =>
        {
            try { completion.SetResult(await PrepareUpdateRestartCoreAsync(scheduleApply)); }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }

    private async Task<bool> PrepareUpdateRestartCoreAsync(Action scheduleApply)
    {
        // Recheck at the host boundary: tracking may have resumed since rendering.
        if (_closing || _session.State.IsRunning || _session.State.IsBusy) return false;
        _closing = true;
        _timer.Stop();
        Enabled = false;
        try
        {
            // Keep the live service available for retry until the paused session
            // has a durable snapshot. Shutdown itself disposes the service.
            await _session.PrepareUpdateRestartAsync();
        }
        catch (Exception)
        {
            _closing = false;
            Enabled = true;
            _timer.Start();
            MessageBox.Show(this,
                "Deine Session konnte nicht gespeichert werden. Grindcrest bleibt geöffnet und das Update wartet. " +
                "Bitte prüfe den freien Speicherplatz und versuche es erneut.",
                "Session noch nicht gespeichert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        try
        {
            await _session.ShutdownAsync();
            await _session.DisposeAsync();
            if (_session.State.ShutdownFailed)
                throw new IOException("Die Session konnte nicht vollständig gespeichert werden.");
            // Start the updater's bounded exit wait only after all saves finish.
            scheduleApply();
            return true;
        }
        catch (Exception)
        {
            ExitCode = 1;
            MessageBox.Show(this,
                "Das Update wurde nicht gestartet, weil das Speichern oder Vorbereiten fehlgeschlagen ist. " +
                "Bitte starte Grindcrest erneut und prüfe deinen Verlauf. Das Update kannst du danach erneut versuchen.",
                "Update nicht installiert", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            _closed = true;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _timer.Dispose();
            _web.Dispose();
            _services.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
