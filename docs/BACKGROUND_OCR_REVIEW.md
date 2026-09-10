# Paddle-Zusatzprüfung und Mengenabgleich

Stand: 10. September 2026.

Windows OCR bleibt der erste Erkennungsweg. PP-OCRv6 Small ersetzt Tesseract als
lokale Zusatzprüfung. Die Anwendung führt das mitgelieferte Modell über ONNX
Runtime 1.29.0 auf der CPU aus. Python, GPU und Downloads beim Start sind nicht
erforderlich. Modell und Zeichensatz sind auf eine überprüfte Revision festgelegt;
Quellen stehen in `data/ocr/paddle-v6-small/SOURCES.md`.

## Auswahl und Annahme

Die [gezielte Trash-Anomalieprüfung](TRASH_QUANTITY_ANOMALIES.md) hat bei
auffälligen, bereits von Windows akzeptierten Mengen Vorrang vor den folgenden
bisherigen Reviewgründen. Sie hebt die Schutzregel für eine vollständige primäre
Endmenge nur für diesen Prüffall auf. Zwei gleiche positive Zahlen und ein
plausibler Ersatz sind erforderlich; etwa `4/0` reicht hier nicht aus.
Gegenstand und Sichtbarkeit bleiben an die ursprüngliche Windows-Zeile gebunden.

Der bisherige Windows-Durchlauf und sein begrenztes Nachlesen bleiben bestehen.
Paddle erhält nur deutliche Grenzfälle: fehlende oder widersprüchliche Mengen,
schwache Namenszuordnung oder katalognahe, zunächst nicht akzeptierte Zeilen.
Eine vollständige, übereinstimmende primäre Menge oder ein zuverlässiges Template
bedarf keiner Zusatzprüfung. Ein sicher zugeordnetes Item mit Minimum/Maximum
1/1 benötigt keine Mengenlesung. Hintergrund ohne Itemhinweis wird im normalen
Zeilenreview nicht geprüft; die unten beschriebene Zuordnungsprüfung hat einen
eigenen, durch die vorherige Bildfolge begrenzten Anlass.

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

### Gezielte Zuordnungsprüfung (10. September 2026)

`+alignment-review-v1` ergänzt eine zweite, eng begrenzte Prüfphase nach den
bisherigen Zeilenreviews. Sie verändert keine primäre Item- oder Mengenlesung.
Die letzten beiden unmittelbar aufeinanderfolgenden Frames müssen jeweils
mindestens zwei lückenlos erkannte Zeilen ab dem untersten Slot enthalten.
Ihre positiven Mengen müssen innerhalb der bekannten Grenzen liegen. Wenn
mindestens zwei unterschiedliche positive Verschiebungen dieselben erkannten
Zeilen erklären, können die nächsten höchstens zwei älteren, vollständig
fehlenden Zeilen mit Paddle gelesen werden. Stillstehende, leere oder eindeutig
zugeordnete Logs starten diese Nachprüfung nicht.

Originalfarbe und Graustufen müssen denselben erlaubten Katalogeintrag und
dieselbe vollständige positive Menge innerhalb der Spotgrenzen liefern. Für
diese zusätzlichen Anker reichen 6/0 oder eine fehlende Endmenge nicht aus.
Die gelesenen Zeilen müssen eine einzige größere Verschiebung gegenüber der
bisherigen Zuordnung belegen, zusammen mit mindestens zwei passenden alten
Zeilen. Ein widersprüchlicher oder weiterhin mehrdeutiger Befund wird verworfen.

Angenommene Zeilen erhalten `isAlignmentAnchor: true`. Sie helfen dem vorhandenen
Zeilenabgleich, dürfen aber selbst weder einen Drop noch eine Mengenrevision
ausgeben – auch nicht bei einem zyklischen Frame-Tag-Überlauf. Über
`alignmentPreviousSlot` behalten sie ausdrücklich die Drop-ID der bestätigten
Vorgängerzeile; ein späterer primärer Scan erzeugt daraus keine neue Identität. Ihre Herkunft
bleibt in der Aufzeichnung erhalten; Replay berücksichtigt dieselbe Eigenschaft.
Der Zähler protokolliert sie mit `alignment-anchor` und Delta 0. Das zusätzliche
Review protokolliert `missing-alignment-anchor` sowie
`alignment-anchor-retained` oder `alignment-anchor-discarded`; erfolglose
Lesungen behalten ihren konkreten Fehler-/Konsensgrund.

