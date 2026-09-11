using System.Reflection;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task UnknownAutomaticClassRetriesAfterThirtySecondsAndStopsOnceDetected()
    {
        await using var fixture = new Fixture(autoUpload: false);
        var calls = 0;
        SetField(fixture.Service, "_detectCharacterClass", (Func<CharacterClassDetection>)(() =>
        {
            Interlocked.Increment(ref calls);
            return fixture.ClassDetection;
        }));
        var before = DateTimeOffset.UtcNow;
        await fixture.Service.TickAsync();
        await AwaitClassRefresh(fixture.Service);
        var next = Assert.IsType<DateTimeOffset>(ReadClassRefreshField(fixture.Service, "_nextClassDetectionAt"));
        Assert.InRange(next, before.AddSeconds(30), DateTimeOffset.UtcNow.AddSeconds(30));
        Assert.Null(fixture.Service.State.CharacterClassId);
        Assert.Equal(1, calls);

        fixture.ClassDetection = DetectedClass("hashashin-awakening");
        SetField(fixture.Service, "_nextClassDetectionAt", DateTimeOffset.UtcNow.AddMinutes(1));
        await fixture.Service.TickAsync();
        Assert.Equal(1, calls);
        Assert.Null(fixture.Service.State.CharacterClassId);

        SetField(fixture.Service, "_nextClassDetectionAt", DateTimeOffset.MinValue);
        await fixture.Service.TickAsync();
        await AwaitClassRefresh(fixture.Service);
        Assert.Equal(2, calls);
        Assert.Equal("hashashin-awakening", fixture.Service.State.CharacterClassId);

        fixture.ClassDetection = DetectedClass("maegu-awakening");
        SetField(fixture.Service, "_nextClassDetectionAt", DateTimeOffset.MinValue);
        await fixture.Service.TickAsync();
        Assert.Equal(2, calls);
        Assert.Equal("hashashin-awakening", fixture.Service.State.CharacterClassId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownStartedSessionCanAcquireItsClassWhileRunningOrPaused(bool paused)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        SetField(fixture.Service, "_sessionClass", null!);
        SetField(fixture.Service, "_classDetection", CharacterClassDetection.Unknown);
        if (paused) await fixture.Service.PauseAsync();
        await fixture.Service.TickAsync();
        await AwaitClassRefresh(fixture.Service);
        Assert.Null(fixture.Service.State.CharacterClassId);

        fixture.ClassDetection = DetectedClass("hashashin-awakening");
        SetField(fixture.Service, "_nextClassDetectionAt", DateTimeOffset.MinValue);
        await fixture.Service.TickAsync();
        await AwaitClassRefresh(fixture.Service);

        Assert.Equal("hashashin-awakening", fixture.Service.State.CharacterClassId);
        Assert.Equal(!paused, fixture.Service.State.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticRefreshDoesNotReplaceKnownStartedSessionClass(bool paused)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        if (paused) await fixture.Service.PauseAsync();
        fixture.ClassDetection = DetectedClass("hashashin-awakening");

        // The first tick still refreshes detection, even for a known session.
        await fixture.Service.TickAsync();
        await AwaitClassRefresh(fixture.Service);

        Assert.Equal("warrior-awakening", fixture.Service.State.CharacterClassId);
    }

    [Fact]
    public async Task SwitchingPausedSessionFromManualToAutoReadsFreshClassBeforeSaving()
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { CharacterClassId = "warrior-awakening" });
        fixture.Begin();
        await fixture.Service.PauseAsync();
        SetField(fixture.Service, "_classDetectionTask", Task.CompletedTask);
        SetField(fixture.Service, "_nextClassDetectionAt", DateTimeOffset.MaxValue);
        fixture.ClassDetection = DetectedClass("hashashin-awakening");

        var result = await fixture.Service.SavePreferencesAsync(
            fixture.Service.Preferences with { CharacterClassId = null });

        Assert.Null(result.Error);
        Assert.Null(fixture.Service.Preferences.CharacterClassId);
        Assert.Null(fixture.Settings.Load().CharacterClassId);
        Assert.Equal("hashashin-awakening", fixture.Service.State.CharacterClassId);
        Assert.False(fixture.Service.State.IsRunning);
    }

    [Fact]
    public async Task ManuallySelectedClassDoesNotCausePeriodicAutomaticRetries()
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { CharacterClassId = "warrior-awakening" });
        await fixture.Service.TickAsync();
        await AwaitClassRefresh(fixture.Service);
        SetField(fixture.Service, "_detectCharacterClass", (Func<CharacterClassDetection>)(() =>
            throw new InvalidOperationException("Manual selection must not be polled.")));
        SetField(fixture.Service, "_nextClassDetectionAt", DateTimeOffset.MinValue);
        var completed = ReadClassRefreshField(fixture.Service, "_classDetectionTask");

        await fixture.Service.TickAsync();

        Assert.Same(completed, ReadClassRefreshField(fixture.Service, "_classDetectionTask"));
        Assert.Equal("warrior-awakening", fixture.Service.State.CharacterClassId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetectionCannotAlterSubmittedSessionEvenIfItsClassWasUnknown(bool knownClass)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.Service.PauseAsync();
        if (!knownClass) SetField(fixture.Service, "_sessionClass", null!);
        SetField(fixture.Service, "_sessionSubmitted", true);
        fixture.ClassDetection = DetectedClass("hashashin-awakening");

        await fixture.Service.TickAsync();
        await AwaitClassRefresh(fixture.Service);
        SetField(fixture.Service, "_nextClassDetectionAt", DateTimeOffset.MinValue);
        var completed = ReadClassRefreshField(fixture.Service, "_classDetectionTask");
        await fixture.Service.TickAsync();

        Assert.Same(completed, ReadClassRefreshField(fixture.Service, "_classDetectionTask"));
        Assert.Equal(knownClass ? "warrior-awakening" : null, fixture.Service.State.CharacterClassId);
        Assert.True(fixture.Service.State.IsSubmitted);
    }

    private static CharacterClassDetection DetectedClass(string id) =>
        new(CompanionCharacterClassCatalog.FindById(id), CharacterClassDetectionStatus.Detected, 4);

    private static object? ReadClassRefreshField(TrackerSessionService service, string name) =>
        typeof(TrackerSessionService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service);

    private static Task AwaitClassRefresh(TrackerSessionService service) =>
        Assert.IsAssignableFrom<Task>(ReadClassRefreshField(service, "_classDetectionTask"))
            .WaitAsync(TimeSpan.FromSeconds(5));
}
