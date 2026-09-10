# Paddle als primäre Loot-OCR: Auswertung

Stand: 10.09.2026. Experiment auf `codex/paddle-primary-ocr`, ausgehend von
`ffb1b4c6ec4d99824aef4ba01eb2e8c61b4f8a63`.

**Ergebnis:** Paddle korrigiert die beiden nachgewiesenen Lesefehler `4 → 43`
und `4 → 45`. Als primärer Leser liefert es mit dem unveränderten Zähler jedoch
7.620 Helme statt 7.362 im aktuellen Windows-Vergleich. Eine zusätzliche Buchung
von 60 Helmen ist anhand der Originalbilder eindeutig als Doppelzählung belegt:
Paddle liest eine ausblendende Zeile länger, während die zyklische Erneuerung des
Zählers daraus einen neuen Drop macht. Dieses Experiment ist damit keine
Freigabe für einen Wechsel der produktiven primären OCR.

## Verfahren und Integrität

- Dieselben 4.655 Bildpaare aus
  `loot-20260910-150334-c7555f5bbf8745829d035edd9248fbe1` wurden zweimal frisch
  verarbeitet: einmal Windows-primär, einmal Paddle-primär mit Windows-Fallback.
  Die historischen OCR-Texte wurden nicht als neue OCR-Ergebnisse eingesetzt.
- Beide Durchläufe verwenden die ursprünglichen Capture-Zeitstempel, dieselbe
  kalibrierte Geometrie, HDR/Tonemapping-Angaben, Spotfilter und Zählregeln.
  Abschluss/Flush wurden ausgeführt; beide Läufe enthalten alle 4.655 Frames.
- Die Ausgangsbasis enthält bereits die Magaia-Mindestmenge **2** und die enge
  Korrektur für den stabilen Black-Stone-Nachbarn. Diese Regeln sind in beiden
  frischen Läufen identisch. Der historische Mitschnitt hatte noch die ältere
  Mindestmenge und Zählerversion.
- Die normalen und seltenen PNG-Ausschnitte werden unverändert an ihrer
  kalibrierten Position im Testframe eingesetzt. Die Kopfzeile jedes neuen
  Journals kennzeichnet die frische PNG-OCR. Manifest und SHA-256-Prüfung
  bestätigen für **9.312 Originaldateien** in beiden Läufen
  `originalFilesUnchanged: true`.
- SHA-256 der ursprünglichen `observations.jsonl`:
  `236F7B57B886360D28DD7895256739C7A88D2F9D2E6E238406A568E63E1C8EB7`.
- Es gab keine Änderungen am gespeicherten Benutzerverlauf, Originaljournal oder
  den Originalbildern. Die Auswertung benötigt weder Spielzugriff noch Netzwerk.

Der Windows-Vergleich behält auch die bereits vorhandene optionale Recovery und
Paddle-Hintergrundprüfung bei. „Windows-primär“ bedeutet deshalb nicht, dass in
diesem Lauf überhaupt keine ergänzende Paddle-Prüfung stattfindet.

Paddle-primär liest die ursprünglichen kalibrierten Bänder vor Windows. Farb-
und Graustufenansicht müssen bei Gegenstand und Menge übereinstimmen; beide
Lesungen benötigen mindestens 0,95 Konfidenz und höchstens 0,05 normalisierte
Namensdistanz. Ein gültiges primäres Ergebnis wird nicht anschließend durch
Windows-Recovery oder die Hintergrundprüfung überschrieben. Bei fehlendem
Konsens bleibt der bisherige Windows-Pfad zuständig.

Das alte Windows-`IsBlank`-Gate wird erst **nach** dem Paddle-Versuch angewandt:
Die fehlerhaften 43/45-Zeilen waren im alten vorbereiteten Bild bereits als leer
markiert, obwohl Paddle ihre ursprünglichen Pixel noch lesen konnte. Es werden
keine Windows-Wortkoordinaten für Paddle-Ergebnisse erfunden.

## Gesamtergebnisse

| Gegenstand | BDO-Screenshot | Historischer Mitschnitt | Frisch Windows-primär | Frisch Paddle-primär |
|---|---:|---:|---:|---:|
| Elion Follower's Helmet | 7.326 | 7.414 | 7.362 | **7.620** |
| Black Stone | 63 | 64 | 63 | 63 |
| Ancient Spirit Dust | nicht abgebildet | 89 | 89 | 89 |
| Caphras Stone | 32 | 32 | 32 | 32 |
| Corrupt Oil of Immortality | 3 | 3 | 3 | 3 |
| JIN Origin Shard | 3 | 3 | 3 | 3 |
| Nev's Fragment | 1 | 1 | 1 | 1 |
| Crimson Primordial Luster - Sovereign | 1 | 1 | 1 | 1 |
| Laila's Petal | 1 | 1 | 1 | 1 |

