using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Ocr;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class CombatStatsFrameReaderTests(ITestOutputHelper output)
{
    [WindowsOcrFact]
    [Trait("Category", "WindowsOcr")]
    public void ReadsActualUserCombatStatsWithNativeWindowsOcr()
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        Assert.NotNull(engine);
        using var frame = Load();
        using var reader = new CombatStatsFrameReader((image, token) =>
        {
            var result = engine.Recognize(image, token);
            output.WriteLine(result.Text);
            foreach (var word in result.Words) output.WriteLine($"{word.Text}: {word.Geometry}");
            return result;
        });
        Assert.Equal(new CombatStatsReading(2374, 826, CombatStatsCategory.Edania), reader.Read(frame, CancellationToken.None));
    }

    [Fact]
    public void ActualUserPixelsVerifyBothIconsAndPurpleNumbersIndependentOfOcrAvailability()
    {
        using var frame = Load();
        var calls = 0;
        using var reader = new CombatStatsFrameReader((image, _) =>
        {
            Assert.InRange(image.Width, 100, 2800);
            Assert.InRange(image.Height, 20, 650);
            calls++;
            return ZoomedHudWords();
        });
        Assert.Equal(new CombatStatsReading(2374, 826, CombatStatsCategory.Edania), reader.Read(frame, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void NumericOcrWithoutTheActualSwordAndShieldCannotBecomeStats()
    {
        using var frame = new Bitmap(640, 400);
        using var reader = new CombatStatsFrameReader((_, _) => ZoomedHudWords());
        Assert.Null(reader.Read(frame, CancellationToken.None));
    }

    [Fact]
    public void MissingShieldRejectsAnOtherwiseReadableUserFrame()
    {
        using var frame = Load();
        using (var graphics = Graphics.FromImage(frame))
        using (var brush = new SolidBrush(Color.FromArgb(85, 85, 85))) graphics.FillRectangle(brush, 264, 10, 44, 38);
        using var reader = new CombatStatsFrameReader((_, _) => ZoomedHudWords());
        Assert.Null(reader.Read(frame, CancellationToken.None));
    }

    [Fact]
    public void ConflictingOcrPreparationsCannotChooseAnAttackValue()
    {
        using var frame = Load();
        var calls = 0;
        using var reader = new CombatStatsFrameReader((_, _) => ZoomedHudWords(++calls == 1 ? "2374" : "2371"));
        Assert.Null(reader.Read(frame, CancellationToken.None));
    }

    [Fact]
    public void GeometryPairsOnlyStatsAndRejectsWeightAndExperience()
    {
        var values = Result(Word("65", 40, 20, 55, 47), Word("38.907%", 23, 80, 88, 18),
            Word("2374", 200, 17, 47, 18), Word("826", 313, 17, 34, 18),
            Word("224", 500, 17, 34, 18), Word("/", 538, 17, 8, 18), Word("224", 550, 17, 34, 18));
        var result = Parse(values);
        Assert.Equal(new CombatStatsReading(2374, 826, CombatStatsCategory.Edania), result);
        Assert.Null(Parse(Result(Word("224", 500, 17, 34, 18), Word("224", 550, 17, 34, 18))));
        Assert.Null(Parse(Result(Word("65", 40, 20, 55, 47), Word("38.907%", 23, 80, 88, 18))));
    }

    [Fact]
    public void WrongBaselineUnknownColorAndMismatchedCategoriesAreRejected()
    {
        Assert.Null(Parse(Result(Word("2374", 200, 17, 47, 18), Word("826", 313, 50, 34, 18))));
        Assert.Null(CombatStatsFrameReader.Parse(NormalWords(), new(640, 150), _ => null, (_, _) => true));
        Assert.Null(CombatStatsFrameReader.Parse(NormalWords(), new(640, 150),
            bounds => bounds.Left < 250 ? CombatStatsCategory.Edania : CombatStatsCategory.General, (_, _) => true));
        Assert.Null(CombatStatsFrameReader.Parse(NormalWords(), new(640, 150),
            _ => CombatStatsCategory.Edania, (_, _) => false));
    }

    [Fact]
    public void MultipleCompletePairsAreAmbiguous()
        => Assert.Null(Parse(Result(Word("2374", 200, 17, 47, 18), Word("826", 313, 17, 34, 18),
            Word("2374", 430, 17, 47, 18), Word("826", 543, 17, 34, 18))));

    [Theory]
    [InlineData("224/224")]
    [InlineData("38.907%")]
    [InlineData("2,374")]
    [InlineData("237A")]
    [InlineData("-826")]
    [InlineData("0")]
    [InlineData("10001")]
    public void DoesNotRepairAmbiguousOcrText(string value) => Assert.Null(CombatStatsFrameReader.ParseNumber(value));

    private static CombatStatsReading? Parse(CompanionOcrResult words) => CombatStatsFrameReader.Parse(words,
        new(640, 150), _ => CombatStatsCategory.Edania, (_, _) => true);
    private static CompanionOcrResult NormalWords() => Result(Word("2374", 200, 17, 47, 18), Word("826", 313, 17, 34, 18));
    private static CompanionOcrResult ZoomedHudWords(string ap = "2374") => Result(
        Word(ap, 416, 50, 94, 36), Word("826", 642, 50, 68, 36), Word("224/224", 1016, 50, 174, 36));
    private static Bitmap Load() => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "experience", "level-65-38.907-hud-user-20260911.png"));
    private static CompanionOcrWord Word(string text, float x, float y, float width, float height) =>
        new(text, new(CompanionOcrGeometryStatus.Success, x, y, width, height));
    private static CompanionOcrResult Result(params CompanionOcrWord[] words) =>
        new(string.Join(" ", words.Select(word => word.Text)), words.FirstOrDefault()?.Geometry ?? default) { Words = words };
}
