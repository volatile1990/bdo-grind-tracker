using System.Runtime.ExceptionServices;
using System.Drawing.Imaging;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
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
                "Branch of Abundance", 155_127, "#Knockdown/Bound", "Adamantine", "adamantine.png"),
            profile => AssertProfile(profile, LootSpotCatalog.HermesiaId, 2220, 2250, 830,
                "Black Crystal Fragment", 160_539, "#Knockback/Floating", "Fighting Spirit", "fighting-spirit.png"),
            profile => AssertProfile(profile, LootSpotCatalog.MagaiaId, 2340, 2370, 840,
                "Elion Follower's Helmet", 181_042, "#Stun/Stiffness/Freezing", "Giant", "giant.png"),
            profile => AssertProfile(profile, LootSpotCatalog.AresionId, 2455, 2485, 850,
                "Scorched Belt Ornament", 182_049, "#DivineAuthority", "Adamantine", "adamantine.png"),
            profile => AssertProfile(profile, LootSpotCatalog.ScalesOfJudgmentId, 2455, 2485, 860,
                "Elion Follower's Mark", 186_458, "#PartyOf3", "Giant", "giant.png"),
            profile => AssertProfile(profile, LootSpotCatalog.EventHorizonId, 2570, 2600, 870,
                "Broken Gloves of the Void", 196_501, "#FeverPowerfulMobs", "Giant", "giant.png"));

        Assert.All(LootSpotPresentationCatalog.Profiles, profile =>
        {
            Assert.Contains("#CombatEXP", profile.Traits);
            Assert.Contains("#HighestTier", profile.Traits);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "spot-backgrounds", profile.BackgroundFileName)),
                $"Packaged background is missing: {profile.BackgroundFileName}");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "spot-icons", profile.IconFileName)),
                $"Packaged spot icon is missing: {profile.IconFileName}");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "crystal-icons", profile.RecommendedCrystalFileName)),
                $"Packaged crystal icon is missing: {profile.RecommendedCrystalFileName}");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "icons", LootIconRepository.CreateSlug(profile.TrashItemName) + ".png")),
                $"Packaged trash icon is missing: {profile.TrashItemName}");
        });
    }

    [Theory]
    [InlineData(LootSpotCatalog.AphrodonId, "Knockdown/Bound", 245, 157, 64)]
    [InlineData(LootSpotCatalog.HermesiaId, "Knockback/Float", 151, 124, 255)]
    [InlineData(LootSpotCatalog.MagaiaId, "Stun/Stiff/Freeze", 75, 205, 239)]
    [InlineData(LootSpotCatalog.AresionId, "Knockdown/Bound", 245, 157, 64)]
    [InlineData(LootSpotCatalog.ScalesOfJudgmentId, "Stun/Stiff/Freeze", 75, 205, 239)]
    [InlineData(LootSpotCatalog.EventHorizonId, "Stun/Stiff/Freeze", 75, 205, 239)]
    public void CompactSpotCardsExposeCcUsingTheRecommendedCrystalColor(
        string spotId, string expectedCc, int red, int green, int blue)
    {
        var profile = LootSpotPresentationCatalog.GetRequired(spotId);

        Assert.Equal(expectedCc, SpotHistoryCard.GetCcLabel(profile));
        Assert.Equal(Color.FromArgb(red, green, blue),
            SpotHistoryCard.ResolveCrystalAccent(profile.RecommendedCrystalName));
    }

    [Fact]
    public void MaximizedLootTableKeepsEveryLootColumnAndOffersHorizontalScrolling()
    {
        RunInSta(() =>
        {
            var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
            var itemNames = new[]
            {
                "Ancient Spirit Dust", "Black Stone", "Caphras Stone", "Refined Essence of Devouring",
                "Fusion Shard", "Bon Origin Shard", "Jin Origin Shard", "Han Origin Shard",
                "Nev's Fragment", "Silent Crystal of Origin", "Refined Origin of Hunger", "Laila's Petal",
                "Corrupt Oil of Immortality", "Black Gem Fragment", "Pure Black Stone"
            };
            var loot = itemNames.Select((name, index) => new KeyValuePair<string, long>(name, index + 1L))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            loot[profile.TrashItemName] = 18_432;
            using var view = new LootHistoryView { Size = new Size(900, 700) };
            view.SetEntries([
                CreateEntry(profile.SpotId, profile.TrashItemName, 18_432,
                    new DateTimeOffset(2026, 9, 6, 20, 14, 0, TimeSpan.FromHours(2)), totals: loot)
            ]);
            view.ShowSpotDetails(profile.SpotId);
            LayoutRecursively(view);

            var details = Assert.Single(FindDescendants<SpotHistoryDetailView>(view));
            var expectedNames = LootSpotCatalog.GetRequired(profile.SpotId).AllowedItems
                .Concat(itemNames)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedNames.Length, details.LootItemNames.Count);
            Assert.Empty(expectedNames.Except(details.LootItemNames, StringComparer.Ordinal));
            Assert.Equal(profile.TrashItemName, details.LootItemNames[0]);
            Assert.True(details.HasScrollableLootOverflow);
            Assert.Single(FindDescendants<BdoHorizontalScrollBar>(details));
            Assert.True(details.LootScrollBounds.Left > 300);
            Assert.True(details.LootScrollBounds.Right <= details.ClientSize.Width);
            Assert.True(details.ScrollLootAt(new Point(
                details.LootViewportBounds.Left + 4,
                details.LootViewportBounds.Top + 4), -120));
            Assert.True(details.LootScrollValue > 0);
            details.LootScrollValue = details.LootScrollMaximum;
            Assert.Equal(details.LootScrollMaximum, details.LootScrollValue);
            SavePreviewWhenRequested(details, "spot-table-pagination");
        });
    }

    [Fact]
    public void CustomLootScrollbarThumbTracksItsValueWithoutWaitingForTheTable()
    {
        RunInSta(() =>
        {
            using var scrollbar = new BdoHorizontalScrollBar
            {
                Size = new Size(420, 18),
                Maximum = 1_000,
                ViewportSize = 300
            };
            scrollbar.CreateControl();
            var start = scrollbar.ThumbBounds;

            scrollbar.Value = 500;
            var middle = scrollbar.ThumbBounds;
            scrollbar.Value = scrollbar.Maximum;
            var end = scrollbar.ThumbBounds;

            Assert.True(start.Left < middle.Left);
            Assert.True(middle.Left < end.Left);
            Assert.Equal(start.Width, end.Width);
        });
    }

    [Fact]
    public void LootColumnsSortBySilverPerHourAcrossAllLocallyTrackedSessions()
    {
        var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
        var now = new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(2));
        var valuableItem = "Broken Vestige of Goldroot";
        var sessions = new[]
        {
            CreateEntry(profile.SpotId, profile.TrashItemName, 1_000, now,
                totals: new Dictionary<string, long>
                {
                    [profile.TrashItemName] = 1_000,
                    [valuableItem] = 1
                }),
            CreateEntry(profile.SpotId, profile.TrashItemName, 1_000, now.AddHours(-2),
                totals: new Dictionary<string, long> { [profile.TrashItemName] = 1_000 })
        };
        var prices = new LootPriceSnapshot("eu",
        [
            new LootPriceQuote(profile.TrashItemName, 0, 155_127,
                LootPriceOrigin.FixedCatalog, null),
            new LootPriceQuote(valuableItem, 0, 3_000_000_000,
                LootPriceOrigin.FixedCatalog, null)
        ]);

        var items = SpotHistoryDetailView.BuildLootItems(profile, sessions, prices,
            SilverTaxOptions.Default);

        Assert.Equal(valuableItem, items[0].Key);
        Assert.Equal(1, items[0].Value);
        Assert.Equal(2_000, Assert.Single(items, item => item.Key == profile.TrashItemName).Value);
        Assert.True(items.ToList().FindIndex(item => item.Key == valuableItem) <
                    items.ToList().FindIndex(item => item.Key == profile.TrashItemName));

        RunInSta(() =>
        {
            using var view = new LootHistoryView { Size = new Size(900, 700) };
            view.SetPricing(prices, SilverTaxOptions.Default);
            view.SetEntries(sessions);
            view.ShowSpotDetails(profile.SpotId);
            LayoutRecursively(view);
            var details = Assert.Single(FindDescendants<SpotHistoryDetailView>(view));
            Assert.Equal(1_500_000_000m, details.GetLootSilverPerHour(valuableItem));
            Assert.Equal(155_127_000m, details.GetLootSilverPerHour(profile.TrashItemName));
        });
    }

    [Fact]
    public void CollapsedChronologicalCardShowsTrashAndEveryDropWorthOverTwoHundredMillion()
    {
        var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
        var entry = CreateEntry(profile.SpotId, profile.TrashItemName, 18_432,
            new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(2)),
            totals: new Dictionary<string, long>
            {
                [profile.TrashItemName] = 18_432,
                ["Rare Relic"] = 2,
                ["Exact Threshold Relic"] = 1,
                ["Cheap Relic"] = 9
            });
        var prices = new LootPriceSnapshot("eu",
        [
            new LootPriceQuote(profile.TrashItemName, 0, 155_127,
                LootPriceOrigin.FixedCatalog, null),
            // Market value controls the threshold; selling tax must not hide a 250M drop.
            new LootPriceQuote("Rare Relic", 250_000_000, 0,
                LootPriceOrigin.LiveMarket, null),
            new LootPriceQuote("Exact Threshold Relic", 0, 200_000_000,
                LootPriceOrigin.FixedCatalog, null),
            new LootPriceQuote("Cheap Relic", 0, 199_999_999,
                LootPriceOrigin.FixedCatalog, null)
        ]);

        var compactItems = ChronologicalHistoryCard.BuildCollapsedLootItems(entry, profile,
            prices, SilverTaxOptions.Default);

        Assert.Equal([profile.TrashItemName, "Rare Relic"],
            compactItems.Select(static item => item.Key));
    }

    [Fact]
    public void ValuableDropsStayInTheSingleCompactChronologicalHeaderRow()
    {
        RunInSta(() =>
        {
            var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
            var entry = CreateEntry(profile.SpotId, profile.TrashItemName, 20_823,
                new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(2)),
                totals: new Dictionary<string, long>
                {
                    [profile.TrashItemName] = 20_823,
                    ["WON Wandering Origin Crystal"] = 2,
                    ["Broken Vestige of Goldroot"] = 1
                });
            using var view = new LootHistoryView { Size = new Size(900, 700) };
            view.SetPricing(LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default);
            view.SetEntries([entry]);
            view.ShowChronological();
            LayoutRecursively(view);

            var card = Assert.Single(FindDescendants<ChronologicalHistoryCard>(view));
            Assert.Equal(3, card.CollapsedLootItemNames.Count);
            Assert.InRange(card.Height, 60, 70);
            SavePreviewWhenRequested(card, "chronological-compact-loot");
        });
    }

    [Theory]
    [InlineData(LootSpotCatalog.AphrodonId)]
    [InlineData(LootSpotCatalog.HermesiaId)]
    [InlineData(LootSpotCatalog.MagaiaId)]
    [InlineData(LootSpotCatalog.AresionId)]
    [InlineData(LootSpotCatalog.ScalesOfJudgmentId)]
    [InlineData(LootSpotCatalog.EventHorizonId)]
    public void MaximizedLootTableIncludesTheCompleteSpotPoolEvenWithoutTrackedDrops(string spotId)
    {
        RunInSta(() =>
        {
            using var view = new LootHistoryView { Size = new Size(900, 700) };
            view.SetEntries([]);
            view.ShowSpotDetails(spotId);
            LayoutRecursively(view);

            var details = Assert.Single(FindDescendants<SpotHistoryDetailView>(view));
            var spot = LootSpotCatalog.GetRequired(spotId);
            Assert.Equal(spot.AllowedItems.Count, details.LootItemNames.Count);
            Assert.Empty(spot.AllowedItems.Except(details.LootItemNames, StringComparer.Ordinal));
            Assert.Equal(details.Profile.TrashItemName, details.LootItemNames[0]);
        });
    }

    [Fact]
    public void ViewUsesCompactSpotCardsAndAMaximizedGarmothStyleDetailView()
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
            Assert.All(cards, card =>
            {
                Assert.InRange(card.Width, (int)(view.ClientSize.Width * 0.35),
                    (int)(view.ClientSize.Width * 0.55));
                Assert.InRange(card.Height, 130, 180);
            });
            var aphrodon = Assert.Single(cards,
                card => card.Profile.SpotId == LootSpotCatalog.AphrodonId);
            Assert.Equal(1, aphrodon.SessionCount);
            Assert.Equal("Knockdown/Bound", SpotHistoryCard.GetCcLabel(aphrodon.Profile));
            Assert.Equal(Color.FromArgb(245, 157, 64),
                SpotHistoryCard.ResolveCrystalAccent(aphrodon.Profile.RecommendedCrystalName));
            SavePreviewWhenRequested(view, "spot-overview-cc-and-buttons");

            view.ShowSpotDetails(LootSpotCatalog.AphrodonId);
            LayoutRecursively(view);
            Assert.True(view.ShowsSpotDetails);
            Assert.Equal(LootSpotCatalog.AphrodonId, view.SelectedSpotId);
            var details = Assert.Single(FindDescendants<SpotHistoryDetailView>(view));
            Assert.True(details.Width >= view.ClientSize.Width * 0.85);
            Assert.Single(details.Sessions);
            Assert.Equal(1_310_000_000m, details.Metrics.TotalSilver);
            Assert.Equal("Adamantine", details.DisplayedCrystalLabel);
            Assert.Contains("#Knockdown/Bound", details.DisplayedTraitLabels);

            view.ShowSpotOverview();
            LayoutRecursively(view);
            Assert.False(view.ShowsSpotDetails);
            Assert.Null(view.SelectedSpotId);

            view.ShowChronological();
            Assert.False(view.ShowsSpots);
            var chronological = FindDescendants<ChronologicalHistoryCard>(view);
            var first = chronological.OrderByDescending(card => card.Entry.UpdatedAt).First();
            var rowHeight = first.Height;
            Assert.InRange(rowHeight, 60, 70);
            Assert.Equal("Branch of Abundance", first.CollapsedLootItemNames[0]);
            first.SetExpanded(true);
            Assert.True(first.Height > rowHeight);
            Assert.True(first.IsExpanded);
            first.SetExpanded(false);
            Assert.Equal(rowHeight, first.Height);
            Assert.False(first.IsExpanded);

            view.ShowSpots();
            view.ShowSpotDetails(LootSpotCatalog.AphrodonId);
            LayoutRecursively(view);
            using var bitmap = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var background = BdoTheme.Background.ToArgb();
            Assert.True(Enumerable.Range(0, Math.Max(1, bitmap.Width / 20))
                .SelectMany(x => Enumerable.Range(4, Math.Max(1, bitmap.Height / 20 - 4))
                    .Select(y => bitmap.GetPixel(Math.Min(bitmap.Width - 1, x * 20),
                        Math.Min(bitmap.Height - 1, y * 20)).ToArgb()))
                .Any(color => color != background), "The maximized spot detail was not rendered.");
        });
    }

    [Fact]
    public void ChronologicalHistoryUsesTwentyFiveEntriesByDefaultAndOffersFourPageSizes()
    {
        RunInSta(() =>
        {
            var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
            var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(2));
            var entries = Enumerable.Range(0, 60)
                .Select(index => CreateEntry(profile.SpotId, profile.TrashItemName, 18_000 + index,
                    now.AddHours(-index)))
                .ToArray();
            using var view = new LootHistoryView { Size = new Size(900, 700) };
            view.SetEntries(entries);
            view.ShowChronological();
            LayoutRecursively(view);

            Assert.Equal(HistoryPaginationBar.DefaultPageSize, view.ChronologicalPageSize);
            Assert.Equal(25, view.ChronologicalEntryCount);
            var pager = Assert.Single(FindDescendants<HistoryPaginationBar>(view));
            Assert.Equal([10, 25, 50, 100], HistoryPaginationBar.PageSizeOptions);
            Assert.Equal(3, pager.PageCount);
            Assert.All(pager.PageSizeButtons, button => Assert.Equal(16, button.CornerRadius));

            var chronologicalList = Assert.Single(FindDescendants<BdoScrollableFlowLayoutPanel>(view),
                panel => panel.Visible && panel.AccessibleName == "Chronologischer Loot-Verlauf");
            Assert.True(chronologicalList.HasCustomScrollBar);
            var thumbAtTop = chronologicalList.ScrollThumbBounds;
            chronologicalList.AutoScrollPosition = new Point(0, 180);
            LayoutRecursively(view);
            Assert.True(chronologicalList.ScrollThumbBounds.Top > thumbAtTop.Top);

            SavePreviewWhenRequested(view, "chronological-pagination-windowed");

            pager.RequestPage(1);
            LayoutRecursively(view);
            Assert.Equal(1, view.ChronologicalPageIndex);
            Assert.Equal(25, view.ChronologicalEntryCount);

            pager = Assert.Single(FindDescendants<HistoryPaginationBar>(view));
            pager.RequestPageSize(50);
            LayoutRecursively(view);
            Assert.Equal(0, view.ChronologicalPageIndex);
            Assert.Equal(50, view.ChronologicalPageSize);
            Assert.Equal(50, view.ChronologicalEntryCount);
        });
    }

    [Fact]
    public void SpotHistoryPaginatesTrackedSessionsWithoutChangingAggregateLootMetrics()
    {
        RunInSta(() =>
        {
            var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
            var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(2));
            var entries = Enumerable.Range(0, 60)
                .Select(index => CreateEntry(profile.SpotId, profile.TrashItemName, 1_000,
                    now.AddHours(-index)))
                .ToArray();
            using var view = new LootHistoryView { Size = new Size(900, 700) };
            view.SetEntries(entries);
            view.ShowSpotDetails(profile.SpotId);
            LayoutRecursively(view);

            var details = Assert.Single(FindDescendants<SpotHistoryDetailView>(view));
            Assert.Equal(60, details.Sessions.Count);
            Assert.Equal(25, details.VisibleSessions.Count);
            Assert.Equal(1_000m, details.Metrics.TrashPerHour);
            var pager = Assert.Single(FindDescendants<HistoryPaginationBar>(details));

            pager.RequestPage(2);
            LayoutRecursively(view);
            Assert.Equal(2, details.PageIndex);
            Assert.Equal(10, details.VisibleSessions.Count);

            pager.RequestPageSize(50);
            LayoutRecursively(view);
            Assert.Equal(0, details.PageIndex);
            Assert.Equal(50, details.PageSize);
            Assert.Equal(50, details.VisibleSessions.Count);
            Assert.Equal(60, details.Sessions.Count);
        });
    }

    [Theory]
    [InlineData("Maegu", "maegu.png")]
    [InlineData("Dark Knight", "dark-knight.png")]
    [InlineData("Wukong", "wukong.png")]
    public void OfficialClassSymbolsArePackagedUnderStableNames(string className, string fileName)
    {
        Assert.Equal(fileName, SpotHistoryDetailView.CreateClassIconFileName(className));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "data", "class-icons", fileName)));
    }

    [Theory]
    [InlineData(1_310_000_000, "1,31 Mrd.")]
    [InlineData(994_000_000, "994 Mio.")]
    [InlineData(155_127, "155.127")]
    public void SilverFormattingIsCompactAndGerman(decimal silver, string expected)
    {
        Assert.Equal(expected, SpotHistoryCard.FormatSilver(silver));
    }

    [Theory]
    [InlineData(20_823, "20.823")]
    [InlineData(153_000, "153K")]
    [InlineData(1_250_000, "1,3M")]
    public void LootQuantityFormattingRemainsReadable(long quantity, string expected)
    {
        Assert.Equal(expected, SpotHistoryCard.FormatQuantity(quantity));
    }

    [Fact]
    public void SpotMetricsUseWeightedRecentAndBestFiveGrindingHours()
    {
        var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
        var now = new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(2));
        var sessions = new[]
        {
            CreateEntry(profile.SpotId, profile.TrashItemName, 40_000, now,
                TimeSpan.FromHours(2), 4_000_000_000m),
            CreateEntry(profile.SpotId, profile.TrashItemName, 40_000, now.AddDays(-1),
                TimeSpan.FromHours(4), 4_000_000_000m),
            CreateEntry(profile.SpotId, profile.TrashItemName, 60_000, now.AddDays(-2),
                TimeSpan.FromHours(2), 6_000_000_000m)
        };

        var metrics = SpotHistoryDetailView.CalculateMetrics(profile, sessions);

        Assert.Equal(8m, metrics.TotalHours);
        Assert.Equal(14_000_000_000m, metrics.TotalSilver);
        Assert.Equal(1_750_000_000m, metrics.AverageSilverPerHour);
        Assert.Equal(17_500m, metrics.TrashPerHour);
        Assert.Equal(14_000m, metrics.RecentFiveHourTrashPerHour);
        Assert.Equal(22_000m, metrics.BestFiveHourTrashPerHour);
    }

    [Theory]
    [InlineData(0, 0, 30, "gerade eben")]
    [InlineData(0, 45, 0, "vor 45 Min.")]
    [InlineData(5, 0, 0, "vor 5 Std.")]
    [InlineData(336, 0, 0, "vor 14 Tagen")]
    public void TimeAgoFormattingIsCompactAndGerman(int hours, int minutes, int seconds, string expected)
    {
        var now = new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(2));
        var timestamp = now - TimeSpan.FromHours(hours) - TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, SpotHistoryDetailView.FormatTimeAgo(timestamp, now));
    }

    private static void AssertProfile(LootSpotPresentation profile, string spotId,
        int recommendedAp, int maxAp, int recommendedDp, string trashName, long trashSilver,
        string distinctiveTrait, string crystalName, string crystalFileName)
    {
        Assert.Equal(spotId, profile.SpotId);
        Assert.Equal(recommendedAp, profile.RecommendedAp);
        Assert.Equal(maxAp, profile.MaxApLimit);
        Assert.Equal(recommendedDp, profile.RecommendedDp);
        Assert.Equal(trashName, profile.TrashItemName);
        Assert.Equal(trashSilver, profile.TrashSilver);
        Assert.Contains(distinctiveTrait, profile.Traits);
        Assert.Equal(crystalName, profile.RecommendedCrystalName);
        Assert.Equal(crystalFileName, profile.RecommendedCrystalFileName);
    }

    private static LootHistoryEntry CreateEntry(string spotId, string trashName, long trash,
        DateTimeOffset updatedAt, TimeSpan? duration = null, decimal silver = 1_310_000_000m,
        IReadOnlyDictionary<string, long>? totals = null) =>
        new()
        {
            SessionId = Guid.NewGuid(),
            StartedAt = updatedAt - (duration ?? TimeSpan.FromHours(1)),
            UpdatedAt = updatedAt,
            Duration = duration ?? TimeSpan.FromHours(1),
            SpotId = spotId,
            CharacterClass = "Maegu · Awakening",
            Totals = totals is null
                ? new Dictionary<string, long>
                {
                    [trashName] = trash,
                    ["Caphras Stone"] = 124
                }
                : totals.ToDictionary(static pair => pair.Key, static pair => pair.Value,
                    StringComparer.Ordinal),
            SilverBeforeTax = silver,
            SilverAfterTax = silver,
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

    private static void SavePreviewWhenRequested(Control control, string name)
    {
        var directory = Environment.GetEnvironmentVariable("BDO_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory) || control.Width <= 0 || control.Height <= 0)
            return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(directory, $"loot-history-{name}.png"), ImageFormat.Png);
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
