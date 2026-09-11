# Garmoth 0.9.13 und Grindcrest: OCR und Tracking

Stand: 10. September 2026. Untersucht wurde die installierte Garmoth-App unter
`C:\Users\marku\AppData\Local\Programs\garmoth-app` und Grindcrests aktueller
Working Tree auf Basis von Commit `ffb1b4c`. Der Working Tree enthält bereits
uncommittete OCR- und Mengenprüfungsänderungen. Aussagen darüber sind nicht
automatisch Aussagen über ein installiertes Grindcrest-Release.

Dieser Bericht bleibt die Bestandsaufnahme **vor** der anschließenden Umstellung
des Grindcrest-Normalzählers. Den neuen Entwicklungsstand dokumentiert
[Zeitliche Loot-Zählung](TEMPORAL_LOOT_TRACKING.md); die nachstehenden Vergleiche
werden dadurch nicht nachträglich zu Messungen des neuen Algorithmus.

**Ergebnis:** Garmoth verwendet lokale PaddleOCR-Modelle in einem eigenen
Rust-Prozess. Der wesentliche Architekturunterschied liegt außerdem in einem
zeitlichen Zeilenmodell namens `ledger-v2`: Es bewertet mehrere mögliche
Dropfolgen und kombiniert wiederholte Lesungen. Grindcrest verwendet primär
Windows OCR mit Zifferntemplates und einen aus BDO Companion 0.7.4 abgeleiteten
Listenabgleich mit zyklischen Frame-Tags. Paddle ist dort eine gezielte Nachprüfung.

Die Installation wurde ausgelesen und ihr Electron-ASAR in ein temporäres
Analyseverzeichnis entpackt. Das ist keine vollständige Rekonstruktion des
Rust-Quellcodes. JavaScript-Regeln sind direkt nachvollziehbar; Aussagen über
native Interna sind nach Belegstärke begrenzt. Ein Vergleich der tatsächlichen
Fehlerraten mit identischen Spielaufnahmen wurde nicht durchgeführt.

**Welches OCR Garmoth verwendet**

Der Helper heißt `resources\resources\garmoth-ocr.exe`. Das mitgelieferte
[Modellmanifest](/C:/Users/marku/AppData/Local/Programs/garmoth-app/resources/resources/paddle-models.json)
legt folgende Modelle fest:

| Aufgabe/Sprache | Modell | Wörterbuch |
|---|---|---|
| Gemeinsame Texterkennung: Lokalisierung der Textbereiche | `ppocrv4_mobile_det.onnx` | – |
| Englisch, `us`/`gl` | `en_ppocrv4_mobile_rec.onnx` | `en_dict.txt` |
| Deutsch, Französisch, Spanisch, Portugiesisch, Türkisch, Indonesisch | `latin_pp-ocrv5_mobile_rec.onnx` | `ppocrv5_latin_dict.txt` |
| Russisch | `cyrillic_pp-ocrv5_mobile_rec.onnx` | `ppocrv5_cyrillic_dict.txt` |
| Koreanisch | `korean_pp-ocrv5_mobile_rec.onnx` | `ppocrv5_korean_dict.txt` |
| Chinesisch | `pp-ocrv5_mobile_rec.onnx` | `ppocrv5_dict.txt` |
| Japanisch / traditionelles Chinesisch | jeweiliges PP-OCRv3-Modell | jeweiliges Wörterbuch |
| Thai | `th_pp-ocrv5_mobile_rec.onnx` | `ppocrv5_th_dict.txt` |

