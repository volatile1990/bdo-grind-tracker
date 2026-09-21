using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using System.Text.Json;

namespace BdoGrindTracker.App.Tests;

public sealed class CombatStatsColorClassifierTests
{
    [Fact]
    public void ActualUserNumberPixelsAreEdaniaWithoutExaminingGoldenIcons()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "fixtures", "experience", "combat-stats-number-colors-20260911.json")));
        var samples = fixture.RootElement.GetProperty("samples");
        Assert.Equal(2, samples.GetArrayLength());
        foreach (var sample in samples.EnumerateArray())
        {
            var colors = sample.GetProperty("rgbCounts").EnumerateArray().SelectMany(row =>
                Enumerable.Repeat(Color.FromArgb(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32()), row[3].GetInt32())).ToArray();
            var bounds = sample.GetProperty("bounds");
            Assert.Equal(bounds[2].GetInt32() * bounds[3].GetInt32(), colors.Length);
            Assert.Equal(CombatStatsCategory.Edania, CombatStatsColorClassifier.Classify(colors));
        }
    }

    [Theory]
    [InlineData(235, 235, 235, CombatStatsCategory.General)]
    [InlineData(227, 210, 235, CombatStatsCategory.Edania)]
    [InlineData(207, 186, 221, CombatStatsCategory.Edania)]
    [InlineData(250, 205, 105, CombatStatsCategory.Demihuman)]
    [InlineData(248, 172, 80, CombatStatsCategory.Demihuman)]
    [InlineData(145, 205, 245, CombatStatsCategory.Kamasylvian)]
    [InlineData(190, 212, 240, CombatStatsCategory.Kamasylvian)]
    public void ReadsWhitePurpleYellowOrangeAndBlueNumberStrokes(int r, int g, int b, CombatStatsCategory expected)
        => Assert.Equal(expected, CombatStatsColorClassifier.Classify(Pixels(Color.FromArgb(r, g, b))));

    [Fact]
    public void DesaturatedObservedPurpleSurvivesBrighterWhiteAntialiasing()
    {
        var pixels = Pixels(Color.FromArgb(227, 210, 235)).ToList();
        pixels.AddRange(Enumerable.Repeat(Color.FromArgb(242, 242, 242), 5));
        Assert.Equal(CombatStatsCategory.Edania, CombatStatsColorClassifier.Classify(pixels));
    }

    [Fact]
    public void ConflictingCategoryStrokesRemainUnknown()
    {
        var pixels = Pixels(Color.FromArgb(227, 210, 235)).ToList();
        pixels.AddRange(Enumerable.Repeat(Color.FromArgb(145, 205, 245), 30));
        Assert.Null(CombatStatsColorClassifier.Classify(pixels));
    }

    [Theory]
    [InlineData(240, 80, 80)]
    [InlineData(80, 240, 80)]
    [InlineData(90, 85, 100)]
    public void UnsupportedHueOrUnreadableDarkTextIsUnknown(int r, int g, int b)
        => Assert.Null(CombatStatsColorClassifier.Classify(Pixels(Color.FromArgb(r, g, b))));

    [Fact]
    public void UniformBackgroundAndTooFewForegroundPixelsCannotBecomeGeneralStats()
    {
        Assert.Null(CombatStatsColorClassifier.Classify(Enumerable.Repeat(Color.White, 100).ToArray()));
        Assert.Null(CombatStatsColorClassifier.Classify(Enumerable.Repeat(Color.Gray, 100)
            .Concat(Enumerable.Repeat(Color.White, 5)).ToArray()));
        Assert.Null(CombatStatsColorClassifier.Classify([]));
    }

    private static Color[] Pixels(Color foreground) => Enumerable.Repeat(Color.FromArgb(85, 85, 85), 90)
        .Concat(Enumerable.Repeat(foreground, 30)).ToArray();
}
