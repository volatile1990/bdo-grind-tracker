# CPU-Optimierungen vom 21.09.2026

Umsetzung der priorisierten Maßnahmen aus dem [CPU-Audit](D:/Projects/bdo-grind-tracker/docs/CPU_AUDIT_2026-09-21.md).

## Änderungen

- Der BGR-Decoder kann einen rechteckigen Bereich direkt aus dem Bitmap lesen. Normales und seltenes Lootpanel sowie der konfigurierte Buffbereich werden vor der Konvertierung ausgeschnitten. Primäre OCR, Recovery und Paddle-Review verwenden dieselben Pixel und Koordinaten. Die vollständige Spielaufnahme bleibt für alle anderen Module und Diagnosen verfügbar.
- Agris decodiert bei bereits bekannter Position nur den vorhandenen lokalen Suchbereich samt vollständigem Verifikationsrand. Bei fehlendem Treffer bleibt die globale Suche erhalten.
- Windows-OCR verwendet pro Engine/Sprache einen exakten Cache mit höchstens 32 Einträgen und 4 MiB Datenbudget. Größe, Quelltyp und sämtliche sichtbaren Gray8-Pixel müssen übereinstimmen. Text und Wortgeometrie sind unveränderlich; fehlerhafte Geometrielesungen werden nicht gespeichert. Abbruch, Timeout und Lebensdauer laufender nativer Erkennungen behalten ihre Schutzmechanismen. Jeder Frame durchläuft weiterhin die Zählung mit seinem aktuellen Zeitpunkt und seinen aktuellen visuellen Belegen.
- Buff- und Lootscroll-Erkennung verwenden begrenzte Caches für skalierte unveränderliche Vorlagen. Jede Vorlage behält höchstens 96 Größenvarianten bzw. 512 KiB je Cache. Temporäre Mat-Ansichten bleiben durch Referenzzählung auch nach Verdrängung gültig. Suchgrößen, Farbbewertung, globale Mehrdeutigkeitsprüfung und Timerprüfungen bleiben erhalten.
- Für den gesamten Buffbereich wird zusätzlich genau ein Iconmatching-Ergebnis samt eigener Pixelkopie von höchstens 4 MiB gespeichert. Nur exakt gleiche Pixel, Bildtyp, Größe und erwartete Iconbreite erlauben Wiederverwendung. Schon ein geändertes Timerpixel löst die reguläre Suche aus. Timerlesung, neue Aufnahmezeit, Klassifizierung und Verbrauchszählung laufen weiterhin bei jedem Scan.
- Buffbuchungen und Dropverlauf liefern unveränderte, schreibgeschützte Snapshots erneut zurück. Neue Buchungen, Unterbrechungen, Wiederherstellung, Sessionwechsel und zurückgesetzte Zeit invalidieren die betroffenen Daten. Der vollständige Dropverlauf muss nur bei tatsächlicher Zeitkorrektur rückwärts nach späteren Drops durchsucht werden.
- Overlay-Lootlisten, Verbrauchsanzeige und Chartdaten werden bei unveränderten Eingangsdaten wiederverwendet. Preise, Steueroptionen, Favoriten, Sprache, neue Drops und Korrekturen aktualisieren weiterhin die Projektionen. Die Tagesgewinnprojektion hängt am gespeicherten Verlauf und der lokalen Zeitzone. Die Uhr läuft unabhängig davon weiter.
- Die Loottabelle berechnet ihre Zeilen einmal pro Render. Native Overlays vergleichen stabile Datenlisten direkt und berücksichtigen die tatsächlichen Verbrauchsitems, auch wenn deren Gesamtkosten gleich bleiben.
- Neue Lootereignisse durchsuchen nicht mehr den gesamten Rotationszeitstrahl. Ein Index hält den letzten Eintrag je Ereignis-ID und bewahrt die Korrekturverkettung auch nach Wiederherstellung.
- Leere dunkle normale Lootzeilen benötigen keine HSV-Konvertierung mehr; die bestehende Helligkeitsprüfung entscheidet vorher über den Abbruch.

