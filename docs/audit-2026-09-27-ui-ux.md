# Grindcrest: UI-, UX- und Accessibility-Audit

Stand: 27. September 2026, aktueller Arbeitsbaum einschließlich vorhandener uncommitteter Änderungen. Schwerpunkt: Blazor-Komponenten, CSS, JavaScript, Nutzerführung und neue Produktfunktionen. Produktdateien wurden nicht verändert; keine Solution-Builds oder Tests durch diesen Audit-Agenten gestartet. Browserbestätigungen wurden vom koordinierenden Agenten in der isolierten Vorschau geliefert.

## Ergebnis und Prioritäten

Die Anwendung hat bereits eine umfangreiche, überwiegend sinnvoll gegliederte Oberfläche: lokale Sessions mit Wiederherstellung, automatische Grinderkennung und Spotwechsel, Mengenbearbeitung, Garmoth-Vorschau und Sammeluploads, Timeline, Grind-Bewertung, Tagesziele, mehrere Overlayfenster mit Vorlagen und eigenen Themes sowie PNG-Sharing. Diese Funktionen sind **vorhanden**, keine neuen Featurevorschläge.

Die wichtigsten UI-Probleme betreffen Lesbarkeit im Light-Theme, die Standardansicht der sehr breiten Verlauftabelle und die Bedienbarkeit der Timeline ohne Maus. P2 bedeutet zeitnah beheben; P3 bedeutet sinnvoller weiterer Feinschliff. Die priorisierten UI-Funde sind keine nachgewiesene Datenkorruption.

| ID | Priorität | Befund | Nachweis |
| --- | --- | --- | --- |
| UI-01 | P2 | Light-Theme: Erfassungskarten und Tabellenheader kaum lesbar | Code und isolierte Browser-Vorschau |
| UI-02 | P2 | Spotverlauf: Metadaten beanspruchen fast die gesamte sichtbare Tabelle | Code und isolierte Browser-Vorschau |
| UI-03 | P2 | Timeline: freies Zoomen/Verschieben und Einzelwerte nicht per Tastatur erreichbar | Code |
| UI-04 | P2 | Ungelöste Spotvarianten und zugehörige Garmoth-Hinweise umgehen die englische Lokalisierung | Code und Browser-Accessibility-Tree |
| UI-05 | P2 | History, Tagesübersicht und Share-Bild verwenden unterschiedliche Silbergrundlagen ohne klare gemeinsame Kennzeichnung | Code; tatsächliche Differenz tritt nach Preis-/Steueränderung auf |
| UI-06 | P3 | Kalender-Tage haben keinen vollständigen Datumsnamen | Code |
| UI-07 | P3 | Fokussierbare Overlay-Widgetgruppe hat keine reine Enter-/Space-Auswahl | Code und Browser; indirekter Weg über Griffbutton vorhanden |
| UI-08 | P3 | Light-Theme: kontrastarmer Ladezustand beim Session-Sharing | CSS-Ableitung und Browser |
| UI-09 | P2 | Gespeicherte Sessions mit ungelöster Spotvariante haben keine passende Korrekturaktion | Code und angezeigter Browserhinweis; Backendbewertung separat |

## Belegte Funde

### UI-01: Themefarben werden mit fest dunklen Erfassungsflächen gemischt

[CaptureConfigurationSettings.razor:125](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/CaptureConfigurationSettings.razor:125) setzt `.capture-active-file` auf `#11171d`, Zeile 128 `.capture-automatic` auf `#151a21` und Zeile 140 den Tabellenheader auf `#1b222b`. [themes.css:423](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/wwwroot/themes.css:423) setzt im Light-Theme `--muted:#526478` und `--text:#243244`; entsprechende Erfassungs-Overrides fehlen.

