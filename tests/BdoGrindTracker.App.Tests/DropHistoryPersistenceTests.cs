using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class DropHistoryPersistenceTests
{
    private const string ItemName = "Black Crystal Fragment";
    private const string CorrectedItemName = "Pure Black Stone";
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(2);

    [Fact]
    public void BothStoresRoundTripObservedDropTimesInOrderWithoutChangingQuantities()
    {
        using var files = new Files();
        SessionDropSample[] samples =
        [
            new(Duration, ItemName, 2),
            new(TimeSpan.Zero, ItemName, 10),
            new(TimeSpan.FromSeconds(30), ItemName.ToLowerInvariant(), 15),
            // A later manual correction to zero does not erase an observed drop.
            new(TimeSpan.FromSeconds(50), CorrectedItemName, 1),
        ];
        files.Save(samples);

        var (history, current) = files.Load();
        var expected = samples.OrderBy(sample => sample.Elapsed).ToArray();
        AssertSamples(expected, history.DropHistory);
        AssertSamples(expected, current.DropHistory);
        Assert.Equal(Duration, history.Duration);
        Assert.Equal(Duration, current.Duration);
        Assert.Equal(27, history.Totals[ItemName]);
        Assert.Equal(27, current.Totals[ItemName]);
        Assert.Equal(0, history.Totals[CorrectedItemName]);
        Assert.Equal(0, current.Totals[CorrectedItemName]);
        Assert.Equal(5, current.ConfirmedEventCount);
        AssertNoErrors(files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyMissingAndExplicitNullTimesRemainUnknownAndWritable(bool explicitNull)
    {
        using var files = new Files();
        var history = JsonSerializer.SerializeToNode(HistoryEntry())!.AsObject();
        var current = JsonSerializer.SerializeToNode(Checkpoint())!.AsObject();
        if (explicitNull)
        {
            history[nameof(LootHistoryEntry.DropHistory)] = null;
            current[nameof(CurrentSessionSnapshot.DropHistory)] = null;
        }
        else
        {
            history.Remove(nameof(LootHistoryEntry.DropHistory));
            current.Remove(nameof(CurrentSessionSnapshot.DropHistory));
        }
        files.Write(history, current);

        var loaded = files.Load();
        Assert.Null(loaded.History.DropHistory);
        Assert.Null(loaded.Current.DropHistory);
        AssertNoErrors(files);

        files.History.Save([loaded.History with { CharacterClass = "Warrior · Awakening" }]);
        files.Current.Save(loaded.Current with { ConfirmedEventCount = 6 });
        loaded = files.Load();
        Assert.Null(loaded.History.DropHistory);
        Assert.Null(loaded.Current.DropHistory);
        Assert.Equal(27, loaded.History.Totals[ItemName]);
        Assert.Equal(27, loaded.Current.Totals[ItemName]);
        AssertNoErrors(files);
    }

    [Fact]
    public void AnObservedEmptyHistoryRemainsDistinctFromUnknownLegacyTimes()
    {
        using var files = new Files();
        files.Save([]);

        var (history, current) = files.Load();
        AssertSamples([], history.DropHistory);
        AssertSamples([], current.DropHistory);
        AssertNoErrors(files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSamplesAreFilteredWithoutBlockingValidSessionData(bool readRawJson)
    {
        using var files = new Files();
        SessionDropSample[] expected =
        [
            new(TimeSpan.Zero, ItemName, 10),
            new(TimeSpan.FromSeconds(50), CorrectedItemName, 1),
            new(Duration, ItemName, 2),
        ];
        SessionDropSample[] samples =
        [
            expected[2],
            new(TimeSpan.FromTicks(-1), ItemName, 1),
            new(Duration + TimeSpan.FromTicks(1), ItemName, 1),
            new(TimeSpan.FromSeconds(20), ItemName, 0),
            new(TimeSpan.FromSeconds(20), ItemName, -1),
            new(TimeSpan.FromSeconds(20), "", 1),
            new(TimeSpan.FromSeconds(20), "  ", 1),
            new(TimeSpan.FromSeconds(20), null!, 1),
            null!,
            new(TimeSpan.FromSeconds(20), new string('x', 4097), 1),
            new(TimeSpan.FromSeconds(20), "Not in session totals", 1),
            expected[1],
            expected[0],
        ];
        if (readRawJson)
            files.Write(JsonSerializer.SerializeToNode(HistoryEntry() with { DropHistory = samples })!.AsObject(),
                JsonSerializer.SerializeToNode(Checkpoint() with { DropHistory = samples })!.AsObject());
        else
            files.Save(samples);

        var (history, current) = files.Load();
        AssertSamples(expected, history.DropHistory);
        AssertSamples(expected, current.DropHistory);
        Assert.Equal(27, history.Totals[ItemName]);
        Assert.Equal(27, current.Totals[ItemName]);
        Assert.Equal(Duration, history.Duration);
        Assert.Equal(Duration, current.Duration);
        AssertNoErrors(files);

        // Optional timing metadata must not make a sound session read-only.
        files.History.Save([history with { Totals = new(history.Totals) { [ItemName] = 28 } }]);
        files.Current.Save(current with { Totals = new(current.Totals) { [ItemName] = 28 } });
        (history, current) = files.Load();
        Assert.Equal(28, history.Totals[ItemName]);
        Assert.Equal(28, current.Totals[ItemName]);
        AssertSamples(expected, history.DropHistory);
        AssertSamples(expected, current.DropHistory);
        AssertNoErrors(files);
    }

    [Fact]
    public void EntirelyInvalidTimesBecomeAnEmptyHistoryWithoutDiscardingLoot()
    {
        using var files = new Files();
        SessionDropSample[] samples = [null!, new(TimeSpan.FromSeconds(-1), ItemName, 1)];
        files.Write(JsonSerializer.SerializeToNode(HistoryEntry() with { DropHistory = samples })!.AsObject(),
            JsonSerializer.SerializeToNode(Checkpoint() with { DropHistory = samples })!.AsObject());

        var (history, current) = files.Load();
        AssertSamples([], history.DropHistory);
        AssertSamples([], current.DropHistory);
        Assert.Equal(27, history.Totals[ItemName]);
        Assert.Equal(27, current.Totals[ItemName]);
        AssertNoErrors(files);
    }

    [Fact]
    public void NormalizationReturnsAnImmutableSortedCopy()
    {
        var later = new SessionDropSample(Duration, ItemName, 2);
        var earlier = new SessionDropSample(TimeSpan.Zero, ItemName, 1);
        SessionDropSample[] input = [later, earlier];

        var normalized = SessionDropHistory.Normalize(input, Duration, Checkpoint().Totals);

        AssertSamples([earlier, later], normalized);
        Assert.Equal(later, input[0]);
        Assert.NotSame(input, normalized);
        input[0] = new(TimeSpan.FromSeconds(10), ItemName, 99);
        AssertSamples([earlier, later], normalized);
        var collection = Assert.IsAssignableFrom<IList<SessionDropSample>>(normalized);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection[0] = later);
    }

    [Theory]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public void NormalizationLimitsNameLengthEvenWhenTheNameOccursInTotals(int length, bool valid)
    {
        var name = new string('x', length);
        SessionDropSample[] input = [new(TimeSpan.Zero, name, 1)];

        var normalized = SessionDropHistory.Normalize(input, Duration, new Dictionary<string, long> { [name] = 1 });

        AssertSamples(valid ? input : [], normalized);
    }

    private static void AssertSamples(IReadOnlyList<SessionDropSample> expected, IReadOnlyList<SessionDropSample>? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.ToArray(), actual.ToArray());
    }

    private static void AssertNoErrors(Files files)
    {
        Assert.Null(files.History.LoadError);
        Assert.Null(files.Current.LoadError);
    }

    private static CurrentSessionSnapshot Checkpoint() => CurrentSessionStoreTests.Example() with
    {
        Totals = new() { [ItemName] = 27, [CorrectedItemName] = 0 },
    };

    private static LootHistoryEntry HistoryEntry()
    {
        var current = Checkpoint();
        return new()
        {
            SessionId = current.SessionId, StartedAt = current.StartedAt!.Value, UpdatedAt = current.UpdatedAt,
            Duration = current.Duration, SpotId = current.SpotId!, Totals = current.Totals,
            SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
        };
    }

    private sealed class Files : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Grindcrest.DropHistory.Tests", Guid.NewGuid().ToString("N"));
        private string HistoryPath => Path.Combine(_directory, "history.json");
        private string CurrentPath => Path.Combine(_directory, CurrentSessionStore.FileName);
        public LootHistoryStore History { get; }
        public CurrentSessionStore Current { get; }

        public Files()
        {
            Directory.CreateDirectory(_directory);
            History = new(HistoryPath);
            Current = new(CurrentPath);
        }

        public void Save(IReadOnlyList<SessionDropSample>? samples)
        {
            History.Save([HistoryEntry() with { DropHistory = samples }]);
            Current.Save(Checkpoint() with { DropHistory = samples });
        }

        public (LootHistoryEntry History, CurrentSessionSnapshot Current) Load() =>
            (Assert.Single(History.Load()), Assert.IsType<CurrentSessionSnapshot>(Current.Load()));

        public void Write(JsonObject history, JsonObject current)
        {
            File.WriteAllText(HistoryPath, new JsonObject { ["Version"] = 1, ["Entries"] = new JsonArray(history) }.ToJsonString());
            File.WriteAllText(CurrentPath, new JsonObject { ["Version"] = 1, ["Session"] = current }.ToJsonString());
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
