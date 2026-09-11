# OCR- und Zählervergleich vom 11. September 2026

Die beiden neuen Aufnahmen sind vollständig ausgewertet und ergänzen die zwei
bisherigen Referenzen. Die vom Nutzer genannten Spielmengen sind unabhängige
Sollwerte. Sie werden weder an die OCR noch an den Zähler übergeben.

**Der neue Test 6 erreicht die echten 604 Helme in der vierten Aufnahme.**
Vollständige frische OCR aller vier Sitzungen ergibt **576 / 264 / 2.038 / 604**;
die Rohlesungen sind gegenüber Test 5 unverändert. Korrigiert wird die
Zuordnung belegter Zeilen im Zähler. Die erste Referenz bleibt exakt, die
lange Aufnahme verbessert sich um vier Helme und bleibt zwölf unter Soll.
Der reine Paddle-Wechsel wird nach dem folgenden Vergleich nicht übernommen.

| Aufnahme (UTC) | Echte Helme | Grindcrest Test 5 | Reines Paddle | Reines Paddle, enger Streifen | Garmoth nativ, offline |
|---|---:|---:|---:|---:|---:|
| 10.09., 19:40 | 576 | 576 | 588 | 588 | 592 |
| 10.09., 20:14 | 324 | 264 | 292 | 308 | 276 |
| 11.09., 05:50 | 2.050 | 2.034 | 2.070 | 2.070 | 2.062 |
| 11.09., 06:23 | 604 | 600 | 608 | 608 | 620 |

**Keine dieser Varianten trifft alle Referenzen.** Paddle verbessert die
Helmzählung in der durch das spielinterne Boss-Popup beeinträchtigten zweiten
Aufnahme, zählt in den übrigen drei aber zu viel. Außerdem verliert der reine
Paddle-Pfad den Ring der ersten Aufnahme durch einen unbrauchbaren Rare-Text.
Die sieben echten Dust der zweiten Aufnahme werden von keinem Pfad erreicht;
alle zählen weiterhin vier.

Der Nutzer beobachtete in der vierten Sitzung **live Garmoth 612**. Der
Offlinewert 620 gehört zu einem anderen, klar abgegrenzten Versuch auf
Grindcrests gespeicherten Panels. Er ersetzt den beobachteten Livewert nicht.

## Was tatsächlich verglichen wurde

Der vollständige Paddle-Wechsel verwendet das vorhandene **PP-OCRv6 Small**
als ONNX-Recognizer: eine Farblesung pro Normalposition und eine für das
Rare-Band. Es laufen weder Windows OCR noch Zifferntemplates, Windows-Recovery
oder eine zweite OCR-Abstimmung. Parser, `lifetime-v2`, Spotregeln, Rare-Fusion
und Anzeigepuffer bleiben gleich. Beide vorher festgelegten Ausschnitte werden
auf allen vier vollständigen Aufnahmen verwendet.

Zusätzlich verarbeitet der installierte native Garmoth-Helper alle 5.512
Normal-PNGs mit seinem **PP-OCRv4-Mobile-Detector**, dem englischen Recognizer
und `scale:2`. Die dynamisch erkannten Textboxen gehen sowohl an den
extrahierten Originalparser/-zähler als auch an Grindcrests aktuellen
`lifetime-v2`-Adapter. **Beide liefern auf diesen identischen Rohlesungen in
allen vier Aufnahmen dieselben finalen Itemmengen.** Das beweist weder
identische Zwischenstände noch vollständige Live-Parität. Dieser zusätzliche
Versuch enthält ausschließlich den Normalfeed, keine Rare-Fusion.

## Konkreter Fehler der 604er-Aufnahme

Die Bildfolge **279–301** enthält neun neue Helmzeilen mit jeweils vier Stück,
also 36 Helme. Im endgültigen Windows-Ledger bleiben acht Zeilen mit 32 Helmen.
Die visuell nachvollzogenen Ankünfte liegen in 279 (zweimal), 280, 283, 285,
289, 290, 291 und 295.

