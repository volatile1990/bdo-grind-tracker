# Maliq: zu wenig gezählter Trashloot am 19.09.2026

## Datengrundlage und Grenzen

- Nutzerbericht: Es wurde deutlich zu wenig Trashloot gezählt. Die richtige Gesamtmenge ist unbekannt.
- Aufnahme: `loot-20260919-014343-a66700477ee34362b40a38a267b3c243`, App 1.7.3, Engine `grindcrest-lifetime-v8`, Normalzähler `lifetime-v5`.
- 1.907 Frames und ein Sitzungsabschluss, 01:43:43–01:50:11 UTC, etwa 388 Sekunden Aufnahme und 351,68 Sekunden aktive Sessionzeit.
- Eingaben: `observations.jsonl`, `count-summary.json` und gespeicherte Normal-/Rare-Bildausschnitte. Die Nutzerdateien und erzeugten Diagnoseberichte bleiben außerhalb des versionierten Repositories.
- Einzelne sichtbare Zeilen sind nachprüfbar. Ihre Mengen dürfen nicht über aufeinanderfolgende Bilder summiert werden: Dieselben Drops bleiben mehrere Frames sichtbar.

## Gespeicherte Mengen und Replay

| Item | Aufnahme | Gespeicherte Session | Replay mit 1.8.0 |
|---|---:|---:|---:|
| Elion Follower's Helmet | 410 | 410 | 410 |
| Black Stone | 6 | 6 | 6 |
| Caphras Stone | 5 | 5 | 5 |

Für das Replay wurde die JSONL-Datei zuerst nach `artifacts/diagnostics/maliq-undercount-20260919/` kopiert. Die CLI schreibt ihren Bericht neben die Eingabedatei; das Original blieb unverändert.

Das Replay verwendet gespeicherte Texte und Erkennungsdaten. Es öffnet keine Bilder und führt keine neue OCR aus. Identische Replay-Mengen belegen deshalb keine korrekte Erkennung und auch keine korrekte Gesamtmenge.

Die öffentliche Replay-Ausgabe meldet ab Sequenz 688 eine unterschiedliche Ereignisfolge. Eine zusätzliche Prüfung mit den ausgelieferten 1.8.0-Assemblies ergab jedoch in **allen 1.907 Frames identische Itemmengen**. Die erste Abweichung betrifft die Projektionsrevision: 50 statt 49 bei gleichen Mengen, gleichem Dropzähler, gleichem letztem Zugangszeitpunkt und jeweils null neuen Ereignissen. Die neue Korrekturevidenz kann die Revision unabhängig von Mengenänderungen erhöhen. Diese Meldung ist kein Beleg für einen neuen Mengenverlust in 1.8.0.

## Belegter Hauptbefund: HDR-Vorverarbeitung entfernt lesbaren Text

Die Aufnahme kennzeichnet die Bilder als HDR und bereits tonemapped. Für diesen Normal-Loot-Pfad verwendet die Vorverarbeitung einen HSV-Helligkeitsgrenzwert von **V ≥ 230**. In den untersuchten Nutzerbildern erreicht die lesbare UI-Schrift nur **V = 227**. Die Maske entfernt diese Schrift damit vollständig, bevor die primäre Texterkennung sie lesen kann.

Die Normal-Crops sind 425 × 300 Pixel groß und enthalten den sichtbaren Loot an der erwarteten Position. Die Kalibrierung meldet 3.840 × 2.160 Pixel und UI-Skalierung 1. Für die untersuchten Normal-Crops ist kein falscher 4K-Ausschnitt belegt; die Schrift ist im gespeicherten Bild vorhanden. Der belegte Fehler liegt in ihrer anschließenden Verarbeitung.

Passend dazu enthält die Aufnahme nur in 415 Frames überhaupt Normal-Observations. Von 849 Normal-Zeilen haben 514 keinen erkannten Itemnamen; 273 tragen `ocr-geometry`, 241 ausschließlich visuelle Belegung. Weitere sieben Zeilen werden nach einer unvollständigen Namenslesung als falsches Spot-Item verworfen. Leere Aufnahmephasen sind darin enthalten; diese Zahlen sind keine Anzahl verlorener Drops.

