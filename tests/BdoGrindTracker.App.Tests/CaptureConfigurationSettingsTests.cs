using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class CaptureConfigurationSettingsTests
{
    private static readonly CaptureConfigurationOption First = Option(@"C:\BDO\first\gameVariable.xml");
    private static readonly CaptureConfigurationOption Second = Option(@"C:\BDO\second\gameVariable.xml");
    private static readonly CaptureConfigurationOption Invalid = Option(@"C:\BDO\invalid\gameVariable.xml") with { Error = "Lootposition fehlt." };
    private const string FullImage = "data:image/png;base64,ZnVsbA==";
    private const string NormalImage = "data:image/png;base64,bm9ybWFs";
    private const string RareImage = "data:image/png;base64,cmFyZQ==";

    [Fact]
    public async Task OpeningLoadsConfigurationsOnDemandAndApplyingRequiresAPreview()
    {
        var session = new Session();
        await Render(session, async (component, markup) =>
        {
            Assert.Equal(0, session.Scans);
            Assert.DoesNotContain(First.Path, markup());
            await Invoke(component, "ToggleOpen");
            Assert.Equal(1, session.Scans);
            Assert.Contains(First.Path, ActiveFile(markup()));
            Assert.Contains("100 %", markup());
            await Invoke(component, "Choose", Second);
            await Invoke(component, "Apply");
            Assert.Empty(session.Selections);
            Assert.Empty(session.Previews);
            await Invoke(component, "Preview");
            Assert.Equal(Second.Path, Assert.Single(session.Previews));
            Assert.Equal(First.Path, session.Preferences.CaptureConfigurationPath);
            Assert.Contains(First.Path, ActiveFile(markup()));
            await Invoke(component, "Apply");
            Assert.Equal(Second.Path, Assert.Single(session.Selections));
            Assert.Equal(Second.Path, session.Preferences.CaptureConfigurationPath);
            Assert.Contains(Second.Path, ActiveFile(markup()));
            Assert.Contains("Dateiauswahl gespeichert.", markup());
        });
    }

    [Fact]
    public async Task PreviewShowsExactPixelRectanglesAndBothUnmodifiedCrops()
    {
        await Render(new Session(), async (component, markup) =>
        {
            await Invoke(component, "ToggleOpen");
            await Invoke(component, "Preview");
            var html = markup();
            Assert.Contains("viewBox=\"0 0 1920 1080\"", html);
            Assert.Contains($"href=\"{FullImage}\"", html);
            Assert.Contains($"src=\"{NormalImage}\"", html);
            Assert.Contains($"src=\"{RareImage}\"", html);
            AssertRect(html, "capture-normal-rect", 120, 680, 500, 270);
            AssertRect(html, "capture-rare-rect", 1100, 160, 600, 56);
            Assert.Contains("Türkis: normales Droplog", html);
            Assert.Contains("Gold: Special-Droplog", html);
            Assert.Contains("Special-Droplog", html);
            Assert.DoesNotContain("<form", html);
            ExportReviewIfRequested(html);
            await Invoke(component, "ToggleOpen");
            Assert.DoesNotContain(FullImage, markup());
        });
    }

    [Fact]
    public async Task AFailedSaveLeavesThePreviouslyActiveConfigurationAndOffersRetry()
    {
        var session = new Session { SelectionError = "Die Auswahl konnte nicht gespeichert werden." };
        await Render(session, async (component, markup) =>
        {
            await Invoke(component, "ToggleOpen");
            await Invoke(component, "Choose", Second);
            await Invoke(component, "Preview");
            await Invoke(component, "Apply");
            Assert.Equal(First.Path, session.Preferences.CaptureConfigurationPath);
            Assert.Contains(First.Path, ActiveFile(markup()));
            Assert.Contains("Die Auswahl konnte nicht gespeichert werden.", markup());
            Assert.DoesNotContain("Dateiauswahl gespeichert.", markup());
            session.SelectionError = null;
            await Invoke(component, "Apply");
            Assert.Equal(Second.Path, session.Preferences.CaptureConfigurationPath);
        });
    }

    [Fact]
    public async Task InvalidConfigurationsShowTheirReasonAndCannotBeSelected()
    {
        var session = new Session { BrowseResult = Invalid };
        await Render(session, async (component, markup) =>
        {
            await Invoke(component, "ToggleOpen");
            var invalidCard = Regex.Matches(markup(), "<article[^>]*>.*?</article>", RegexOptions.Singleline)
                .Single(match => match.Value.Contains(Invalid.Path, StringComparison.Ordinal)).Value;
            Assert.Contains("Nicht verwendbar: Lootposition fehlt.", invalidCard);
            Assert.Matches("<input[^>]*disabled", invalidCard);
            await Invoke(component, "Choose", Invalid);
            await Invoke(component, "Browse");
            await Invoke(component, "Preview");
            Assert.Equal(First.Path, Assert.Single(session.Previews));
            Assert.Empty(session.Selections);
        });
    }

    [Fact]
    public async Task PreviewFailureCannotBeAppliedAndChangingSelectionDiscardsAnEarlierPreview()
    {
        var session = new Session();
        await Render(session, async (component, markup) =>
        {
            await Invoke(component, "ToggleOpen");
            await Invoke(component, "Preview");
            await Invoke(component, "Choose", Second);
            Assert.DoesNotContain(FullImage, markup());
            await Invoke(component, "Apply");
            Assert.Empty(session.Selections);
            session.PreviewError = "Das Spielfenster ist minimiert.";
            await Invoke(component, "Preview");
            Assert.Contains(session.PreviewError, markup());
            await Invoke(component, "Apply");
            Assert.Empty(session.Selections);
        });
    }

    [Fact]
    public async Task RunningCaptureBlocksSnapshotsAndExistingSessionBlocksApplying()
    {
        var session = new Session { State = new() { IsRunning = true, HasSession = true } };
        await Render(session, async (component, markup) =>
        {
            await Invoke(component, "ToggleOpen");
            Assert.Contains("zuerst die laufende Erfassung pausieren", markup());
            Assert.True(IsDisabled(markup(), "Vorschau aktualisieren"));
            await Invoke(component, "Preview");
            Assert.Empty(session.Previews);
            session.State = session.State with { IsRunning = false };
            await Invoke(component, "Preview");
            Assert.Single(session.Previews);
            Assert.Contains("vor dem Start einer neuen Session", markup());
            Assert.True(IsDisabled(markup(), "Verwenden"));
            await Invoke(component, "Apply");
            Assert.Empty(session.Selections);
        });
    }

    [Fact]
    public async Task NativeFileChoiceAndAutomaticModeBothNeedExplicitPreviewAndApply()
    {
        var extra = Option(@"D:\Backup\gameVariable.xml");
        var session = new Session { BrowseResult = extra };
        await Render(session, async (component, markup) =>
        {
            await Invoke(component, "ToggleOpen");
            await Invoke(component, "Browse");
            Assert.Contains(extra.Path, markup());
            Assert.Empty(session.Selections);
            await Invoke(component, "Preview");
            Assert.Equal(extra.Path, Assert.Single(session.Previews));
            await Invoke(component, "Apply");
            Assert.Equal(extra.Path, Assert.Single(session.Selections));
            await Invoke(component, "ChooseAutomatic");
            await Invoke(component, "Apply");
            Assert.Single(session.Selections);
            await Invoke(component, "Preview");
            Assert.Null(session.Previews[^1]);
            await Invoke(component, "Apply");
            Assert.Null(session.Selections[^1]);
            Assert.Null(session.Preferences.CaptureConfigurationPath);
            Assert.Contains("Automatische Auswahl gespeichert.", markup());
            Assert.Contains(First.Path, ActiveFile(markup()));
        });
    }

    [Fact]
    public async Task BrowsingACorrectedFileReplacesItsEarlierInvalidScanResult()
    {
        var session = new Session { BrowseResult = Invalid with { Error = null } };
        await Render(session, async (component, markup) =>
        {
            await Invoke(component, "ToggleOpen");
            Assert.Contains("Nicht verwendbar: Lootposition fehlt.", markup());
            await Invoke(component, "Browse");
            Assert.DoesNotContain("Nicht verwendbar: Lootposition fehlt.", markup());
            Assert.Contains("Verfügbare Dateien: 3", markup());
            await Invoke(component, "Preview");
            Assert.Equal(Invalid.Path, Assert.Single(session.Previews));
            await Invoke(component, "Scan");
            Assert.Contains("Nicht verwendbar: Lootposition fehlt.", markup());
        });
    }

    private static CaptureConfigurationOption Option(string path) => new(path, "BDO-Einstellungen", new(2026, 9, 13, 12, 30, 0, DateTimeKind.Utc),
        1920, 1080, 1, new(120, 680, 500, 270), new(1100, 160, 600, 56), "Erkannt");

    // Optional, standalone visual QA artifact; normal test runs write no images.
    private static void ExportReviewIfRequested(string markup)
    {
        var directory = Environment.GetEnvironmentVariable("GRINDCREST_CAPTURE_CONFIGURATION_REVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        using var bitmap = new Bitmap(1920, 1080);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", 24))
        {
            graphics.Clear(Color.FromArgb(22, 31, 42));
            graphics.DrawString("Testbild · keine Spielaufnahme", font, Brushes.LightGray, 600, 500);
            graphics.FillRectangle(Brushes.DarkSlateGray, First.NormalBounds!.Value);
            graphics.FillRectangle(Brushes.DarkGoldenrod, First.RareBounds!.Value);
            graphics.DrawString("Normales Droplog", font, Brushes.White, 140, 700);
            graphics.DrawString("Special-Droplog", font, Brushes.White, 1120, 166);
        }
        using var normal = bitmap.Clone(First.NormalBounds!.Value, bitmap.PixelFormat);
        using var rare = bitmap.Clone(First.RareBounds!.Value, bitmap.PixelFormat);
        static string DataUrl(Bitmap image)
        {
            using var stream = new MemoryStream();
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Directory.Build.props"))) repository = repository.Parent;
        var css = repository is null ? "" : File.ReadAllText(Path.Combine(repository.FullName, "src", "BdoGrindTracker.App", "wwwroot", "app.css"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "capture-configuration-preview.html"),
            "<!doctype html><html lang=\"de\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Erfassungsbereiche – UI-Test</title><style>" + css +
            "html,body{height:auto;overflow:auto}body{margin:0}.page{max-width:1300px}</style><div class=\"page\">" +
            markup.Replace(FullImage, DataUrl(bitmap)).Replace(NormalImage, DataUrl(normal)).Replace(RareImage, DataUrl(rare)) + "</div></html>");
    }

    private static async Task Render(Session session, Func<CaptureConfigurationSettings, Func<string>, Task> test)
    {
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(session)
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<CaptureConfigurationSettings>();
            var component = activator.Components.OfType<CaptureConfigurationSettings>().Single();
            string Markup()
            {
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
                return WebUtility.HtmlDecode(root.ToHtmlString());
            }
            await test(component, Markup);
        });
    }

    private static async Task Invoke(CaptureConfigurationSettings component, string method, params object?[] arguments)
    {
        var result = typeof(CaptureConfigurationSettings).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments);
        if (result is Task task) await task;
    }

    private static string ActiveFile(string markup) => Regex.Match(markup, "<div class=\"capture-active-file\".*?</div>", RegexOptions.Singleline).Value;

    private static bool IsDisabled(string markup, string label)
    {
        var button = Regex.Matches(markup, "<button(?<attrs>[^>]*)>(?<content>.*?)</button>", RegexOptions.Singleline)
            .Single(match => Regex.Replace(match.Groups["content"].Value, "<[^>]+>", "").Trim() == label);
        return Regex.IsMatch(button.Groups["attrs"].Value, @"\bdisabled(?:\s|=|$)");
    }

    private static void AssertRect(string markup, string cssClass, int x, int y, int width, int height)
    {
        var rect = Regex.Match(markup, $"<rect class=\"{cssClass}\"[^>]*>").Value;
        Assert.NotEmpty(rect);
        foreach (var (name, value) in new[] { ("x", x), ("y", y), ("width", width), ("height", height) })
            Assert.Contains($"{name}=\"{value}\"", rect);
    }

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

    private sealed class Session : ITrackerSession
    {
        public event Action? Changed { add { } remove { } }
        public TrackerState State { get; set; } = new();
        public TrackerPreferences Preferences { get; private set; } = new() { CaptureConfigurationPath = First.Path };
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [];
        public IReadOnlyList<LootHistoryEntry> History { get; } = [];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        public int Scans { get; private set; }
        public List<string?> Previews { get; } = [];
        public List<string?> Selections { get; } = [];
        public string? SelectionError { get; set; }
        public string? PreviewError { get; set; }
        public CaptureConfigurationOption? BrowseResult { get; set; }
        public Task<CaptureConfigurationScan> ScanCaptureConfigurationsAsync()
        {
            Scans++;
            return Task.FromResult(new CaptureConfigurationScan([First, Second, Invalid], Preferences.CaptureConfigurationPath ?? First.Path));
        }
        public Task<CaptureConfigurationPreview> PreviewCaptureConfigurationAsync(string? gameVariablePath)
        {
            Previews.Add(gameVariablePath);
            return Task.FromResult(PreviewError is not null ? new CaptureConfigurationPreview(Error: PreviewError) :
                new CaptureConfigurationPreview(Option(gameVariablePath ?? First.Path), FullImage, NormalImage, RareImage, DateTimeOffset.UnixEpoch));
        }
        public Task<CaptureConfigurationOption?> BrowseCaptureConfigurationAsync() => Task.FromResult(BrowseResult);
        public Task<TrackerCommandResult> SelectCaptureConfigurationAsync(string? gameVariablePath)
        {
            Selections.Add(gameVariablePath);
            if (SelectionError is not null) return Task.FromResult(new TrackerCommandResult(SelectionError));
            Preferences = Preferences with { CaptureConfigurationPath = gameVariablePath };
            return Success();
        }
        private static Task<TrackerCommandResult> Success() => Task.FromResult(TrackerCommandResult.Success);
        public Task<TrackerCommandResult> ToggleTrackingAsync() => Success();
        public Task<TrackerCommandResult> PauseAsync() => Success();
        public Task<TrackerCommandResult> NewSessionAsync() => Success();
        public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Success();
        public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Success();
        public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Success();
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false) => Task.FromResult(new PreferenceSaveResult());
        public Task<TrackerCommandResult> UploadAsync() => Success();
        public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => Success();
        public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals, string? characterClass = null) => Success();
        public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity) => Success();
        public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => Success();
        public Task RefreshPricesAsync() => Task.CompletedTask;
        public Task TickAsync() => Task.CompletedTask;
        public Task PrepareUpdateRestartAsync() => Task.CompletedTask;
        public Task RunPreparedUpdateAsync(Func<Task> install) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
