using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class PaddleCharacterConfidenceTests
{
    [Fact]
    public void ScoresFollowTrimmedUtf16TextIncludingSurrogatesAndMultiCharacterTokens()
    {
        string[] alphabet = ["blank", " ", "ü", "𠀀", "10", "\t"];
        (int Index, float Score)[] tokens = [(1, .81f), (1, .82f), (2, .83f), (3, .84f), (4, .85f), (5, .86f)];
        var result = Decode(alphabet, tokens);

        Assert.Equal("ü𠀀10", result.Text);
        Assert.Equal(new[] { .83f, .84f, .84f, .85f, .85f }, result.CharacterConfidences);
        Assert.Equal(result.Text.Length, result.CharacterConfidences.Count);
        Assert.True(Assert.IsAssignableFrom<IList<float>>(result.CharacterConfidences).IsReadOnly);
        Assert.InRange(result.Confidence, .837f, .839f); // Existing token mean includes trimmed spaces.
    }

    [Fact]
    public void BlankSeparatedRepeatedDigitsKeepTheirOwnScores()
    {
        var result = Decode(["blank", "1", "0"], [(1, .99f), (1, .98f), (0, .99f), (1, .63f), (2, .97f)]);
        Assert.Equal("110", result.Text);
        Assert.Equal(new[] { .99f, .63f, .97f }, result.CharacterConfidences);
        Assert.InRange(result.Confidence, .863f, .864f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptyAndWhitespaceOnlyReadsHaveNoMisalignedEvidence(int index)
    {
        var result = Decode(["blank", " \t"], [(index, .99f)]);
        Assert.Empty(result.Text);
        Assert.Empty(result.CharacterConfidences);
    }

    [Fact]
    public void EntireRowAverageCannotHideTheWeakFinalQuantityDigit()
    {
        var name = "Scorched Belt Ornament x 10";
        var alphabet = new[] { "blank" }.Concat((name + "1").Distinct().Select(character => character.ToString())).ToArray();
        var indices = new List<(int, float)>();
        foreach (var character in name)
        {
            indices.Add((Array.IndexOf(alphabet, character.ToString()), .999f));
            indices.Add((0, .999f));
        }
        indices.Add((Array.IndexOf(alphabet, "1"), .6494229f));
        var result = Decode(alphabet, indices.ToArray());

        Assert.Equal(name + "1", result.Text);
        Assert.True(result.Confidence > .98f);
        Assert.Equal(.6494229f, result.CharacterConfidences[^1]);
        Assert.Equal(.999f, result.CharacterConfidences[^2]);
    }

    private static SecondaryLootOcrResult Decode(string[] alphabet, (int Index, float Score)[] tokens) =>
        PaddleLootOcrRecognizer.Decode(tokens.SelectMany(token => Enumerable.Range(0, alphabet.Length)
            .Select(index => index == token.Index ? token.Score : .001f)).ToArray(), alphabet);
}