Browser bestätigt: Light → Einstellungen → Erfassung → Prüfen. Die berechneten Farben entsprechen diesen Regeln. Kontraste nach Standard-sRGB-Luminanz: Text/aktive Dateikarte **1,39:1**, Text/automatische Auswahl **1,34:1**, Headertext/Headerfläche **2,64:1**. Das ist für normale Beschriftungen deutlich zu niedrig. Sichtbarer Beleg: [light-capture.png](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/light-capture.png).

Verbesserung: Erfassungskarten und Tabellenköpfe verwenden semantische Flächentokens, etwa `--theme-inset` und `--panel-raised`, einschließlich passender Textfarben. Alle sieben Themes anhand derselben Zustände prüfen, besonders Light und Valencia. Die Änderung sollte auch Fehler-, Auswahl-, Hover- und Disabled-Zustände abdecken.

### UI-02: Die Verlauftabelle priorisiert Metadaten vor dem eigentlichen Loot

[HistoryDashboard.razor:141](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/HistoryDashboard.razor:141) stellt Klasse, Alter, Zeit, Silber/h, Erfahrung, AP/DP und Verbrauchte Items vor sämtliche Lootspalten. [HistoryDashboard.razor:514](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/HistoryDashboard.razor:514) verlangt mindestens `944 + 88 × Lootspalten` Pixel. Die Tabelle erzeugt zudem alle erlaubten Lootpool-Items, auch ohne Menge, in Zeilen 434–436.

Browserbeispiel: 1280-Pixel-Fenster, Tabellenviewport 983 Pixel, Tabelle 3848 Pixel, 33 Lootspalten. Zwischen Metadaten und Aktionen bleiben in der Standardansicht ungefähr 32 Pixel für die erste Trashspalte. Beleg: [history-table.png](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/history-table.png). Horizontales Scrollen und Tastaturreihenfolge sind vorhanden; das Problem ist die Informationspriorität.

Verbesserung: Standardmäßig Datum, Dauer, Silber/h, Trash und Favoriten zeigen. AP/DP, XP und Verbrauch in eine Session-Detailzeile verlagern oder über einen Spaltenwähler zuschalten. Leere Lootspalten optional anzeigen. Kleine Fenster bekommen Sessionkarten oder eine kompakte Vergleichsansicht. Das bestehende Verschieben/Favorisieren sollte erhalten bleiben.

### UI-03: Timeline-Detailfunktionen verlangen eine Maus

[SessionTimeline.razor:79](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/SessionTimeline.razor:79) bindet nur Wheel- und Pointerereignisse. Der Track hat weder `tabindex` noch Tastaturhandler. Die Anleitung in Zeile 254 nennt Shift+Mausrad und Ziehen. Balken und Phasen liefern Daten über SVG-`title` in Zeilen 104–107 und 154–157, sind jedoch nicht fokussierbar; das SVG ist als ein Bild mit generischem Namen ausgezeichnet.

Die Layercheckboxen, Lootauswahl und separate Rotationsauswahl sind bereits per Tastatur bedienbar. Es fehlen die Tastaturalternativen für freie Ausschnittswahl und genaue Einzelwerte.

Verbesserung: sichtbare Zoom-Plus/Minus-Buttons, links/rechts verschieben, Zeitraumfelder und optional „Daten als Tabelle“. Tastaturfokus muss die Dropzeit, Menge und Phase zugänglich machen; eine alternative Datentabelle vermeidet hunderte Tabstopps im SVG.

### UI-04: Variantenaufforderungen umgehen die Lokalisierung

[Presentation.cs:33](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/Presentation.cs:33) gibt `LootSpotCatalog.DisplayName` direkt zurück. Der Katalog enthält in [LootSpotCatalog.ScreenshotSpots.cs:860](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.Core/LootSpotCatalog.ScreenshotSpots.cs:860) `[Dehkia] Ash Forest (Stufe wählen)` und in Zeile 909 `Winter Tree Fossil (AP bestätigen)`. Der englische Accessibility-Tree zeigt beide Aufforderungen unverändert deutsch.