Die Auswahl merkt sich nur die normalen Beobachtungen des vorherigen Frames,
keine Bilder und keine ergänzten Anker. Pause/Abschluss, Reset und Sprachwechsel
löschen diese Auswahlhistorie. Bei Zeitabständen über einer Sekunde wird sie
nicht verwendet. Ausschnitte stammen aus derselben kalibrierten Slotgeometrie
wie der primäre Lauf; Itemnamen werden über den mehrsprachigen Katalog verglichen.
Paddle arbeitet weiterhin im vorhandenen Arbeiterpool und der aktuelle Frame
erreicht den Zähler erst nach Abschluss seiner Prüfungen.

Die fünf lokalen Aufnahmen wurden mit den bisherigen finalen Beobachtungen,
den zuvor geprüften Paddle-Mengenkorrekturen und echten neuen ONNX-Lesungen für
die ausgewählten Anker wiedergegeben. Windows OCR wurde nicht erneut über alle
Bilder ausgeführt:

| Aufnahme | Bisheriger Stand | Mit Zuordnungsprüfung | Bekannter Istwert |
| --- | ---: | ---: | ---: |
| 09.09. 17:37:40 | 1524 | 1530 | 1534 |
| 09.09. 16:00:35 | 1749 | 1749 | 1735 |
| 08.09. 14:16:16 | 1318 | 1318 | 1360 |
| 08.09. 15:00:10 | 3526 | 3526 | 3508 |
| 08.09. 16:38:51 | 2202 | 2202 | unbekannt |

Bei 17:37:40 werden 32 Zeilen in 25 der 1022 Frames zusätzlich geprüft.
Zwei ältere Zeilen werden als Anker übernommen; Sequenz 816 stellt dadurch
sechs fehlende Helme wieder her. Vier Helme bleiben ungeklärt. Alle anderen
Item-Summen bleiben in diesen Wiedergaben gleich. Die bestehenden Grenzen bei
verdecktem Droplog werden durch diese Änderung nicht behandelt.

Prüfprogramm, Lesungen und Summen: `artifacts/alignment-review/`. Automatische
Tests decken unter anderem konkurrierende Verschiebungen, verworfene Befunde,
englische/deutsche Aliasnamen, mehrere UI-Skalierungen, Blockgrenzen,
Tag-Überläufe, verzögerte Ergebnisse und serialisierte Diagnose-Wiedergabe ab.

Der vollständige Testlauf am 10.09.2026 ergibt 2305 erfolgreiche und zwei
fehlgeschlagene Tests. Beide Fehler betreffen dieselbe bestehende synthetische
deutsche Schriftprobe (`Bruchstück` wird als `Bruchstüuk` gelesen). Ein isolierter
Export des unveränderten HEAD reproduziert beide Fehler ohne die neue Logik;
die OCR-Implementierung und diese Tests wurden nicht verändert.

### Bestehender Zählpfad

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

Eine eng begrenzte Ausnahme schützt seit Engine v8 einen eindeutig erkannten
Nachbareintrag, wenn der zyklische Tag einer anderen Zeile die gesamte Zuordnung
verwirft. Dafür müssen zwei höchstens eine Sekunde auseinanderliegende Frames
dieselben vollständig gelesenen Zeilen an denselben Positionen enthalten. Der
geschützte Itemname muss jeweils eindeutig sein und sein eigener Tag regulär
von 1 auf 2 oder von 2 auf 3 fortschreiten. Nur diese einzelne Track-ID wird
übernommen (`verified-stable-neighbor`); mehrdeutige gleiche Zeilen behalten die
bisherige Erneuerungsentscheidung. Verschiebungen, geänderte Mengen, fehlende
Lesungen, ein eigener Tag-Umbruch oder längere Aufnahmelücken lösen diese Ausnahme
nicht aus. Eine vollständig identische Ersetzung zwischen zwei Aufnahmen bleibt
anhand von Text und Position allein grundsätzlich nicht sicher unterscheidbar.

## Sprache, Diagnose und Prüfung

