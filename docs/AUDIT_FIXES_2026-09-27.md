# Grindcrest – Korrekturen zum Audit vom 27.09.2026

Die Änderungen bauen auf dem bereits vorhandenen Arbeitsbaum von Version 1.13.0 auf. Bestehende lokale Änderungen wurden beibehalten; es wurde kein Commit erstellt. Der [Gesamtaudit](D:/Projects/bdo-grind-tracker/docs/AUDIT_2026-09-27.md) und seine Detailberichte beschreiben den Zustand vor diesen Korrekturen. Dieser Bericht dokumentiert die Umsetzung und ihre anschließende Prüfung.

## Behobene Bugs

| Audit | Änderung | Regression |
| --- | --- | --- |
| B01 | Shutdown zieht abschließenden Leerlauf wie Pause ab; bereits erfasster Loot und frühere aktive Segmente bleiben erhalten | Shutdown nach mehreren Segmenten, Pause-/Shutdown-Parität und Wiederherstellung |
| B02 | Historische Sessions unterstützen die Wahl einer kompatiblen Spotvariante unter Verlauf → Bearbeiten | Floodlands, Dehkia und Winter Tree; aktuelle Session bleibt unverändert; Upload-, Journal- und Speicherfehlerguards |
| B03 | XP/AP/DP und automatische Klasse verwenden die gebundene Loot-Konfiguration; veraltete Klassenerkennung wird beim Konfigurationswechsel verworfen | Unterschiedliche Profile, externe/ausgewählte XML, widersprüchliche Datei-/Ordnerzeiten und laufender Klassenreader |
| B04 | Nativer Chartcache vergleicht Rotationsereignisse und Specialmarker nach Inhalt | Gleiche Eventzahl mit geändertem Zeitpunkt/Inhalt sowie gleiche Inhalte in neuen Snapshotobjekten |
| B05 | Initiale Bannerprüfung erhält Abbruchtoken und ein Zeitbudget; native Arbeit behält Bildpuffer bis zum tatsächlichen Ende | Abbruch, ignorierender Reader, Zeitbudget, Dispose und anschließende Pufferfreigabe |

Historische Varianten werden über einen separaten, ausdrücklich beschrifteten Übernehmenbutton gespeichert. Das ändert keine ungespeicherten Mengen-/Klassenfelder. Übertragene oder gesperrte Sessions und offene Uploads bleiben geschützt; vorhandene Garmoth-Einträge werden nicht automatisch geändert. Im Browser wurde eine bestehende Floodlands-Session auf Orbita umgestellt: Mengen und Klasse blieben erhalten, das anschließende Speichern führte zur passenden Spotroute. [Bildnachweis](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/historical-variant.png).

## Behobene UI-Probleme

| Audit | Änderung |
| --- | --- |
| U01 | Capture-Dateikarte, automatische Auswahl und Dateitabelle verwenden Themefarben für Fläche, Text und Rand |
| U02 | Lootspalten stehen vor XP, AP/DP und Verbrauch; Favoriten werden ohne benutzerdefinierte Reihenfolge nach Trash priorisiert; ein Detailsbutton zeigt Sessionkontext direkt |
| U03 | Timeline bietet sichtbare Zoom-/Verschiebebuttons, Tastatursteuerung und eine paginierte Tabelle exakter Werte |
| U04 | Ungeklärte Spotnamen und Garmoth-Handlungsaufforderungen folgen der UI-Sprache; Hinweise führen zur betreffenden Session |
| U05 | Verlauf nennt aktuelle Preise/Steuern; Goals benennt gespeicherte Silberwerte im Kalender und in der Tagesübersicht sowie aktuelle Preise auf der Lootebene; Share-Bilder nennen ihre Preisgrundlage |
| U06 | Kalendertage haben vollständige zugängliche Datumsnamen; Overlaymodule lassen sich mit Enter/Leertaste auswählen |
| U07 | Share-Ladezustand verwendet Themefarben; Setup unterscheidet besuchte Schritte von einer tatsächlichen Erfassungsprüfung und bietet einen Rücksprung |

Die Timeline unterstützt Plus/Minus, Pfeil links/rechts und Pos1. Normales Scrollen, Tab und Browser-Zoom bleiben verfügbar. Die alternative Tabelle zeigt ungekürzte Dropmengen, Zeitabschnitte und Rotationsphasen mit Zeitangaben bis auf Millisekunden und 25 Zeilen je Seite. Die Setup-Einrichtung bleibt ohne Spiel abschließbar; besuchte Schritte behalten ihre Nummern, während der Abschluss den noch ausstehenden Erfassungscheck ausdrücklich nennt.

