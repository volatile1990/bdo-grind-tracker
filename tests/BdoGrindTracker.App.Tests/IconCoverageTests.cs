using System.Security.Cryptography;
using System.Text.Json;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class IconCoverageTests
{
    private static string IconDirectory => Path.Combine(AppContext.BaseDirectory, "data", "icons");

    public static IEnumerable<object[]> ExactIconNames() => LootSpotCatalog.Spots
        .SelectMany(static spot => spot.AllowedItems)
        .Concat(LootSpotCatalog.EventItems)
        .Append("Black Gem Fragment")
        .Where(static name => name != "Pure Black Stone")
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .Select(static name => new object[] { name });

    [Theory]
    [MemberData(nameof(ExactIconNames))]
    public void EveryUnambiguousSupportedItemHasVerifiedPackagedIconAndUiLookup(string name)
    {
        var catalog = ReadCatalog();
        Assert.True(catalog.TryGetValue(name, out var entry), $"Icon catalog entry missing: {name}");
        var fileName = entry.GetProperty("file").GetString()!;
        Assert.Equal(AssetNames.ItemSlug(name) + ".png", fileName);
        Assert.Equal(fileName, Path.GetFileName(fileName));
        var bytes = File.ReadAllBytes(Path.Combine(IconDirectory, fileName));
        Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.True(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        Assert.InRange(image.Width, 44, 48);
        Assert.Equal(44, image.Height);
        Assert.Equal(image.Width, entry.GetProperty("width").GetInt32());
        Assert.Equal(image.Height, entry.GetProperty("height").GetInt32());

        var sourcePage = new Uri(entry.GetProperty("page").GetString()!);
        var sourceIcon = new Uri(entry.GetProperty("sourceIcon").GetString()!);
        Assert.Equal("https", sourcePage.Scheme);
        Assert.Equal("bdocodex.com", sourcePage.Host);
        Assert.Equal($"/us/item/{entry.GetProperty("itemId").GetString()}/", sourcePage.AbsolutePath);
        Assert.Equal("https", sourceIcon.Scheme);
        Assert.Equal("bdocodex.com", sourceIcon.Host);
        Assert.StartsWith("/items/", sourceIcon.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith(".webp", sourceIcon.AbsolutePath, StringComparison.Ordinal);

        if (GarmothCatalog.TryGetDropKey(name, out var dropKey))
            Assert.Equal(dropKey.Split('_')[0], entry.GetProperty("itemId").GetString());
        var priceDefinition = LootPriceCatalog.Definitions.Single(definition => definition.ItemName == name);
        if (priceDefinition.MarketItemId is { } marketId)
            Assert.Equal(marketId.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.GetProperty("itemId").GetString());

        Assert.Equal("assets/icons/" + fileName, Presentation.ItemIcon(name));
    }

    [Theory]
    [InlineData("Branch of Abundance", "980127")]
    [InlineData("Black Crystal Fragment", "980128")]
    [InlineData("Elion Follower's Helmet", "980129")]
    [InlineData("Scorched Belt Ornament", "980131")]
    [InlineData("Elion Follower's Mark", "980130")]
    [InlineData("Broken Gloves of the Void", "980132")]
    public void EachSpotTrashHasItsOwnExactItemIcon(string name, string itemId)
    {
        var entry = ReadCatalog()[name];
        Assert.Equal(itemId, entry.GetProperty("itemId").GetString());
        Assert.Contains(itemId, entry.GetProperty("sourceIcon").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EverySupportedItemHasAnIconIncludingRepresentativePureBlackStone()
    {
        var catalog = ReadCatalog();
        var names = LootSpotCatalog.Spots.SelectMany(static spot => spot.AllowedItems)
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(56, names.Length);
        Assert.DoesNotContain(names, name => !catalog.ContainsKey(name));
        Assert.Equal(58, catalog.Count);
        Assert.Equal(58, Directory.GetFiles(IconDirectory, "*.png").Length);
        Assert.True(catalog.ContainsKey("Pure Black Stone"));
        Assert.Equal("assets/icons/pure-black-stone.png", Presentation.ItemIcon("Pure Black Stone"));
        Assert.Equal(35, catalog.Values.Count(static entry => entry.TryGetProperty("addedAtUtc", out _)));
    }

    private static Dictionary<string, JsonElement> ReadCatalog()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(IconDirectory, "catalog.json")));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        return document.RootElement.GetProperty("items").EnumerateArray().ToDictionary(
            static entry => entry.GetProperty("name").GetString()!, static entry => entry.Clone(), StringComparer.Ordinal);
    }
}
