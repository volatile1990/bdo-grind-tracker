# BDO-Companion-0.7.4-Paritätsnachweis

Nachweis des Companion-basierten Ports bis 0.5.1 und seiner Wiederherstellung in
0.6.2. In 0.6.0/0.6.1 wurden Matching und zeitliche Zählung durch eigene Regeln
ersetzt; diese führten im berichteten Live-Einsatz zu zu vielen verworfenen Drops.
Ab 0.6.2 sind die nachfolgend beschriebenen ursprünglichen Reconciler und der
globale Matcher wieder der aktive Erkennungspfad. Zusätzlich bleibt ein eigener,
nachgelagerter Spotfilter mit automatischer Erkennung am Trashloot erhalten.
Die aktuelle Architektur und die bewusst verbleibenden Abweichungen stehen in
[ANALYSIS.md](ANALYSIS.md). Vollständige Produktparität wird nicht behauptet.

## Wiederherstellung des eigenen 0.5.1-Stands in 0.6.2

Die ausgelieferten Dateien `artifacts/current/BdoGrindTracker.dll` und
`artifacts/current/BdoGrindTracker.Core.dll` waren beim Rückbau noch aus dem Stand
0.5.1 erhalten. Die App-Assembly weist diesen Stand in ihren Versionsmetadaten aus.
Die Core-Assembly wurde gegen den bereits dokumentierten SHA-256-Wert geprüft:
`8978479DDAC7705291AF0FF3D86C58A6A90A9FD8B596C0789AE172877D65AB2B`.
Mit ILSpy wurde daraus der eigene frühere C#-Code wiederhergestellt; es wurde
keine neue Erkennungsheuristik aus Vermutungen über BDO ergänzt.
Die OCR-Assembly in diesem Ordner war bereits durch eine 0.6.x-Ausgabe ersetzt;
sie wird ausdrücklich nicht als unveränderte 0.5.1-Binärreferenz ausgegeben.

Der reproduzierbare Offline-Differenztest vergleicht den wiederhergestellten Core
mit dieser hashgeprüften Referenz. Beim Rückbau bestanden 50.836 Einzelvergleiche
für Matcher, normalen Abgleich und Rare-Abgleich einschließlich gemeinsamem Ledger.
Der Test lädt ausschließlich die eigene Referenz-Assembly; er startet kein Spiel
und nimmt keine Bildschirminhalte auf:

```powershell
dotnet run --project tools/CompanionBaselineParity -c Release -- artifacts/current/BdoGrindTracker.Core.dll
```

Der Hashcheck verhindert, dass eine später ersetzte Datei versehentlich als
Referenz verwendet wird. Die Vergleiche belegen Gleichheit der geprüften Core-
Fälle, nicht eine bestimmte reale OCR-/Grind-Trefferquote.

Der aktive Pfad verwendet wieder die globale Namensauflösung, Text-/Mengenregeln,
normalen und Rare-10-Frame-Abgleich sowie das gemeinsame Korrektur-Ledger dieses
Stands. Die eigene Lebensdauer-Zuordnung und zusätzlichen Bestätigungsgates aus
0.6.x sind entfernt. Die native Bildaufbereitung und die in 0.5.1 nachgewiesenen
Korrekturen bleiben erhalten. Der automatisch erkannte Spotfilter ist ausdrücklich
eine nachgelagerte Produkterweiterung, kein Bestandteil des nativen Nachweises.

Die UI bleibt von der Capture-Schleife entkoppelt; das optionale Live-Log bleibt
begrenzt und standardmäßig aus. Die hier genannten nativen Adressen und alten
Live-Crop-Ergebnisse sind historische Untersuchungsbelege, keine neue Messung der
Grind-Genauigkeit von 0.6.2. Auch die kleinere Überzählung aus 0.5.1 kann zurückkehren.

## Untersuchte Installation

Die Analyse bezieht sich ausschließlich auf die am 2026-09-04 lokal vorgefundene,
signierte Installation unter `C:\Program Files\BDO Companion`:

| Datei | Version | Größe | SHA-256 |
|---|---:|---:|---|
| `bdo_companion.exe` | 0.7.4 | 29.671.952 Byte | `8B75E114D3D33D227A01CDFE592F13AA65133A36363EAA2E6A82E5ADFC77EFCF` |
| `app_lib.dll` | — | 6.347.792 Byte | `19BD93D5FFDAADD3EC1ED9E61C89CC7E94A9567283A47BD64931049817CB6B62` |

Die Authenticode-Signatur der EXE war gültig und auf `IQON Digital LLC` ausgestellt.
Die Dateien wurden nur gelesen; BDO Companion wurde für die Untersuchung weder gestartet
noch verändert.

## Werkzeug und Methode

Die Binärdatei wurde mit einem Ghidra-SLEIGH-basierten Rizin/Cutter-Workflow untersucht:
PE-Metadaten, Strings, Importpfade, x64-Disassembly und gezielte Kontrollfluss-/
Datenflussverfolgung. Ergänzend kamen Capstone und Microsoft `dumpbin /DISASM:BYTES`
für reproduzierbare Instruktionsadressen zum Einsatz.
Laufzeitlogs wurden nur als zusätzlicher Plausibilitätsbeleg benutzt; implementiert wurde
nur statisch nachvollzogenes Verhalten.

