using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class ModernUiTests
{
    [Theory]
    [InlineData("Black Crystal Fragment", "black-crystal-fragment")]
    [InlineData("Nev's Fragment", "nevs-fragment")]
    [InlineData("Embers of Ynix - Helmet", "embers-of-ynix-helmet")]
    [InlineData("  Twilight of the End - Ring  ", "twilight-of-the-end-ring")]
    public void IconSlugMatchesAssetNamingConvention(string itemName, string expectedSlug)
    {
        Assert.Equal(expectedSlug, LootIconRepository.CreateSlug(itemName));
    }

    [Fact]
    public void TotalsViewStoresOnlyDistinctAggregates()
    {
        RunInSta(() =>
        {
            using var icons = new LootIconRepository(
                Path.Combine(Path.GetTempPath(), $"bdo-icons-not-present-{Guid.NewGuid():N}"));
            using var view = new LootTotalsView(icons);

            view.SetTotals(
            [
                new KeyValuePair<string, long>("Black Stone", 4),
                new KeyValuePair<string, long>("Black Crystal Fragment", 12)
            ]);

            Assert.Equal(2, view.EntryCount);
            Assert.Equal(16, view.TotalQuantity);
            Assert.Empty(FindDescendants<DataGridView>(view));
        });
    }

    [Fact]
    public void RepositoryLoadsPackagedIconWithoutKeepingItsFileOpen()
    {
        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "data", "icons");
        var iconPath = Path.Combine(iconDirectory, "nevs-fragment.png");
        Assert.True(File.Exists(iconPath), $"Missing packaged test icon: {iconPath}");

        using var icons = new LootIconRepository(iconDirectory);
        var icon = icons.GetIcon("Nev's Fragment");

        Assert.NotNull(icon);
        Assert.Equal(new Size(44, 44), icon.Size);
        using var readWhileLoaded = new FileStream(
            iconPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        Assert.True(readWhileLoaded.Length > 0);
    }

    [Fact]
    public void TransientUiGateLimitsRefreshesWithoutSuppressingFirstFrame()
    {
        long timestamp = 0;
        var gate = new TransientUiRefreshGate(
            TimeSpan.FromMilliseconds(500),
            () => timestamp,
            timestampFrequency: 1000);

        Assert.True(gate.TryAcquire());
        timestamp = 499;
        Assert.False(gate.TryAcquire());
        timestamp = 500;
        Assert.True(gate.TryAcquire());

        gate.Reset();
        Assert.True(gate.TryAcquire());
    }

    private static IReadOnlyList<TControl> FindDescendants<TControl>(Control root)
        where TControl : Control
    {
        var results = new List<TControl>();
        foreach (Control child in root.Controls)
        {
            if (child is TControl match)
            {
                results.Add(match);
            }

            results.AddRange(FindDescendants<TControl>(child));
        }

        return results;
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI test did not finish.");

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
