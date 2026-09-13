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
    [InlineData("best")]
    [InlineData("sectors")]
    [InlineData("ideal")]
    public async Task WidgetUsesSelectedComparisonModeWithoutVisibleLabels(string mode)
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
        var reference = RotationTimelinePresentation.Reference(OverlaySnapshot.Demo.Rotation, mode)!;
        var spawn = reference.Events.First(e => e.Kind == "drakania");
        Assert.Contains("Drakania-Spawn · " + RotationTimelinePresentation.Time(spawn.Seconds),html);
        if (mode == "sectors") Assert.Contains("Bestabschnitt", html);
        Assert.Contains("AFK",html);
        Assert.Contains("Drakania",html);
        Assert.Contains("rotation-phase-duration",html);
        Assert.Contains("rotation-pack-marker",html);
        Assert.DoesNotContain("rotation-heading",html);
        Assert.DoesNotContain("rotation-legend",html);
        Assert.DoesNotContain("overlay-widget-label",html);
    }

    [Fact]
    public void FirstEventAfterGrindStartSetsZeroAndResumeStartsFresh()
    {
        var tracker = new HermesiaRotationTracker();
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(20)).Synchronized);
        tracker.Observe("porter", "Träger", Epoch.AddSeconds(27));
        Assert.Equal(0, tracker.Snapshot(Epoch.AddSeconds(27)).Elapsed);
        Assert.Equal("porter", tracker.Snapshot(Epoch.AddSeconds(27)).Events[1].Kind);
        Assert.Equal(5, tracker.Snapshot(Epoch.AddSeconds(32)).Elapsed);
        tracker.Interrupt();
        tracker.Observe("drakania", "Drakania", Epoch.AddSeconds(90));
        Assert.Equal(0, tracker.Snapshot(Epoch.AddSeconds(90)).Elapsed);
        Assert.DoesNotContain(tracker.Snapshot(Epoch.AddSeconds(90)).Events,e=>e.Kind=="porter");
    }

    [Fact]
    public void LegacyRecordsRebaseToFirstObservedEventExactlyOnce()
    {
        var tracker = new HermesiaRotationTracker(); Start(tracker); Complete(tracker,0,10,50,60);
        var current = tracker.Snapshot(Epoch.AddSeconds(60)).Best!;
        var legacy = current with { TimingVersion = 0, Duration = 80,
            Events = current.Events.Select(e=>e.Kind=="start" ? e : e with { Seconds=e.Seconds+20 }).ToArray() };
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+"-rotation.json");
        try {
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new[] { legacy }));
            var migrated = new HermesiaRotationTracker(path).Snapshot(Epoch).Best!;
            Assert.Equal(60,migrated.Duration);
            Assert.Equal(0,migrated.Events[1].Seconds);
            Assert.Equal(2,migrated.TimingVersion);
            Assert.Same(migrated,HermesiaRotationTracker.FromFirstEvent(migrated));
        } finally { File.Delete(path); }
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
        tracker.Observe("porter", "Träger", Epoch);
        Assert.True(tracker.Snapshot(Epoch).Synchronized);
        tracker.Observe("mine-cleared", "Mine", Epoch.AddSeconds(10));
        Assert.Equal(10, tracker.Snapshot(Epoch.AddSeconds(10)).Elapsed);
        tracker.Observe("afk", "AFK", Epoch.AddSeconds(20));
        tracker.Observe("mine-cleared", "Mine", Epoch.AddSeconds(80));
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(82)).Synchronized);
        Assert.Equal(80, tracker.Snapshot(Epoch.AddSeconds(82)).Elapsed);
        tracker.Observe("offer", "Opfergabe", Epoch.AddSeconds(90));
        Assert.Equal(0, tracker.Snapshot(Epoch.AddSeconds(90)).Elapsed);
        Assert.Equal(0,tracker.Snapshot(Epoch.AddSeconds(90)).Completed);
    }

    [Fact]
    public void FullRunIncludesAfkAndWaitsForNextEvent()
    {
        var tracker = new HermesiaRotationTracker();
        Start(tracker);
        Complete(tracker,0,10,50,60);
        var snapshot = tracker.Snapshot(Epoch.AddSeconds(65));
        Assert.Equal(60,snapshot.Best!.Duration);
        Assert.Equal(60,snapshot.Elapsed);
        Assert.False(snapshot.Synchronized);
        Assert.Equal(1,snapshot.Completed);
        Assert.False(snapshot.IsAfk);
        Assert.Equal("end",snapshot.Events[^1].Kind);
        tracker.Observe("porter","Träger",Epoch.AddSeconds(70));
        Assert.Equal(0,tracker.Snapshot(Epoch.AddSeconds(70)).Elapsed);
        Assert.Equal(5,tracker.Snapshot(Epoch.AddSeconds(75)).Elapsed);
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
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(60)).Synchronized);
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
        tracker.Observe("offer","Opfergabe",Epoch);
    }
    private static void Complete(HermesiaRotationTracker tracker,double offset,double dragon,double afk,double end) {
        if (!tracker.Snapshot(Epoch.AddSeconds(offset)).Synchronized) tracker.Observe("offer","Opfergabe",Epoch.AddSeconds(offset));
        tracker.Observe("drakania","Drakania",Epoch.AddSeconds(offset+dragon*.2));
        tracker.Observe("drakania-kill","Drakania besiegt",Epoch.AddSeconds(offset+dragon*.4));
        tracker.Observe("transfer","Buff",Epoch.AddSeconds(offset+dragon*.5));
        tracker.Observe("mine-enter","Mine",Epoch.AddSeconds(offset+dragon*.7));
        tracker.Observe("dragon","Dragon",Epoch.AddSeconds(offset+dragon));
        tracker.Observe("afk","AFK",Epoch.AddSeconds(offset+afk));
        tracker.Observe("mine-cleared","Mine",Epoch.AddSeconds(offset+end));
    }
}
