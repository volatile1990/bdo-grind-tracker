using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Ocr;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay.Native;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class HermesiaRotationTests
{
    private static readonly DateTimeOffset Epoch = new(2026,9,13,0,0,0,TimeSpan.Zero);

    [Theory]
    [InlineData("best", "Beste vollständige Rotation")]
    [InlineData("sectors", "Bestrotation + Mechanik-Bestzeiten")]
    [InlineData("ideal", "Ideale Rotation")]
    public async Task WidgetUsesSelectedComparisonMode(string mode, string expected)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => {
            var component = await renderer.RenderComponentAsync<OverlayWidgetPreview>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                ["Widget"] = OverlayCatalog.CreateWidget("rotation-monitor") with { RotationComparison = mode },
                ["Snapshot"] = OverlaySnapshot.Demo
            }));
            return System.Net.WebUtility.HtmlDecode(component.ToHtmlString());
        });
        Assert.Contains(expected,html);
        Assert.Contains("AFK",html);
        Assert.Contains("Drakania",html);
        if (mode=="sectors") Assert.Contains("Bestzeit",html);
    }

    [Fact]
    public void NativeModuleRendersAtDifferentSizesAndModes()
    {
        using var renderer = new NativeOverlayRenderer();
        foreach (var mode in new[] { "best","sectors","ideal" })
            foreach (var size in new[] { new Size(600,280),new Size(320,200),new Size(900,360) }) {
                var widget = OverlayCatalog.CreateWidget("rotation-monitor",0,0) with { Width=size.Width,Height=size.Height,RotationComparison=mode };
                using var image = renderer.Render(size,new() { Width=size.Width,Height=size.Height,Widgets=[widget] },OverlaySnapshot.Demo,out _);
                Assert.Equal(size,image.Size);
                var folder = Environment.GetEnvironmentVariable("GRINDCREST_HERMESIA_FIXTURES");
                if (folder is not null && size.Width==600) image.Save(Path.Combine(folder,"native-"+mode+".png"));
            }
    }

    [Fact]
    public void IdenticalMineMessageOnlyEndsRotationAfterAfk()
    {
        var tracker = new HermesiaRotationTracker();
        tracker.Observe("mine-cleared", "Mine", Epoch);
        Assert.False(tracker.Snapshot(Epoch).Synchronized);
        tracker.Observe("afk", "AFK", Epoch.AddSeconds(1));
        tracker.Observe("mine-cleared", "Mine", Epoch.AddSeconds(62));
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(62)).Synchronized);
        tracker.Observe("mine-cleared", "Mine", Epoch.AddSeconds(100));
        Assert.Equal(38,tracker.Snapshot(Epoch.AddSeconds(100)).Elapsed);
        Assert.Equal(0,tracker.Snapshot(Epoch.AddSeconds(100)).Completed);
    }

    [Fact]
    public void FullRunIncludesAfkAndImmediatelyStartsNextRotation()
    {
        var tracker = new HermesiaRotationTracker();
        Start(tracker);
        Complete(tracker,0,10,50,60);
        var snapshot = tracker.Snapshot(Epoch.AddSeconds(65));
        Assert.Equal(60,snapshot.Best!.Duration);
        Assert.Equal(5,snapshot.Elapsed);
        Assert.Equal(1,snapshot.Completed);
        Assert.False(snapshot.IsAfk);
        Assert.Single(snapshot.Events);
    }

    [Fact]
    public void ComparisonSupportsBestAndIdealWithMatchingSectorPaths()
    {
        var tracker = new HermesiaRotationTracker(); Start(tracker);
        Complete(tracker,0,10,50,60);
        Complete(tracker,60,8,52,60);
        var snapshot = tracker.Snapshot(Epoch.AddSeconds(120));
        Assert.Equal(60,snapshot.Best!.Duration);
        Assert.Equal(56,snapshot.Ideal!.Duration);
        Assert.Same(snapshot.Best,RotationTimelinePresentation.Reference(snapshot,"sectors"));
        Assert.Same(snapshot.Ideal,RotationTimelinePresentation.Reference(snapshot,"ideal"));
    }

    [Fact]
    public void InterruptedRunCannotBecomeARecord()
    {
        var tracker = new HermesiaRotationTracker(); Start(tracker);
        tracker.Observe("dragon","Dragon",Epoch.AddSeconds(10));
        tracker.Interrupt();
        tracker.Observe("afk","AFK",Epoch.AddSeconds(50));
        tracker.Observe("mine-cleared","Mine",Epoch.AddSeconds(60));
        Assert.Null(tracker.Snapshot(Epoch.AddSeconds(60)).Best);
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(60)).Synchronized);
    }

    [Fact]
    public void PorterMessagesDoNotSplitMechanicTimesOrPreventIdealComparison()
    {
        var tracker = new HermesiaRotationTracker(); Start(tracker);
        tracker.Observe("porter", "Träger", Epoch.AddSeconds(1));
        Complete(tracker,0,10,50,60);
        Complete(tracker,60,8,52,60);
        var snapshot = tracker.Snapshot(Epoch.AddSeconds(120));
        Assert.Equal(2.4,snapshot.SectorBests["dragon:1"],6);
        Assert.False(snapshot.SectorBests.ContainsKey("porter:1"));
        Assert.Equal(56,snapshot.Ideal!.Duration);
    }

    [Fact]
    public void BriefOcrLossDoesNotDuplicateTheSameVisibleBanner()
    {
        var gate = new HermesiaMessageGate(); const string text = "Markthanan's patrol descends.";
        gate.Read(text,Epoch); Assert.Single(gate.Read(text,Epoch.AddSeconds(.5)));
        Assert.Empty(gate.Read(text,Epoch.AddSeconds(4)));
        Assert.Empty(gate.Read(text,Epoch.AddSeconds(4.5)));
    }

    [Fact]
    public void BannerNeedsTwoReadsAndRearmsOnlyAfterAbsence()
    {
        var gate = new HermesiaMessageGate(); const string text = "Markthanan's patrol descends.";
        Assert.Empty(gate.Read(text,Epoch));
        Assert.Equal(Epoch,Assert.Single(gate.Read(text,Epoch.AddSeconds(.5))).At);
        for (var i=1;i<16;i++) Assert.Empty(gate.Read(text,Epoch.AddSeconds(i)));
        Assert.Empty(gate.Read(text,Epoch.AddSeconds(19)));
        Assert.Single(gate.Read(text,Epoch.AddSeconds(19.5)));
        Assert.Empty(new HermesiaMessageGate().Read("unrelated chat message",Epoch));
    }

    [Fact]
    public void RecordsSurviveRestartWithoutResumingHalfARotation()
    {
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+"-rotation.json");
        try {
            var tracker = new HermesiaRotationTracker(path); Start(tracker); Complete(tracker,0,10,50,60);
            var restored = new HermesiaRotationTracker(path).Snapshot(Epoch);
            Assert.Equal(60,restored.Best!.Duration); Assert.False(restored.Synchronized);
        } finally { File.Delete(path); File.Delete(path+".tmp"); }
    }

    [Fact]
    public void ReferenceCaptureRecognizesEveryAnnotatedMessageType()
    {
        // Optional local recording validation; no video or user paths enter CI.
        var folder = Environment.GetEnvironmentVariable("GRINDCREST_HERMESIA_FIXTURES");
        if (string.IsNullOrEmpty(folder)) return;
        var engine = CompanionWindowsOcrRecognizer.TryCreate("en-US",requirePreferredLanguage:true);
        Assert.NotNull(engine);
        var expected = new[] { "porter","drakania","drakania-kill","transfer","mine-enter","offer","mine-second","mine-cleared","dragon","afk","mine-cleared" };
        for (var i=0;i<expected.Length;i++) {
            using var bitmap = new Bitmap(Path.Combine(folder,$"event-{i}.png"));
            using var pixels = CompanionFrameDecoder.Decode(bitmap);
            var text = HermesiaMessages.Recognize(pixels, engine);
            Assert.True(HermesiaMessages.Parse(text).Any(e=>e.Kind==expected[i]),$"{expected[i]}: {text}");
        }
    }

    private static void Start(HermesiaRotationTracker tracker) {
        tracker.Observe("afk","AFK",Epoch.AddSeconds(-60)); tracker.Observe("mine-cleared","Mine",Epoch);
    }
    private static void Complete(HermesiaRotationTracker tracker,double offset,double dragon,double afk,double end) {
        tracker.Observe("drakania","Drakania",Epoch.AddSeconds(offset+dragon*.2));
        tracker.Observe("drakania-kill","Drakania besiegt",Epoch.AddSeconds(offset+dragon*.4));
        tracker.Observe("transfer","Buff",Epoch.AddSeconds(offset+dragon*.5));
        tracker.Observe("mine-enter","Mine",Epoch.AddSeconds(offset+dragon*.7));
        tracker.Observe("dragon","Dragon",Epoch.AddSeconds(offset+dragon));
        tracker.Observe("afk","AFK",Epoch.AddSeconds(offset+afk));
        tracker.Observe("mine-cleared","Mine",Epoch.AddSeconds(offset+end));
    }
}
