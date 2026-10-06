namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static void AddRareDrops(Dictionary<string, string> text)
    {
        text["Über Durchschnitt · Ø {0} erwartet"] = "Above average · Ø {0} expected";
        text["Unter Durchschnitt · Ø {0} erwartet"] = "Below average · Ø {0} expected";
        text["Im Durchschnitt · Ø {0} erwartet"] = "At average · Ø {0} expected";
        text["Garmoth: {0} Drops / h bei 100 % Dropratenbonus."] = "Garmoth: {0} drops / h at a 100% drop rate bonus.";
        text["Dein Bonus: {0} %. Hochrechnung: (100 + {0}) / 200 = {1}; {2} Drops / h."] = "Your bonus: {0}%. Scaling: (100 + {0}) / 200 = {1}; {2} drops / h.";
        text["Dein Bonus: {0} %. An diesem Grindspot beeinflusst die Droprate die Referenz nicht: {1} Drops / h."] = "Your bonus: {0}%. The drop rate does not affect the reference at this grind spot: {1} drops / h.";
        text["Erwartet für {0} aktive Grindzeit (ohne Pausen): {1} Drops. Referenz: Stand {2}. Quelle: {3}"] = "Expected for {0} active grind time (excluding pauses): {1} drops. Reference: updated {2}. Source: {3}";
        text["Trash-Abgleich: {0} Trash / h in deiner Session / {1} Trash / h bei Garmoth ({2}) = Faktor {3}. Angepasst: {4} Drops / h."] = "Trash adjustment: {0} trash / h in your session / {1} trash / h at Garmoth ({2}) = factor {3}. Adjusted: {4} drops / h.";
        text["Droprate"] = "Drop rate";
        text["Droprate für den Rare-Drop-Vergleich"] = "Drop rate for the rare drop comparison";
        text["Rare-Drop-Vergleich"] = "Rare drop comparison";
        text["Garmoth Ø nicht verfügbar"] = "Garmoth average unavailable";
        text["Vergleich ab erstem Drop"] = "Comparison from the first drop";
        text["Vergleich ab erstem Trash-Drop"] = "Comparison from the first trash drop";
        text["Der Rare-Drop-Vergleich berücksichtigt deinen Dropratenbonus und dein Trash pro Stunde im Verhältnis zur Garmoth-Referenz."] = "The rare drop comparison accounts for your drop rate bonus and your trash per hour relative to the Garmoth reference.";
        text["Garmoth-Durchschnitt bei 100 % × (100 + deine Droprate) / 200 × aktive Grindzeit."] = "Garmoth average at 100% × (100 + your drop rate) / 200 × active grind time.";
        text["Öffentliche Garmoth-Referenz bei 100 %; auf deine Droprate hochgerechnet."] = "Public Garmoth reference at 100%; scaled to your drop rate.";
        text["Bitte gib eine Droprate zwischen 0 und 1.000 % ein."] = "Please enter a drop rate between 0 and 1,000%.";
        text["Die Droprate muss zwischen 0 und 1000 % liegen."] = "The drop rate must be between 0 and 1000%.";
        text["Garmoth zeigt den Dropratenbonus. Der Vergleich rechnet mit (100 + deinem Bonus) / 200 gegenüber dem öffentlichen Wert bei 100 %."] = "Garmoth shows the drop rate bonus. The comparison scales the public value at 100% by (100 + your bonus) / 200.";
    }
}
