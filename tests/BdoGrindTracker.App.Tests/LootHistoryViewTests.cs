using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LootHistoryViewTests
{
    [Fact]
    public void SpotProfilesMatchTheInGameValuesAndProvidedTraits()
    {
        Assert.Collection(LootSpotPresentationCatalog.Profiles,
            profile => AssertProfile(profile, LootSpotCatalog.AphrodonId, 2090, 2120, 810,
                "Branch of Abundance", 155_127, "#Knockdown/Bound"),
            profile => AssertProfile(profile, LootSpotCatalog.HermesiaId, 2220, 2250, 830,
                "Black Crystal Fragment", 160_539, "#Knockback/Floating"),
            profile => AssertProfile(profile, LootSpotCatalog.MagaiaId, 2340, 2370, 840,
                "Elion Follower's Helmet", 181_042, "#Stun/Stiffness/Freezing"),
            profile => AssertProfile(profile, LootSpotCatalog.AresionId, 2455, 2485, 850,
                "Scorched Belt Ornament", 182_049, "#DivineAuthority"),
            profile => AssertProfile(profile, LootSpotCatalog.ScalesOfJudgmentId, 2455, 2485, 860,
                "Elion Follower's Mark", 186_458, "#PartyOf3"),
            profile => AssertProfile(profile, LootSpotCatalog.EventHorizonId, 2570, 2600, 870,
                "Broken Gloves of the Void", 196_501, "#FeverPowerfulMobs"));

        Assert.All(LootSpotPresentationCatalog.Profiles, profile =>
        {
            Assert.Contains("#CombatEXP", profile.Traits);
            Assert.Contains("#HighestTier", profile.Traits);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "spot-backgrounds", profile.BackgroundFileName)),
                $"Packaged background is missing: {profile.BackgroundFileName}");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "icons", LootIconRepository.CreateSlug(profile.TrashItemName) + ".png")),
                $"Packaged trash icon is missing: {profile.TrashItemName}");
        });
    }

    [Fact]
    public void ViewUsesFullWidthExpandableSpotCardsAndChronologicalDetails()
    {
        RunInSta(() =>
        {
            using var view = new LootHistoryView { Size = new Size(900, 700) };
            view.SetEntries([
                CreateEntry(LootSpotCatalog.AphrodonId, "Branch of Abundance", 18_432,
                    new DateTimeOffset(2026, 9, 6, 20, 14, 0, TimeSpan.FromHours(2))),
                CreateEntry(LootSpotCatalog.HermesiaId, "Black Crystal Fragment", 21_774,
                    new DateTimeOffset(2026, 9, 5, 18, 7, 0, TimeSpan.FromHours(2)))
            ]);
            LayoutRecursively(view);

            Assert.True(view.ShowsSpots);
            Assert.Equal(6, view.SpotCardCount);
            Assert.Equal(2, view.ChronologicalEntryCount);
            Assert.Empty(FindDescendants<DataGridView>(view));

            var cards = FindDescendants<SpotHistoryCard>(view);
            Assert.All(cards, card => Assert.True(card.Width >= view.ClientSize.Width * 0.85,
                $"Spot card is not full width: {card.Width} within {view.ClientSize.Width}."));
            var aphrodon = Assert.Single(cards,
                card => card.Profile.SpotId == LootSpotCatalog.AphrodonId);
            var collapsedHeight = aphrodon.Height;
            Assert.Equal(1, aphrodon.SessionCount);
            aphrodon.SetExpanded(true);
            Assert.True(aphrodon.Height > collapsedHeight);
            Assert.True(aphrodon.IsExpanded);

            view.ShowChronological();
            Assert.False(view.ShowsSpots);
            var chronological = FindDescendants<ChronologicalHistoryCard>(view);
            var first = chronological.OrderByDescending(card => card.Entry.UpdatedAt).First();
            var rowHeight = first.Height;
            first.SetExpanded(true);
            Assert.True(first.Height > rowHeight);
            Assert.True(first.IsExpanded);

            using var bitmap = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            Assert.NotEqual(BdoTheme.Background.ToArgb(), bitmap.GetPixel(30, 150).ToArgb());
        });
    }

    [Theory]
    [InlineData(1_310_000_000, "1,31 Mrd.")]
    [InlineData(994_000_000, "994 Mio.")]
    [InlineData(155_127, "155.127")]
    public void SilverFormattingIsCompactAndGerman(decimal silver, string expected)
    {
        Assert.Equal(expected, SpotHistoryCard.FormatSilver(silver));
    }

    private static void AssertProfile(LootSpotPresentation profile, string spotId,
        int recommendedAp, int maxAp, int recommendedDp, string trashName, long trashSilver,
        string distinctiveTrait)
    {
        Assert.Equal(spotId, profile.SpotId);
        Assert.Equal(recommendedAp, profile.RecommendedAp);
        Assert.Equal(maxAp, profile.MaxApLimit);
        Assert.Equal(recommendedDp, profile.RecommendedDp);
        Assert.Equal(trashName, profile.TrashItemName);
        Assert.Equal(trashSilver, profile.TrashSilver);
        Assert.Contains(distinctiveTrait, profile.Traits);
    }

    private static LootHistoryEntry CreateEntry(string spotId, string trashName, long trash,
        DateTimeOffset updatedAt) =>
        new()
        {
            SessionId = Guid.NewGuid(),
            StartedAt = updatedAt.AddHours(-1),
            UpdatedAt = updatedAt,
            Duration = TimeSpan.FromHours(1),
            SpotId = spotId,
            CharacterClass = "Maegu · Awakening",
            Totals = new Dictionary<string, long>
            {
                [trashName] = trash,
                ["Caphras Stone"] = 124
            },
            SilverBeforeTax = 1_310_000_000,
            SilverAfterTax = 1_310_000_000,
            SilverIsComplete = true
        };

    private static IReadOnlyList<TControl> FindDescendants<TControl>(Control root)
        where TControl : Control
    {
        var results = new List<TControl>();
        foreach (Control child in root.Controls)
        {
            if (child is TControl match)
                results.Add(match);
            results.AddRange(FindDescendants<TControl>(child));
        }
        return results;
    }

    private static void LayoutRecursively(Control control)
    {
        control.CreateControl();
        control.PerformLayout();
        foreach (Control child in control.Controls)
            LayoutRecursively(child);
        control.PerformLayout();
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Loot history UI test did not finish.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
