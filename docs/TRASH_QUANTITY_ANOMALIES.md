# Gezielte Prüfung auffälliger Trashmengen

Windows OCR bleibt primär. Der Paddle-Primärversuch wurde nach der
[Auswertung der vollständigen Magaia-Aufnahme](https://github.com/volatile1990/bdo-grind-tracker/blob/codex/paddle-primary-ocr/docs/PADDLE_PRIMARY_EVALUATION.md)
als separater Experimentstand gesichert. Die produktive Änderung prüft nur
auffällige Mengen bereits erkannter Windows-Zeilen; sie erweitert weder deren
Sichtbarkeit noch den Zählalgorithmus.

## Auswahl

`TrashQuantityAnomalyDetector` betrachtet ausschließlich akzeptierten normalen
Trashloot des aktuellen Spots. Die letzten höchstens 64 tatsächlich neu
gebuchten Drops bilden die Stichprobe. Wiederholte HUD-Sichtungen, dieselbe
Drop-ID, Mengenrevisionen, Mindestmengenschätzungen, Platzhalter und Anker
werden nicht als neue Stichproben gelernt. Spotwechsel und Session-Reset
löschen die Historie; Benutzerdaten werden dafür nicht gespeichert.

Ab acht Stichproben gilt die eindeutige häufigste Menge als typisch, wenn sie
mindestens die Hälfte der Stichprobe ausmacht. Sonst dient die aktuelle
Katalogmindestmenge als vorsichtige Ausgangsbasis. Bei einer stabilen Historie
startet die Prüfung ab dem Fünffachen der typischen Menge; mit Katalogbasis
ab dem Achtfachen. Auch eine zusätzliche Dezimalziffer hinter der typischen
Menge löst die Prüfung aus, beispielsweise `4 → 43` oder `4 → 45`.
Normale Wechsel zwischen `2`, `4`, `6` und `8` werden dadurch bei Magaia nicht
allein wegen des Mengenwechsels geprüft.

Diese Schwellen lösen nur eine OCR-Nachprüfung aus. Sie sind keine neue
Höchstmenge und kürzen keine Drops. Die vorhandenen Minima und Maxima pro
Drop gelten unverändert; Magaia hat Minimum 2.

## Annahme einer Korrektur

Der vorhandene Paddle-Pool liest Originalfarbe und Graustufen derselben
kalibrierten Zeile. Beide Lesungen benötigen mindestens 0,95 Modellscore,
höchstens 0,05 normalisierte Namensdistanz, denselben bereits durch Windows
zugeordneten Gegenstand sowie dieselbe vollständige positive Menge. Eine
fehlende Menge, `4/0`, widersprüchliche Zahlen, ein anderer Gegenstand oder ein
Modellfehler lassen die Windows-Beobachtung bestehen.

Bestätigt Paddle die vorhandene Menge, bleibt sie auch bei einem großen
Drop wie 60 oder 338 bestehen. Ein anderer Wert muss innerhalb der bestehenden
Kataloggrenzen und unter der jeweiligen Anomalieschwelle liegen. Erst dann wird
die Menge ersetzt. Identität, Slot und Sichtbarkeit der Windows-Zeile bleiben
erhalten. Die beiden OCR-Varianten sind keine unabhängigen statistischen Beweise;
dieses Verfahren garantiert keine fehlerfreie Erkennung jedes Bildes.

Die Anomalie hat Vorrang vor dem bisherigen Schutz einer vollständigen
Windows-Endmenge. Dies ist auch dann nötig, wenn bereits ein Review wegen
unsicherer Namenszuordnung gestartet wurde: Bei der ursprünglichen
43-Fehlmenge hatte Paddle bereits zweimal 4 gelesen, durfte diese Menge aber
noch nicht übernehmen.

## Diagnose und reproduzierbarer Test

Der Pfad trägt `+trash-quantity-anomaly-v1`. Reviews protokollieren
`trash-quantity-anomaly` sowie typische Menge, Ausgangswert, Basis und
Stichprobenzahl. `anomaly-quantity-corrected` kennzeichnet eine angenommene
Korrektur; andere Ergebnisse begründen die Beibehaltung des Windows-Werts.
Laufzeit und Rohlesungen bleiben im üblichen Diagnosejournal enthalten.

Der lokale Prüflauf unter `artifacts/windows-anomaly-qa/harness` verarbeitet
gespeicherte Bilder erneut durch die aktuelle Produktionspipeline. Modus
`windows` aktiviert diese Prüfung, `windows-baseline` deaktiviert nur diese
Ergänzung. Das Prüfprogramm des separaten Paddle-Branches diente dafür als
Grundlage; dessen primärer Paddle-Pfad wurde nicht nach `main` übernommen.
Ein normales App-`--replay` spielt gespeicherte Beobachtungen ab und führt
keine neue OCR durch.

## Vollständige Magaia-Prüfung vom 10.09.2026

Die Umsetzung liegt auf `main`, ausgehend von `ffb1b4c`, dem gesicherten Stand
vor dem Paddle-Primärversuch. Produktionsfactory, Windows-Vorverarbeitung und
Zählalgorithmus entsprechen dieser Ausgangsbasis. Es wurde kein primärer
Paddle-Leser übernommen.

Alle 4.655 Normal-/Rare-Bildpaare der Aufnahme
`loot-20260910-150334-c7555f5bbf8745829d035edd9248fbe1` wurden erneut durch die
aktuelle Pipeline verarbeitet. Originalzeitstempel, Bildgeometrie und
abschließender Flush wurden beibehalten. Pixelvergleiche und SHA-256-Prüfungen
bestätigen 9.312 unveränderte Originaldateien. Der nachfolgende Zähler-Replay
reproduziert sowohl Summen als auch Ereignisse je Frame exakt.

| Prüffall | Ergebnis |
| --- | --- |
| Frame 1532 / Y224 | Windows 43 → zwei Paddle-Lesungen 4 → übernommen |
| Frame 3450 / Y149 | Windows 45 → zwei Paddle-Lesungen 4 → übernommen |
| Gelernte Referenz an beiden Stellen | Typische Menge 4 aus 64 gebuchten Drops |
| Alle 15 zuvor bestätigten großen Drops | Menge und Buchung unverändert, einschließlich 338 |
| Ausblendende 60er-Zeile | Frame 1641 einmal 60; Frame 1644 keine neue Beobachtung/Buchung |
| Zusätzliche angenommene Zeilen gegenüber Windows-Kontrolle | 0 |
| Gezielte Anomalieprüfungen | 43: 41 bestätigt, 2 korrigiert |

Nur die beiden genannten Beobachtungen ändern ihre Menge. Der Zähler reagiert
auch auf den dadurch veränderten Zeilenabgleich; die Nettowirkung ist deshalb
nicht einfach die Differenz der beiden Rohzahlen.

| Gegenstand | Windows-Kontrolle | Mit Anomalieprüfung | BDO-Screenshot |
| --- | ---: | ---: | ---: |
| Elion Follower's Helmet | 7.362 | **7.266** | 7.326 |
| Black Stone | 63 | 63 | 63 |
| Ancient Spirit Dust | 89 | 89 | nicht abgebildet |
| Caphras Stone | 32 | 32 | 32 |
| Corrupt Oil of Immortality | 3 | 3 | 3 |
| JIN Origin Shard | 3 | 3 | 3 |
| Nev's Fragment | 1 | 1 | 1 |
| Crimson Primordial Luster - Sovereign | 1 | 1 | 1 |
| Laila's Petal | 1 | 1 | 1 |

Die verbleibende Differenz von 60 Helmen zum Screenshot ist ungeklärt. Die
Screenshotgrenze ist nicht unabhängig mit dem Aufnahmezeitraum synchronisiert;
die Restdifferenz allein belegt daher nicht genau 60 verlorene Drops. Es wurde
keine Summenanpassung oder weitere Änderung am Zählalgorithmus vorgenommen.

Die Windows-OCR-Aufrufe bleiben exakt bei 42.204. Über alle Frames steigen die
Paddle-Lesungen von 914 auf 1.002, ohne OCR-Fehler. Die 43 Anomalieprüfungen
umfassen 86 Lesungen; eine bisherige Namensprüfung wird ersetzt und zwei
bestehende Zuordnungsprüfungen kommen hinzu, zusammen netto 88 zusätzliche
Lesungen. Die gemessene mittlere Frame-Analyse beträgt 34,15 ms gegenüber
33,94 ms; der gesamte lokale Durchlauf 200,82 s gegenüber 199,98 s. Das sind
Messwerte dieser Aufbereitung und Maschine, kein isolierter Live-Capture-Benchmark.

Der vollständige App-Testlauf bestand mit 2.821 Tests. Nach Ergänzung des
Regressionsfalls für den bestehenden Einzelmengenfilter bestanden alle 84
gezielten Anomalietests. Zähler- und OCR-Quellcode blieben unverändert; deren
336 beziehungsweise 243 Tests waren bereits erfolgreich geprüft.

Ausführliche lokale Belege: `artifacts/windows-anomaly-qa/fresh-validation.json`,
`windows-full/summary.json`, `windows-full/observations.jsonl` und `tests/`.