Zusätzlich enthält [GarmothCatalog.cs:175](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Integrations/Garmoth/GarmothCatalog.cs:175) drei deutsche Uploadhinweise zu Floodlands, Dehkia und Winter Tree. [GarmothUploadPreview.cs:48](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Integrations/Garmoth/GarmothUploadPreview.cs:48) übernimmt sie als Error. Das UI ruft zwar `T(row.Preview.Error)` auf ([GarmothDashboard.razor:77](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/GarmothDashboard.razor:77)), passende Übersetzungskeys/Templates fehlen aber; dadurch erscheinen auch die vollständigen Handlungsanweisungen deutsch im englischen UI. Der Browser bestätigt Dehkia- und Winter-Tree-Zeilen.

Verbesserung: Spotidentität und Handlungsaufforderung trennen. ID-basierter Anzeigename plus separat lokalisierter Hinweis „Select tier“ bzw. „Confirm AP variant“; Daten-IDs unverändert lassen. Suchbegriffe können beide Sprachen berücksichtigen.

### UI-05: Die Bedeutung von „Silber netto“ hängt von der Ansicht ab

Der Verlauf bewertet Sessions in [HistoryDashboard.razor:258](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/HistoryDashboard.razor:258) mit aktuellen Preisen und Steueroptionen neu. Sharing lädt jedoch in Zeile 207 erneut den gespeicherten Historieneintrag. [SessionSharePresentation.cs:68](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/SessionSharePresentation.cs:68) übernimmt dessen gespeicherten `SilverAfterTax`; die Exportfunktion berechnet absichtlich keine neuen Preise.

Auch die Tagesübersicht summiert in [GrindGoalDaySummary.razor:8](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/GrindGoalDaySummary.razor:8) gespeicherte Silberwerte und zeigt diese in Headern, während darunter `LootTable` den Loot mit aktuellen Preisen bewertet (Zeilen 41–43, 54–55; Berechnung in [LootTable.razor:129](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/LootTable.razor:129)). Die Goals-Speicherbasis ist ausdrücklich in `docs/GRIND_GOALS.md` dokumentiert und für feste Zielerreichung sinnvoll. Ohne deutliche gemeinsame Kennzeichnung können Header, Lootsumme, Verlauf und Share-Bild trotzdem widersprüchlich wirken.

Verbesserung: „Gespeicherter Wert“/„Aktueller Wert“ klar neben den Kennzahlen kennzeichnen; optional eine gemeinsame Umschaltung. Share-Bild enthält Preisstand bzw. „Wert beim Speichern“. Keine bestehende Historie stillschweigend überschreiben.

### UI-06 bis UI-08: kleinere Accessibility- und Zustandslücken

- Kalender: [GrindGoals.razor:38](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/GrindGoals.razor:38) benennt Tage über sichtbare Tageszahl und Kennzahlen. Vollständiges Datum inklusive Monat/Jahr, Zielstatus und gegebenenfalls „außerhalb des angezeigten Monats“ als zugänglichen Namen ergänzen. Optional Pfeilnavigation innerhalb des Kalenders und Sprung „Heute“ anbieten.
- Overlay: [OverlayEditor.razor:137](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/OverlayEditor.razor:137) verwendet eine fokussierbare `role=group` mit Klickauswahl. [overlay-editor.js:298](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/wwwroot/overlay-editor.js:298) verarbeitet Pfeile, kein Enter/Space zur Auswahl. Pfeile wählen und verschieben zugleich ([OverlayEditor.razor.cs:367](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/OverlayEditor.razor.cs:367)). Browserbestätigt: Enter und Space auf der Widgetgruppe wählen nicht; Enter am enthaltenen Griffbutton wählt ohne Positionsänderung. Die Widgetgruppe selbst sollte Enter/Space ohne Positionsänderung auswählen; Bewegung und Größenänderung bleiben getrennte Aktionen.
- Sharing: [session-share.css:20](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/wwwroot/session-share.css:20) belässt den Vorschauhintergrund auf `#0d141d`; der Ladezustand nutzt `--muted` und `--gold-bright`. Im Light-Theme ergeben sich daraus 3,04:1 für Ladetext und 2,12:1 für das Icon; der Browser bestätigt den schwachen Ladezustand. Semantischen Ladeflächen-Token benutzen oder explizit eine vollständig dunkle lokale Themegrenze definieren. Die fertige PNG-Grafik ist absichtlich eigenständig gestaltet.