Der Recovery-Pfad führt 15.252 OCR-Aufrufe aus und meldet 335 wiederhergestellte Zeilen, aber keine gesondert wiederhergestellten Mengen. Er liefert somit teilweise lesbare Einzelzeilen, während viele benachbarte Zeilen fehlen.

## Konkrete Bild-/Zählerbeispiele

| Sequenz | Bild und gespeicherte Erkennung | Zählerverhalten |
|---|---|---|
| 298–302 | Bild 299 zeigt fünf lesbare Helmet-Zeilen mit jeweils ×4. In 298 und 302 wird jeweils nur Slot 2 als Helmet ×4 gelesen; 299 enthält für fünf Slots lediglich `visual-occupancy-only`. | Helmet-Gesamtmenge bleibt bei 34. |
| 381–386 | Bilder 381, 382 und 385 zeigen zwei lesbare Helmet-Zeilen mit jeweils ×4. Gespeichert werden nur vereinzelte ×4-Lesungen in 381, 383 und 384; andere Frames liefern lediglich Belegung oder keine Normal-Zeilen. | 34 → 38 → 34 → 38 → 34; der zwischenzeitlich erkannte Zugang bleibt am Ende nicht erhalten. |
| 770–771 | Bild 770 zeigt zweimal Helmet ×4. Die primäre OCR liest jedoch `Helmet x 40`; zwei Review-Lesungen ergeben ×4, werden wegen ihrer Konfidenz nicht übernommen. | Zunächst 74 → 114, danach 114 → 90 durch erneute Bewertung. Eine pauschale Sperre negativer Korrekturen würde diesen Fehlwert festhalten. |
| 1821 | Die höhere Menge Helmet ×56 ist im Bild tatsächlich sichtbar; benachbarte Bilder zeigen außerdem Black Stone ×5 und Caphras Stone ×5. | Der Zugang von 56 ist kein belegter OCR-Fehler. Ein generelles Mengenlimit auf ×4 wäre falsch. |

Ein weiteres Mengenproblem zeigt Sequenz 112: Die primäre Lesung `Helmet 14` ohne Multiplikationszeichen wird vom Rohtextparser als Menge 14 interpretiert. Beide Review-Lesungen ergeben ×4, erreichen aber nicht beide den erforderlichen Konfidenzwert. Auch das spricht gegen eine nachträgliche pauschale Erhöhung oder Deckelung der Mengen.

## Verhalten des Zählers bei den beschädigten Eingaben

Der Lifetime-Zähler hält mehrere zeitliche Erklärungen offen und darf veröffentlichte Mengen korrigieren. Bei sehr lückenhafter Texterkennung kann eine Erklärung ohne die zwischenzeitlich gelesene Zeile gewinnen. `visual-occupancy-only` liefert dabei weder Itemnamen noch Mengenstimmen; die vorhandenen zusätzlichen Belegungsregeln schützen nicht jeden dünn gelesenen Stapel.

Die Sequenzen 381–388 wurden zusätzlich isoliert mit einem frisch initialisierten Zähler abgespielt. Auch ohne vorherige Lernhistorie entstand 4 → 0 → 4 → 0. Langfristig veränderte Lernwahrscheinlichkeiten sind für diesen konkreten Verlust also nicht erforderlich. Die fehlenden OCR-Lesungen reichen als Auslöser aus.

Insgesamt enthält die Aufnahme 27 negative Helmet-Mengenänderungen mit zusammen −144 und positive Änderungen mit zusammen +554. Diese Bruttowerte sind **keine** unabhängigen Dropmengen: Zurückgenommene Hypothesen können erneut erscheinen, und falsche Mengen können korrigiert werden. Daraus lässt sich die richtige Sessionmenge nicht berechnen.

## Weitere Abgrenzungen und relevante Stellen

