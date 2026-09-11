using System.Text.Json;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class CurrentSessionStoreTests
{
    [Fact]
    public void RoundTripRetainsCurrentSessionIncludingZeroQuantityCorrections()
    {
        using var fixture = new StoreFixture();
        var expected = Example() with
        {
            Totals = new() { ["Black Stone"] = 0, ["Black Crystal Fragment"] = 27 },
            ManualLootItems = ["Black Stone"],
            SessionSubmitted = true,
            GarmothLocallyModified = true,
            RecordLoot = true,
        };
        fixture.Store.Save(expected);

        var actual = Assert.IsType<CurrentSessionSnapshot>(fixture.Store.Load());
        Assert.Null(fixture.Store.LoadError);
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.StartedAt, actual.StartedAt);
        Assert.Equal(expected.Duration, actual.Duration);
        Assert.Equal(expected.CharacterClassId, actual.CharacterClassId);
        Assert.Equal(expected.Totals, actual.Totals);
        Assert.Equal(expected.ManualLootItems, actual.ManualLootItems);
        Assert.Equal(expected.ExperienceGainedPercentagePoints, actual.ExperienceGainedPercentagePoints);
        Assert.True(actual.SessionSubmitted);
        Assert.True(actual.GarmothLocallyModified);
        Assert.True(actual.RecordLoot);
    }

    [Fact]
    public void EmptyStartedSessionCanBeRestoredBeforeAnySpotOrDropWasSeen()
    {
        using var fixture = new StoreFixture();
        fixture.Store.Save(EmptySession());
        var actual = Assert.IsType<CurrentSessionSnapshot>(fixture.Store.Load());
        Assert.Empty(actual.Totals);
        Assert.Equal(TimeSpan.Zero, actual.Duration);
        Assert.Null(actual.SpotId);
    }

    [Fact]
    public void ExplicitEmptyMarkerDoesNotRestoreThePreviousBackup()
    {
        using var fixture = new StoreFixture();
        fixture.Store.Save(Example());
        fixture.Store.Save(null);
        Assert.True(File.Exists(fixture.Path + ".bak"));
        Assert.Null(fixture.Store.Load());
        Assert.Null(fixture.Store.LoadError);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"Version\":2,\"Session\":null}")]
    [InlineData("{\"Version\":1}")]
    [InlineData("{\"Version\":1,\"Session\":{\"SessionId\":\"11111111-1111-1111-1111-111111111111\"}}")]
    public void UnreadableCheckpointRemainsUntouched(string content)
    {
        using var fixture = new StoreFixture();
        File.WriteAllText(fixture.Path, content);
        Assert.Null(fixture.Store.Load());
        Assert.NotNull(fixture.Store.LoadError);
        Assert.Throws<IOException>(() => fixture.Store.Save(null));
        Assert.Equal(content, File.ReadAllText(fixture.Path));
    }

    public static IEnumerable<object[]> InvalidSnapshots()
    {
        yield return [Example() with { SessionId = Guid.Empty }];
        yield return [Example() with { Duration = TimeSpan.FromSeconds(-1) }];
        yield return [Example() with { ConfirmedEventCount = -1 }];
        yield return [Example() with { SpotId = "unknown-spot" }];
        yield return [Example() with { CharacterClassId = "unknown-class" }];
        yield return [Example() with { Totals = new() { ["Black Stone"] = -1 } }];
        yield return [Example() with { Totals = new() { ["A"] = long.MaxValue, ["B"] = 1 } }];
        yield return [Example() with { Totals = new() { ["Black Stone"] = 1, ["black stone"] = 2 } }];
        yield return [Example() with { ManualLootItems = ["not-present"] }];
        yield return [Example() with { AgrisActiveDuration = TimeSpan.FromHours(1) }];
        yield return [Example() with { ExperienceObservedDuration = TimeSpan.Zero }];
        yield return [Example() with { ExperienceStartLevel = null }];
        yield return [Example() with { GameLanguage = "unsupported" }];
    }

    [Theory]
    [MemberData(nameof(InvalidSnapshots))]
    public void InvalidStateDoesNotReplaceLastSuccessfulCheckpoint(object value)
    {
        using var fixture = new StoreFixture();
        fixture.Store.Save(Example());
        var before = File.ReadAllText(fixture.Path);
        Assert.ThrowsAny<Exception>(() => fixture.Store.Save((CurrentSessionSnapshot)value));
        Assert.Equal(before, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void InvalidSerializedStateReportsLoadErrorInsteadOfEscapingValidation()
    {
        using var fixture = new StoreFixture();
        var content = JsonSerializer.Serialize(new { Version = 1, Session = Example() with { SpotId = "unknown" } });
        File.WriteAllText(fixture.Path, content);
        Assert.Null(fixture.Store.Load());
        Assert.NotNull(fixture.Store.LoadError);
        Assert.Equal(content, File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void AtomicWriteFailureRetainsPreviousCheckpointAndCanRetry()
    {
        using var fixture = new StoreFixture();
        var original = Example();
        fixture.Store.Save(original);
        Directory.CreateDirectory(fixture.Path + ".tmp");
        try
        {
            Assert.ThrowsAny<Exception>(() => fixture.Store.Save(null));
            Assert.Equal(original.SessionId, fixture.Store.Load()!.SessionId);
        }
        finally { Directory.Delete(fixture.Path + ".tmp"); }
        fixture.Store.Save(null);
        Assert.Null(fixture.Store.Load());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    [InlineData(2500000)]
    public void SmallUploadClockLeadPreservesExactActiveTimeAndFrozenHourlyCutoff(long leadTicks)
    {
        using var fixture = new StoreFixture();
        var ledger = new GarmothUploadIntervals();
        ledger.Observe(TimeSpan.FromHours(1), new Dictionary<string, long> { ["Black Crystal Fragment"] = 27 },
            DateTimeOffset.UtcNow);
        var snapshot = Example() with
        {
            Duration = TimeSpan.FromHours(1) - TimeSpan.FromTicks(leadTicks),
            Uploads = ledger.ExportState(),
        };
        fixture.Store.Save(snapshot);
        var saved = fixture.Store.Load()!;
        Assert.Equal(snapshot.Duration, saved.Duration);
        Assert.Equal(TimeSpan.FromHours(1), saved.Uploads!.ObservedDuration);
        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(saved.Uploads.Hours).EndDuration);
    }

    [Fact]
    public void UploadClockLeadOutsideSamplingToleranceIsRejected()
    {
        using var fixture = new StoreFixture();
        var ledger = new GarmothUploadIntervals();
        ledger.Observe(TimeSpan.FromHours(1), new Dictionary<string, long> { ["Black Crystal Fragment"] = 27 },
            DateTimeOffset.UtcNow);
        var snapshot = Example() with
        {
            Duration = TimeSpan.FromHours(1) - CurrentSessionStore.UploadClockTolerance - TimeSpan.FromTicks(1),
            Uploads = ledger.ExportState(),
        };
        Assert.Throws<InvalidDataException>(() => fixture.Store.Save(snapshot));
    }

    internal static CurrentSessionSnapshot Example() => EmptySession() with
    {
        Duration = TimeSpan.FromMinutes(2),
        SpotId = LootSpotCatalog.HermesiaId,
        CharacterClassId = "warrior-awakening",
        Totals = new() { ["Black Crystal Fragment"] = 27 },
        ConfirmedEventCount = 5,
        AgrisActiveDuration = TimeSpan.FromSeconds(20),
        AgrisObservedDuration = TimeSpan.FromSeconds(40),
        ExperienceGainedPercentagePoints = .125m,
        ExperienceObservedDuration = TimeSpan.FromMinutes(1),
        ExperienceStartLevel = 65,
        ExperienceEndLevel = 65,
    };

    internal static CurrentSessionSnapshot EmptySession() => new()
    {
        SessionId = Guid.NewGuid(),
        StartedAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 9, 11, 12, 2, 0, TimeSpan.Zero),
        Duration = TimeSpan.Zero,
        SpotId = null,
        CharacterClassId = null,
        SessionSubmitted = false,
        Totals = new(),
        ConfirmedEventCount = 0,
        ManualLootItems = [],
        GarmothLocallyModified = false,
        AgrisActiveDuration = TimeSpan.Zero,
        AgrisObservedDuration = TimeSpan.Zero,
        ExperienceGainedPercentagePoints = null,
        ExperienceObservedDuration = TimeSpan.Zero,
        ExperienceStartLevel = null,
        ExperienceEndLevel = null,
        GameLanguage = "en",
        MonitorDeviceName = "synthetic-monitor",
        RecordLoot = false,
    };

    private sealed class StoreFixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public CurrentSessionStore Store { get; }
        public StoreFixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, CurrentSessionStore.FileName);
            Store = new(Path);
        }
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