Damit stimmen acht Gegenstandssummen zwischen den frischen Läufen überein;
Helme unterscheiden sich um **+258**. Die Abweichung zum Screenshot beträgt
Windows +36, Paddle +294. Der Screenshot wurde nicht unabhängig mit der
Aufnahmegrenze synchronisiert. Diese Restdifferenzen sind deshalb für sich
genommen kein Beweis für genau so viele fehlende oder doppelte Gegenstände.
Ancient Spirit Dust wurde in den Droplog-Bildern geprüft; sein Fehlen im
zusammengefassten BDO-Screenshot bedeutet nicht, dass seine Menge null war.

## Rohlesungen und Zählentscheidungen

Paddle liefert gegenüber dem frischen Windows-Lauf **380 zusätzliche akzeptierte
Beobachtungen**: 377 Helmet-Zeilen, eine Oil-Zeile und zwei Dust-Zeilen. Das sind
weitere Sichtungen, nicht automatisch weitere Drops. Keine zuvor akzeptierte
Beobachtung geht verloren. Auf gemeinsam akzeptierten Zeilen ändern sich nur
zwei Mengen: die belegten 43/45 werden jeweils zu 4; es gibt keinen zusätzlichen
nachgewiesenen Mengenfehler auf einer gemeinsam akzeptierten Zeile. Die 380
zusätzlichen Sichtungen wurden nicht vollständig visuell als korrekt bestätigt.

943 Zeilenentscheidungen des Zählers unterscheiden sich. Davon haben 560 am
gleichen Capture/Pixelplatz denselben Gegenstand, dieselbe Rohmenge und dieselbe
Mengenregel. Hier unterscheidet sich die Zuordnung infolge der vorangegangenen
Beobachtungen. Zufällig neu erzeugte UUIDs wurden nicht direkt verglichen;
maßgeblich sind ihr erster beobachteter Capture/Slot und die Matching-Entscheidung.

Die Nettoänderung der Helmet-Buchungen lässt sich nach den jeweiligen
Zeilenkoordinaten exakt zerlegen:

| Unterschied an der jeweiligen Koordinate | Betroffene Buchungszeilen | Nettomenge Paddle − Windows |
|---|---:|---:|
| Zusätzlich akzeptierte Beobachtung | 46 | +238 |
| Gleicher Gegenstand und gleiche Rohmenge, andere Zählentscheidung | 217 | +108 |
| Geänderte Rohmenge an den beiden bekannten Fehlerstellen | 2 | −88 |
| **Summe** | **265** | **+258** |

Dies ist eine arithmetische Zuordnung, keine unabhängig addierbare kausale
Korrekturliste. Die Menge beeinflusst auch den Zeilenabgleich: Ein anderer Wert
kann sowohl seine eigene Buchung als auch spätere Track-Zuordnungen verändern.
Die +108 bestehen beispielsweise aus +488 und −380 an unterschiedlichen
Koordinaten. Mehr OCR-Beobachtungen allein erklären nicht jede einzelne Buchung.

## Nachgewiesene Fälle

### Die beiden 43/45-Lesefehler werden korrigiert

- **Frame 1532, normales Band Y224:** Windows akzeptiert `Helmet ×43`, Paddle
  liest in beiden Ansichten `Helmet ×4`. Das Originalbild zeigt 4.
- **Frame 3450, normales Band Y149:** Windows akzeptiert `Helmet ×45`, Paddle
  liest in beiden Ansichten `Helmet ×4`. Das Originalbild zeigt 4.

Die ursprüngliche Windows-Recovery reproduziert somit die bekannten Fehler
auch bei frischer Bildverarbeitung. Paddle behebt diese konkreten Lesungen;
das allein macht den gesamten Zählverlauf noch nicht genauer.

### Zusätzliche 60 durch die längere Sichtbarkeit einer alten Zeile

| Frame | Originalbild und OCR | Windows-Zähler | Paddle-Zähler |
|---|---|---|---|
| 1641 | Neue helle einzelne Helmet-×60-Zeile | +60, neue ID | +60, neue ID |
| 1642 | Dieselbe ortsfeste Zeile | bestehende ID, Tag 2 | bestehende ID, Tag 2 |
| 1643 | Dieselbe Zeile stark verblasst | bestehende ID, Tag 3 | bestehende ID, Tag 3 |
| 1644 | Dieselbe Zeile nahezu unsichtbar | keine akzeptierte Beobachtung | erneut +60, neue ID, Tag 1 |
| 1645 | Zeile verschwunden | keine Beobachtung | keine Beobachtung |