- Kein belegter Capture-Rückstau: Aufnahmeabstand median 208 ms, Maximum 313 ms; Queue-Verzögerung median 0,026 ms, Maximum 64 ms. Die Analyse dauert median 83 ms.
- `count-summary.json` zeigt für historische Normal-Zeilen-Traces null neue Drops und null Mengenrevisionen. Der Lifetime-Adapter schreibt nur Modell-/Lane-Informationen und keine einzelnen Trace-Zeilen. Diese Nullwerte sind eine Diagnosegrenze, kein Nachweis fehlender Zähleraktivität.
- Die Rare-Kalibrierung stammt aus `UISettingPreset0` mit Status `PresetFallback`. Das ist ein Prüfhinweis; ein falscher Rare-Ausschnitt oder dadurch verlorener Rare-Loot ist damit nicht belegt.
- Zählerhypothesen und Mengenentscheidungen: `Core/LifetimeLootReconciler.cs`, insbesondere `Advance`, `UnreadableSlotCoverage`, `Decide` und `Project`.
- Reiner Belegungsnachweis: `App/Analysis/NormalLootOccupancyTracker.cs`; Rohtext-Mengenparser: `App/Analysis/LifetimeLootTextParser.cs`.
- Eingeschränkte Lifetime-Traces: `App/Analysis/LifetimeNormalReconciliationAdapter.cs`; Replay-Vergleich: `App/Diagnostics/LootDiagnosticReplay.cs`.

## Gezielte Korrektur und Bildprüfung

Der Helligkeitsgrenzwert in `ToneMappedNormalRowProcessor` wurde von 230 auf 220
gesetzt. Die unveränderten Sättigungs-, Zeilen-, Geometrie- und Katalogprüfungen
bleiben wirksam. Zählregeln, Review-Konfidenzen und Mengenlimits wurden nicht
auf einen vermuteten Sollwert angepasst.

Die isolierte Probe verglich die veröffentlichte Vorverarbeitung mit Varianten
für 225 und 220; die nachgebildete 230-Variante wurde gegen die tatsächlichen
Ausgabepixel der veröffentlichten Implementierung geprüft. Windows OCR wurde
anschließend erneut auf den vorbereiteten Originalbildzeilen ausgeführt:

| Helligkeitsgrenze | Korrekte Mengen in 14 sichtbaren Zeilen | Falscher OCR-Text in 48 leeren Zeilen |
|---|---:|---:|
| 230 (bisher) | 0 | 0 |
| 225 | 12 | 0 |
| 220 (Korrektur) | 14 | 0 |

225 lässt nur die hellsten Buchstabenteile übrig: Namen bleiben beschädigt und
die sichtbare 56 wird als `5b` gelesen. Mit 220 werden elf Helmet-×4-Zeilen,
Helmet ×56, Caphras ×5 und Black Stone ×5 mit ihren richtigen Mengen gelesen.
Bei einer leeren Landschaftszeile startet ein zusätzlicher OCR-Aufruf, der
keinen Text liefert und die Geometrieprüfung nicht besteht; die übrigen 47
leeren Zeilen bleiben bereits vor der OCR leer. Die Mengenprüfung dieser
Stichprobe ist kein Nachweis aller Item-Zuordnungen oder der gesamten Session.

Zwei unveränderte Bildzeilen (Helmet ×4/×56) und eine leere Szene liegen als
kleine Testfixtures unter `tests/fixtures/tone-mapped-loot/`. Sie werden nur vom
OCR-Testprojekt kopiert und nicht als App-Assets ausgeliefert. Die neuen
Realbildtests und der synthetische Fall mit Schriftwert 227 scheiterten vor dem
Fix am Blank-Filter. Nach dem Fix bestehen alle 388 OCR-Tests einschließlich
der echten Windows-Texterkennung, ohne übersprungene Tests.

Der vollständige Release-Testlauf mit SDK 9.0.318 und erzwungener Windows-OCR
besteht ebenfalls: 11.315 .NET-Tests (388 OCR, 1.073 Core, 9.626 App und 228
BrowserPreview), keine Fehler und keine übersprungenen Tests. TRX-Protokolle:
`artifacts/diagnostics/maliq-undercount-20260919/verification-after/`.

Messprotokoll und Rohdaten liegen in `artifacts/maliq-hdr-threshold-probe/`.
Das zuvor gebaute Store-MSIX 1.8.0 wurde nicht verändert und enthält diese
Quellcodekorrektur nicht. Das anschließend erstellte und geprüfte
[Patch-Release 1.8.1](release-notes/1.8.1.md) liefert sie aus.
Die richtige Gesamtmenge ist weiterhin unbekannt;
ein vollständiger neuer OCR-Durchlauf würde ebenfalls keinen Inventarabgleich
ersetzen. Das reine Replay der alten Erkennungseingaben bleibt bei 410 Helmen.