`tools/Inspect-CompanionCode.py` stellt die adressgenaue, rein lesende Prüfung bereit
(Python mit `pefile` und `capstone`). `--function` nutzt die PE-Exceptiontabelle für
korrekte Funktionsgrenzen; `--callers` und `--references` helfen beim Datenflussabgleich.
Referenzkandidaten müssen an der tatsächlichen Instruktion gegengeprüft werden.

## Zentrale statische Anker

| Bereich | Nachweis in 0.7.4 | Portierung |
|---|---|---|
| Zeilenworker | Funktion bei `0x14016BB80` | `CompanionNormalRowProcessor`, `CompanionRareRowProcessor` |
| Normale Template-Ladestelle | Loaderaufruf bei `0x1406C022B`, Zielhöhe 24 | `CompanionDigitTemplateLoader.LoadNormalQuantity` |
| Normaler Worker-Modus | Aufruf bei `0x1406B5B85`, Modus 0 | feste sechs Zeilen, Mengen-ROI |
| Zweiter Worker-Modus | Aufruf bei `0x1406B5DE9`, Modus 1 | vollständige Zeile, keine Template-Menge |
| Modus-1-Aktivierung | Flag aus Kalibrierung `+0x58` | sichtbares `UIData 161` |
| Modus-1-Anker | Felder `+0x5C/+0x60` | `RareLootAnchorX/Y` |
| UIData-Parser | `0x1406EC37A..0x1406EC6A7` | RelativePosX/Y und exakt `IsShow=true` |
| UIData 161 | Branch bei `0x1406EFC71`, Writer `0x1406EFCEC..0x1406EFD03` | optionaler zweiter Lootanker |
| Ausgabegerät/HDR | Enumerator `0x1406C2070`, `IDXGIOutput6::GetDesc1`; Writer `0x1406DA4C0`/`0x1406D52D0` | DXGI-Auswahl und `IsHdr` pro aufgenommenem Frame |
| HDR im Zeilenworker | Reads bei `0x14016C10E` und `0x14016C24D` | getrennte native Maskenzweige beider Modi |
| Ziffernressourcen | drei unabhängige Dispatch-Tabellen mit je `0.png`–`9.png` | 30 byteidentische PNGs mit RVA- und Hash-Provenienz |
| Batchgröße | `update_frequency` bei `0x1414EDE0D`, Default 10 bei `0x1406AD955..95E`; Vergleich des Framefortschritts bei `0x1401AA58C..5A3` | Companion-Default 10; getrennte Framefolgen beider Modi |
| Gemeinsame Voranalyse | `FUN_1401AFE90`; Modus-0-Aufruf `0x1401AFF25`, Modus-1-Aufruf `0x1401AFF33` | identische Reparatur-/Überlappungsfolge, getrennte Framefolgen |
| Reihenfolge im normalen Modus | Vorbereitung aufsteigend nach Y: `0x1406D5390`, Aufruf `0x1406D100E..1021`; Sentinel-Präfix `0x1406CC604..631`; Umkehr der akzeptierten Einträge `0x1406D06E2..077C` | OCR/Präfixfilter aufsteigend, erst vor dem Frame-Abgleich absteigend |
| Frame-Tag-Austausch | `0x1401BBAF0` erhöht den aktuellen Zielindex; `0x1401BBB77..7A` begrenzt den Vergleich an diesem Index | alle vorherigen Einträge des rechten Frames, auch bereits bearbeitete Überlappungszeilen |
| Geometrische Lückenreparatur | `0x1401C07A1` prüft Y+50; `0x1401C07E7` schreibt nach einem Treffer den aktuellen Y-Wert zurück | `lastY` auch im regulären Trefferzweig fortschreiben |
| Mengen-1-Filter | `FUN_1401C2B70`; Aufrufe `0x1401AE340`, `0x1401AEA4F`, `0x1401AF787` | dieselben 23 ordinalen Namen vor und nach Katalogauflösung |
| Modus-1-Zustand | `FUN_1401BD490` und `FUN_1401BD710` | Frameindex, 12-Frame-Fenster, Support, Alias-/Konfliktkorrektur |
| Nativer Ergebniskanal | Konstruktion `0x1406B3C44..0x1406B3D9B`, Kapazität `0x20`; Sendepaket `0x1406D0963..097E` enthält erkannte Einträge | keine Bitmap-Queue daraus ableiten; Tracker verarbeitet Aufnahmen seriell |
| Aufnahmeintervall | Duration `450.000.000 ns` bei `0x1406D0C83..92`; Aufnahmezeitpunkt `0x1406C6CA1`; Deadline `0x1406C729B`; Restwarten `0x1406C72DB..730F` | 450-ms-Mindesttakt ab Aufnahmeende, OCR-Zeit angerechnet, kein Screenshot-Rückstau |

