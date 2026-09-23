using System.Reflection;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyBuffProfileCannotSelectAManualReaderOrBlockPreferenceSaving(bool invalidPath)
    {
        var legacyPath = LegacyBuffProfilePath(invalidPath);
        await using var fixture = new Fixture(autoUpload: false,
            initialSettingsJson: JsonSerializer.Serialize(new AppSettings
            {
                BuffRecognitionProfilePath = legacyPath,
            }));

        Assert.Null(fixture.Service.Preferences.BuffRecognitionProfilePath);
        var reader = AssertAutomaticBuffReader(fixture);
        var saved = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
        {
            BuffRecognitionProfilePath = legacyPath,
            AutoPauseMinutes = 12,
        });

        Assert.True(saved.Succeeded, saved.Error);
        Assert.Null(fixture.Service.Preferences.BuffRecognitionProfilePath);
        Assert.Null(fixture.Settings.Load().BuffRecognitionProfilePath);
        Assert.Equal(12, fixture.Settings.Load().AutoPauseMinutes);
        Assert.Same(reader, AssertAutomaticBuffReader(fixture));
        Assert.False(fixture.Service.State.HasSession);
        Assert.False(fixture.Service.State.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IgnoredLegacyProfileSavePreservesExistingBuffBookingsAndCountdown(bool invalidPath)
    {
        BuffFrameReading? reading = BuffReading(60);
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor, lootScrollVisible: _ => true);
        BeginBuffSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-4));
        reading = BuffReading(1200);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        var before = Assert.IsType<BuffLedgerSnapshot>(fixture.Service.State.Buffs);
        Assert.Single(before.Consumptions);
        var sessionId = fixture.Service.State.SessionId;

        var saved = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
        {
            BuffRecognitionProfilePath = LegacyBuffProfilePath(invalidPath),
            AutoPauseMinutes = 12,
        });

        Assert.True(saved.Succeeded, saved.Error);
        Assert.Null(fixture.Service.Preferences.BuffRecognitionProfilePath);
        Assert.Null(fixture.Settings.Load().BuffRecognitionProfilePath);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(sessionId, fixture.Service.State.SessionId);
        Assert.Equal(before.Consumptions, fixture.Service.State.Buffs!.Consumptions);
        Assert.Equal(before.Usage, fixture.Service.State.Buffs.Usage);
        Assert.Equal(before.Active, fixture.Service.State.Buffs.Active);

        reading = BuffReading(1199);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
        var continued = fixture.Service.State.Buffs!;
        Assert.Equal(before.Consumptions, continued.Consumptions);
        Assert.Equal(1_200_000m, continued.ConsumedCost);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(continued.Usage).ObservedDuration);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(before.Consumptions, Assert.Single(fixture.HistoryStore.Load()).Buffs!.Consumptions);
    }

    [Fact]
    public async Task RecoveringLegacySettingsKeepsAutomaticBuffRecognitionAndClearsTheObsoletePath()
    {
        await using var fixture = new Fixture(autoUpload: false);
        var reader = AssertAutomaticBuffReader(fixture);
        File.WriteAllText(fixture.SettingsPath, "{");
        fixture.Settings.Load();
        Assert.NotNull(fixture.Settings.LoadError);
        File.WriteAllText(fixture.SettingsPath, JsonSerializer.Serialize(new AppSettings
        {
            BuffRecognitionProfilePath = LegacyBuffProfilePath(invalidPath: true),
            AutoPauseMinutes = 12,
        }));

        var recovered = await fixture.Service.SaveSessionAsync();

        Assert.True(recovered.Succeeded, recovered.Error);
        Assert.Null(fixture.Settings.LoadError);
        Assert.Null(fixture.Service.Preferences.BuffRecognitionProfilePath);
        Assert.Null(fixture.Settings.Load().BuffRecognitionProfilePath);
        Assert.Equal(12, fixture.Service.Preferences.AutoPauseMinutes);
        Assert.Same(reader, AssertAutomaticBuffReader(fixture));
    }

    private static string LegacyBuffProfilePath(bool invalidPath) => invalidPath
        ? "invalid\0buff-profile.json"
        : Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"), "missing-buff-profile.json");

    private static AutomaticBuffFrameReader AssertAutomaticBuffReader(Fixture fixture)
    {
        var monitor = Assert.IsType<BuffMonitor>(fixture.Service.GetType()
            .GetField("_buffMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Service));
        return Assert.IsType<AutomaticBuffFrameReader>(typeof(BuffMonitor)
            .GetField("_reader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor));
    }
}