Paddle liest in Frame 1644 in beiden Ansichten weiterhin korrekt 60
(Konfidenz jeweils ca. 0,9595). Der Gegenstand wurde jedoch schon in 1641
gezählt. Die Originalbilder zeigen keine neue Zeile oder Verschiebung,
sondern ihr Ausblenden. `overlap-rejected-by-frame-tags` und der Übergang
**3 → 1** führen zur zweiten Buchung. Diese zusätzlichen **60 sind eine
bestätigte Doppelzählung**, kein OCR-Mengenfehler.

Der historische Textfilter beendete hier die Beobachtung vor der Erneuerung.
Paddles höhere Lesbarkeit verlängert die Beobachtung derselben alten Zeile
über diese Grenze hinaus. Die Zählheuristik muss diese andere Sichtbarkeitsdauer
berücksichtigen, bevor ein primärer Leserwechsel veröffentlicht werden kann.
Andere Mehr- oder Minderbuchungen sind ohne eigenen Bildbeleg nicht pauschal
als Doppelzählung oder verlorener Drop einzustufen.

## Laufzeit und Verifikation

| Messwert für alle 4.655 Frames | Windows-primär | Paddle-primär |
|---|---:|---:|
| Gesamtlaufzeit | 199,98 s | 1.031,94 s |
| CPU-Zeit | 266,98 s | 2.027,86 s |
| Durchschnittliche Analyzer-Zeit je Frame | 33,94 ms | 212,54 ms |
| Windows-OCR-Aufrufe | 42.204 | 37.795 |
| Alle vom Analyzer erfassten OCR-Aufrufe | 43.118 | 103.705 |
| Höchster Working Set | ca. 402 MiB | ca. 474 MiB |
| Beobachtete OCR-Fehler/Exceptions | 0 | 0 |

Die Läufe sind sequenzielle Bildverarbeitungen ohne künstliche Capture-Pausen,
kein isolierter Hardware-Benchmark und kein im Spiel gemessener FPS-Test.
Paddle reduziert hier die Windows-Aufrufe nur begrenzt, weil viele Bänder
abstinent bleiben und weiterhin die vorhandenen Fallbacks auslösen.

Zentrale Prüfung nach Implementierung: **3.358 Tests erfolgreich** über App,
Core und OCR. Dazu gehören Primär-/Fallback-Routing, beide Bildkanäle, zuvor
als leer maskierte Bänder, Geometrie, Konsens, Mengenpolitik, Schutz vor späterem
Überschreiben, Sprache, Lebenszyklus und Cancellation. Die erfolgreichen Tests
ersetzen nicht die hier nachgewiesene Genauigkeitsgrenze des unveränderten
Zählers.

## Lokale Belege und Folgerung

Die großen Laufartefakte bleiben lokal unter `artifacts/paddle-primary-qa/`:

- `windows-full/` und `paddle-full/`: `summary.json`, `manifest.json`, neue
  `observations.jsonl` und `frames.jsonl`.
- `comparison/comparison.md` und `comparison/summary.json`: Gesamtauswertung.
- `comparison/focus-context.json`: Beobachtungen und Zählerentscheidungen an
  den bekannten Stellen, einschließlich 1641–1644.
- `comparison/windows-to-paddle-observation-changes.json` und
  `comparison/windows-to-paddle-counter-changes.json`: vollständige Unterschiede.
- `comparison/normal-counting-timeline.csv`: Buchungen dem ursprünglichen
  Capture zugeordnet; `booked-event-timeline.csv`: tatsächlicher Batch-/Flush-Zeitpunkt.
- `comparison/compare.py`: erneuter read-only Vergleich abgeschlossener Läufe.

Die ursprünglichen PNGs bleiben in der Benutzerdiagnose. Ein Counter-only-Replay
kann die neuen Journale anschließend prüfen, ersetzt aber keine frische OCR.

Für den nächsten Ansatz bleibt Windows primär. Paddle soll gezielt verdächtige
Trashmengen überprüfen, statt die Sichtbarkeit sämtlicher älterer Zeilen zu
verlängern. Auch dabei dürfen echte große Mengen nicht pauschal durch einen
typischen kleinen Wert ersetzt werden; die Entscheidung braucht plausible,
konkret gelesene Alternativen und einen eigenen Vergleich gegen diesen Korpus.
