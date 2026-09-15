# Aresion-Aufnahme vom 14. September 2026

## Ergebnis und Referenz

Der Nutzerscreenshot zeigt **14.080 Scorched Belt Ornament** und **201 Ancient
Spirit Dust**, bei **59 min 25 s** und **1.681 besiegten Monstern**. Die Aufnahme
`loot-20260914-210046-876e580f104e42d3961fe7f2b5892d69` enthält dagegen **14.025
Trashloot** und **197 Dust**; gespeicherte Session und letzte Projektion stimmen
überein. Die Nettoabweichung beim Trashloot beträgt zunächst **−55**.

Der gemeldete Fehler **10 → 101 ist nachgewiesen**. Ein finaler Drop bleibt
fälschlich bei 101. Seine Korrektur reduziert den Stand um 91 auf **13.934**.
Damit bleiben **146 Trashloot gegenüber dem Screenshot ungeklärt**. Die
101-Korrektur allein stellt die tatsächliche Gesamtmenge nicht wieder her.

## Ursache und Bildnachweise

In den Frames **10320–10324, 10336 und 10339**, jeweils Slot 1, steht vor der
zusätzlichen Paddle-Prüfung bereits die korrekte Menge 10. Der Rohtext enthält
jedoch nur den Itemnamen. Die Prüfung läuft deshalb mit dem Grund
`quantity-without-complete-text`. Original- und Graustufenvariante lesen beide
`Scorched Belt Ornament x 101`; `observation-revised` ersetzt daraufhin 10 durch
101. Die ursprünglichen Diagnosen zeigen dabei `quantityAnomaly: null`.

Frame **010320-normal.png** zeigt sichtbar `x 10` mit heller Hintergrundgeometrie
direkt rechts daneben. Der erneute native Mengen-Probe erkennt per Vorlage 10
mit Score **0,8415302**. Das liegt unter `TrustedTemplateScore = 0,90` in
`BackgroundLootRowReview`; die zusätzliche Ganzzeilenprüfung wird dadurch nicht
übersprungen. Der Fehler entsteht somit beim nachträglichen Überschreiben einer
korrekten, aber noch nicht ausreichend sicher bewerteten Vorlagenmenge.

Ein erneuter nativer Lauf auf den exakten Zeilenausschnitten (Panel-X 40,
Breite 385, Höhe 50, UI-Skalierung 1) reproduziert **alle 133 aufgezeichneten
Paddle-Mengenänderungen** einschließlich der sieben falschen 101-Lesungen.
Die bisherige Konfidenz ist der Mittelwert über die ganze Textzeile. In Frame
10320 beträgt er **0,98158 / 0,97287** für Original und Graustufen; die falsche
Endziffer 1 erreicht jedoch nur **0,64942 / 0,39570**. Der lange, gut gelesene
Itemname verdeckt somit die unsichere Mengenziffer.

| Befund | Frames / finaler Drop | Endmenge |
|---|---|---:|
| Dauerhafter 101-Fehler | 10320–10325, fünf Lesungen 101 und eine Lesung 10 | **101 statt 10** |
| Zweiter 101-Verdacht | 10335–10340, vier Lesungen 10 und zwei Lesungen 101 | 10, bereits korrekt |
| Weitere angehängte Ziffer | 10971: 61; 13392: 121; 14194: 41 | 6 / 12 / 4, bereits korrekt |

Der dauerhafte Fehler gehört zum Ereignis
`8e720aaa-0a1d-427a-656d-69746566694c`, Ankunft
`2026-09-14T21:35:41.857+00:00`. Die 121- und 41-Lesungen entstehen ebenfalls
durch Paddle-Änderungen von zuvor 12 beziehungsweise 4.

Echte größere Mengen sind vorhanden: **001355-normal.png** zeigt unter anderem
30, 28, 30 und 22. Weitere stabile Beispiele sind Frame 1087 mit 20/22,
Frame 1081 mit 26 und Frame 1311 mit 24. Eine pauschale Mengenobergrenze von 10
oder das ungeprüfte Entfernen einer Endziffer wäre daher keine geeignete Lösung.

## Replay und Grenzen

- Der unveränderte Rohtext-Replay reproduziert **14.025** aus **1.626 finalen
  Trashloot-Ereignissen**. Ausgewählte Lebensdauer: 1.350 ms. Der Audit betrachtet
  Normal-Loot; drei reine Rare-Twilight-Drops gehören nicht zu dieser Abbildung.
- Ein ausdrücklich **abgeleiteter Eingabeversuch** ersetzt ausschließlich die
  sieben genannten 101-Beobachtungen durch 10 samt passendem Rohtext. Er ergibt
  **13.934**, bei gleicher Ereignisanzahl und unveränderten anderen Itemmengen.
  Ein weiterer Sechser-Drop verschiebt seine geschätzte Ankunft um 201 ms, ohne
  Mengenwirkung. Dies ist ein Versuch zur Zählerwirkung, **kein erneuter
  vollständiger OCR-Lauf** der Bilder und kein Beleg für eine fertig korrigierte
  Aufnahme.