Die Aufnahme- und HUD-Intervalle wurden nicht geändert. Es gibt keine neue Nutzeroption und keinen Prozess-weiten OpenCV-Threadschalter. Alle Erkennungsvarianten und Fallbacks bleiben aktiv. Die Persistenzformate und das Sicherungsintervall sind unverändert.

## Prüfung

Erfolgreiche Release-Testläufe:

| Bereich | Erfolgreich | Ausgelassen |
| --- | ---: | ---: |
| OCR einschließlich neuer ROI-/Cachetests | 413 | 0 |
| Core vollständig | 1.204 | 0 |
| Core-Buffs nach zusätzlicher Absicherung gegen Snapshotzugriff im Preisresolver | 111 | 0 |
| App: Erkennung, Buffs, Agris, Lootscroll und neue Projektionstests | 6.502 | 0 |
| App: Overlay, Verlauf, Rotation, Capture, Lifetime und Source-/Fade-Integration | 1.511 | 2 |
| Browser-UI vollständig | 621 | 0 |
| App: abschließende Buffprüfung einschließlich exaktem Iconmatching-Cache | 44 | 0 |

Überlappende Testläufe sind nicht als zusätzliche unabhängige Fälle zu zählen. Zusammen wurden 10.054 unterschiedliche Testnamen erfolgreich geprüft. Die beiden ausgelassenen Rotations-Video-Replays benötigen externe Aufzeichnungen; die übrigen Rotationsprüfungen liefen erfolgreich.

Neue Tests prüfen insbesondere bytegleiche Ausschnitte bei 24-/32-Bit-Bildern, Alpha und negativem Zeilenabstand, Pixelbesitz nach Freigabe des Quellbilds, identische Normal-/Rare-Panelpixel, Cachegrenzen und Speicherlebensdauer, einzelne geänderte Bildbytes, OCR-Wortgeometrie und Abbruch auf Cachehits. Weitere Prüfungen sichern Agris an Bildrändern und nach Positionsänderung, zusätzliche konkurrierende Lootscroll-Symbole, Rewind, Preis-/Steuer-/Sprachänderungen sowie die Korrekturverkettung im Rotationsverlauf ab.

## Vergleichsmessungen

Die beim ursprünglichen Audit gesicherten Release-DLLs wurden mit dem optimierten Stand in identischen, nacheinander ausgeführten Probeprogrammen verglichen. CPU-Zeit umfasst alle Threads des jeweiligen Probeprozesses. Die Maschine und das OpenCV-Threadbudget blieben gleich; andere Tests oder Builds dieses Tasks liefen während der Messungen nicht.

| Teilpfad und Daten | CPU je Aufruf vorher → nachher | Laufzeit vorher → nachher |
| --- | ---: | ---: |
| Produktive normale Loot-Pipeline, 48 Frames mit wiederholten echten HDR-Zeilen und Leerphasen | 35,807 → 3,581 ms | 7,314 → 2,815 ms (Mittel) |
| Vollständiger Buffscan, 25 exakt gleiche Buffleisten nach Warmup | 603,125 → 1,250 ms | 175,297 → 0,6942 ms (Median) |
| Agris, bekannte Position auf 4K | 83,984 → 23,438 ms | 27,228 → 23,960 ms (Median) |
| Lootscroll-Gauge auf 4K | 1.432,292 → 1.432,292 ms | 1.460,450 → 1.492,930 ms (Median) |
| AP/DP, identische vorbereitete Bilder | 20,833 → 5,208 ms | 24,267 → 3,671 ms (Median) |
| Overlay-Projektion, 5.000 unveränderte Drops bei laufender Uhr | 0,922 → 0,0156 ms | 0,5893 → 0,0086 ms (Median) |

Der Loot-Vergleich durchläuft die echte Normalpipeline einschließlich Windows-OCR, Lifetime-Zählung und visueller Belege. Alle 48 Ergebnisdatensätze stimmen überein; beide Versionen zählen sechs getrennte Drops mit zusammen 180 Helmen. Dieser Fall enthält weder Rare-Loot noch Recovery/Paddle. Die 15 Ergebnisstrings der ursprünglichen HUD-/Overlay-Vergleiche stimmen ebenfalls überein, einschließlich Buffidentitäten, Restzeiten und OCR-Wortgeometrie.

