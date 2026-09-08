using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionTextPipelineTests
{
    [Fact]
    public void NormalizeRawText_RemovesOnlyTheVerifiedCharacterSet()
    {
        const string input = "$?<>-/\\&\u2022_!:;,.\u00af%\u00b1 A+'()\"  B";

        var result = CompanionTextPipeline.NormalizeRawText(input);

        Assert.Equal(" A+'()\"  B", result);
    }

    [Fact]
    public void NormalizeRawText_AppliesLiteralRepairsAfterCharacterRemoval()
    {
        const string input = "Fiery Hide | Moss-Covered | MossCovered | Faded Mask";

        var result = CompanionTextPipeline.NormalizeRawText(input);

        Assert.Equal(
            "Fiery Troll Hide | Moss-Covered | Moss-Covered | Faded Bandit Mask",
            result);
    }

    [Fact]
    public void NormalizeRawText_UsesCanonicalDecompositionAndRemovesCombiningMarks()
    {
        const string input = "Caf\u00e9 A\u0308 \u00df \ufb03";

        var result = CompanionTextPipeline.NormalizeRawText(input);

        // NFD decomposes accented letters, but does not compatibility-decompose ß or ﬃ.
        Assert.Equal("Cafe A ß ﬃ", result);
    }

    [Theory]
    [InlineData("Black Stone x123", 123)]
    [InlineData("Black Stone x 123", 123)]
    [InlineData("Black Stone kI", 1)]
    [InlineData("Black Stone vi", 1)]
    public void Process_ExtractsTheFirstVerifiedQuantityForm(
        string input,
        int expectedQuantity)
    {
        var result = CompanionTextPipeline.Process(input, -1, false, 300);

        Assert.Equal("Black Stone", result.Name);
        Assert.Equal(expectedQuantity, result.Quantity);
        Assert.True(result.HasParsedOcrQuantity);
    }

    [Theory]
    [InlineData("Black Stone X123")]
    [InlineData("Black Stone K123")]
    [InlineData("Black Stone V123")]
    [InlineData("Black Stone x  123")]
    public void Process_QuantityRegexIsCaseSensitiveAndAllowsAtMostOneSpace(
        string input)
    {
        var result = CompanionTextPipeline.Process(input, 47, false, 300);

        Assert.Equal(input, result.Name);
        Assert.Equal(47, result.Quantity);
        Assert.False(result.HasParsedOcrQuantity);
    }

    [Fact]
    public void Process_UnanchoredQuantityRegexConsumesOnlyTheFirstFourDigits()
    {
        var result = CompanionTextPipeline.Process("Black Stone x12345", -1, false, 300);

        Assert.Equal("Black Stone 5", result.Name);
        Assert.Equal(1234, result.Quantity);
        Assert.True(result.HasParsedOcrQuantity);
    }

    [Fact]
    public void Process_RemovesOnlyTheFirstQuantityMatch()
    {
        var result = CompanionTextPipeline.Process("Loot x1 x2", -1, false, 300);

        Assert.Equal("Loot  x2", result.Name);
        Assert.Equal(1, result.Quantity);
        Assert.True(result.HasParsedOcrQuantity);
    }

    [Fact]
    public void Process_UsesModeSpecificFallbackWhenQuantityIsNotParsed()
    {
        var normal = CompanionTextPipeline.Process("Loot", 83, false, 300);
        var rare = CompanionTextPipeline.Process("Loot", 83, true, 300);

        Assert.Equal(83, normal.Quantity);
        Assert.Equal(1, rare.Quantity);
        Assert.False(normal.HasParsedOcrQuantity);
        Assert.False(rare.HasParsedOcrQuantity);
    }

    [Theory]
    [InlineData("Helmet x0", 6, 6)]
    [InlineData("Helmet x00", 6, 6)]
    [InlineData("Helmet x0", 0, -1)]
    [InlineData("Helmet x00", 0, -1)]
    [InlineData("Helmet x0", -1, -1)]
    [InlineData("Helmet x00", -1, -1)]
    public void Process_ZeroOcrQuantityUsesPositiveTemplateOrRemainsMissing(
        string input,
        int templateQuantity,
        int expectedQuantity)
    {
        var result = CompanionTextPipeline.Process(input, templateQuantity, false, 300);

        Assert.Equal("Helmet", result.Name);
        Assert.Equal(expectedQuantity, result.Quantity);
        Assert.False(result.HasParsedOcrQuantity);
    }

    [Theory]
    [InlineData("Helmet x0", 0)]
    [InlineData("Helmet x00", 6)]
    [InlineData("Helmet x0", -1)]
    public void Process_ZeroRareOcrQuantityKeepsSingleItemFallback(
        string input,
        int templateQuantity)
    {
        var result = CompanionTextPipeline.Process(input, templateQuantity, true, 300);

        Assert.Equal("Helmet", result.Name);
        Assert.Equal(1, result.Quantity);
        Assert.False(result.HasParsedOcrQuantity);
    }

    [Theory]
    [InlineData("Helmet", 0, -1)]
    [InlineData("Helmet", -1, -1)]
    [InlineData("Helmet x\u0666", 6, 6)]
    [InlineData("Helmet x\u0666", 0, -1)]
    public void Process_UnparsedNormalQuantityUsesOnlyPositiveTemplate(
        string input,
        int templateQuantity,
        int expectedQuantity)
    {
        var result = CompanionTextPipeline.Process(input, templateQuantity, false, 300);

        Assert.Equal("Helmet", result.Name);
        Assert.Equal(expectedQuantity, result.Quantity);
        Assert.False(result.HasParsedOcrQuantity);
    }

    [Theory]
    [InlineData("Tainted Golem's Heart", "Tainted Golem's Heart Fragment")]
    [InlineData("Tainted Heart Fragment", "Tainted Golem's Heart Fragment")]
    [InlineData("Tainted Golem's Fragment", "Tainted Golem's Heart Fragment")]
    [InlineData("Destroyed Ancient Weapon Stone", "Destroyed Ancient Weapon Power Stone")]
    [InlineData("Destroyed Ancient Weapon", "Destroyed Ancient Weapon Power Stone")]
    [InlineData("Destroyed Ancient Weapon Power", "Destroyed Ancient Weapon Power Stone")]
    [InlineData("Underwater Ancient Weapon Stone", "Underwater Ancient Weapon Power Stone")]
    [InlineData("Underwater Ancient Weapon", "Underwater Ancient Weapon Power Stone")]
    [InlineData("Underwater Ancient Weapon Power", "Underwater Ancient Weapon Power Stone")]
    [InlineData("Tainted Moonlight Spirit", "Tainted Moonlight Spirit Powder")]
    [InlineData("ABV1ONCD", "ABWONCD")]
    [InlineData("Troll Hide", "Fiery Troll Hide")]
    [InlineData("Fiery Troll", "Fiery Troll Hide")]
    [InlineData("Sealed Black", "Sealed Black Magic Crystal")]
    [InlineData("Refxyz Essence of Devouring", "Refined Essence of Devouring")]
    [InlineData("Stone Golem s Core", "Stone Golem's Core")]
    public void Process_AppliesTheVerifiedRegexRepairs(string input, string expected)
    {
        var result = CompanionTextPipeline.Process(input, 1, false, 300);

        Assert.Equal(expected, result.Name);
    }

    [Fact]
    public void Process_UsesReplaceOnceExceptForTheWonRepair()
    {
        var result = CompanionTextPipeline.Process(
            "Golem s Golem s V1ON V2ON",
            1,
            false,
            300);

        Assert.Equal("Golem's Golem s WON WON", result.Name);
    }

    [Fact]
    public void Process_TrimsUnicodeWhitespaceAfterAllRegexRepairs()
    {
        var result = CompanionTextPipeline.Process("\u2003Loot\u3000", 1, false, 300);

        Assert.Equal("Loot", result.Name);
    }

    [Fact]
    public void Process_RustEndAnchorDoesNotMatchBeforeTheWhitespaceThatIsTrimmedLater()
    {
        var result = CompanionTextPipeline.Process("Sealed Black\n", 1, false, 300);

        Assert.Equal("Sealed Black", result.Name);
    }

    [Theory]
    [InlineData(193, "Black")]
    [InlineData(194, "Black Stone")]
    [InlineData(198, "Black Stone")]
    [InlineData(199, "Black")]
    public void Process_BlackStoneRepairUsesStrictVerifiedWidthBounds(
        int recognizedTextWidth,
        string expected)
    {
        var result = CompanionTextPipeline.Process(
            "Black",
            1,
            false,
            recognizedTextWidth);

        Assert.Equal(expected, result.Name);
    }

    [Theory]
    [InlineData(94, 115)]
    [InlineData(95, 45)]
    [InlineData(249, 45)]
    [InlineData(250, 25)]
    [InlineData(299, 25)]
    [InlineData(300, 0)]
    [InlineData(399, 0)]
    [InlineData(400, -15)]
    [InlineData(449, -15)]
    [InlineData(450, -30)]
    [InlineData(499, -30)]
    [InlineData(500, -85)]
    public void PassesExpectedWidth_UsesVerifiedPiecewiseBase(
        int recognizedTextWidth,
        int expectedBase)
    {
        var result = new CompanionTextResult("A", 1, false);
        var exactExpectedRight = expectedBase + 24;

        Assert.True(CompanionTextPipeline.PassesExpectedWidth(
            result,
            recognizedTextWidth,
            exactExpectedRight,
            1f));
        Assert.False(CompanionTextPipeline.PassesExpectedWidth(
            result,
            recognizedTextWidth,
            exactExpectedRight + 1,
            1f));
    }

    [Fact]
    public void PassesExpectedWidth_UsesUtf8ByteLengthAndScaleOnlyOnMeasuredTerms()
    {
        var result = new CompanionTextResult("ß", 1, false);

        // base(300)=0; UTF-8 byte length(ß)=2; 2 * 0.5 * 24 = 24.
        Assert.True(CompanionTextPipeline.PassesExpectedWidth(result, 300, 48, 0.5f));
        Assert.False(CompanionTextPipeline.PassesExpectedWidth(result, 300, 50, 0.5f));
    }

    [Fact]
    public void PassesExpectedWidth_ExplicitQuantityBypassesComparisonButNotEmptyName()
    {
        var parsed = new CompanionTextResult("Loot", 10, true);
        var empty = new CompanionTextResult(string.Empty, 10, true);

        Assert.True(CompanionTextPipeline.PassesExpectedWidth(parsed, 500, 10000, 1f));
        Assert.False(CompanionTextPipeline.PassesExpectedWidth(empty, 500, 0, 1f));
    }
}
