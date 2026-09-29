# Grindcrest – Audit Core, Session-Service und Datenintegrität, 27.09.2026

Geprüft wurde der aktuelle Arbeitsbaum einschließlich vorhandener, nicht eingecheckter Änderungen. Produktdateien wurden nicht verändert. Die Gesamt-Builds und Tests wurden vom Hauptagenten ausgeführt; dieser Teil ergänzt gezielte isolierte Reproduktionen gegen den aktuellen Debug-Build. Kein echter Garmoth-Upload, kein Zugriff auf Nutzersessions oder API-Schlüssel.

## Belastbare Befunde

| ID | Priorität | Befund | Beleg |
|---|---|---|---|
| BD01 | P2 | App-Beenden zählt abschließenden Leerlauf als aktive Grindzeit | Echter Service, gleiche Session: Pause 2 Min., Shutdown 4 Min. |
| BD02 | P2 | Abgeschlossene Sessions mit uneindeutigem Spot können nicht mehr für Garmoth aufgelöst werden | Echter Service lehnt historische Variantenwahl und Upload ab |
| BD03 | P2 | XP/AP/DP können weiterhin eine andere BDO-Konfiguration als Loot verwenden | Echte Reader: Loot Profil 111/4K/149 %, HUD 1080p/100 % |

### BD01 – App-Beenden speichert andere aktive Dauer als Pausieren

**Trigger:** Bei laufendem Tracking nach dem letzten Drop die App wirklich beenden, etwa über den Tray oder den Beenden-Dialog, bevor der nächste automatische Pause-Tick erfolgt. Der Update-Pfad verwendet dieselbe Shutdown-Routine. Ein bloßes Verstecken im Tray ist nicht betroffen.

**Reproduktion:** Eine synthetische Session mit 100 `Black Crystal Fragment` nach zwei Minuten, anschließend zwei Minuten ohne Drop. `PauseAsync()` speichert zwei Minuten; `ShutdownAsync()` speichert vier Minuten. Beide Aufrufe laufen gegen dieselben neu gebauten Produktassemblies und dieselbe Test-Fixture. Die zusätzliche Leerlaufzeit halbiert in diesem Beispiel Silber/h und betrifft auch andere Berechnungen pro aktiver Stunde und spätere Garmoth-Uploads.

**Ursache:** [TrackerSessionService.cs:946](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.cs:946) pausiert die Session-Uhr ohne Abzug; unmittelbar danach wird die Inaktivitätsuhr gestoppt. Der normale Pausenpfad bestimmt dagegen zunächst den Leerlauf und zieht ihn ab: [TrackerSessionService.cs:499](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.cs:499). Das dokumentierte Nutzerverhalten in [README.md:38](D:/Projects/bdo-grind-tracker/README.md:38) beschreibt aktive Dauer mit Abzug seit dem letzten Drop.

**Korrektur:** Bei Shutdown den gleichen atomar ermittelten Leerlauf wie bei manueller Pause von der aktuellen Uhrsegmentdauer abziehen. Bestehende Drain- und Save-Fehlerbehandlung erhalten; späte OCR darf die pausierte Uhr weiterhin nicht neu starten.

**Regression:** Gleiches Loot-/Zeitprofil für manuelle Pause, automatische Pause, echtes Beenden und Update-Shutdown vergleichen. Zusätzlich verzögerte finale OCR, Warten auf ersten Drop sowie eine bereits pausierte Session prüfen.

### BD02 – Historische Variantenwahl fehlt und blockiert den vorgesehenen Upload

**Trigger:** Eine Session an einem Spot mit gemeinsamem Trashloot abschließen, ohne zuvor in der Live-Session das konkrete Gebiet zu wählen. Beispiele sind `dark-energy-floodlands`, die noch nicht spezifizierte Dehkia-Ash-Forest-Variante und Winter Tree Fossil.

**Reproduktion:** Zwei Minuten und 100 `Tainted Armor Fragment` als `dark-energy-floodlands` gespeichert; anschließend `NewSessionAsync()`. `SelectSpotVariantAsync(alteId, "dark-energy-floodlands-orbita")` antwortet „Der Spot kann für diese Session nicht mehr geändert werden.“ Der historische Upload antwortet „… das genaue Gebiet … in der Live-Session auswählen …“. Es wird kein Request gesendet. Die alte Session ist jedoch nur noch im Verlauf vorhanden.