- Die 146 verbleibenden Trashloot und vier Dust sind noch nicht vollständig
  erklärt. Es wird keine automatische Korrektur auf den Screenshotwert abgeleitet.
- **004368-normal.png** zeigt eine Questabschluss-Einblendung über der unteren
  Lootzeile einschließlich ihrer Menge. Benachbarte Frames erkennen teils nur
  `Scorched`. Diese Verdeckung ist eine zusätzliche sichtbare Einschränkung;
  eine bestimmte fehlende Menge ist daraus nicht belegt.
- Die Aufnahme enthält 17.518 Frames; deren Zeitstempel umfassen 59 min 14,5 s,
  während `activeSeconds` 59 min 0,6 s ausweist. Unterschiedliche Zeitangaben
  belegen für sich keine Ursache der Mengendifferenz.
- Originaljournal SHA-256:
  `6C3F04126DE07D0B0EC1CEC7CC50D3D392CC13627531F91CE6358A519BEF23E0`.
  Originaljournal und Bilder wurden nicht geändert.

## Implementierte Korrektur und Prüfung

`paddle-review-v3` erhält zusätzlich zur bisherigen Zeilenkonfidenz die
Einzelzeichen-Scores des lokalen Paddle-Modells. Soll die Nachprüfung eine
bereits akzeptierte positive Menge desselben Items ändern, müssen in beiden
Bildvarianten alle Mengenziffern mindestens **0,90** erreichen. Die bestehende
Zeilengrenze von 0,95 und die Übereinstimmung beider Lesungen bleiben erforderlich.
Die Zifferngrenze berücksichtigt den echten 30er-Kontrollausschnitt: Seine
schwächste Ziffer erreicht 0,91091, die nachgewiesenen falschen Endziffern dagegen
höchstens 0,69004. Das sind Modellscores, keine kalibrierten Wahrscheinlichkeiten.

Die Prüfung ersetzt keine Ziffern durch Vermutungen. Sie behält die ursprüngliche
Menge bei unzureichender Ersetzungsevidenz bei. Fehlende Mengen werden weiterhin
nach den bestehenden Regeln ergänzt. Bestätigte feste Einermengen und die
konfigurierten Dropgrenzen bleiben erhalten. Neue Diagnosen speichern außerdem
die Konfidenz der Mengenziffern getrennt vom Mittelwert der ganzen Zeile.

- Der erneute native Review aller **133 ursprünglich geänderten Trashzeilen**
  verhindert genau **neun** falsche Ersetzungen: siebenmal 10 → 101, einmal
  12 → 121 und einmal 4 → 41. Die übrigen **124 Mengenänderungen bleiben gleich**.
- Ein weiterer abgeleiteter Zählerlauf mit diesen neun tatsächlich neu geprüften
  Zeilenergebnissen und den übrigen Originalbeobachtungen ergibt ebenfalls
  **13.934**; die anderen Itemmengen bleiben gleich. Auch dies ist kein
  vollständiger erneuter OCR-Lauf aller 17.518 Bilder.
- Vier verlustfreie Testausschnitte sichern die realen 10er-Fehler, die Erkennung
  eines echten 30ers und die Ergänzung einer fehlenden Sechsermenge ab.
  Synthetische native OCR-Kontrollen bestätigen weiterhin mögliche Mengen
  **101, 104 und 338**. Decoder- und Reviewtests prüfen auch Unicode,
  Zeichenpositionen, Grenzwerte, ungültige Scores und bisherige OCR-Adapter.
- Abschließende Tests: **9.319 App-, 348 OCR-, 1.044 Core- und 169
  BrowserPreview-Tests bestanden**, insgesamt **10.880**, ohne übersprungene
  Tests. Der gesamte Lösungslauf bestand zunächst bis auf den neuen
  30er-Grenztest; nach der Anpassung wurden die betroffenen nativen Tests und
  sämtliche App-Tests erfolgreich erneut ausgeführt. Die Grenze 0,90 verändert
  gegenüber dem Vergleichslauf keine der 133 geprüften Ergebnismengen.

Die lokalen Prüfergebnisse liegen unter `artifacts/aresion-20260914/`:
`baseline-audit/`, `counterfactual101-audit/`, `counterfactual101-diff.json`,
`derived-correct-seven-101-changes.json`, `event-coverage.json` und
`native-review-before-exact.jsonl`, `native-review-final.jsonl` und
`native-review-counter-audit/`. Die abgeleiteten Journale tragen eine ausdrückliche
Versuchskennzeichnung im Header. Die dauerhaften Bildfixtures samt Herkunft
liegen unter `tests/fixtures/quantity-confidence/`.
