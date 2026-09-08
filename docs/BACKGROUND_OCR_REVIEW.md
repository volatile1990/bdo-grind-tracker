# Paddle-Zusatzprüfung und Mengenabgleich

Stand: 8. September 2026, Entwicklungsstand nach 1.0.2-test.4.

Windows OCR bleibt der erste Erkennungsweg. PP-OCRv6 Small ersetzt Tesseract als
lokale Zusatzprüfung. Die Anwendung führt das mitgelieferte Modell über ONNX
Runtime 1.29.0 auf der CPU aus. Python, GPU und Downloads beim Start sind nicht
erforderlich. Modell und Zeichensatz sind auf eine überprüfte Revision festgelegt;
Quellen stehen in `data/ocr/paddle-v6-small/SOURCES.md`.

## Auswahl und Annahme

Der bisherige Windows-Durchlauf und sein begrenztes Nachlesen bleiben bestehen.
Paddle erhält nur deutliche Grenzfälle: fehlende oder widersprüchliche Mengen,
schwache Namenszuordnung oder katalognahe, zunächst nicht akzeptierte Zeilen.
Eine vollständige, übereinstimmende primäre Menge oder ein zuverlässiges Template
bedarf keiner Zusatzprüfung. Ein sicher zugeordnetes Item mit Minimum/Maximum
1/1 benötigt keine Mengenlesung. Hintergrund ohne Itemhinweis wird nicht geprüft.

Zwei Arbeiter mit eigenen Modellinstanzen verarbeiten Zeilen parallel. Jeder
liest ausschließlich die ursprüngliche Zeile dieses Frames als Originalbild und
Graustufenbild. Die starke Schriftmaske des früheren Tesseract-Pfads entfällt.
Normale Ausschnitte richten sich nach der kalibrierten UI-Skalierung; die anders
aufgebaute Rare-Anzeige behält ihren vollständigen kalibrierten Textbereich.

Beide Lesungen müssen denselben globalen Katalogeintrag liefern, innerhalb des
erlaubten Spotpools, mit Modellscore mindestens 0,95 und Namensdistanz höchstens
0,05. Scores sind keine kalibrierten Wahrscheinlichkeiten. Eine sichere primäre
Namenszuordnung darf nicht durch ein anderes Item ersetzt werden.

Für Mengen werden vollständige Endungen `x6`, `x 6` oder `×6` ausgewertet.
Zwei unterschiedliche positive Zahlen bleiben ungelöst. Null ist ungültig und
kein Gegenbeweis: Die vollständigen Lesungen 6/0 können 6 liefern, sofern beide
Namen sicher übereinstimmen. Eine fehlende Endmenge kann eine einzelne Zahl nicht
bestätigen. Eine gute primäre Menge bleibt maßgeblich; nur bereits als
widersprüchlich/unvollständig ausgewählte positive Mengen dürfen korrigiert werden.

Die spotbezogenen Grenzen werden im Zähler angewendet: positive Zahlen unter dem
Minimum werden angehoben, Zahlen über dem Maximum begrenzt. Ungelöste Mengen
erhalten das Minimum. Die Beobachtung behält ihre rohe OCR-Menge für die Diagnose.

Die zwei Varianten sind keine statistisch unabhängigen Beweise. Modellfehler oder
ein überschrittenes Zeilenbudget von zwei Sekunden erhalten die primäre Beobachtung.
Abbruch wird auch während der nativen ONNX-Ausführung unterstützt.

## Reihenfolge und Zählung

Paddle erzeugt weder Ereignisse noch eigene Zählerzustände. Der Analyzer wartet
auf die Zeilenprüfungen eines Frames; genau eine abschließende Beobachtung pro
Quelle/Zeilenposition erreicht den Zähler. Ergebnisse werden im normalen laufenden
Verarbeitungspfad übernommen. Pause/Beenden leert die vorhandene Capture-Queue und
den verbleibenden Zählerblock. Der Companion-Block umfasst weiterhin zehn Frames.

Der normale Laufzeitpfad verwendet kalibrierte Slotindizes, neueste Zeile zuerst.
Innere OCR-Lücken zwischen erkannten Zeilen behalten ihren Platz als ungezählte
Platzhalter. Ein Präfix-/Suffixabgleich muss eine einheitliche Verschiebung der
Slots und mindestens einen passenden benannten Anker enthalten. Nachträgliche
Mengenübernahme erfolgt erst nach diesem Abgleich; gleiche Listenlänge und
gleicher Itemname an einem Index reichen dafür nicht mehr.

