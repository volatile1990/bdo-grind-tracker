namespace BdoGrindTracker.Ocr.Tests;

public sealed class PrivateItemChatParserTests
{
    [Theory]
    [InlineData("System You have obtained [Elion Follower's Helmet] x6. (11:48)", "Elion Follower's Helmet", 6)]
    [InlineData("You have obtained [Elion Follower's Helmet] x4.", "Elion Follower's Helmet", 4)]
    [InlineData("[System] You have obtained ◈[Caphras Stone] × 2 (23:59)", "Caphras Stone", 2)]
    [InlineData("You have obtained t' [Elion Follower's Helmet] x6.", "Elion Follower's Helmet", 6)]
    [InlineData("  You have obtained [  Ancient   Spirit Dust ] X100. (00:00:12) ", "Ancient Spirit Dust", 100)]
    [InlineData("You have obtained [Black Stone (Weapon)] x1", "Black Stone (Weapon)", 1)]
    [InlineData("Ihr habt 6 x [Helm eines Anhängers Elions] erhalten.", "Helm eines Anhängers Elions", 6)]
    [InlineData("System Ihr habt 4 x [Schwarzkristallfragment] erhalten. (11:48)", "Schwarzkristallfragment", 4)]
    [InlineData("[System] Ihr habt 2 × ◈[Caphras-Stein] erhalten. (00:00:12)", "Caphras-Stein", 2)]
    [InlineData("Ihr habt 1.234 x [Urgeiststaub] erhalten.", "Urgeiststaub", 1234)]
    [InlineData("Ihr habt 1 x [[Event] Mysteriöses Erz] erhalten.", "[Event] Mysteriöses Erz", 1)]
    public void ParsesCompletePersonalLootMessages(string text, string expectedItem, int expectedQuantity)
    {
        Assert.True(PrivateItemChatParser.TryParse(text, out var item, out var quantity));
        Assert.Equal(expectedItem, item);
        Assert.Equal(expectedQuantity, quantity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[Welt] Alice: Ihr habt 6 x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Alice hat 6 x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt 6 x [Schwarzkristallfragment]")]
    [InlineData("Ihr habt 6.5 x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt 6,5 x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt 6O x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt 0 x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt -1 x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt 2147483648 x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt 2.147.483.648 x [Schwarzkristallfragment] erhalten.")]
    [InlineData("Ihr habt 1 x [Schwarzkristallfragment] erhalten. (24:00)")]
    [InlineData("Ihr habt 1 x [Schwarzkristallfragment] erhalten. Noch eine Nachricht")]
    [InlineData("[World] Alice: You have obtained [Elion Follower's Helmet] x6.")]
    [InlineData("[Private] You have obtained [Elion Follower's Helmet] x6.")]
    [InlineData("You have obtained [Elion Follower's Helmet]")]
    [InlineData("You have obtained [Elion Follower's Helmet] 6.")]
    [InlineData("You have obtained [Elion Follower's Helmet] x4O.")]
    [InlineData("You have obtained [Elion Follower's Helmet] x123abc")]
    [InlineData("You have obtained [Elion Follower's Helmet] x6 4")]
    [InlineData("You have obtained [Elion Follower's Helmet] x6.5")]
    [InlineData("You have obtained [Elion Follower's Helmet] x-6")]
    [InlineData("You have obtained [Elion Follower's Helmet] x0")]
    [InlineData("You have obtained [Elion Follower's Helmet] x2147483648")]
    [InlineData("You have obtained [Elion Follower's Helmet] x６")]
    [InlineData("You have obtained Elion Follower's Helmet x6.")]
    [InlineData("You have obtained unrelated [Elion Follower's Helmet] x6.")]
    [InlineData("You have obtained [ ] x6.")]
    [InlineData("You have obtained [Elion Follower's Helmet] x6. (24:00)")]
    [InlineData("You have obtained [Elion Follower's Helmet] x6. (11:60)")]
    [InlineData("You have obtained [Elion Follower's Helmet] x6. Alice says hello")]
    [InlineData("You have obtained [Elion Follower's Helmet] x6.\nYou have obtained [Black Stone] x1.")]
    public void RejectsAmbiguousOrNonPersonalPayloads(string? text)
    {
        Assert.False(PrivateItemChatParser.TryParse(text, out var item, out var quantity));
        Assert.Empty(item);
        Assert.Equal(0, quantity);
    }

    [Fact]
    public void RepeatedIdenticalLinesRemainSeparateObservations()
    {
        const string text = "You have obtained [Elion Follower's Helmet] x6. (11:48)";
        Assert.True(PrivateItemChatParser.TryParse(text, out var item, out var quantity));
        var first = new PrivateItemChatLine(item, quantity, 20, text);
        var second = new PrivateItemChatLine(item, quantity, 56, text);

        Assert.Equal(first.ItemName, second.ItemName);
        Assert.Equal(first.Quantity, second.Quantity);
        Assert.NotEqual(first.Y, second.Y);
        Assert.Equal(text, first.RawText);
    }
}