**Ursache:** Die Varianten-API ist ausdrücklich an die aktuelle Session gebunden: [TrackerSessionService.SpotVariants.cs:35](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.SpotVariants.cs:35). Historische Updates ändern nur Klasse und Loot: [TrackerSessionService.History.cs:217](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.History.cs:217). Der historische Editor besitzt ebenfalls keine Spotwahl: [HistoryDashboard.razor:192](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/HistoryDashboard.razor:192). Die Upload-Vorschau verweigert uneindeutige Spot-IDs: [GarmothUploadPreview.cs:48](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Integrations/Garmoth/GarmothUploadPreview.cs:48), mit Limitationstexten aus [GarmothCatalog.cs:173](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Integrations/Garmoth/GarmothCatalog.cs:173).

**Auswirkung:** Eine lokal gültige gespeicherte Session ist innerhalb der normalen App-Bedienung dauerhaft nicht uploadfähig. Loot/Klasse zu bearbeiten oder eine neue aktuelle Session anzulegen behebt die alte Spot-ID nicht.

**Korrektur:** Für noch nicht gesendete oder möglicherweise gesendete Sessions die bestehenden Schutzregeln unterscheiden: historische Variantenwahl innerhalb derselben Trash-Familie für sicher ungesendete Einträge anbieten und atomar speichern; laufende Requests, Journal-Sperren und bereits gesendete Einträge schützen. Den Uploadhinweis direkt zum historischen Editor verlinken. Alternativ beim Abschluss zwingend vorab das Gebiet auflösen, wobei ein historischer Reparaturpfad weiterhin für bestehende Daten benötigt wird.

**Regression:** Jede uneindeutige Familie abschließen, im Verlauf eine gültige Variante zuweisen und die Vorschau/den Mock-Upload prüfen. Fremde Familie, laufender Auto-Upload, möglicherweise übertragene Session und fehlgeschlagenes Speichern müssen abgewiesen werden.

### BD03 – HUD-Reader sind nicht an die ausgewählte Loot-Konfiguration gebunden

Dies ist ein **verbleibender Teil von B02 aus dem Audit vom 19.09.2026**, keine unveränderte Wiederholung des alten Befunds. Die Standard-Klassenauswahl verwendet inzwischen XML-Speicherzeiten.

**Reproduktion:** Profil 111 hat die neuere XML, 3840×2160 und 149 % Skalierung; Profil 222 den neueren Ordnerzeitstempel, 1920×1080 und 100 %. `CompanionCalibrationReader.Read(root)` liefert Profil 111/4K/149 %. `ExperienceHudConfigurationReader.Read(root)` liefert 1080p/100 %.

**Ursache:** [ExperienceHudConfigurationReader.cs:76](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/ExperienceHudConfigurationReader.cs:76) nimmt weiterhin `Directory.GetLastWriteTimeUtc`. Für XP und AP/DP werden unabhängig davon Standardreader injiziert: [TrackerSessionService.cs:128](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.cs:128). Auch ein manuell ausgewählter `CaptureConfigurationPath` wird diesen Readern nicht übergeben. Die Standard-Klassenerkennung bleibt bei manueller Wahl einer anderen Installation ebenfalls ein unabhängiger `DetectDefault`-Pfad.

**Auswirkung und Grenze:** Ein falscher HUD-Layout-Hinweis kann verwendet werden oder der konfigurierte Pfad wird wegen abweichender Auflösung verworfen und der allgemeinere Fallback genutzt. Bei gleicher Auflösung, aber anderer Skalierung kann der zuerst verwendete kalibrierte Ausschnitt falsch sein. Das ist eine belegte Konfigurationsinkonsistenz; ein vollständiger OCR-Ausfall oder eine konkrete falsche XP-Zahl wurde hier **nicht** reproduziert. `ExperienceFrameReader` und `CombatStatsFrameReader` besitzen Fallbacks und zusätzliche Prüfungen.

**Korrektur:** Gewählte Root-/Profilidentität und Displayparameter einmal gemeinsam auflösen. XP/AP/DP und Klassencharakterwahl daran binden; explizit ausgewählte Dateien und alternative Installationen berücksichtigen. Bei aktiven Profilwechseln den gemeinsamen Kontext gezielt erneuern.