Die Binärdatei enthält Pfade zu Rust `oar-ocr 0.8.0`, `oar-ocr-core 0.8.0`
und `ort 2.0.0-rc.12`, ferner ONNX-Runtime-Buildinformationen für 1.24.2.
`DirectML.dll` ist vorhanden und DirectML-Code im Build enthalten. Das belegt
Unterstützung für diesen Ausführungspfad, aber allein noch keine tatsächliche
GPU-Ausführung. Die [OAR-Dokumentation](https://github.com/GreatV/oar-ocr)
beschreibt die zugrunde liegende Rust-/ONNX-Architektur; ihre aktuellen Beispiele
sind kein Beleg für Garmoths konkrete native Parameter.

Ein kontrollierter Lauf des Helpers mit einem selbst erzeugten PNG bestätigte
`ready engine=paddle`. Sowohl mit `language: de` als auch `language: us` wurden
die zwei künstlichen Zeilen vollständig erkannt; der Helper meldete 55 bzw.
52 ms Bearbeitungszeit. Das ist ein Funktionscheck mit klarer künstlicher Schrift,
kein Performance- oder Genauigkeitsbenchmark für BDO. Der Helfer wurde anschließend
regulär beendet. Ein aktiver GPU-Provider wurde dabei nicht ausgewiesen.

Die OCR-Sprache folgt im Renderer der Profilsprache. Ein deutscher Garmoth-Nutzer
verwendet deshalb den Latin-v5-Recognizer, sofern das Profil entsprechend
eingestellt ist. Das Manifest allein verrät nicht die aktuell eingestellte
Sprache dieses Nutzers.

**Vom Spielbild zum OCR-Ergebnis**

1. Die App ermittelt die zuletzt geänderte `gamevariable.xml` unter
   `Documents\Black Desert\UserCache\…`. Sie liest Auflösung, UI-Skalierung
   und die gespeicherten Positionen von `UIData Index 159` (normaler Lootfeed)
   und `161` (seltene Dropmeldung).
2. Daraus berechnet sie relative Bildausschnitte. Der normale Ausschnitt hat
   nominal **500 × 250 Pixel bei UI-Skala 1**; der Headerbereich wird ausgelassen.
   Der seltene Ausschnitt reicht relativ zum Anker horizontal von −125 bis +260
   und vertikal von −30 bis +30, jeweils mit UI-Skalierung und Bildschirmbegrenzung.
3. Der native Helper enthält Windows-Graphics-Capture-Aufrufe für das Spiel-HWND:
   `GraphicsCaptureItem`, `CreateForWindow`, `Direct3D11CaptureFramePool` und
   D3D11-Staging/Map. Garmoth erfasst damit das Spielfenster. Grindcrest erfasst
   derzeit den gewählten Monitor über DXGI Desktop Duplication.
4. Der Hauptfeed wird nominal alle **200 ms** aufgenommen, die Rare-Region alle
   **300 ms**. Wenn beide fällig sind, kann derselbe Capture-Aufruf beide
   Bildbereiche liefern. Das ist ein Solltakt, keine garantierte Messrate.
5. Rohdaten in BGRA gehen über stdin/stdout an OCR-Worker. Nachrichten bestehen
   aus längenpräfixiertem JSON und Binärdaten. Capture-Zeitstempel werden
   weitergereicht; die Verarbeitung verwendet nicht einfach die spätere
   Fertigstellungszeit der OCR als Dropzeit.
6. Der aktive normale OCR-Aufruf hat **einen Pass mit `scale: 2`**. Es werden hier
   keine expliziten `threshold`-/`invert`-Werte übergeben. Das bedeutet nicht,
   dass innerhalb der nativen OCR keinerlei Vorverarbeitung stattfindet.
7. Ein Worker ist der Normalfall. Wartet der älteste Lootauftrag länger als
   **2 Sekunden**, kann ein zweiter Worker starten. Nach **60 Sekunden** ohne
   entsprechende Arbeit wird er wieder abgebaut. Ergebnisse werden anhand ihrer
   Sequenznummer stets in Aufnahme-Reihenfolge ausgeliefert.
8. Bei mehr als **600** ausstehenden/einzureichenden Frames werden Captures
   übersprungen und der Rückstau angezeigt. Vor Stop/Rotation versucht die App
   die Pipeline zu leeren, mit einem Zeitlimit von **30 Sekunden**.

Belege: formatiertes `main.js`, Zeilen 230–388 (Pool), 391–618 (Capture),
870–979 (Protokoll), 1159–1409 (Geometrie), 3049–3051 (OCR-Pass),
3127–3131 (Konfigurations-Watcher). Die Formatierung ändert nur die Lesbarkeit.

**Setup-Regeln für gute Erkennung**

Der normale Itemlog muss sichtbar und seine Position gespeichert sein. Für den
zweiten Erkennungsweg muss zusätzlich der Rare-Itemlog aktiviert sein. Garmoths
Setup fordert nach Aktivierung oder Verschiebung Charakterauswahl beziehungsweise
Neustart und erneutes Einloggen, damit die gespeicherte Konfiguration zum Bild
passt. Der Setup-Dialog zeigt Ausschnitte und überdeckende UI-Elemente an; die
Überdeckungsberechnung meldet ab 1 % Flächenanteil, mit 2 Pixel Randtoleranz.
Diese Prüfung ist ein Hinweis auf mögliche Überdeckung und keine vollständige
visuelle Verdeckungserkennung.

Laut [offizieller App-Seite](https://garmoth.com/app) können kleinere UI-Skalen und
dünne Schrift die Erkennung verschlechtern; größere, deutlichere Schrift hilft.
Eine harte allgemeine Pflicht zu exakt 100 % UI-Skalierung ist aus den
untersuchten Regeln nicht abzuleiten. Beleg für Setuptexte: `CgnLv73t.js`,
Zeilen 30–99 und 277–283.

**Text-, Item- und Mengenregeln**

- Normaler Loot wird als Name plus Menge gelesen, unter anderem `Item x 12`,
  `Item 12` und `12 Item`. Mehrere zusammengezogene Einträge können getrennt
  werden. Unicode und Varianten des Multiplikationszeichens werden behandelt.
- Die Parsergrenze beträgt standardmäßig **OCR-Confidence 50** auf der vom
  Helper gelieferten Skala. Das ist keine gemessene 50-%-Trefferwahrscheinlichkeit.
- Mengen müssen positiv und kleiner als **100.000.000** sein. Punkt und Komma
  werden als Tausendertrenner entfernt. Häufige OCR-Verwechslungen wie `I/l/| → 1`,
  `O → 0`, `S → 5`, `B → 8` werden behandelt. Endständige Buchstabenverwechslungen
  nach echten Ziffern werden teilweise entfernt; beispielsweise kann `12S` zu
  `12` werden. Diese Reparatur kann auch echte Ziffernverwechslungen falsch lösen.
- Items werden gegen einen geladenen Katalog abgeglichen. Die aktive allgemeine
  Namensmetrik ist **Jaro-Winkler**, die Schwelle **0,86**; bestimmte beschnittene Namensvarianten benötigen
  mindestens **0,90**. Sehr kurze Namen mit höchstens vier normalisierten Zeichen
  benötigen einen exakten Treffer oder zuvor etablierten Kontext.
- Einträge mit Namensscore mindestens **0,96** bauen einen Kontext bereits
  bekannter Items auf. Dieser hilft bei mehrdeutigen Namen; er ist kein lernendes
  Modell für typische Trashmengen.
- Ein zusätzlicher Fallback akzeptiert eindeutig passende abgeschnittene Namen
  ab zehn Zeichen und mindestens 70 % Abdeckung des vollständigen Namens,
  sofern das reguläre Matching zuvor scheiterte und die Scoregrenze erreicht wird.
- `[Event] Desert Light` wird explizit aus der normalen Buchung ausgeschlossen.
- In diesem Garmoth-Parser wurde keine Grindcrest entsprechende itembezogene
  Min-/Max-Tabelle für einzelne Dropmengen gefunden. Das ist eine Aussage über
  den untersuchten Codepfad, nicht über unbekannte serverseitige Validierung.

Belege: `CapIpwiK.js`, Zeilen 10168–10358 (Parser/Matching),
11487–11518 (etablierte Items); weitere Details im Quellenverzeichnis unten.

**Wie Garmoth neue Drops von wiederholten Bildern unterscheidet**

Das aktive Verfahren heißt `ledger-v2`. Es modelliert **fünf feste Feed-Slots**.
OCR-Textstücke werden anhand ihrer Y-Koordinate zu Zeilen zusammengeführt und
auf eine Referenzhöhe von 250 Pixeln normiert. Die nominellen Slotpositionen
liegen im Abstand von 50 Pixeln, mit der unteren Referenzposition bei Y=213.
Es sind keine fünf separaten allgemeinen OCR-Aufrufe erforderlich.

Für die sichtbare Lebensdauer einer Zeile laufen vier Hypothesen parallel:
**1.250, 1.350, 1.450 und 1.550 ms**. Jede behält bis zu **24 mögliche Verläufe**.
Bei jedem Bild wird geprüft, wie viele neue Zeilen seit dem letzten Bild
entstanden sein könnten, welche bestehenden Zeilen dadurch nachrücken und welche
alt genug sind, um zu verschwinden. Das angenommene Entstehungsraster liegt bei
100 ms. Es ist eine Modellannahme, keine aus BDO ausgelesene Ereignisgarantie.

Die Bewertung unterscheidet unter anderem:

- gleiches Item und gleiche Menge;
- gleiches Item mit widersprüchlicher oder unbekannter Menge;
- anderes Item;
- ähnlicher, noch nicht sicher identifizierter Text;
- erwartete Zeile ohne sichtbare OCR-Lesung;
- OCR-Lesung in einem hypothetisch freien Slot.

Zeilenalter fließt in acht Klassen à 200 ms ein. Die Wahrscheinlichkeiten werden
aus den beobachteten Ergebnissen nachgelernt, zunächst nach 25, dann 50, 100,
200, 400 Frames und anschließend in Abständen von 250 Frames. Die Lebensdauer-
Hypothese mit der höchsten aufgelaufenen Evidenz liefert den aktiven Stand.

Eine Zeile sammelt ihre OCR-Lesungen. Für die Buchung gewinnt die häufigste
Kombination aus Item und Menge, bei Stimmengleichheit der bessere Namensscore.
Ein einzelnes `x500` zwischen mehreren `x5`-Lesungen muss somit nicht die
gebuchte Menge bestimmen. Bereits sichtbare Zwischenstände können sich ändern;
die App ersetzt Feed-Gesamtstände aus dem aktuellen Ledger. Das ist mehr als
einmaliges Addieren jedes erkannten Textes. Es handelt sich um die häufigste
Lesung, nicht zwingend um eine absolute Mehrheit: Auch eine einzige gültige
Lesung kann bereits zählen; es gibt hier keine allgemeine Zwei-Treffer-Pflicht.

Auch dieses Verfahren bleibt heuristisch: nicht beobachtete Drops können nicht
sicher rekonstruiert werden, wiederkehrende identische Bilder sind mehrdeutig,
und falsche Lebensdauer-/Geometrieannahmen können Fehler verursachen.

Belege: `CapIpwiK.js`, Zeilen 11127–11518 und 11665–11766.

**Spotfilter, seltene Drops und Sitzungen**

Der Spotfilter wartet auf **drei abgeschlossene relevante Trash-Pickups** und
einen Vorsprung des führenden Trash-Typs von mindestens **zwei** gegenüber dem
zweitbesten. Gezählt werden entschiedene Zeilen aus dem Ledger, nicht drei Bilder
derselben Meldung. Danach wird der Kandidatenkatalog auf passende Combat-Spots,
deren Drops und globale Items eingeschränkt. Teilen mehrere Spots denselben
Trash, können mehrere Spots im Filter bleiben. Der Filter wechselt nicht allein
aufgrund späterer widersprüchlicher Meldungen automatisch den Spot.

Grindcrest fixiert seinen Spot dagegen schon mit der **ersten erkannten
Trashart** über die [AutomaticLootSpotLock](/D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.Core/AutomaticLootSpotLock.cs:10).
Mehr Evidenz vor dem Festlegen könnte einen falschen anfänglichen Match besser
abfangen; das sollte mit realen Replays geprüft werden.

Die seltene Dropmeldung verwendet einen separaten `rare-log-v1`-Wächter:

- Zwei bestätigende Erkennungen derselben neuen Item-ID führen zu **+1**.
  Einzelne leere Bilder dazwischen unterbrechen den Kandidaten nicht sofort.
- Dasselbe bereits gezählte Item wird während fortbestehender Anzeige nicht
  erneut gebucht. Erst mindestens **2 Sekunden beobachtete Abwesenheit** setzen
  diesen Zustand zurück. Ein anderes Item kann nach zwei Treffern zählen.
- Bei Zeitstempellücken größer als `intervalMs + 200 ms`, Capturefehlern oder
  Geometrieänderung wird die laufende Kandidatenbestätigung zurückgesetzt;
  der bereits gezählte Itemschlüssel bleibt bei diesem Gap-Reset erhalten.
- Das beste Namensmatching gewinnt. Anders als im normalen Feed gibt es hier
  keinen Filter auf die ursprüngliche OCR-Confidence; die erzeugte Dropmeldung
  erhält intern den Wert 100. Das ist kein unabhängiger Qualitätsnachweis.

**Feed und Rare werden nicht addiert.** Die Session startet aus den Rare-Summen.
Sobald für ein Item eine positive Feed-Summe vorhanden ist, überschreibt diese
den Rare-Wert. Das ist auch kein Maximum der beiden Werte:

| Feed-Summe | Rare-Summe | Session-Summe |
|---:|---:|---:|
| 0 / fehlt | 3 | 3 |
| 1 | 3 | 1 |
| 5 | 3 | 5 |

Das vermeidet Doppelzählung zwischen beiden Anzeigen. Bei lückenhafter
Feed-Erfassung kann es aber dazu führen, dass zusätzliche Rare-Erkennungen
die positive Feed-Summe nicht ergänzen. Das ist eine aus dem Code abgeleitete
Grenze, kein nachgewiesener Fehler in der Spielsitzung des Nutzers.

Die optionale Sessionautomatik ist standardmäßig **aus**. Bei Aktivierung startet
sie nach zwei geschätzten Pickups innerhalb von 60 Sekunden oder zwei
unterschiedlichen gesehenen Item-/Mengen-Kombinationen in diesem Zeitfenster.
Nach standardmäßig **vier Minuten** ohne weitere Pickups wird die Session
beendet; ein OCR-Rückstau verhindert den vorschnellen Abschluss. Vor dem Ende
wird die Pipeline geleert. Die Endzeit richtet sich nach dem letzten Pickup,
nicht dem Ende der Wartefrist. Es ist Auto-Start/Auto-Stop; Grindcrest bietet
derzeit eine automatische Pause mit standardmäßig drei Minuten Inaktivität.

Belege: `CapIpwiK.js`, Zeilen 10398–10484 (Spotfilter), 10620–10625
(Feed-Priorität), 10856–10975 (Automatik), 10994–11105 (Rare-Wächter).

**Durchgeführte Funktionsprüfungen**

Neben dem OCR-Helper wurden aus der Analysekopie extrahierte reine Parser- und
Ledger-Funktionen in einer isolierten Node-VM mit künstlichen Eingaben geprüft.
Diese Probe verwendet keinen Spielbildschirm und keinen laufenden Spielprozess.

| Künstlicher Fall | Ergebnis |
|---|---|
| Eine 1.300 ms sichtbare `x5`-Zeile, 200-ms-Sampling | 5 |
| Derselbe Fall, einmal `x500` statt `x5` gelesen | 5 |
| Drei gleiche `x5`-Drops mit Nachrücken, 100-ms-Variante | 15 |
| Drei gleiche `x5`-Drops mit Nachrücken, 200-ms-Variante | **20 statt 15** |

In der letzten Variante wurden Birth-Zeitpunkte 10.000/10.600/11.200 ms und eine
harte Sichtbarkeitsdauer von jeweils 1.300 ms simuliert. Das Modell schätzte vier
statt drei Zeilen. Die 100-ms-Variante verwendete 10.000/10.500/11.000 ms; diese
beiden Fälle isolieren deshalb nicht allein den Einfluss des Aufnahmetakts.
Die Probe zeigt eine Grenze der Heuristik unter künstlichen Bedingungen und
liefert weder eine reale Fehlerquote noch einen Beweis, dass die künstliche
Darstellung exakt BDO entspricht.

Artefakte: [Ledger-/Parser-Probe](/C:/Users/marku/AppData/Local/Temp/codex-garmoth-analysis-20260910/tracking-probes.cjs),
[OCR-Runtime-Ergebnis](/C:/Users/marku/AppData/Local/Temp/codex-garmoth-analysis-20260910/native/synthetic-result.json).

**Vergleich mit Grindcrests aktuellem Code**

| Bereich | Garmoth 0.9.13 | Grindcrest Working Tree |
|---|---|---|
| Primäre OCR | PaddleOCR mit Textdetektor und sprachabhängigem Recognizer | Windows OCR; OpenCV-Templates für Mengen |
| Paddle-Nutzung | regulärer OCR-Pfad | PP-OCRv6 Small, gezielte zweite Prüfung ohne Textdetektor |
| Aufnahme | Spielfenster über Windows Graphics Capture | ausgewählter Monitor über DXGI |
| Nominaltakt | Feed 200 ms, Rare 300 ms | 450 ms |
| Rückstau | Capture und OCR getrennt, bis zu 2 Worker, geordnete Ausgabe, Grenze 600 | ein Analyse-Consumer, 4 wartende Frames, Aufnahme wartet bei voller Queue |
| Normaler Zähler | 5 Slots, mehrere zeitliche Hypothesen, Stimmen über Lesungen | 10-Frame-Batches, Listenüberlappung, zyklische Tags, persistente Zeilen-IDs |
| Mengenfehler | Mehrheitsentscheidung wiederholter Lesungen | OCR-/Template-Priorität, Kataloggrenzen, Recovery und Paddle-Konsens |
| ROI-Konfiguration | gespeicherte BDO-UI-Konfiguration | ebenfalls gespeicherte BDO-UI-Konfiguration; zusätzliche Font-/Profilprüfung |

Grindcrests [Factory](/D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/FrameAnalyzerFactory.cs:38)
aktiviert Windows OCR, Recovery, die Hintergrundprüfung und Tracking mit
Zeilen-IDs. Die [Aufnahmeschleife](/D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Capture/PassiveCaptureSession.cs:7)
definiert den Takt und die kleine Queue.

Die [Zählerheuristik](/D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.Core/CompanionFrameReconciler.cs:493)
verwendet weiterhin Tags `1 → 2 → 3 → 1`. Ein vorhandener
[Test](/D:/Projects/bdo-grind-tracker/tests/BdoGrindTracker.Core.Tests/TrackedCompanionReconciliationTests.cs:93)
zeigt ausdrücklich: Vier identische einzelne Zeilen können zwei Events ergeben.
Das soll neue identische Folgedrops erfassen. Es beweist aber nicht, dass zwischen
diesen Bildern tatsächlich ein neuer Drop entstanden ist. Persistente IDs lösen
die Buchung und Revision, nicht diese grundsätzliche Beobachtungsmehrdeutigkeit.

Die gezielte [Paddle-Prüfung](/D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/BackgroundLootRowReview.cs:174)
verlangt zwei passende Bildansichten desselben Items, hohe OCR-Confidence und
enge Namensübereinstimmung. Trotz ihrer Bezeichnung werden die gestarteten
Prüfungen im [Frame-Analyzer](/D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/CompanionLootFrameAnalyzer.cs:273)
für diesen Frame abgewartet. Bei langsamen Prüfungen kann deshalb die effektive
Capture-Rate sinken. Die Zeitbudgets begrenzen den Beginn weiterer Aufrufe und
sind nicht durchgehend harte Timeouts laufender OCR.

Grindcrests [Mengenbegrenzung](/D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.Core/CompanionFrameReconciler.cs:469)
klemmt Mengen auf Kataloggrenzen. Zusätzlich erkennt der neue
[Anomaliedetektor](/D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/TrashQuantityAnomalyDetector.cs:27)
auffällige Trashmengen. Das kann Einzel-Ausreißer abfangen; ein unzutreffender
Katalog kann umgekehrt echte Mengen verändern. Kataloggrenzen und temporal
bestätigte Messwerte sollten bei weiteren Änderungen getrennt bewertet werden.

**Welche Änderungen für Grindcrest daraus folgen**

1. Den nächsten Vergleich anhand derselben aufgenommenen Frames und unabhängiger
   Sollmengen durchführen. Namensfehler, Mengenfehler, fehlende Drops und doppelte
   Drops getrennt auswerten. Eine modernere Modellversionsnummer beweist keine
   bessere Zählung.
2. Ein zeitliches Zeilenmodell als austauschbaren Vergleichszähler entwickeln.
   Slotbewegungen, mehrere Lesungen und Mengenmehrheiten sind besonders relevant.
   Die bisherige Tag-Erneuerung anhand von Replays dagegen testen.
3. Die bereits getrennte Capture-/Analysepipeline so weiterentwickeln, dass
   optionale Nachprüfungen den Analysedurchsatz seltener unter den Aufnahmetakt
   drücken. Aufnahmezeitstempel und geordnete Buchungen beibehalten; Queue-Alter,
   OCR-Laufzeit und effektive Aufnahmerate messen. Einen 200-ms-Takt anschließend
   bewerten; die Garmoth-Grenze von 600 nicht blind übernehmen.
4. Paddle als möglichen Hauptpfad gegen Windows OCR testen, insbesondere auf
   deutschen Originalbildern. Detektion ganzer Feedbereiche und das vorhandene
   erkennerbasierte Lesen ausgeschnittener Zeilen sind unterschiedliche Aufgaben.
5. Für Gegenprüfungen während echter Grinds unabhängige Inventarmengen vor/nach
   der Sitzung oder manuell annotierte Lootaufnahmen verwenden. Verkäufe und
   Lagertransfers müssen beim Inventarvergleich berücksichtigt werden.
   Die beobachtete gute Funktion von Garmoth ist plausibel, aber aus
   der Architektur allein lässt sich kein Genauigkeitsvorsprung quantifizieren.

**Quellen und Reproduzierbarkeit**

- [Entpacktes Paketmanifest](/C:/Users/marku/AppData/Local/Temp/codex-garmoth-analysis-20260910/app/package.json)
  bestätigt Version 0.9.13.
- [Formatiertes Electron-main.js](/C:/Users/marku/AppData/Local/Temp/codex-garmoth-analysis-20260910/pretty/main.js)
  und [Renderer-Kern](/C:/Users/marku/AppData/Local/Temp/codex-garmoth-analysis-20260910/pretty/CapIpwiK.js)
  enthalten die untersuchten Regeln. Genannte Zeilen beziehen sich auf diese
  formatierten Analysekopien, nicht auf die minifizierten Originaldateien.
- [Setup-Komponente](/C:/Users/marku/AppData/Local/Temp/codex-garmoth-analysis-20260910/pretty/CgnLv73t.js)
  dokumentiert Sichtbarkeit, gespeichertes Layout und Überdeckungswarnungen.
- [Native Zeichenketten mit Dateioffsets](/C:/Users/marku/AppData/Local/Temp/codex-garmoth-analysis-20260910/native/rdata-strings.txt)
  belegen Bibliotheken, Capture-APIs und Protokollfelder. Bibliotheksstrings allein
  belegen nicht, welche optionalen Codepfade zur Laufzeit ausgewählt werden.
- [Offizielle Garmoth-App-Seite](https://garmoth.com/app) bestätigt Version,
  Bildschirm-OCR und Bedienvoraussetzungen; [OAR-OCR](https://github.com/GreatV/oar-ocr)
  beschreibt die zugrunde liegende Bibliothek. Die konkreten Zählerregeln stammen
  aus der lokalen Installation.

SHA-256 der untersuchten Originale:

| Datei | SHA-256 |
|---|---|
| `resources/app.asar` | `F862E47C23DBBE78709DE8C576EA1BB70DE3017765662E572468CCEAB200A723` |
| `resources/resources/garmoth-ocr.exe` | `7357579E1901D935829C27CB82C10B890E7B2A1D63BC8CEBE4434B419B24727B` |
| `dist-electron/main.js` im ASAR | `BE9DC24D6DE6260A2A6B8B11A59E4FCC9EA46671FEEA80485F897FC6288E9473` |
| `.output/public/CapIpwiK.js` im ASAR | `B2520505DBD42BAFBBD7E7D44576A571A87A1B80C87675365646F8B53292DC8E` |

Die Analysekopien liegen im lokalen Temp-Verzeichnis und können durch eine
spätere Systembereinigung verschwinden. Dieser Bericht enthält die wesentlichen
eigenständig zusammengefassten Befunde; die Installation und der Tracking-Code
wurden durch diese Untersuchung nicht geändert.