Die Verlauftabelle behält sämtliche Loot- und Metadatenspalten. Die neue Reihenfolge und aufklappbare Details verbessern die Ausgangsansicht; bei vielen Items bleibt horizontales Scrollen notwendig. Es wurde keine allgemeine Spaltenauswahl eingeführt.

## Weitere Korrekturen

- Overlay-Geometrie wird auch während eines laufenden Trackingbefehls gespeichert.
- Fehlgeschlagener Capture-Ausschluss wird begrenzt erneut versucht; nur Erfolg gilt als angewendet.
- Ein Bitmap im alternativen DXGI-Screencapturepfad wird bei fehlgeschlagenem Frameabschluss freigegeben.
- Die Browser-Vorschau verwendet direkt einen ephemeren DataProtection-Provider.
- Die Gültigkeitsdauer eines erkannten Autostart-Banners verwendet monotone Zeit und bleibt bei einer Systemuhrkorrektur stabil.

## Automatisierte Verifikation

| Prüfung | Bestanden | Übersprungen | Fehlgeschlagen |
| --- | ---: | ---: | ---: |
| OCR | 427 | 0 | 0 |
| Core | 1.267 | 0 | 0 |
| BrowserPreview | 822 | 0 | 0 |
| Windows-App | 10.793 | 2 | 0 |
| **.NET gesamt** | **13.309** | **2** | **0** |
| JavaScript/UI | 66 | 0 | 0 |

Die gemeinsame Solution-Suite ist grün. Die beiden ausgelassenen Rotation-Replaytests benötigen externe Aufnahmen, die für diesen Durchlauf nicht vorhanden sind. Ihre native Video-/Screenshotwiedergabe ist damit weiterhin ungeprüft. [Testprotokoll](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/final-dotnet-tests.log), [Zusammenfassung](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/test-summary.json), [TRX-Ergebnisse](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/final-test-results).

Nach der letzten Ergänzung des Preisgrundlagenhinweises im Kalender wurden die BrowserPreview-Tests erneut ausgeführt: **822 von 822 bestanden**, der Build meldete **0 Warnungen und 0 Fehler**. Dieser Wiederholungslauf ist zusätzlich zur Gesamtzahl oben und erhöht sie nicht. [Abschließender UI-Testlauf](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/final-ui-tests.log), [Browser-Build](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/browser-build.log).

Der abschließende Windows-App-Build ist mit **0 Warnungen und 0 Fehlern** erfolgreich; `git diff --check` ist sauber. [App-Build](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/final-app-build.log).

Alle 66 JavaScript-Tests bestanden. Die Validatoren für die Paket-Runtime und Store-Versionen/Payloads sind ebenfalls grün. Die Runtimeprüfung bestätigt das vorhandene Paket gegen die Mindestversion 9.0.20 und verwirft die vorgesehenen veralteten beziehungsweise unvollständigen Testfälle. Das ist kein neuer MSIX-Build oder Store-Zertifizierungslauf. [JavaScript-Protokoll](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/javascript-tests.log), [Runtimeprüfung](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/runtime-validation.log), [Storeprüfung](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/store-validation.log).

## Tatsächlich geprüfte Oberfläche

Die UI-Prüfung erfolgte mit der gemeinsamen Blazor-Oberfläche in einer isolierten Browser-Vorschau und mit Beispieldaten. Es wurden keine echten Nutzersessions geändert und keine externen Uploads ausgeführt.

