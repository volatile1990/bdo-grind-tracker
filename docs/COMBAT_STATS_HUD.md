# AP/DP aus der Spielanzeige

Grindcrest liest während der Aufnahme AP und DP links oben im Black-Desert-
Spielfenster. Die zwei Werte und ihre Schriftfarbe bilden eine gemeinsame
Beobachtung. Es werden die tatsächlich angezeigten Werte übernommen; AAP oder
nicht angezeigte Kategorien werden nicht berechnet.

| Schriftfarbe | Kategorie |
|---|---|
| Weiß | Allgemein |
| Lila | Edania |
| Orange/Gelb | Halbmenschen |
| Blau | Kamasilvia |

Die Zuordnung folgt der vom Nutzer beschriebenen Spielanzeige. Sie wird aus den
Pixeln der Zahlen erkannt, nicht aus dem Grindspot abgeleitet. Die vorhandene
Referenz `tests/fixtures/experience/level-65-38.907-hud-user-20260911.png` zeigt
2.374 AP / 826 DP in Lila. Die benachbarte Level-/EP-Anzeige, Gewichtsangaben und
goldene HUD-Symbole dürfen nicht als Kampfwerte oder Farbnachweis dienen.

Erfasste Werte werden nur verwendet, wenn ihre Kategorie zum aktuellen Spot
passt oder es weiße **Allgemeinwerte** sind. An einem Edania-Spot sind also
Edania und Allgemein zulässig, Halbmenschen und Kamasilvia werden ignoriert.
Die Prüfung verwendet die bestätigte Monsterkategorie des Spots, nicht seine
geografische Region. Solange der Spot unbekannt ist, sind nur Allgemeinwerte
zulässig. Ein Wechsel der Kategorie überschreibt keinen zuvor erfassten,
weiterhin passenden Stand mit unpassenden Werten.

## Bestätigung und Anzeige

Die optionale Erkennung läuft im Hintergrund während der bestehenden Aufnahme.
Mindestens zwei aufeinanderfolgende übereinstimmende Beobachtungen bestätigen
Werte und Kategorie. Fehler oder eine nicht sichtbare Spielanzeige blockieren
die Loot-Erfassung nicht. Unklare Werte werden nicht geraten.

In der Live-Session erscheinen AP, DP und der ausgeschriebene Kategoriename in
der passenden Farbe. Nach einer Pause oder bei fehlenden aktuellen Werten wird
der letzte bestätigte, zum Spot passende Stand mit **Zuletzt erkannt** bezeichnet. Ohne Bestätigung
steht dort **AP/DP noch nicht erkannt**.

Die aktuelle Session und der Verlauf speichern AP, DP, Kategorie und
Beobachtungszeitpunkt. Es ist jeweils der zuletzt bestätigte Stand dieser
Session, kein Durchschnitt und kein vollständiger Änderungsverlauf. Eine neue
Session beginnt ohne übernommene Kampfwerte. Beim Wiederherstellen einer
pausierten Session bleibt ihr gespeicherter Stand erhalten, gilt aber nicht als
frische Erkennung. Auch beim Wiederherstellen, Speichern und Anzeigen im Verlauf
gilt die Kategorieprüfung für den jeweiligen Session-Spot. Alte Sessions ohne
passende Daten zeigen **AP/DP nicht erfasst**.

Der frühere Garmoth-Build-Import samt Linkfeld, Abruf und Gear-Cache wurde
entfernt. Garmoths bestehende Session-Uploads und öffentliche Grindspot-
Referenzwerte sind davon unabhängig.

## Vorschau und Prüfung

Die portable Browser-Vorschau zeigt gekennzeichnete Beispieldaten einschließlich
verschiedener Kategorien und Sessions ohne AP/DP. Sie liest kein Spielfenster.
Produktive OCR und die native Bildverarbeitung laufen in der Windows-App.
Tests prüfen Erkennungsmerkmale, Farbzuordnung, Bestätigung, veraltete und
abgebrochene Beobachtungen, Sessionwechsel sowie Speicherung und Darstellung.
Die vorhandene Lila-Referenz deckt nicht jede HUD-Skalierung, Farbdarstellung
oder die drei weiteren Kategorien in echten Spielaufnahmen ab.