### UI-09: Der Garmoth-Variantenhinweis führt für alte Sessions ins Leere

Bei einer gespeicherten Dehkia-/Winter-Tree-/Floodlands-Session mit ungelöster Variante fordert [GarmothCatalog.cs:175](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Integrations/Garmoth/GarmothCatalog.cs:175) zur Variantenwahl in der Live-Session auf. Die entsprechende Preview liefert in [GarmothUploadPreview.cs:49](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Integrations/Garmoth/GarmothUploadPreview.cs:49) `Unavailable(limitation)` ohne Korrekturlink. Die Historienbearbeitung erlaubt Klasse und Mengen ([HistoryDashboard.razor:192](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/HistoryDashboard.razor:192), Speichern in Zeile 421), keine Spotvariante. Ist diese gespeicherte Session nicht mehr die aktuelle Live-Session, fehlt damit der passende UI-Weg zur Auflösung.

Verbesserung: explizite nachträgliche Auswahl ausschließlich aus kompatiblen Varianten derselben Lootfamilie. Geänderte Historie neu bewerten, bestehende Upload-/Journalzustände respektieren und vor Upload eine neue Vorschau verlangen. Falls eine sichere Korrektur nicht angeboten werden soll, muss der Hinweis die tatsächliche Einschränkung und einen unterstützten Wiederherstellungsweg benennen, nicht auf die fremde aktuelle Live-Session verweisen.

## Verbesserungen nach Nutzerfluss

| Bereich | Bereits gut | Konkrete nächste Verbesserung |
| --- | --- | --- |
| Live | klare Start/Pause-Aktion, Autoerkennung, manuelle Mengen, Warnungen, aufklappbare Details | Sichtbarer Erfassungszustand mit letztem erfolgreichen Frame/Drop und direktem Link zur passenden Prüfung; Gründe deaktivierter Aktionen als sichtbarer Hilfetext |
| Settings | sechs Kategorien, persistente URLs, Validierung, automatische Speicherung | „Speichert… / Gespeichert / Fehler“ konsistent für Änderungen; Diagnosepfade mit Kopieren/Ordner öffnen; Feldhinweise via `aria-describedby` anbinden |
| History | Filter, Pagination, Favoriten, Reihenfolge, Edit/Delete-Dialoge | kompakte Standardspalten; Filter zurücksetzen auch bei Klasse/Zeitraum; aktueller/gespeicherter Preisstand; Undo beim Löschen |
| Overlay | Mehrfenster, Vorlagen, Hotkeys, Raster, automatische Ausrichtung, Vorschau | Undo/Redo, Duplizieren eines Moduls, Reset einzelner Eigenschaften, Tastaturauswahl; deutlicherer Hinweis auf teilweise außerhalb der Fläche liegende Module |
| Share | Momentaufnahme, Vorschau, Retry, Kopieren und PNG, Schutz gegen veraltete asynchrone Ergebnisse | Preisgrundlage in der Grafik, Auswahl sichtbarer Informationen, kompakte/ausführliche Vorlage; sichtbare Auswahlmöglichkeit für Schrift-/Bildgröße |
| Setup | aufschiebbar, schrittweiser Fokus, Hinweise zu OCR und verdeckten Logs, Prüfung der echten Aufnahme | Prüfabschluss mit verständlichem Ergebnis pro normalem/Special-Log; „Noch nicht geprüft“ vom bloßen Speichern der Auswahl unterscheiden; optionaler Check statt Pflichtgate |
| Garmoth | konkrete Payload-Vorschau, Uploadstatus, Schutz nach lokalen Korrekturen, Batch-Zusammenfassung | explizit „Alle uploadfähigen Sessions“ am Batchbutton, optional „Gefilterte hochladen“; stärkerer Wiederaufnahmefluss nach gesperrtem/unsicherem Ergebnis |
| Goals | lokaler Kalender, Wochen-/Monatsaktionen, Tagesübersicht, klare Erfolgsmeldung | vollständige Datumslabels; „Heute“; gespeicherte Wertbasis; Retry beim Laden defekter Zieldateien mit klarer Wiederherstellungsaktion |

