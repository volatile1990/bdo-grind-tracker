# Black-Desert-Textsprache: Deutsch und Englisch

Grindcrest unterstützt beide Spielsprachen für das normale Droplog, Rare-Drops,
die zusätzliche Namens-/Mengenerkennung. Die Einstellung
**Spielsprache in Black Desert** steht standardmäßig auf **Automatisch aus BDO-Einstellungen**
und wird wie die übrigen Einstellungen automatisch gespeichert.

## Automatische Erkennung

Die App sucht die Installation anhand ihrer Windows-Deinstallationseinträge
(Steam und eigenständiger Client, Benutzer/Maschine, 32-/64-Bit-Registrierungsansicht).
Sie liest ausschließlich die Textspracheneinstellung:

1. Einen expliziten `Language`-Eintrag in `Documents/Black Desert/GameOption.txt`, falls vorhanden.
2. Andernfalls `[SERVICE] RES` in `Resource.ini` des Installationsverzeichnisses:
   `_EN_` entspricht Englisch, `_DE_` Deutsch.

Launcher-Sprache (`Lan.txt`, `webvLanguage`), Sprachausgabe (`AudioResourceType`),
Chatkanal-Sprache (`LangType`) und Vertriebsregion (`service.ini`) sind keine
Ersatzwerte. In der geprüften Installation standen beispielsweise `Lan.txt` auf
`DE`, aber `Resource.ini` auf `_EN_`; die erkannte Textsprache ist folglich Englisch.

Beim Öffnen der App und vor jedem Tracking-Start wird die Konfiguration erneut
gelesen. Mehrere Installationen mit widersprüchlichen Werten, ungültige Dateien
oder nicht unterstützte Textsprachen führen zu einer verständlichen Meldung.
Die Automatik startet dann keine Erfassung mit einer geratenen Sprache. Deutsch
oder Englisch kann vor einer neuen Session manuell ausgewählt werden.

Für die Erkennung wird das passende installierte Windows-OCR-Modell verwendet
(`de-DE` bzw. `en-US`). Fehlt es, bleibt die Session ungestartet; die Fehlermeldung
verweist auf die Windows-Sprachoptionen. Eine manuelle Sprachwahl benötigt keinen
App-Neustart. Die Spieleinstellungen werden niemals verändert.

## Itemnamen, Identität und vollständige Abdeckung

[`data/items.de.json`](../data/items.de.json) enthält alle 57 Itemnamen des
Tracker-Vokabulars, einschließlich aller 207 Item/Spot-Kombinationen der
Dropmengentabelle und des unterstützten Event-Items. Die deutschen Namen wurden
am 8. September 2026 über die bereits zugeordneten Item-IDs von den deutschen
BDO-Codex-Itemseiten gelesen. Jeder Eintrag enthält seine Quelle, beispielsweise
[BON-Kristall des wandernden Ursprungs](https://bdocodex.com/de/item/15295/).
Alle 57 Namen wurden zusätzlich mit der installierten NAEU-Datei
`ads/languagedata_de.loc` (Itemtabelle 0, Namensfeld 0) abgeglichen: keine Abweichung.

Der eingebettete Katalog ordnet deutsche Namen den bestehenden kanonischen
Schlüsseln zu. Preise, NPC-Werte, Spotfilter, Min-/Max-Grenzen, feste 1/1-Drops,
Verlauf, manuelle Mengenänderungen und Garmoth verwenden dadurch dieselben Items.
Es gibt keine sprachabhängigen Doppelbuchungen und keine Datenmigration.

Umlaute, ß, Bindestriche und OCR-Leerzeichen werden bei deutschen Namen einheitlich
normalisiert. Bei unscharfen Treffern muss ein Item klar unterscheidbar bleiben;
insbesondere werden fehlende Varianten von Ursprungskristallen, Urklasse-Farbmitteln
und Zubehör nicht einfach ergänzt. Die vorhandene englische Erkennung bleibt aktiv.

Die Lootanzeige und Mengen-Editoren verwenden bei deutscher Spielsprache deutsche
Namen. Die Lootsuche akzeptiert in beiden Anzeigemodi deutsche und englische Namen.

## Prüfung

- Jede erlaubte deutsche Item/Spot-Kombination: Zuordnung, Minimum-Fallback,
  Maximum und feste 1er-Mengen, einschließlich des Rare-Kanals für Nicht-Trashdrops.
- Alle 57 Namen im normalen und Rare-Matcher, einschließlich normalisierter
  Schreibweisen und ähnlicher, unvollständiger Rare-Namen.
- Konfigurationspriorität, widersprüchliche Installationen, Sprachwechsel,
  fehlendes OCR-Modell, persistierte manuelle Auswahl und Startblockade.
- Tatsächliche Windows-OCR mit `de-DE`: alle 57 synthetisch gerenderten Itemnamen korrekt erkannt.
- Isolierte WebView-Vorschau: Sprachwahl, zweisprachige Suche, Mengenänderung,
  automatisches Speichern und Layout bei 1440, 860 und 760 Pixeln geprüft.

Synthetische OCR-Prüfungen ersetzen keine deutsche Grind-Aufnahme mit Kampfeffekten,
HDR und dem individuellen UI-Layout. Es wurde für diese Änderung kein fertiges
App-Artefakt veröffentlicht oder gepackt.