In Frame 283 wächst der Feed von drei auf vier Zeilen. Windows hat die vierte
Zeile bereits vollständig und korrekt gelesen. Das Zeitmodell verwirft diese
obere Lesung trotzdem und setzt die alten Ereignisse an der falschen Position
fort. Bei 290 verbindet es ebenfalls zwei verschiedene Ankünfte; ein falscher
Neubeginn bei 287 kompensiert einen dieser Verluste. **Netto fehlt hier genau
ein Viererdrop.** Ein besserer OCR-Text allein beseitigt diese falsche
Zuordnung nicht.

Zwei zunächst auffällige andere Stellen erklären keine fehlende Endmenge:

- Frame 131: Paddle erkennt eine neue Zeile früher; Windows holt nach 0,608 s
  auf. Beide finalen Ledger enthalten jeweils drei Viererdrops.
- Frame 1148: Windows wechselt von 1.350 auf 1.450 ms Lebensdauer und nimmt
  acht Helme zurück. Alle acht lokalen Viererdrops bleiben erhalten; die
  Modellabweichung stammt aus früheren Ereignisketten. Ein pauschales
  Verhindern dieser Rücknahme ist deshalb keine belegte Fehlerbehebung.

Die 2.050er-Aufnahme enthält ebenfalls nachgewiesene falsche Verbindungen:
In Frame 96 geht eine bereits korrekt gelesene zusätzliche Zeile verloren.
Um 1804 kommen zwei neue Zeilen an, der Windows-Pfad erfasst nur eine davon.
Weitere Paddle-Zeilen sind dagegen nachweislich erneute Zählungen ausblendender
Einträge. Die gesamte Differenz von 16 Helmen ist noch nicht vollständig
einzelnen Ursachen zugeordnet.

## Verworfene Korrekturprobe: Rohtext allein reicht nicht

Eine isolierte Gegenregel verlangt eine Erklärung für einen wachsenden,
vollständig gelesenen Stapel, wenn die beiden vorherigen Bilder denselben
zusammenhängenden Stapel zeigen und dessen Namen/Mengen an den verschobenen
Positionen wieder auftauchen. Sie schreibt keine bestimmte neue Menge,
Lebensdauer oder Ereignis-ID vor. Mit identischer Interpretation der
Produktlesungen liefert diese Probe **576 / 264 / 2.038 / 604 Helme**.
In der vierten Aufnahme kommt genau eine zusätzliche finale Viererkette
im nachgewiesenen Fehlerabschnitt hinzu; keine Basiskette entfällt.

Die entscheidende Gegenprobe scheitert jedoch: Eine tatsächlich durchgehende
einzelne Viererzeile, neben der die OCR einmalig einen zusätzlichen gültigen
Slot meldet, bleibt im bisherigen Modell bei vier Helmen. Die neue harte
Abdeckungsregel behält acht. **Die besseren Referenzsummen reichen deshalb
nicht für eine Übernahme in die App.** Einzelne Mengen-/Namensfehler und der
isolierte Einzelbildfehler ohne bestehenden Stapel bleiben zwar korrigierbar;
der zusätzliche falsche Slot ist aber ein konkreter Rückschritt.

Ein separater Versuch mit dem vorhandenen Glyphenvergleich verhindert in
Frame 290 eine falsche Fortsetzung. Der Fehler verschiebt sich anschließend
auf eine andere Verbindung; die Endmengen bleiben unverändert. Die
Kombination mit der harten Abdeckung beseitigt deren Fehlalarmproblem nicht.
Neue Ankünfte allein aus irgendeiner Helligkeitsänderung abzuleiten wäre
ebenfalls unbegründet.

Bei der Prüfung wurde zudem ein Fehler im **isolierten Testhilfsprogramm**
entdeckt und korrigiert: Die frühe Abdeckungsnormierung verwendete nur den
Rohtextparser, während das Produkt bei verkürzten Texten auch den bereits
akzeptierten Namen und die Menge erhält. Frame 291, Slot 3 ist so ein Fall;
diese Zeile ist akzeptiert. Die betroffenen frühen Abdeckungsergebnisse liegen
als `superseded-rawparser-only-*` getrennt vor. Die hier genannten Werte
verwenden die korrigierte Normierung mit gesondertem Vertragstest. Die
vollständige OCR-Matrix und die unveränderten Baseline-Replays waren von
diesem Hilfsprogrammfehler nicht betroffen.

