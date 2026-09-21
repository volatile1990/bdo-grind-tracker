using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;

namespace BdoGrindTracker.App.Tests;

public sealed partial class LiveLootInteractionTests
{
    [Theory]
    [InlineData("de", "Gerade eben", "vor 3 Min.", "vor 4 Min.", "★ FAVORIT", "Manuell korrigiert")]
    [InlineData("en", "Just now", "3m ago", "4m ago", "★ FAVORITE", "Manually corrected")]
    public async Task OnlyRareLogItemsShowTheirLatestDropAndRefreshWithSessionTime(string language,
        string justNow, string threeMinutes, string fourMinutes, string favorite, string manual)
    {
        var session = new Session();
        session.Preferences = session.Preferences with { UiLanguage = language, FavoriteItems = ["Pure Black Stone"] };
        session.State = session.State with
        {
            IsRunning = true, Elapsed = TimeSpan.FromMinutes(10),
            Loot = new(new Dictionary<string, long>
            {
                ["Branch of Abundance"] = 100, ["Pure Black Stone"] = 10, ["Essence of Devouring"] = 1,
                ["Black Stone"] = 1, ["Empty Picture Frame"] = 1, ["Unknown Legacy Item"] = 1,
            }, 114, 8),
            ManualLootItems = ["Essence of Devouring"],
            DropHistory = [new(TimeSpan.FromMinutes(7), "Pure Black Stone", 1),
                new(TimeSpan.FromSeconds(598), "Branch of Abundance", 5),
                new(TimeSpan.FromMinutes(1), "Pure Black Stone", 9),
                new(TimeSpan.FromMinutes(9), "Black Stone", 1),
                new(TimeSpan.FromMinutes(9), "Empty Picture Frame", 1),
                new(TimeSpan.FromMinutes(9), "Unknown Legacy Item", 1)],
        };

        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            var html = markup();
            Assert.Equal(2, Regex.Matches(html, "class=\"item-tag last-drop-tag\"").Count);
            var trashRow = RowFor(html, "Branch of Abundance");
            Assert.Contains("TRASH", trashRow);
            Assert.DoesNotContain("last-drop-tag", trashRow);
            foreach (var name in new[] { "Black Stone", "Empty Picture Frame", "Unknown Legacy Item" })
                Assert.DoesNotContain("last-drop-tag", RowFor(html, name));
            var stoneRow = RowFor(html, "Pure Black Stone");
            Assert.Contains(threeMinutes, stoneRow);
            Assert.Contains(favorite, stoneRow);
            var manualRow = RowFor(html, "Essence of Devouring");
            Assert.Contains("last-drop-tag", manualRow);
            Assert.Contains("—", manualRow);
            Assert.Contains(manual, manualRow);

            session.State = session.State with { Elapsed = TimeSpan.FromMinutes(11) };
            Assert.Contains(fourMinutes, RowFor(markup(), "Pure Black Stone"));
            session.State = session.State with { IsRunning = false };
            Assert.Contains(fourMinutes, RowFor(markup(), "Pure Black Stone"));

            session.State = session.State with { DropHistory = [.. session.State.DropHistory,
                new(TimeSpan.FromMinutes(11), "Pure Black Stone", 1)] };
            Assert.Contains(justNow, RowFor(markup(), "Pure Black Stone"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ManualQuantityChangesDoNotResetAnObservedItemsLastDrop()
    {
        var session = new Session();
        session.State = session.State with { Elapsed = TimeSpan.FromMinutes(5),
            Loot = new(new Dictionary<string, long> { ["Pure Black Stone"] = 10 }, 10, 1),
            DropHistory = [new(TimeSpan.FromMinutes(2), "Pure Black Stone", 1)] };
        await Render<LiveDashboard>(session, null, async (_, markup, _) =>
        {
            Assert.Contains("vor 3 Min.", RowFor(markup(), "Pure Black Stone"));
            await session.UpdateLootQuantityAsync(session.State.SessionId, "Pure Black Stone", 15, 10);
            Assert.Contains("vor 3 Min.", RowFor(markup(), "Pure Black Stone"));
        });
    }

    [Fact]
    public async Task HistoryTablesDoNotBorrowLastDropTimesFromTheLiveSession()
    {
        var session = new Session();
        session.State = session.State with
        {
            Loot = new(new Dictionary<string, long> { ["Pure Black Stone"] = 1 }, 1, 1),
            DropHistory = [new(TimeSpan.Zero, "Pure Black Stone", 1)],
        };
        await Render<LootTable>(session, new Dictionary<string, object?>
        {
            [nameof(LootTable.Totals)] = session.State.Loot.Totals,
            [nameof(LootTable.EditableSessionId)] = Guid.NewGuid(),
        }, (_, markup, _) =>
        {
            Assert.DoesNotContain("last-drop-tag", markup());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task UnknownOrOtherSessionsNeverInventARecentDrop()
    {
        var session = new Session();
        session.State = session.State with
        {
            Loot = new(new Dictionary<string, long> { ["Pure Black Stone"] = 1 }, 1, 1),
            DropHistory = [new(TimeSpan.Zero, "Pure Black Stone", 1)],
        };
        await Render<LootTable>(session, new Dictionary<string, object?>
        {
            [nameof(LootTable.Totals)] = session.State.Loot.Totals,
            [nameof(LootTable.EditableSessionId)] = Guid.NewGuid(),
            [nameof(LootTable.ShowLastDrop)] = true,
        }, (_, markup, _) =>
        {
            var row = RowFor(markup(), "Pure Black Stone");
            Assert.Contains("last-drop-tag", row);
            Assert.Contains("—", row);
            Assert.DoesNotContain("vor 1 Min.", row);
            return Task.CompletedTask;
        });
    }

    private static string RowFor(string html, string name) => Regex.Matches(html, "<tr[^>]*>.*?</tr>", RegexOptions.Singleline)
        .Select(match => match.Value).Single(row => row.Contains($"<strong>{name}</strong>", StringComparison.Ordinal));
}
