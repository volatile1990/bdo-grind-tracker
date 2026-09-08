# Dropmengen pro Item und Spot

Die vollständige [Recherche und bestätigte Mengentabelle](DROP_QUANTITIES_RESEARCH.md)
umfasst 56 Items und 207 erlaubte Item/Spot-Paare einschließlich optionalem Eventloot.
Der Nutzer hat sämtliche Min-/Max-Werte in `Dropmengen-Eingabe.xlsx` am
8. September 2026 angegeben. Alle 207 Paare sind in
[`data/drop-quantities.json`](../data/drop-quantities.json) übernommen, mit
Quellzellen und SHA-256 der Excel-Datei. Es gibt keine offenen Werte.
Diese Angaben sind Nutzervorgaben, keine nachträglich behaupteten Datenbankbelege.

Die App bindet den Datensatz als Ressource ein und prüft beim Laden, dass jedes
erlaubte Item/Spot-Paar genau einmal vorhanden und jeder Bereich gültig ist.
Excel wird zur Laufzeit nicht benötigt. Spotabhängige Unterschiede bleiben
erhalten, zum Beispiel Black Stone (Aphrodon 1–15, Hermesia 1–20, Magaia 1–50)
und Caphras Stone (Scales of Judgment 1–1, Hermesia 1–20, Event Horizon 1–50).

## Bedeutung

- **Minimum:** letzter Mengenersatz bei einem akzeptierten Itemnamen und einer
  nicht gelesenen Anzahl. Es ist keine Untergrenze für erfolgreich gelesene Zahlen.
- **Maximum:** Obergrenze der Menge eines einzelnen Drops. Sie gilt im normalen
  Lootpanel, im Rare-Kanal und nach einer Mengenrettung aus dem Itemchat.
  Mehrere getrennte Drops dürfen die Obergrenze in Summe überschreiten.
- Die Grenzen müssen zum Spot und zur tatsächlich angezeigten Menge passen;
  Trashboni und besondere Gegner sind bei der Bestätigung einzubeziehen.
- Manuelle Gesamtkorrekturen sind von diesen Einzel-Dropgrenzen unabhängig.

## Verarbeitung

**175 Paare haben Minimum = Maximum = 1.** Nach Identifikation eines solchen
Items setzt die App direkt Menge 1. Eine Zahl im OCR-Text wird dafür nicht
geparst; getrennte Mengen-Nachlesungen und die Übernahme einer Chatmenge sind
unnötig. Auch ein erst bei der Namenrettung erkanntes festes Item benötigt keine
anschließende Mengen-OCR. Der gesamte Itemchat wird weiterhin beobachtet, um
seine zeitliche Zuordnung für andere Items zuverlässig zu halten.

Die Ziffernsuche bei der ursprünglichen Vorbereitung normaler Panelzeilen bleibt
erhalten: Sie bestimmt zusätzlich den Namensausschnitt und die Zeilenauswahl,
bevor der Itemname bekannt ist. Diesen geometrischen Teil global abzuschalten
würde auch die Namenserkennung variabler Drops verändern. Der Rare-Bildpfad
verwendet bereits keine Ziffern-Templates.

Der Analyzer ordnet die Grenzen nach der Spotbestimmung den akzeptierten Zeilen
zu. Vor der ersten Trashzeile werden nur Grenzen genutzt, die alle passenden
unterstützten Spots abdecken. Fehlt bei einem davon ein Maximum, wird vor dem
Spotlock keine Obergrenze aus einem anderen Spot übernommen.

Die Zähler begrenzen positive Eingabemengen **vor dem Identitätsvergleich**.
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

Neue Aufnahmen verwenden `companion-0.7.4-drop-quantity-v5`. Jede Beobachtung
speichert ihre unveränderlichen `quantityBounds` und das Kennzeichen des
impliziten Rare-Ersatzes. Bei direkt gesetzten Einermengen kennzeichnen
`usesFixedUnitQuantity` und `companion-fixed-unit-quantity` die Herkunft.
Der OCR-Text bleibt unverändert; bei variablen Drops bleibt auch die gelesene
Menge erhalten. `companion-maximum-quantity-clamp` macht eine Begrenzung nachvollziehbar.
Das Replay verwendet ausschließlich die aufgezeichneten Grenzen. Es liest dafür
keine später geänderte Spot-Tabelle. Aufnahmen v1–v4 bleiben abspielbar; v4 darf
weiterhin die alte Trash-Mindestmengentabelle enthalten.

Tests prüfen beide Kanäle, wechselnde Fehlmengen, getrennte Drops, Maxima größer
als 1, echte Mengen unter dem Minimum, fehlende Mengen und signierte Korrekturen.
Integrationstests prüfen den Spotbezug und die unveränderte Wiedergabe gespeicherter
Rohmengen mit Grenzen. Alle 175 festen Item/Spot-Paare werden jeweils im normalen
und im Rare-Kanal getestet. Synthetische Testwerte sind keine Spieldaten.