Die abschließende Zusatzprobe für den exakten Buffcache liefert dieselben zehn Kandidaten und sechs angenommenen Buffs samt Restzeiten. Sie bildet den Sonderfall perfekter Bild- und OCR-Cachetreffer ab: Animationen, bewegte Hintergründe oder auch nur ein geändertes Timerpixel erfordern erneut das volle Iconmatching. Die Timer-Recognizer-API wurde weiterhin zwölfmal je Scan aufgerufen.

Beim Gauge wurde kein CPU-Gewinn gemessen; die verwalteten Allokationen sanken von 609.394 auf 467.226 Byte je Aufruf. Die kalte globale Agris-Suche blieb ebenfalls ungefähr gleich teuer. Die größten gemessenen Verbesserungen betreffen wiederholte Arbeit bei unveränderten Eingaben und das direkte Decodieren kleiner Bildbereiche.

Diese kurzen Messreihen verwenden vorhandene echte Bildausschnitte auf synthetischen 4K-Bildern oder vorbereitete UI-Daten. Identische Wiederholungen begünstigen die Caches; die Loot-Schleife enthält keine echten 200-ms-Pausen. Hintergrundlast und OpenCV-Threadkosten beeinflussen das Ergebnis. Einzelne sehr kleine CPU-Werte liegen unter der Zeitauflösung. Teilpfade überlappen und dürfen nicht addiert werden.

Damit ist **keine Gesamt-App-CPU-Ersparnis im laufenden Spiel gemessen**. Diese hängt unter anderem von Auflösung, Bildbewegung, Cachetreffern und Sessionlänge ab. Eine belastbare Live-Gegenprobe benötigt die neue EXE unter vergleichbarer Spielsituation.

Reproduzierbarer Quellcode, Rohdaten, DLL-Hashes und genauere Grenzen:

- [HUD-/Overlay-Vergleich](../.artifacts/cpu-optimization-2026-09-21/benchmark-report.md)
- [Loot-Pipeline-Vergleich](../.artifacts/cpu-optimization-2026-09-21/loot-pipeline/RESULTS.md)
- [Zusatzvergleich für identische Buffleisten](../.artifacts/cpu-optimization-2026-09-21/identical-buff-report.md)

## Lokales Testpaket

Gebaut am 21.09.2026 um 20:27 Uhr als eigenständiges Windows-x64-Release mit .NET 9.0.20:

[`Grindcrest.exe`](../artifacts/local-test/Grindcrest-cpu-optimized-20260921-202740/Grindcrest.exe)

Die EXE zusammen mit den zugehörigen Dateien im Ausgabeordner verwenden. Die Prüfung aller drei mitgelieferten .NET-Laufzeiten, `--startup-smoke-test` und `--ui-smoke-test` waren erfolgreich (je Exitcode 0). Die Smoke-Tests verwenden getrennte temporäre Appdaten. Der laufende Tracker wurde nicht ersetzt und keine laufende Session angehalten.

Paketpfad, EXE-SHA-256 und Smoke-Ergebnisse stehen in [package.json](../.artifacts/cpu-optimization-2026-09-21/package.json); Publish- und Smoke-Protokolle liegen daneben. Das lokale Paket wurde nicht veröffentlicht.

## Noch getrennt zu bewertende Maßnahmen

Ein globales OpenCV-Threadlimit, ONNX-Spinning, gemeinsame Rotations-OCR zwischen verschiedenen Profilen und eine Aufteilung der Verlaufsdatei sind nicht Teil dieser Änderung. Diese Eingriffe betreffen gemeinsame Laufzeitbudgets, Profil-Lebensdauer oder Wiederherstellung und benötigen eigene End-to-End-Vergleiche. Die im Audit gemessene günstige DXGI-/HDR-Metadatenabfrage wurde nicht umgebaut.

Testprotokolle und Vergleichsprogramme liegen unter `.artifacts/cpu-optimization-2026-09-21/`; die ursprünglichen Auditmessungen wurden erhalten.