Design: Die wiederholte Panel-, Button-, Leerzustand- und Dialogsprache ist brauchbar. Der größte Designhebel ist weniger gleichzeitig sichtbare Metadaten, nicht zusätzliche Dekoration. Fest codierte Farben und Inline-CSS schwächen die Theme-Konsistenz. Oberflächen-, Text-, Status- und Fokusfarben sollten als semantische Tokens geführt werden. Informationsdichte wählbar machen; 9–11-px Zusatztexte vor allem im Overlay und Kalender reduzieren.

Responsive: In der koordinierenden Prüfung funktionierten das Iconmenü und die Capture-Seite bei der unterstützten Fenstergröße 860×640 ohne globalen horizontalen Overflow. Die extrem breite Historienspot-Tabelle bleibt dennoch ein eigenständiges UX-Problem. Browserdarstellung ist kein Beleg für native DPI-/Mehrmonitorparität.

Dialoge und Zustände: Native `dialog.showModal()`, Beschriftungen, Escape, Modal-Fokus, Fehlerrollen, Ladezustände und Retry sind großteils vorhanden. Erfassungs- und Uploadwarnungen sind konkret. Ein generisches „Ein Vorgang wird gerade ausgeführt“ erklärt aber weder abgeschlossene Sessions noch alle Fälle ausstehender Uploads; deaktivierte Aktionen brauchen den passenden Grund. Löschen ist endgültig und wird bestätigt, eine Wiederherstellung gibt es im UI nicht.

Onboarding-Browserprüfung: Die Fortschrittsliste zeigt nach „Weiter“ den grünen Abschluss für „Erfassung“, selbst wenn OCR/Erfassungsbereiche nicht bereit sind. Das ist in [SetupWizard.razor:15](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/SetupWizard.razor:15) an `index < _step` gekoppelt; `Next` in Zeilen 94–99 validiert gespeicherte Feldwerte, keine erfolgreiche reale Capture-Prüfung. Der finale Warnhinweis in Zeilen 76–78 ist bereits vorhanden. Empfehlung: abgeschlossener Einrichtungsschritt und technische Bereitschaft getrennt benennen, etwa „Einstellungen gespeichert · Erfassung noch nicht geprüft“. Der Nutzer darf Setup weiterhin ohne Spiel und ohne Pflichtprüfung abschließen.

## Performance aus UI-Sicht

[TrackerComponentBase.cs:18](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/TrackerComponentBase.cs:18) lässt jede Tracker-Komponente auf jeden `Changed`-Event reagieren. Im Verlauf materialisiert Zeile 10 die gesamte gefilterte Historie und berechnet alle Preise neu (Zeilen 247–261); die Spotkarten filtern die Liste anschließend pro Spot erneut (Zeile 81). Die acht Zeilen Pagination begrenzen die sichtbaren Rows, nicht diese Vorarbeit. Ziele cachen bereits die Tagesnettoprojektion, führen aber `DailyDrops` für die ganze Historie pro Render aus ([GrindGoals.razor:10](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/GrindGoals.razor:10)).

Empfehlung: Projektionen nach Historyrevision, Preisrevision, Steueroptionen und Filterzustand cachen. Uhrticks sollten keine Historien-/Zielaggregation auslösen. Keine gemessene UI-Latenz wird aus dem Quellcode allein behauptet; Lastmessung mit 1.000/10.000 Sessions und offener/geschlossener Timeline gehört in den Performance-Audit.