Zugeordnete Zeilen teilen eine Drop-ID. Ein bereits gebuchter Mindestwert 4 kann
später als Menge 6 derselben ID, Revision 1, mit Delta +2 berichtigt werden.
Die UI verwendet die absolute Dropmenge und Revision für idempotente Übernahme,
auch bei wiederholter oder vertauschter Zustellung. Eine Revision erhöht die
Anzahl der Drops nicht. Manuelle Summenkorrekturen bleiben als eigene Änderungen
erhalten. Ein anderer Itemname darf keine bereits vergebene Drop-ID übernehmen.
Nach jedem abgeschlossenen Block behält der normale Zähler nur den letzten
Ankerframe; die vollständige Bildfolge bleibt nicht im Arbeitsspeicher.

**Grenze:** Die bisherige zyklische Erneuerungsheuristik für nicht unterscheidbare
gleiche Zeilen bleibt erhalten. Ein vollständiger Ersatz durch reine
Namens-/Positionszuordnung hat in den vorhandenen Aufnahmen deutlich zu wenig
gezählt. Es wird daher keine sichere Unterscheidung aller identischen Drops oder
eine vollständige Beseitigung von Doppelzählungen behauptet. Mengenwechsel von
geschätzt zu gelesen allein lösen auch am Tag-Überlauf keine neue Buchung aus.
Der separate Rare-Zähler behält seine bisherige Abgleichlogik.

## Sprache, Diagnose und Prüfung

Die eingestellte beziehungsweise aus der BDO-Konfiguration bestimmte Spielsprache
bleibt für Windows OCR und den Itemkatalog maßgeblich. Deutsche Aliasnamen werden
auf dieselben kanonischen Itemschlüssel abgebildet. Das zusätzliche Modell ist
mehrsprachig; weitere Spielsprachen erfordern weiterhin passende Katalogaliase
und Unterstützung im primären Erkennungsweg.

Diagnose-Engine: `companion-0.7.4-row-tracks-v7`. `rowReviews` protokolliert Backend,
Sprache, Anlass, beide Lesungen, Laufzeit, Entscheidung und Fehler. Normale
Mengenrevisionen enthalten dieselbe `eventId`, eine steigende `revision`, die
absolute `totalDropQuantity` und das signierte `quantity`-Delta. Der Variantenmarker
`+row-tracks-v1` wählt den passenden Replay-Zählmodus. Historische Aufnahmen ohne
Marker bleiben im bisherigen Modus; Replay liest keine Screenshots erneut.

Der C#-ONNX-Wrapper liefert auf allen 305 Prüfausschnitten denselben Text wie der
vorherige Python-ONNX-Versuch. Der produktive Zusatzpfad löst 35 der 42 zuvor
visuell geprüften Mengenprobleme korrekt; sieben bleiben ungelöst. Keine falsche
Menge wurde in dieser gelabelten Problemgruppe übernommen. Gute Windows-Lesungen
der 60 Kontrollzeilen bleiben erhalten. Sechs leere Ausschnitte liefern keinen
angenommenen Itembefund. Dies ist ein begrenzter lokaler Datensatz, kein allgemeiner
Genauigkeitsnachweis.

Über 4.122 aufgezeichnete Frames wurden die gespeicherten primären Beobachtungen
und die tatsächlichen C#-Paddle-Reviews durch den aktuellen Zähler verarbeitet.
Windows OCR wurde dabei nicht über alle Screenshots neu ausgeführt:

| Aufnahme | Vorheriger Zähler mit Tesseract-Reviews und Minimum 4 | Paddle + Zeilenabgleich | Vom Nutzer bestätigter Trash |
| --- | ---: | ---: | ---: |
| 14:16:16 | 1.348 | 1.318 | 1.360 |
| 15:00:10 | 3.684 | 3.526 | 3.508 |
| 16:38:51 | 2.338 | 2.202 | unbekannt |

Damit verbessert sich die zweite bekannte Summe deutlich, die erste verschlechtert
sich um 30 gegenüber dem Vergleichsstand. Die Summe der dritten Aufnahme kann
ohne unabhängigen Istwert nicht bewertet werden. Reale deutsche/anders skalierte
Sessions und schwierige Rare-Zeilen sind in diesem Datensatz noch nicht abgedeckt.
Skalierungs- und deutsche Schriftproben existieren zusätzlich als automatisierte
Tests; sie ersetzen solche echten Aufnahmen nicht.

Lokale Prüfdaten und Wiederholung: `artifacts/paddle-implementation/Program.cs`,
`production-readings.jsonl`, `production-summary.json` und `test-results/`.
Der vorherige Modellvergleich bleibt unter `artifacts/paddle-ocr-evaluation/REPORT.md`.
