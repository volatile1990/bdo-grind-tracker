using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationMessageLanguageTests
{
    public static IEnumerable<object[]> Messages() => LocalizedRotationMessages.Patterns
        .Select(p => new object[] { p.Spot, p.Kind, p.Language, p.Text });

    [Theory]
    [MemberData(nameof(Messages))]
    public void BannerTextRecognizesExactlyItsExistingEvent(string spot, string kind, string language, string text)
    {
        var parse = spot == "magaia-names" ? RotationProfiles.Messages("magaia", language)!.Names!.Parse
            : RotationProfiles.Messages(spot, language)!.Parse;
        Assert.Equal(kind, Assert.Single(parse(text)).Kind);
        Assert.Equal(kind, Assert.Single(parse(text.ToUpperInvariant().Normalize(NormalizationForm.FormD))).Kind);
    }

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("de", "de-DE")]
    [InlineData("fr", "fr-FR")]
    [InlineData("sp", "es-ES")]
    public void ProfilesSelectMatchingWindowsOcrLanguage(string language, string tag)
    {
        Assert.All(RotationProfiles.SupportedSpotIds, spot =>
            Assert.Equal(tag, RotationProfiles.Messages(spot, language)!.OcrLanguageTag));
        Assert.Equal(language, RotationMessageLanguage.Resolve("auto", language));
        Assert.Equal(language, RotationMessageLanguage.Resolve(language, "en"));
    }

    [Theory]
    [InlineData("de", "Die Sünder werden gerufen.")]
    [InlineData("fr", "Les Pécheurs sont invoqués.")]
    [InlineData("sp", "Los culpables reciben la llamada.")]
    public void StandbyWatcherUsesSelectedLanguage(string language, string text)
    {
        using var frame = new Bitmap(2560, 1440);
        using var watcher = new RotationStartWatcher(_ => text, language);
        Assert.Equal("magaia", watcher.Observe(frame, DateTimeOffset.UnixEpoch)?.SpotId);
        using var english = new RotationStartWatcher(_ => text, "en");
        Assert.Null(english.Observe(frame, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("sp")]
    public void RepeatedFragmentsCountOncePerOccurrenceAcrossOcrPasses(string language)
    {
        var text = LocalizedRotationMessages.Patterns.Single(p => p.Spot == "magaia" &&
            p.Kind == "fragment" && p.Language == language).Text;
        var profile = RotationProfiles.Messages("magaia", language)!;
        Assert.Equal(2, profile.CountLines!($"{text} {text}\n{text} {text}", "fragment"));
        Assert.Equal(2, profile.CountLines!($"{text}\n{text}\f{text}\n{text}", "fragment"));
        Assert.Equal(1, profile.CountLines!($"{text}\n{text}", "fragment"));
        Assert.Equal(0, profile.CountLines!("", "fragment"));
    }

    [Fact]
    public void SpanishAgrisDoesNotAlsoTriggerTheOrdinaryHog()
    {
        var profile = RotationProfiles.Messages("aphrodon", "sp")!;
        const string hog = "Se siente la energía de la abundancia.";
        const string agris = "Se siente la energía de la abundancia fascinante.";
        Assert.Equal("hog", Assert.Single(profile.Parse(hog)).Kind);
        Assert.Equal("agris", Assert.Single(profile.Parse(agris)).Kind);
        Assert.Equal(new[] { "hog", "agris" }, profile.Parse(hog + "\n" + agris).Select(p => p.Kind));
    }

    [Theory]
    [InlineData("de", "Mein Vater wartet.")]
    [InlineData("fr", "Mon père est ici.")]
    [InlineData("sp", "Mi padre está aquí.")]
    public void ShortDialogueDoesNotMatchGenericFatherText(string language, string text) =>
        Assert.Empty(RotationProfiles.Messages("hermesia", language)!.Parse(text));

    [Theory]
    [InlineData("de", "Träne Elions (Ereignis)")]
    [InlineData("fr", "La Larme d'Elion (événement)")]
    [InlineData("sp", "Lágrima de Elion (Evento)")]
    public void AmbiguousEventMonsterNamesAreNotNewNameBarAliases(string language, string text) =>
        Assert.Empty(RotationProfiles.Messages("magaia", language)!.Names!.Parse(text));

    [Theory]
    [InlineData("auto")]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("sp")]
    public void LanguagePreferenceSurvivesSettingsUpgrade(string language)
    {
        var settings = new AppSettings { RotationMessageLanguage = language };
        settings.UpgradeDefaults();
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        restored.UpgradeDefaults();
        Assert.Equal(language, restored.RotationMessageLanguage);
    }

    [Fact]
    public void UnsupportedLanguagesCannotSilentlySelectAnOcrEngine()
    {
        Assert.Throws<ArgumentException>(() => RotationProfiles.Messages("hermesia", "ru"));
        var settings = new AppSettings { RotationMessageLanguage = "ru" };
        settings.UpgradeDefaults();
        Assert.Equal("auto", settings.RotationMessageLanguage);
    }
}