Belege: `counter-analysis/experiment/appearance-results.json`,
`normalization-contract.json`, `guard-results.json` und
`counter-analysis/recording4/coverage-established-extra-events.json`.

## Übernommene Korrektur: bildbestätigte Platzbelegung

`lifetime-v3` ergänzt die Abdeckungsregel um einen unabhängigen Bildnachweis.
Der oberste zusätzlich belegte Platz muss mit einer Glyphenmaske aus einer
vorher akzeptierten Zeile korrelieren (mindestens 0,90). Der Vergleich nutzt
den Namenbereich ohne Mengenziffern. Die alte Maske bleibt auch dann brauchbar,
wenn der aktuelle Text bereits ausblendet und keine eigene starke Maske mehr
liefert. Die beiden vorherigen vollständig gelesenen Stapel müssen weiterhin
übereinstimmen; ihre Namen und Mengen müssen im längeren aktuellen Stapel an
den verschobenen Positionen wiederkehren.

Dieser Bildnachweis bestätigt ausschließlich die Platzbelegung. Er liefert
weder eine Itemmenge noch eine eindeutige Ereignis-ID. In Frame 291 passen
beispielsweise zwei vorherige Zeilen ausreichend gut: Das belegt Text an der
Position, erlaubt aber keine eindeutige Zuordnung zu einer dieser Zeilen.
Der Zähler verlangt eine mit diesen belegten Plätzen vereinbare Hypothese.
Würde eine Lebensdauer dadurch überhaupt keine Kandidaten behalten, verwendet
sie für dieses Bild die unbeschränkten Kandidaten und protokolliert den Fallback.

Die vorher gescheiterte Gegenprobe mit einem zusätzlichen OCR-Slot auf einer
leeren Bildstelle bleibt damit bei vier Helmen. Auch isolierter Einzelbildfehler,
Namensglitch und Wiederkehr beim Ausblenden bestehen ihre Gegenproben.
Der Produktionsvergleich reproduziert die unabhängig gemessenen Glyphenmatches
auf **allen 5.512 Bildern exakt**; er verändert keine OCR-Lesung oder Reihenfolge.

Auf den Original-Rohlesungen liefert V3 **576 / 264 / 2.038 / 604 Helme**.
Der erste zusätzliche Vierer der 2.050er-Aufnahme liegt bei Frame 96, der
zusätzliche Vierer der 604er-Aufnahme im nachgewiesenen Abschnitt 279–301.
Die genaue zeitliche Zuordnung einzelner Ankünfte bleibt dort unvollkommen;
eine korrekte Endsumme beweist keine perfekte Ereignisidentität.

Die vollständige neue OCR aller vier Aufnahmen bestätigt diese Ergebnisse.
Sämtliche **5.512 Frames**, unveränderte Originaldateien und identische
Ereignis-Replays sind geprüft. Nach Entfernen allein der neuen
Belegungsmetadaten sind sämtliche Beobachtungen, ihre Reihenfolge und
Zeitstempel exakt gleich zum eingefrorenen Test-5-Pfad.

| Aufnahme | Test 6: Helme | Andere Items | Interne / sichtbare Rücknahmen |
|---|---:|---|---:|
| 1 | 576 | 3 Black Stones, 1 Caphras, 1 Ring | 10 / 0 |
| 2 | 264 | 4 Dust | 13 / 1 |
| 3 | 2.038 | 38 Dust, 8 Black Stones, 10 Caphras, je 1 Fusion/JIN/Nev | 21 / 1 |
| 4 | 604 | 1 Caphras, 2 Black Stones, 1 Dust | 22 / 0 |

Die beiden exakten Helmreferenzen zeigen im getesteten Anzeigepuffer keine
Rückzählung. Die anderen beiden behalten jeweils eine späte Korrektur;
eine generelle Garantie gegen Rückzählungen besteht weiterhin nicht.
Die mittlere reine Analysezeit liegt je Aufnahme bei 35–38 ms. Jeder Lauf
hat einen einzelnen Frame über 200 ms; Capturezeit ist darin nicht enthalten.
Keiner der vier Läufe benötigt den Constraint-Fallback.
Nachweise: `artifacts/lifetime-test-6-qa/verification-results.json` sowie
`verified-ocr-recording1` bis `verified-ocr-recording4`.

