using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class AmpersandLabelLocalizationTests
{
    [Theory]
    [InlineData("Verbindung & Upload", "Connection & Upload")]
    [InlineData("Kalender & Ziele", "Calendar & Goals")]
    [InlineData("Einführung & Einrichtung", "Introduction & Setup")]
    [InlineData("Anzeige & Verhalten", "Display & Behavior")]
    [InlineData("Fenster & Tray", "Window & Tray")]
    [InlineData("Silber & Markt", "Silver & Market")]
    [InlineData("Silber & Zentralmarkt", "Silver & Central Market")]
    [InlineData("Buffs & Kosten", "Buffs & Costs")]
    [InlineData("Metriken & Steuerung", "Metrics & Controls")]
    [InlineData("Seltene Drops & Favoriten", "Rare Drops & Favorites")]
    [InlineData("Verschieben & bedienen", "Move & Interact")]
    [InlineData("Dezent · Gold & Grau", "Subtle · Gold & Gray")]
    [InlineData("Uhrzeit & Tag/Nacht", "Clock & Day/Night")]
    public void ShortLabelsUseConsistentEnglishCapitalization(string german, string english)
    {
        Assert.Equal(german, AppText.Translate(german, "de"));
        Assert.Equal(english, AppText.Translate(german, "en"));
    }
}