| Bereich | Bestätigtes Ergebnis | Nachweis |
| --- | --- | --- |
| Capture-Themes | Grindcrest, Black Desert, Light, Cats, Obsidian, Kamasylvia und Valencia erreichen auf den drei vormals problematischen Textflächen jeweils mindestens 4,5:1 Kontrast | [Messdaten](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/theme-contrast.json), [Light-Ansicht](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/light-capture.png) |
| Kleine Capture-Ansicht | Bei 860×640 kein globaler horizontaler Overflow und keine abgeschnittenen Eingaben | [Screenshot](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/capture-small.png) |
| Verlauftabelle | Bei 1280 Pixeln Fensterbreite: 983 Pixel sichtbarer Tabellenbereich; Trashspalte vollständig mit 88 Pixeln und vier weitere Lootspalten vor den Metadaten sichtbar | [Screenshot](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/history-table.png) |
| Historische Variante | Floodlands → Orbita im Edit übernommen; Mengen und Klasse unverändert; anschließendes Speichern führt zur korrekten Spotroute | [Screenshot](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/historical-variant.png) |
| Timeline | Plus setzt Zoom auf 1,25; Pos1/Home setzt zurück. Exakte Werte zeigen Millisekunden; zweite Seite von 71 ist erreichbar | [Screenshot](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/timeline.png) |
| Overlayeditor | Enter und Leertaste auf der fokussierten Modulgruppe öffnen den passenden Inspector | [Screenshot](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/overlay-keyboard.png) |
| Kalender | Zugängliche Tagesnamen enthalten das volle Datum einschließlich Jahr. Der Hinweis zur gespeicherten Preisgrundlage steht direkt unter dem Monat | [Preisgrundlagenhinweis](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/goals-calendar-price-basis.png) |
| Tagesübersicht | Gespeicherte Silberwerte und die Bewertung mit aktuellen Preisen auf der Lootebene sind jeweils ausdrücklich bezeichnet | [Screenshot](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/goals-price-basis.png) |
| Setup | Besuchte Schritte zeigen Nummern statt grüner Prüfhaken; finaler Capturestatus bleibt als ungeprüft erkennbar. „Check capture now“ führt zu Schritt 3 | [Screenshot](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/setup-readiness.png) |
| Sessionbild | „Current value“ als Preisgrundlage im generierten Live-Bild bestätigt; Escape schließt den Dialog und stellt den Fokus wieder her | [Preisgrundlage im Bild](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/session-share-price-basis.png), [Dialog](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/session-share.png) |

Im Light-Theme erreichen Dateikarte, automatische Auswahl und Tabellenheader jetzt **12,03:1 / 11,57:1 / 5,41:1**; zuvor waren es 1,39:1 / 1,34:1 / 2,64:1. Die Messung betrifft diese drei konkreten Textflächen und ist keine vollständige Kontrastprüfung sämtlicher Komponenten und Zustände.

Die erfassten Browser-Consolefehler zwischen **08:24 und 08:25 UTC** gehören zu einem Vorschau-Serverneustart. Zwischen den Serverneustarts zeigte die Browser-Console während der geprüften Abläufe keine neuen Warnungen oder Fehler.

Im Serverlog des letzten Vorschau-Neustarts ist einmal eine `AntiforgeryValidationException` dokumentiert: Der Browsercookie stammt noch vom vorherigen ephemeren Schlüssel. Nach der Wiederverbindung funktionierte die Oberfläche. Der ursprüngliche DataProtection-Keyringfehler ist behoben; der beobachtete Cookie-/Neustarteffekt bleibt ausdrücklich dokumentiert. [Protokoll der neu gestarteten Vorschau](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/final-calendar-preview.log), [vorheriges Serverprotokoll](D:/Projects/bdo-grind-tracker/artifacts/audit-fixes-2026-09-27/final-browser-preview.log).

## Offene Punkte und Grenzen

- **Zeitquellen:** Die Bannerfrische ist auf monotone Zeit umgestellt. Das Probeintervall in `RotationStartWatcher` und die Zeitstempel beziehungsweise Replayzeiten der Autostart-Erkennung verwenden weiterhin UTC-basierte Werte; diese Punkte bleiben offen. [RotationStartWatcher](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/RotationStartWatcher.cs:43), [AutomaticGrindMonitor](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/AutomaticGrindMonitor.cs:72).
- **Reale Erkennung und native Darstellung:** Kein neuer kontrollierter Ingame-Grind, keine visuelle Prüfung echter nativer Overlayfenster und keine vollständige Mehrmonitor-/DPI-Matrix. Capture-, OCR- und native Änderungen wurden durch vorhandene Fixtures und gezielte Regressionen geprüft; die Browser-Vorschau simuliert diese Schnittstellen.
- **Externe Integration:** Kein tatsächlicher Garmoth-Upload und keine Überprüfung eines externen Kontos. Die lokalen Varianten- und Uploadsperren sind automatisiert geprüft.
- **Accessibility:** Tastaturbedienung und zugängliche Datumsnamen wurden praktisch geprüft; kein vollständiger Screenreadertest und keine Barrierefreiheitszertifizierung.
- **Performance und Erweiterungen:** Die übrigen Cache-/Skalierungsverbesserungen und neuen Featureideen aus dem Gesamtaudit sind weiterhin Vorschläge. Dieser Korrekturdurchlauf belegt keinen allgemeinen CPU-/FPS-Gewinn und keine Vollständigkeit aller denkbaren Fehler.
