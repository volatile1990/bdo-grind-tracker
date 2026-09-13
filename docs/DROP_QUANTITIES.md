# Dropmengen pro Item und Spot

Die ursprünglichen Nutzervorgaben aus den Excel-Dateien vom 8. und 13. September
2026 bleiben die Grundlage. Die Screenshot-Erweiterung ergänzt alle neuen
Item/Spot-Paare in `data/drop-quantities.json`. Nicht bestätigte seltene Dropmengen
bleiben `null`; daraus wird keine feste Menge oder Obergrenze abgeleitet.

Die anschließende Nutzervorgabe vom 13. September gilt für alle Spots:

| Item | Minimum | Maximum |
| --- | ---: | ---: |
| Trashloot bisheriger Spots | bisheriges Minimum erhalten | 1000 |
| Trashloot neuer Spots | 1 | 1000 |
| Black Stone | 1 | 100 |
| Caphras Stone | 1 | 100 |
| Ancient Spirit Dust | 1 | 100 |
| Laila's Petal | 1 | 10 |

Aphrodon und Hermesia behalten Trash-Minimum 4; Magaia, Aresion, Scales of Judgment
und Event Horizon behalten Minimum 2. Die bisherigen Outer-Edania-Minima bleiben 1.
Das Maximum von Scales of Judgment ist nun ebenfalls 1000. Empty Picture Frame
behält die frühere globale Vorgabe 1–10. Andere bestätigte seltene Mengen bleiben
unverändert. Konkrete Floodlands-Gebiete übernehmen die Mengen ihres bisherigen
Sammelprofils.

Die App prüft beim Laden, dass jedes erlaubte Item/Spot-Paar genau einmal
vorhanden und jeder Bereich gültig ist. Excel wird zur Laufzeit nicht benötigt.
[Spot-Erweiterung und Quellen](SCREENSHOT_SPOTS.md).

## Bedeutung

- **Minimum:** Untergrenze der Menge eines einzelnen Drops und Ersatzwert bei
  einem akzeptierten Itemnamen mit weiterhin nicht gelesener Anzahl.
- **Maximum:** Obergrenze der Menge eines einzelnen Drops. Sie gilt im normalen
  Lootpanel und im Rare-Kanal.
  Mehrere getrennte Drops dürfen die Obergrenze in Summe überschreiten.
- Die Grenzen müssen zum Spot und zur tatsächlich angezeigten Menge passen;
  Trashboni und besondere Gegner sind bei der Bestätigung einzubeziehen.
- Manuelle Gesamtkorrekturen sind von diesen Einzel-Dropgrenzen unabhängig.

## Verarbeitung

**Bestätigte feste Einermengen haben Minimum = Maximum = 1.** Nach Identifikation eines solchen
Items setzt die App direkt Menge 1. Eine Zahl im OCR-Text wird dafür nicht
geparst; getrennte Mengen-Nachlesungen sind unnötig. Auch ein erst bei der
Namenrettung erkanntes festes Item benötigt keine anschließende Mengen-OCR.

Die Ziffernsuche bei der ursprünglichen Vorbereitung normaler Panelzeilen bleibt
erhalten: Sie bestimmt zusätzlich den Namensausschnitt und die Zeilenauswahl,
bevor der Itemname bekannt ist. Diesen geometrischen Teil global abzuschalten
würde auch die Namenserkennung variabler Drops verändern. Der Rare-Bildpfad
verwendet bereits keine Ziffern-Templates.

Der Analyzer ordnet die Grenzen nach der Spotbestimmung den akzeptierten Zeilen
zu. Vor der ersten Trashzeile werden nur Grenzen genutzt, die alle passenden
unterstützten Spots abdecken. Fehlt bei einem davon ein Maximum, wird vor dem
Spotlock keine Obergrenze aus einem anderen Spot übernommen.

Die Zähler begrenzen positive Eingabemengen auf **Minimum bis Maximum vor dem
Identitätsvergleich**. Bei Minimum 4 entspricht `1 → 4` somit `4 → 4`; die
Mengenanhebung erzeugt für eine weiter sichtbare Zeile keinen zusätzlichen Drop.
Eine OCR-Folge `1 → 7 → 1` mit bestätigtem Maximum 1 entspricht dadurch derselben
Mengenfolge `1 → 1 → 1`. Die vorhandene Erkennung getrennter Drops bleibt aktiv;
eine Mengenobergrenze allein garantiert keine fehlerfreie Ereigniserkennung.

Fehlende normale Mengen werden zuerst aus passenden echten Nachbarlesungen des
offenen Batches ergänzt. Erst danach ersetzt ein explizit konfiguriertes Minimum
noch ungelöste Mengen, einschließlich isolierter und letzter Zeilen. Intern
bleibt der Vergleichswert einer solchen Schätzung 1. Für alte Eingaben ohne
Grenzen bleiben die bisherigen Zählerregeln erhalten.

Im Rare-OCR-Pfad ist die fehlende Endzahl bereits bisher ein sofortiger
Einermengen-Ersatz. `UsesImplicitUnitQuantity` unterscheidet diesen Ersatz jetzt
von einer ausdrücklich gelesenen `x1`. Mit expliziten Grenzen wird der Ersatz
zunächst als fehlende Menge weitergereicht: echte Nachbarlesungen haben Vorrang,
erst danach greift das Minimum. Negative Rare-Korrekturen werden nicht durch ein positives Dropmaximum
verändert.

## Diagnose und Tests

Neue Aufnahmen verwenden `companion-0.7.4-drop-quantity-v6`. Jede Beobachtung
speichert ihre unveränderlichen `quantityBounds` und das Kennzeichen des
impliziten Rare-Ersatzes. Bei direkt gesetzten Einermengen kennzeichnen
`usesFixedUnitQuantity` und `companion-fixed-unit-quantity` die Herkunft.
Der OCR-Text bleibt unverändert; bei variablen Drops bleibt auch die gelesene
Menge erhalten. `companion-minimum-quantity-clamp` und `companion-maximum-quantity-clamp` machen
die angewendeten Grenzen nachvollziehbar.
Das Replay verwendet ausschließlich die aufgezeichneten Grenzen. Es liest dafür
keine später geänderte Spot-Tabelle. Aufnahmen v1–v5 bleiben mit dem aktuellen Zähler abspielbar; ab v4 darf
weiterhin die alte Trash-Mindestmengentabelle enthalten.

Tests prüfen beide Kanäle, wechselnde Fehlmengen, getrennte Drops, Maxima größer
als 1, die Anhebung gelesener Mengen unter dem Minimum, fehlende Mengen und signierte Korrekturen.
Integrationstests prüfen den Spotbezug und die unveränderte Wiedergabe gespeicherter
Rohmengen mit Grenzen. Alle bestätigten festen Item/Spot-Paare werden jeweils im normalen
und im Rare-Kanal getestet. Synthetische Testwerte sind keine Spieldaten.