Die eingestellte beziehungsweise aus der BDO-Konfiguration bestimmte Spielsprache
bleibt für Windows OCR und den Itemkatalog maßgeblich. Deutsche Aliasnamen werden
auf dieselben kanonischen Itemschlüssel abgebildet. Das zusätzliche Modell ist
mehrsprachig; weitere Spielsprachen erfordern weiterhin passende Katalogaliase
und Unterstützung im primären Erkennungsweg.

Diagnose-Engine: `companion-0.7.4-row-tracks-v8`. `rowReviews` protokolliert Backend,
Sprache, Anlass, beide Lesungen, Laufzeit, Entscheidung und Fehler. Normale
Mengenrevisionen enthalten dieselbe `eventId`, eine steigende `revision`, die
absolute `totalDropQuantity` und das signierte `quantity`-Delta. Der Variantenmarker
`+row-tracks-v1` wählt den passenden Replay-Zählmodus. Historische Aufnahmen ohne
Marker bleiben im bisherigen Modus; Replay liest keine Screenshots erneut.
Aufnahmen der Engine v7 bleiben als Versionsvergleich lesbar. Die darin
gespeicherten Mengengrenzen werden beibehalten, auch wenn sich der aktuelle
Katalog geändert hat; ein Replay ändert keine gespeicherte Session.

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

## Paddle-Mengenprüfung v2

Der Diagnosemarker `+paddle-review-v2` kennzeichnet die Korrektur vom 9. September
2026: Ein Review wegen eines unsicheren Itemnamens bewertet die primäre Menge
unabhängig vom Namen. Eine unvollständige oder widersprüchliche Menge kann durch
den bestehenden Paddle-Konsens ersetzt werden. Zuvor konnte `uncertain-name` die
Menge 1 schützen, obwohl beide Paddle-Varianten eindeutig 10 gelesen hatten.
Das konnte anschließend den Zeilenabgleich unterbrechen und neue Buchungen auslösen.

Ein vollständiger passender primärer Mengenbefund beziehungsweise eine nach den
bestehenden Regeln verlässliche Mengen-Schablone behält Vorrang. Die Auslöser für
Zusatzprüfungen sowie die Anforderungen an Paddle-Konsens und Lesesicherheit
bleiben erhalten. Die Änderung verwendet keine sprach- oder auflösungsspezifischen
Sonderfälle. Die Behandlung verdeckter Lootzeilen wurde auf Nutzerwunsch nicht geändert.

Prüfung: 2.280 Tests bestanden, einschließlich deutscher Aliasnamen, Schutz guter
primärer Mengen und widersprüchlicher beziehungsweise unzuverlässiger Zweitlesungen.
487 zuvor geprüfte Zeilen aus vier Aufnahmen wurden mit dem produktiven C#-Paddle-Pfad
erneut aus ihren Bildausschnitten gelesen. Genau die sieben bekannten Fälle in der
Aufnahme `loot-20260909-160035-34f376870c6a4f06beb30a9543b02b93` ändern ihre Menge
von 1 auf 10 (Sequenzen 67, 244, 378, 415, 472, 665, 732).

Der anschließende Zähler-Replay liefert 1.749 statt 1.821 Helme bei einem bestätigten
Istwert von 1.735. Die verbleibenden 14 entsprechen den beiden erneut gebuchten
7er-Zeilen bei verdecktem unterstem Slot in Bild 346. Die drei älteren Aufnahmen
bleiben bei 1.318, 3.526 und 2.202 Helmen; auch ihre übrigen Itemmengen bleiben gleich.
Die ursprüngliche Windows-OCR wurde dabei nicht erneut über alle Bilder ausgeführt.
Prüfprogramm und Ergebnisse: `artifacts/paddle-consensus-fix/Probe.csproj`,
`readings.jsonl` und `summary.json`.

## Doppelzählungen in neuen Aufnahmen untersuchen

Vor dem Start unter Einstellungen **Diese Session aufzeichnen** aktivieren.
Die Zählregeln bleiben unverändert. Neue Aufnahmen enthalten zusätzlich
`reconciliationTraceVersion: 1` im Header; die Zähler-Engine bleibt bei v7.

