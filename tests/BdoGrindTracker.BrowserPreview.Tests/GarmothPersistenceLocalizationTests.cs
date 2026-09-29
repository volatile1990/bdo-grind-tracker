using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class GarmothPersistenceLocalizationTests
{
    [Fact]
    public void UploadResultKeepsEveryOptionalNoteInTheSelectedLanguage()
    {
        const string source = "Sitzung erfolgreich an Garmoth übertragen. " +
            "Ohne Garmoth-Zuordnung ausgelassen: Black Crystal Fragment, Origin of Hunger. " +
            "Silber ist eine Teilsumme; fehlende Preise wurden nicht geschätzt. " +
            "Silber verwendet den letzten verfügbaren Preisstand. " +
            "Zum Weitergrinden eine neue Sitzung starten. " +
            "Der lokale Verlaufstatus konnte nicht gespeichert werden. Der Dublettenschutz bleibt im Uploadjournal erhalten.";

        Assert.Equal(source, AppText.Translate(source, "de"));
        var english = AppText.Translate(source, "en");
        Assert.StartsWith("Session uploaded to Garmoth successfully.", english);
        Assert.Contains("Skipped without a Garmoth mapping: Black Crystal Fragment, Origin of Hunger.", english);
        Assert.Contains("Silver is a subtotal; missing prices were not estimated.", english);
        Assert.Contains("Silver uses the last available prices.", english);
        Assert.Contains("Start a new session to continue grinding.", english);
        Assert.Contains("The local history status could not be saved.", english);
        Assert.DoesNotContain("nicht gespeichert", english);
    }

    [Fact]
    public void RejectedResultAndJournalFailureTranslateWhenCombined()
    {
        const string source = "Garmoth hat die Sitzungsdaten abgelehnt. Klasse, Loot und Dauer prüfen. " +
            "Das Upload-Ergebnis konnte lokal nicht gespeichert werden. Die Uploadabsicht bleibt erhalten; weitere Uploads sind zum Schutz vor Dubletten gesperrt.";

        Assert.Equal(source, AppText.Translate(source, "de"));
        Assert.Equal("Garmoth rejected the session data. Check the class, loot and duration. " +
            "The upload result could not be saved locally. The upload intent remains recorded; further uploads are blocked to prevent duplicates.",
            AppText.Translate(source, "en"));
    }

    [Theory]
    [InlineData("Die aktuelle Session konnte nicht gelesen werden und wird nicht überschrieben. Bitte prüfe die Datei C:\\User Data\\current-session-v1.json.",
        "The current session could not be read and will not be overwritten. Check the file C:\\User Data\\current-session-v1.json.")]
    [InlineData("Die Einstellungen konnten nicht gelesen werden und werden nicht überschrieben. Bitte prüfe die Datei C:\\User Data\\settings.json und versuche das erneute Laden und Sichern.",
        "Settings could not be read and will not be overwritten. Check the file C:\\User Data\\settings.json and try loading and saving again.")]
    [InlineData("Der Verlauf konnte nicht gelesen werden und wird nicht überschrieben. Bitte prüfe die Datei C:\\User Data\\loot-history-v1.json und versuche das Speichern erneut.",
        "History could not be read and will not be overwritten. Check the file C:\\User Data\\loot-history-v1.json and try saving again.")]
    public void PersistenceLoadErrorPreservesTheFilePath(string german, string english)
    {
        Assert.Equal(german, AppText.Translate(german, "de"));
        Assert.Equal(english, AppText.Translate(german, "en"));
    }

    [Theory]
    [InlineData("Verlauf noch nicht gespeichert. Die Session bleibt in Grindcrest erhalten.",
        "History has not been saved yet. The session remains in Grindcrest.")]
    [InlineData("Die aktuelle Session ist noch nicht gespeichert.", "The current session has not been saved yet.")]
    [InlineData("Die neue Session konnte noch nicht angelegt werden.", "The new session could not be created yet.")]
    [InlineData("Das Garmoth-Uploadjournal konnte nicht gelesen werden. Uploads sind zum Schutz vor Dubletten gesperrt.",
        "The Garmoth upload journal could not be read. Uploads are blocked to prevent duplicates.")]
    [InlineData("Die Uploadabsicht konnte nicht sicher gespeichert werden. Es wurde nichts gesendet.",
        "The upload intent could not be saved safely. Nothing was sent.")]
    public void StaticPersistenceErrorsTranslate(string german, string english)
    {
        Assert.Equal(german, AppText.Translate(german, "de"));
        Assert.Equal(english, AppText.Translate(german, "en"));
    }
}