**Regression:** Gegensätzliche Ordner-/XML-Zeitstempel, gleiche Auflösung mit unterschiedlicher Skalierung sowie explizite XML aus anderer Installation. Alle Subsysteme müssen denselben gewählten Kontext erhalten; mehrdeutige Evidenz muss als solche erhalten bleiben.

## Reproduktion und Prüfgrenzen

Aufruf: `pwsh -NoProfile -File artifacts/audit-2026-09-27/bugs-data/session-repro.ps1`.

- [Skript](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/bugs-data/session-repro.ps1) nutzt die aktuelle `BdoGrindTracker.App.Tests.dll`-Fixture und echte interne Service-/Reader-Typen per Reflection; keine Änderung an vorhandenen Tests.
- [Shutdown/Pause-Ergebnis](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/bugs-data/session-results.json).
- [Historische Variantenwahl](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/bugs-data/historical-variant-results.json).
- [Konfigurationswahl](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/bugs-data/configuration-results.json).
- Alle synthetischen Dateien und der prozessspezifische Temp-Ordner liegen unter `artifacts/audit-2026-09-27/bugs-data`. Die HTTP-Fixture nutzt einen Mock-Handler; der historische Variantenfall erreicht diesen nicht.

Geprüfte Bereiche: Sessionstart/-pause/-abschluss/-restore/-shutdown, automatische Startbestätigung und Spotwechsel, Mailbox/Produzentenlocks, manuelle Korrekturen, History/Checkpoint-Zweiteilung, Journal-Dublettensteuerung und Background-Upload-Warteschlange, Schlüsselvalidierung/DPAPI, optionale HUD-/Buff-Zustände, relevante Core-Spot- und Startbestätigungslogik. Keine Vollständigkeitsgarantie für jede OCR-Screenshotkombination und keine Live-Garmoth-API-Verifikation.

## Positive Kontrollen und ausgeschlossene Kandidaten

- Der frühere B01-Stundenkorrekturbefund gilt nicht unverändert: Der aktuelle Live-Service verwendet `ObserveSessionTotals` und lädt abgeschlossene Gesamt-Sessions hoch. Die frühere Stundenklasse bleibt für alte Checkpoints lesbar.
- Der frühere B03-Klassen-Encodingfehler ist im Quellstand über `GameVariableXmlReader.Open` behoben: [CompanionCharacterClassDetector.cs:64](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Character/CompanionCharacterClassDetector.cs:64).
- Der frühere B05-Preset-Displayfehler ist über die Auswahl aktiver `GameOptionGlobal`-/Top-Level-Felder im Calibrationreader adressiert. Gespeicherte Presets werden nicht mehr global als erste beliebige Nachfahren verwendet.
- Der numerische statt array-/objektförmige `RotationTimeline`-Wert wird von beiden Stores als `LoadError` erfasst und verhindert Überschreiben. Der zunächst vermutete ungefangene `InvalidOperationException`-Fall ließ sich **nicht** bestätigen: [Kontrollergebnis](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/bugs-data/malformed-results.json).
- Für zunächst erwogene Offset-Wiederherstellungs- und Korrektur-Races ergab die Prüfung keinen belastbaren neuen Bug. Die Aggregate validieren vollständig vor Mutation; der manuelle Before-Commit-Pfad hält den Mailbox-Lock, und der Uploadzustand wird unter konsistenter Lock-Reihenfolge gelesen. Diese Kandidaten werden nicht als Findings übernommen.
- Uploadabsichten werden vor Dispatch persistiert; unklare Requests bleiben gesperrt. DPAPI-Schlüssel sind getrennt gespeichert; untrusted Response-/Schlüsseltexte werden im HTTP-Ergebnis nicht an die UI durchgereicht. Hintergrund-Uploads schützen die jeweils betroffene historische Session vor Edit/Delete und werden vor Client-Dispose abgewartet.

## Anschlussverbesserungen

Ein sichtbarer Wiederherstellungs-/Backup-Assistent mit konsistenten History-, Checkpoint- und Journaldaten ist sinnvoll. Eine Upload-Warteschlange mit dauerhaften Fehlergründen würde ungültige Klasse/Variante, sichere Ablehnung und unklaren Remote-Status besser unterscheiden. Das sind Produktvorschläge und werden hier nicht als neue Sicherheitsbugs bewertet.