Der neue Pfad verwendet `grindcrest-lifetime-v3` und `visual-occupancy-v1`.
Historische V1/V2-Diagnosen behalten ihre ursprüngliche Logik. Die vier
Lebensdauern, fünf Zählpositionen, sechs OCR-Ausschnitte, OCR-Engines,
200-ms-Aufnahme und zweisekündige Anzeigebestätigung bleiben unverändert.
Die gesamte Suite besteht mit **3.757 Tests**: Core 448, OCR 243, App 3.066.

## Aufnahme und Laufzeit

Die neuen Sitzungen erreichen im Mittel etwa **203 ms pro Aufnahme**.
Das größte gemessene Intervall beträgt 233 ms beziehungsweise 225 ms;
es gibt keinen Capture-Stau, der die Verluste erklärt. Die endgültigen
Rohsummen entsprechen den gespeicherten Summen. In der 604er-Aufnahme liegen
zwischen letzter Ankunft und Stopp mehr als acht Sekunden.

In der 604er-Aufnahme benötigt der bisherige Pfad durchschnittlich 27 ms reine
Analyse, reines Paddle 97 ms und der engere Paddle-Streifen 168 ms. Zusammen
mit PNG-Vorbereitung liegen die Mittelwerte bei 39/105/176 ms. Diese
Offlinezeiten schließen die echte Fensteraufnahme aus; sie garantieren keinen
Live-Takt. Der engere Streifen wird vom Recognizer auf dieselbe Höhe vergrößert
und erzeugt dadurch breitere, aufwendigere Eingaben.

## Nachweise und Grenzen

Alle OCR-Versuche wurden nacheinander auf den vollständigen PNG-Folgen mit
Originalzeitstempeln ausgeführt. Replays der erzeugten Journale reproduzieren
Endmengen und Ereignisabläufe. Die Produktassemblies blieben für den Vergleich
eingefroren.

Beim engen Paddle-Lauf der dritten Aufnahme wurde die Hilfsdatei
`count-summary.json` extern geändert. Der strenge Fehlerstatus des Versuchs
bleibt dokumentiert. Die zusätzliche Prüfung bestätigt unveränderte 4.998
PNGs und das unveränderte Eingabejournal; die Hilfsdatei fließt nicht in die
Auswertung ein. Alle anderen Läufe bestehen auch die vollständige
Quelldateiprüfung.

Lokale Artefakte unter `artifacts/ocr-comparison-20260911/`:

- `comparison-results.json`: vollständige Matrix der zwölf Grindcrest-Läufe.
- `EXPERIMENT.md`: Geometrie, Modell, Laufzeit, Wiederholung und Hash-Ausnahme.
- `native-garmoth/README.md`: Original-Garmoth-Vergleich und Befehle.
- `native-garmoth/recording4-event-audit/README.md`: Bild- und Ereignisbelege
  für den verlorenen Vierer; alle 35 Kontextbilder und das Journal geprüft.
- `recording4-analysis/131-evidence.md` und `1148-evidence.md`:
  ausgeschlossene Verdachtsstellen.
- `counter-analysis/experiment/`: isolierte Zählerexperimente, verworfene und
  korrigierte Vorstände sowie der konkrete nicht bestandene Fehlalarmfall.

Die bekannten Sollwerte werden zusätzlich in
`tests/fixtures/lifetime/reference-sessions.json` festgehalten. Die neuen
Fixtures `recording3.txt` und `recording4.txt` enthalten ausschließlich originale
akzeptierte Normalzeilen mit ihren Zeitstempeln. Alle vier erweiterten
historischen Replaytestfälle bestehen.
Historische Replaytests prüfen die Reproduzierbarkeit des ursprünglichen
Zählers; ein erwarteter historischer Wert 600 ist ausdrücklich kein bestandener
Genauigkeitsnachweis für den Sollwert 604.
