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

    [Fact]
    public void CompletedRunsAreCollectedOnceAndSurvivePause()
    {
        var tracker = new HermesiaRotationTracker();
        // The live recognition reads the reference's startup porter banners as offering orders.
        foreach (var e in HermesiaRotationDemo.Reference.Events.Where(e => e.Kind != "start"))
            tracker.Observe(e.Kind switch { "end" => "mine-cleared", "porter" => "offer", _ => e.Kind }, e.Label, Epoch.AddSeconds(e.Seconds));
        tracker.Interrupt();
        var completed = Assert.Single(tracker.DrainCompleted());
        Assert.Equal(Epoch, completed.StartedAt);
        Assert.Equal(HermesiaRotationDemo.Reference.Duration, completed.Run.Duration, 5);
        Assert.Empty(tracker.DrainCompleted());
    }

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

    [Theory]
    [InlineData("Intruder alert in effect. Valid authorization not confirmed.")]
    [InlineData("lntruder alert in effect. Valid authorization not confirmed.")]
    [InlineData("Intruder alert in effect. Val1d authorizat1on not conf1rmed.")]
    public void IntruderAlertIsRecognizedOnceAsRotationFailure(string text)
    {
        Assert.Equal(("failure", "Rotation Failed"), Assert.Single(HermesiaMessages.Parse(text)));
    }

    [Fact]
    public void IntruderAlertFailsTheRotationAndOnlyTheNextOfferingOrderRestartsIt()
    {
        var tracker = new HermesiaRotationTracker(); Start(tracker);
        tracker.Observe("drakania", "Drakania", Epoch.AddSeconds(20));
        tracker.Observe("drakania-kill", "Drakania besiegt", Epoch.AddSeconds(40));
        tracker.Observe("failure", "Rotation Failed", Epoch.AddSeconds(55));

        var failed = tracker.Snapshot(Epoch.AddSeconds(70));
        Assert.False(failed.Synchronized);
        Assert.Equal(55, failed.Elapsed);
        Assert.Equal(("failure", "Rotation Failed", 55d), (failed.Events[^1].Kind, failed.Events[^1].Label, failed.Events[^1].Seconds));
        Assert.Equal("Rotation Failed · warte auf Opfergabe-Befehl", failed.Status);
        Assert.Equal(55, RotationPhases.Create(BdoGrindTracker.Core.LootSpotCatalog.HermesiaId, failed.Events, failed.Elapsed)[^1].End);

        // Mechanics and the end of an AFK phase in between never start a rotation.
        foreach (var (kind, seconds) in new[] { ("porter", 60d), ("afk", 70), ("mine-cleared", 90), ("dragon", 100) })
            tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(110)).Synchronized);
        Assert.Equal(55, tracker.Snapshot(Epoch.AddSeconds(110)).Elapsed);

        tracker.Observe("offer", "Opfergabe", Epoch.AddSeconds(120));
        var restarted = tracker.Snapshot(Epoch.AddSeconds(125));
        Assert.True(restarted.Synchronized);
        Assert.Equal(5, restarted.Elapsed);
        Assert.Equal(new[] { "start", "offer" }, restarted.Events.Select(e => e.Kind));
        Assert.Empty(tracker.DrainCompleted());
        Assert.Equal(0, restarted.Completed);

        Complete(tracker, 120, 10, 50, 60);
        Assert.Equal(60, Assert.Single(tracker.DrainCompleted()).Run.Duration);
    }

    [Fact]
    public void AFailedRotationStillWaitsForTheOfferingOrderAfterAnInterruption()
    {
        var tracker = new HermesiaRotationTracker();
        tracker.Observe("failure", "Rotation Failed", Epoch);
        tracker.Interrupt("Bildsignal unterbrochen · warte auf erstes Ereignis");
        Assert.Equal("Rotation Failed · warte auf Opfergabe-Befehl", tracker.Snapshot(Epoch.AddSeconds(5)).Status);
        tracker.Observe("porter", "Träger", Epoch.AddSeconds(10));
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(10)).Synchronized);
        Assert.Empty(tracker.Snapshot(Epoch.AddSeconds(10)).Events);
        tracker.Observe("offer", "Opfergabe", Epoch.AddSeconds(20));
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(20)).Synchronized);
        tracker.Interrupt("Tracking pausiert · warte auf Rotationsstart");
        Assert.Equal("Tracking pausiert · warte auf Rotationsstart", tracker.Snapshot(Epoch.AddSeconds(30)).Status);
    }

    [Fact]
    public async Task BufferedMonitorReadsTheIntruderAlertFromTheMessageArea()
    {
        using var frame = new Bitmap(1920, 1080);
        var tracker = new HermesiaRotationTracker();
        var current = "";
        using var monitor = new BufferedRotationProfileMonitor(tracker, RotationMessageProfile.Hermesia, _ => current);
        async Task Show(string text, double from)
        {
            current = text;
            for (var i = 0; i < 8; i++)
            {
                monitor.Observe(frame, Epoch.AddSeconds(from + i * .5));
                await monitor.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
            }
        }
        await Show("The overseer orders the Black Crystals to be offered up.", 0);
        await Show("Intruder alert in effect. Valid authorization not confirmed.", 4);
        var state = monitor.Snapshot(Epoch.AddSeconds(8));
        Assert.Null(state.Error);
        Assert.False(state.Synchronized);
        Assert.Equal("failure", state.Events[^1].Kind);
    }

    [Fact]
    public void WithoutFiveOfferingOrdersBeforeDrakaniaTheStartupIsDiscardedAndTheRotationDoesNotCount()
    {
        var tracker = new HermesiaRotationTracker();
        foreach (var seconds in new[] { 0d, 18, 36, 54 }) tracker.Observe("offer", "Opfergabe", Epoch.AddSeconds(seconds));
        Assert.Equal("Startup · 4 / 5 Opfergaben", tracker.Snapshot(Epoch.AddSeconds(60)).Status);

        tracker.Observe("drakania", "Drakania", Epoch.AddSeconds(80));
        var running = tracker.Snapshot(Epoch.AddSeconds(90));
        Assert.Equal(90, running.Elapsed);
        Assert.Equal("drakania", running.Events[0].Kind);
        Assert.DoesNotContain(running.Events, e => e.Kind is "start" or "offer");
        Assert.Contains("Startup verworfen (4 / 5 Opfergaben)", running.Status);
        Assert.Equal("drakania", RotationPhases.Create(BdoGrindTracker.Core.LootSpotCatalog.HermesiaId, running.Events, running.Elapsed)[0].Id);

        foreach (var (kind, seconds) in new[] { ("offer", 95d), ("drakania-kill", 100), ("transfer", 101), ("mine-enter", 103),
                     ("dragon", 400), ("afk", 450), ("mine-cleared", 520) })
            tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));

        var finished = tracker.Snapshot(Epoch.AddSeconds(525));
        Assert.Empty(tracker.DrainCompleted());
        Assert.Null(finished.Best);
        Assert.Equal(0, finished.Completed);
        Assert.Equal(new[] { "drakania", "offer" }, finished.Events.Take(2).Select(e => e.Kind));
        Assert.StartsWith("AFK beendet · Startup unvollständig (4 / 5 Opfergaben), Rotation nicht gezählt", finished.Status);

        // The next rotation with a complete startup counts normally.
        Complete(tracker, 530, 10, 50, 60);
        Assert.Equal(60, Assert.Single(tracker.DrainCompleted()).Run.Duration);
    }

    [Fact]
    public void ALateConfirmedOfferingOrderStillCompletesTheStartup()
    {
        var tracker = new HermesiaRotationTracker();
        foreach (var seconds in new[] { 0d, 18, 54, 72 }) tracker.Observe("offer", "Opfergabe", Epoch.AddSeconds(seconds));
        tracker.Observe("drakania", "Drakania", Epoch.AddSeconds(80));
        Assert.DoesNotContain(tracker.Snapshot(Epoch.AddSeconds(82)).Events, e => e.Kind == "start");
        tracker.Observe("offer", "Opfergabe", Epoch.AddSeconds(36));

        var state = tracker.Snapshot(Epoch.AddSeconds(84));
        Assert.Equal(new[] { "start", "offer" }, state.Events.Take(2).Select(e => e.Kind));
        Assert.Equal(5, HermesiaRotationTracker.StartupOfferCount(state.Events));
        Assert.DoesNotContain("verworfen", state.Status);
    }

    [Fact]
    public void SavedRotationsWithoutACompleteStartupAreKeptButNeverUsedAsReference()
    {
        var tracker = new HermesiaRotationTracker(); Start(tracker); Complete(tracker,0,10,50,60);
        var valid = tracker.Snapshot(Epoch.AddSeconds(60)).Best!;
        // Like a recording that began after the fourth offering order: shorter and without its startup.
        var partial = new RotationRun(55, valid.Events.Where(e => e.Kind != "offer" || e.Seconds == 0)
            .Select(e => e.Kind == "end" ? e with { Seconds = 55 } : e).ToArray()) { TimingVersion = 2 };
        Assert.False(HermesiaRotationTracker.HasCompleteStartup(partial));
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+"-rotation.json");
        try {
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new[] { partial, valid }));
            var restored = new HermesiaRotationTracker(path);
            Assert.Equal(60,restored.Snapshot(Epoch).Best!.Duration);
            Assert.Equal(1,restored.Snapshot(Epoch).Completed);

            Start(restored); Complete(restored,0,8,52,59);
            var saved = System.Text.Json.JsonSerializer.Deserialize<RotationRun[]>(File.ReadAllText(path))!;
            Assert.Equal(new[] { 55d, 59, 60 }, saved.Select(run => run.Duration).Order());
            Assert.Equal(59,new HermesiaRotationTracker(path).Snapshot(Epoch).Best!.Duration);
        } finally { File.Delete(path); File.Delete(path+".tmp"); }
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
        for (var i=1;i<HermesiaRotationTracker.StartupOffers;i++) tracker.Observe("offer","Opfergabe",Epoch.AddSeconds(offset+dragon*.04*i));
        tracker.Observe("drakania","Drakania",Epoch.AddSeconds(offset+dragon*.2));
        tracker.Observe("drakania-kill","Drakania besiegt",Epoch.AddSeconds(offset+dragon*.4));
        tracker.Observe("transfer","Buff",Epoch.AddSeconds(offset+dragon*.5));
        tracker.Observe("mine-enter","Mine",Epoch.AddSeconds(offset+dragon*.7));
        tracker.Observe("dragon","Dragon",Epoch.AddSeconds(offset+dragon));
        tracker.Observe("afk","AFK",Epoch.AddSeconds(offset+afk));
        tracker.Observe("mine-cleared","Mine",Epoch.AddSeconds(offset+end));
    }
}