## Neue Funktionen mit hohem Nutzen

| Rang | Funktion | Nutzerwert / kleinster sinnvoller Umfang | Relativer Aufwand |
| --- | --- | --- | --- |
| 1 | Lokales Backup/Restore plus CSV-Export | Datenordner als versioniertes Paket exportieren, Integrität prüfen, Import-Vorschau mit Session-ID-Deduplizierung; API-Key separat behandeln. PNG-Sharing und automatische Wiederherstellung sind schon vorhanden, ein allgemeiner UI-Export/Restore wurde nicht gefunden. | Mittel–hoch |
| 2 | Vergleich ausgewählter Sessions | Zwei bis vier Sessions mit gleichen Bedingungen nebeneinander: Trash/h, Silber/h, Buffkosten, Agriszeit, Klasse/AP/DP, Rotation; aktuelle/gespeicherte Preisbasis explizit wählen. Vorhandene Trends und Grind-Bewertung ersetzen diesen direkten Vergleich nicht. | Mittel |
| 3 | Erfassungs-Check mit Qualitätsstatus | Bestehende Preview um klare Normal-/Special-Log-Ergebnisse, letzte erfolgreiche Erkennung und handlungsorientierten Hinweis ergänzen. Keine unbelegten Prozentwerte für OCR-Zuverlässigkeit anzeigen. | Mittel |
| 4 | Sessionnotizen und Tags | Kurze lokale Notiz plus optionale Tags für Build, Kanal, Gearänderung oder Tests; in Verlauf filtern. Sensible Freitexte nur auf Wunsch in Share-Bilder aufnehmen. | Niedrig–mittel |
| 5 | Undo/Redo und Modulduplikation im Overlay | Kleine lokale Änderungshistorie für Layoutaktionen, Modul duplizieren, mehrere Module gemeinsam ausrichten; vorhandene Vorlagen und Bestätigungsdialoge bleiben erhalten. | Mittel |
| 6 | Item-/Zeitziele zusätzlich zu Silberzielen | Konkretes Itemziel oder Grindzeit pro Tag/Woche, Fortschritt direkt im vorhandenen Goals-Overlay; keine neue unabhängige Zielwelt. | Mittel |
| 7 | Persönlicher Spotplaner | Eigene tatsächliche Erträge und verbleibendes Ziel kombinieren, benötigte Grindzeit schätzen; Stichprobengröße, fehlende Preise und Agris-/Buffbedingungen sichtbar machen. | Mittel–hoch |

Aufwand ist eine relative Einschätzung aus dem bestehenden Code, keine verbindliche Zeitkalkulation.

## Grenzen und ausgeschlossene Hypothesen

- Fokusverlust bei jeder Änderung des History-Suchfelds wurde vermutet, durch Browserprüfung **ausgeschlossen**: `M` und danach `agaia` bleiben im Eingabefeld, der Queryparameter ist `Magaia`.
- Keine Aufforderung, bestehende Funktionen wie Session-Sharing, Multi-Overlay, Tagesziele oder automatische Spotwechsel erneut zu entwickeln.
- Keine pauschale Barrierefreiheitszertifizierung; ein realer Screenreaderlauf fehlt. Die beschriebenen fehlenden fokussierbaren Ziele und Tastaturhandler sind am Code belegt.
- Keine native Ingame-Erfassung, OCR-Installation, echten Garmoth-Uploads oder Nutzerdatenänderungen in diesem UI-Audit.
- Numerische Extremwerte: Das Editformular akzeptiert `Int64.MaxValue` pro Menge, spätere `Sum(long)`-Aggregationen können mit mehreren solchen Einträgen überlaufen. Dieser Robustheitskandidat wurde an den Backend-Audit weitergegeben und ist hier nicht als alltagsnaher UI-Hauptbefund eingestuft.