Die Konstanten 125, 150 und 260 für den zweiten Ausschnitt wurden unmittelbar aus den
referenzierten Maschineninstruktionen rekonstruiert. Für den normalen linken Rand wurde
entsprechend 165 verifiziert.

## Nachprüfung der Mehrfachzählungen (0.5.1)

Die erneute Prüfung korrigiert vier Abweichungen der bisherigen Portierung:

- Vor dem Abgleich fehlte die Umkehr der akzeptierten normalen Einträge. Der native
  Präfix-/Suffixabgleich erhält die Liste von unten nach oben, nicht die aufsteigende
  Reihenfolge des vorgeschalteten Sentinel-Filters.
- Beim Austausch der Frame-Tags wurde nur der neue Präfix geprüft. Native reicht die
  Schleife bis zum jeweils aktuellen Zielindex, einschließlich früherer Overlap-Zeilen.
- Nach einer regulär passenden Zeile fehlte in beiden Modi das Fortschreiben von
  `lastY`, wodurch die anschließende Reparatur einer fehlenden Zeile scheitern konnte.
- Die Aufnahme konnte während langsamer OCR weitere Bitmaps produzieren. Native
  arbeitet den Frame fertig ab und wartet nur die verbleibende Zeit bis zur nach
  der Aufnahme gesetzten 450-ms-Deadline. Die alte Bitmap-Queue wurde entfernt.

Zusätzlich behandelt die Sitzungsanzeige negative Korrekturen nicht mehr als neue
Logeinträge und entfernt auf null korrigierte Itemarten aus ihrer Summenliste. Native
zieht `FUN_1401BD710` genau eine Einheit ab und entfernt den Datensatz beim Erreichen
von null; es wird kein positiver Drop erzeugt.

Der Frame-Tag-Zyklus 1/2/3/1 ist bei `0x1401BBB3E..4F` tatsächlich Bestandteil des
Originals und wurde nicht durch eine eigene Lebensdauer-Heuristik ersetzt. Im normalen
Produzenten wurde kein Vergleich mit vorherigen Screenshotinhalten gefunden: nach dem
Aufbau der aktuellen Liste folgt der Sendepfad bei `0x1406D0963..097E`. Der Wert von
450 ms bleibt unverändert; korrigiert wurde dessen Einbindung in den Aufnahmezyklus.

Die bisherige Batch-Belegstelle `0x1406B5A01` war falsch zugeordnet: sie subtrahiert
zehn Sekunden von einem Zeitwert, nicht zehn Frames. Der Default 10 wurde stattdessen
über den Konfigurationsleser, den Analyse-Threadparameter und den tatsächlichen
Framefortschrittsvergleich verifiziert (Adressen in der Tabelle).

Diese Nachweise und deterministische Regressionen belegen die korrigierten Fälle;
sie sind keine Garantie für eine vollständige fehlerfreie Live-Erkennung. Für diese
Nachprüfung wurde weder BDO bedient noch eine Live-Sitzung gestartet.

## Übernommene Verhaltensblöcke

- Profil-, Datei-, Auflösungs-, Skalierungs-, Schrift- und UIData-Auswahl;
- feste Normal- und Modus-1-Geometrie;
- Zeilenscheduling, getrennte SDR-/HDR-Masken, Schwellwerte, Größenänderung und Leergates;
- Ziffernlader, ziffernspezifische Schwellen, Template-Matching und Gruppierung;
- Windows-Media-OCR, Wortgeometrie und Textnormalisierung;
- bytebasierter Katalogmatcher mit Grenzwert 0,34 sowie die zusätzlichen
  Substring-, Zubehörpräfix- und Runner-up-Regeln von Modus 1;
- getrennte 10-Frame-Reconciliation beider Modi einschließlich Mengenreparatur und
  Überlappungslogik;
- Modus-1-Aktualitäts-/Supportzustand und dessen Korrekturen gegen dieselbe
  Sitzungssumme, in die Modus 0 schreibt;
- DXGI Desktop Duplication einschließlich Ausgabegeräte- und HDR-Ermittlung;
- serieller Aufnahme-/OCR-Zyklus mit 450-ms-Deadline und angerechneter Analysezeit.

Jeder Block besitzt fokussierte Regressionstests. Die Assettests prüfen alle drei
vollständigen 0–9-Tabellen gegen die statisch dokumentierten PNG-Hashes und Abmessungen.
Die zwei vom Nutzer bereits diagnostizierten 4K-HDR-Zeilen wurden außerhalb des
Produktbuilds erneut durch die exakte Pipeline geführt: beide ergaben
`Black Crystal Fragment ×6` mit Templatewerten 0,9380 und 0,9022.

## Nicht übernommen

Es wurden keine ausführbaren Dateien, internen Datenbanken oder dekompilierten
Quelltextteile aus BDO Companion übernommen. Namen und Struktur der C#-Implementierung
sind eigenständig. Für die verlangte exakte Mengenerkennung sind die 30 Ziffern-PNGs
byteidentisch eingebettet und einzeln auf die oben festgelegte EXE zurückführbar.