`observations.jsonl` enthält unter `normalReconciliation` die Entscheidungen des
normalen Zeilenabgleichs für jedes verarbeitete Bild. Ein Zehnerblock wird beim
Abschluss des Blocks protokolliert, Restbilder beim Pausieren/Beenden. Jeder
Abgleich verweist trotzdem auf die ursprüngliche Aufnahmesequenz und deren
`normal.png`, sowie das vorherige Bild. `captureIndex` zählt nur analysierte Bilder;
`recordingSequence` berücksichtigt zusätzlich Abschluss-Einträge bei Pausen.
Beide Nummern können sich daher unterscheiden.

Pro Bild stehen geometrisch mögliche und tatsächlich bestätigte Überlappung
sowie die geprüften Überlappungslängen mit Annahme-/Ablehnungsgrund im Log.
Pro Zeile werden Slot, native Y-Position, Item, Eingangsmenge, reparierte Menge,
Min-/Max-Grenzen, Schätzstatus, zyklischer Frame-Tag, Drop-ID, Revision und gebuchtes
Mengendelta festgehalten. Synthetische Lücken haben keine erfundene Pixelposition.
`candidatePreviousTrackId` bezeichnet den geometrischen Vorgänger;
`matchedPreviousTrackId` die tatsächlich übernommene Drop-ID. Die interne
reparierte Schätzmenge kann 1 sein, während `quantityDelta` den tatsächlich
gebuchten Mindestwert enthält, beispielsweise 4.

Die Ergebnisse unterscheiden neue Buchungen (`counted-new`), bekannte Drops
(`matched-existing`), bestätigte Mindestmengen (`estimate-confirmed`),
Mengenrevisionen (`quantity-revised`) und ungelöste Zeilen. Bei Neuzählungen ist
ersichtlich, ob der vorherige Droplog leer war, die Zeile neu hinzukam, keine
Überlappung passte oder beispielsweise der zyklische Frame-Tag einen geometrisch
passenden Vorgänger ausschloss. Eine Mengenrevision behält die Drop-ID und ist
keine neue Dropbuchung.

Beim erfolgreichen Speichern der aktuellen Session entsteht im selben
Diagnoseordner zusätzlich **`count-summary.json`**. Die Datei wird bei weiteren
Speichervorgängen aktualisiert und enthält:

- Session-ID, Speicherzeit, aktive Dauer, abgeschlossene und noch ausstehende Bilder.
- Anzahl neuer normaler Drops und Mengenrevisionen sowie alle Ergebnisarten.
- Verdachtsstellen nach Item und Grund: Neuzählung trotz geometrischer
  Überlappung wegen Frame-Tags oder Itemkonflikt nach einer ungelesenen Zeile.
- Die ersten 100 Verdachtsstellen mit Drop-IDs, Mengen, Zeitpunkten und Bildnamen.
  Die Gesamtzahlen umfassen auch weitere Stellen; das vollständige Log bleibt erhalten.
- Aufgezeichnete Mengensummen einschließlich Normal-/Rare-Korrekturen und
  gespeicherte Sessionsummen. Differenzen können durch manuelle Änderungen oder
  frühere Aufzeichnungsabschnitte derselben Session entstehen.

Verdachtsstellen sind **keine nachgewiesenen Doppelzählungen**. Auch echte,
identische neue Drops können die zyklische Erneuerung auslösen. Andere Ursachen
wie abweichende OCR-Mengen lassen sich über die vollständigen Zeilenabgleiche
prüfen; die Übersicht ist keine vollständige automatische Fehlererkennung.
Der Rare-Zähler erhält keine zusätzliche interne Zeilenanalyse; dessen bisherige
Beobachtungen und Ereignisse bleiben im Log erhalten.

Für eine Auswertung den gesamten `loot-…`-Ordner einschließlich Screenshots,
`observations.jsonl` und `count-summary.json` sowie die tatsächliche Lootmenge
bereitstellen. Es werden keine zusätzlichen Screenshots aufgenommen. Bei
deaktivierter Aufzeichnung entstehen diese Diagnosedateien nicht. Schreibfehler
der Diagnose verhindern das Speichern der Session nicht.

Prüfung dieser Diagnose-Erweiterung: 2.271 automatisierte Tests bestanden;
der erneute Zähler-Replay der drei oben genannten Aufnahmen liefert weiterhin
1.318, 3.526 und 2.202 Helme. Dies prüft unveränderte Zählergebnisse auf den
gespeicherten Beobachtungen, keine erneute OCR der gesamten Bildfolge.
