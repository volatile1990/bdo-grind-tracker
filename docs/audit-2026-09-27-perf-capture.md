# Grindcrest-Audit: Capture, OCR, native Overlays und Performance — 27.09.2026

## Umfang und Belegstand

Geprüft wurde der aktuelle, vom Root-Agent frisch gebaute Arbeitsbaum. Keine Produktdateien, Appdaten oder BDO-Konfiguration wurden geändert; keine laufende Grindcrest-/Spielinstanz wurde bedient. Quellprüfung von Capture, OCR, optionalen HUD-Workern, Rotations-OCR, nativen Overlays und dem Verlaufsprojektionspfad. Die gesamten Solutiontests liefen ausschließlich beim Root-Agent. Dieser Teilbericht verwendet zwei unabhängige Korrektheitsrepros und eine isolierte Komponentenmessung.

Der Harness liegt unter [Program.cs](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/perf-capture/Program.cs), seine Daten unter [results.json](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/perf-capture/results.json). Er referenziert direkt die frisch gebauten Debug-DLLs, ohne ProjectReferences. Sein Release-Build verändert keine Solutionprodukte. App-DLL SHA-256: `C48459EBBD7EDB66185DE07EE70A9D51BE26AA642440A73B648D6C418A6D6C6E`; Laufzeit `.NET 9.0.10`.

Reproduktion aus dem Repository:

```powershell
dotnet build artifacts/audit-2026-09-27/perf-capture/AuditHarness.csproj --configuration Release --nologo
dotnet artifacts/audit-2026-09-27/perf-capture/bin/Release/net9.0-windows10.0.19041.0/BdoGrindTracker.App.Tests.dll
```

Die AssemblyName des isolierten Harness ist `BdoGrindTracker.App.Tests`, um die vorhandene InternalsVisibleTo-Freigabe für die echten internen Klassen zu nutzen. Er ist kein neuer Solutiontest und wird nicht ausgeliefert.

## Bestätigte Bugs

### P2 — Native Chartansicht kann geänderte Rotationsphasen und Specialmarker überspringen

**Quelle:** [NativeOverlayRenderState.cs:63](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Overlay/Native/NativeOverlayRenderState.cs:63), insbesondere Zeilen 67–68; [NativeOverlayHost.cs:154](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Overlay/Native/NativeOverlayHost.cs:154); tatsächliche Projektion [OverlaySessionTimeline.cs:107](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Overlay/OverlaySessionTimeline.cs:107).

`SameTimeline` vergleicht die Anzahl der Specialereignisse und Mechanikereignisse, aber weder `SpecialEventSeconds` noch die Inhalte von `Events`. Bei gleichem SessionElapsed, Rotationsbeginn, Dauer und Outcome betrachtet der Cache unterschiedliche gezeichnete Zeitpunkte als identisch. Der Host unterdrückt dadurch den Repaint und merkt sich trotzdem den neuen Snapshot. Bei laufender Session korrigiert ein späterer Zeitfortschritt das Bild meist; bei pausierter oder anderweitig statischer Ansicht kann es bis zur nächsten sichtbaren Änderung veraltet bleiben.

**Repro mit echten Klassen:** Drakania von 30 auf 60 Sekunden verschoben, Specialmarker von 35 auf 75 Sekunden, Anzahl jeweils unverändert. `OverlaySessionTimeline.Create` liefert andere Phasengrenzen (0,25 → 0,5) und Markerpositionen (0,2917 → 0,625). Trotzdem liefert `NativeOverlayRenderState.Matches` **true**. Dies bestätigt den fehlerhaften Cachevergleich, nicht die Häufigkeit dieses Eingabewechsels im laufenden Spiel.

**Empfehlung:** Alle tatsächlich gezeichneten Mechanik- und Specialzeitpunkte vergleichen oder eine unveränderliche Rotationsprojektion/Revision verwenden. Ein Regressionstest muss gleicher Anzahl bei geänderten Zeiten/Kind/Occurrence abdecken, einschließlich pausierter Ansicht. Bereits stabile Referenzen weiterhin direkt wiederverwenden.

### P2 — Initiale Banner-OCR beim Autostart ignoriert Abbruch und verzögert manuelle Aktionen

**Quelle:** [AutomaticGrindMonitor.cs:71](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/AutomaticGrindMonitor.cs:71), [RotationStartWatcher.cs:37](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/RotationStartWatcher.cs:37) und Zeilen 62–68, [RotationMessageProfile.cs:16](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/RotationMessageProfile.cs:16), [CompanionWindowsOcrRecognizer.cs:50](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.Ocr/CompanionWindowsOcrRecognizer.cs:50) und Zeile 186. Der Wartepfad führt über [TrackerSessionService.AutoStart.cs:203](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.AutoStart.cs:203) und [TrackerSessionService.cs:789](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.cs:789), beim Schließen zusätzlich Zeile 952.

Die erste Rotationsbannerprüfung findet vor dem fünfsekündigen Analyzer-Watchdog statt. `RotationStartWatcher.Observe` hat keinen CancellationToken; beide OCR-Pässe rufen `Recognize` ohne Token auf. Ein Nutzerabbruch des Autostarts erreicht sie daher nicht. `StopAutoStartProbeCoreAsync` wartet auf diese Probe, und manuelle Start-/Neue-Session-Vorgänge sowie Shutdown warten wiederum auf deren Abschluss. Eine native Erkennung kann bis zum eigenen zehnsekündigen OCR-Wait weiterlaufen. Bei mehreren normalen OCR-Pässen kommen deren Laufzeiten hinzu; ein Timeout beendet den aktuellen Pfad mit Fehler. Keine pauschale maximale Gesamtdauer oder gemessene Native-Hängedauer wird behauptet.

**Repro:** Ein ausschließlich synthetischer Bannerreader schläft je 200 ms. Direkt nach Beginn der ersten Lesung wird der Token abgebrochen. Die Probe führt trotzdem beide Regionenlesungen aus, beendet sich erst rund **431 ms nach Abbruch** und liefert null, ohne OperationCanceledException. Keine native OCR, kein Spielcapture und keine Appbedienung wurden dafür verwendet.

**Empfehlung:** Token durch Watcher, Profilrecognizer und beide OCR-Pässe führen und vor/nach regionenweiser Lesung prüfen. Die initiale Bannerprüfung benötigt ebenfalls ein begrenztes Gesamtbudget. Bei einem noch laufenden nativen Vorgang müssen dessen eigene Pixel/Engine bis zur tatsächlichen Beendigung erhalten bleiben; die bereits vorhandene `OcrOperationLease` nicht umgehen. Ein Test muss den Abbruch während der initialen Bannerlesung und manuelle Befehlsfreigabe prüfen.

## Weitere konkrete Quellenbefunde — noch ohne Laufzeitrepro

Diese Punkte sind von den beiden bestätigten Repros zu unterscheiden.

| Priorität | Befund und Trigger | Auswirkung / Empfehlung |
| --- | --- | --- |
| P3 | [NativeOverlayHost.cs:187](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Overlay/Native/NativeOverlayHost.cs:187) delegiert Positions-/Größencommit an denselben `RunCommandAsync`-Gate wie Tracking. Zeile 212 verwirft ihn während eines laufenden Befehls. | Wird ein Overlay während einer asynchronen Trackingaktion verschoben/vergrößert, kann die Speicherung verworfen werden und der nächste Tick die alte Geometrie zurücksetzen. Neueste ausstehende Geometrie nach Befehlsabschluss speichern oder Manipulation während des Befehls sichtbar sperren. Keine reale Dragsequenz durchgeführt. |
| P3 | [NativeOverlayHost.cs:132](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Overlay/Native/NativeOverlayHost.cs:132) setzt `_captureExcluded` auch bei fehlgeschlagenem `SetCaptureExcluded`. | Ein transienter Windows-Fehler wird ohne Handle-Neuanlage oder Optionswechsel nicht erneut versucht. Erfolgreich angewendeten Zustand getrennt merken und begrenzten Retry anbieten; die Fehlermeldung ist bereits vorhanden. Kein Windowsfehler injiziert. |
| P3 | [AutomaticGrindMonitor.cs:29](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/AutomaticGrindMonitor.cs:29), Zeilen 65/149 und [RotationStartWatcher.cs:40](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/RotationStartWatcher.cs:40) benutzen Wallclock für Bannerfrische, Probeabstand und Replayzeiten. | Ein rückwärts korrigierter UTC-Zeitwert lässt einen Banner mit negativem Alter als gültig gelten und unterdrückt weitere Bannerprobes bis die Uhr aufholt; Replay kann rückwärts laufen. Live-Capture löst dies bereits monoton, Autostart sollte denselben Ansatz für Frische und Reihenfolge verwenden. Seltener Uhrkorrekturpfad; kein Live-NTP-/Uhrtest. |
| P3, latent | [PassiveScreenCapture.cs:270](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Capture/PassiveScreenCapture.cs:270) erzeugt das zurückzugebende Bitmap, bevor das `finally` bei ReleaseFrame (277–283) noch werfen kann. | Bei ReleaseFrame-Fehler nach erfolgreichem Copy bleibt ein Bitmap ohne expliziten Besitzer bis zur GDI+-Finalisierung zurück; bei AccessLost kann der äußere Retry neue Bilder erzeugen. Ergebnis erst nach erfolgreichem Release übertragen, bei Releasefehler Bitmap entsorgen. Kein DXGI-Fault-Repro. Der ausgelieferte normale Startpfad verwendet heute `PassiveWindowCapture` (Program.cs:107), weshalb dies keine behauptete aktuelle Hauptpipeline-Störung ist. |

## Performance: gemessener offener Verlaufspfad

Die ROI-/OCR-/Buff-/Overlaycaches aus [CPU_OPTIMIZATION_2026-09-21.md](D:/Projects/bdo-grind-tracker/docs/CPU_OPTIMIZATION_2026-09-21.md) sind vorhanden. Sie werden hier nicht als neue fehlende Verbesserungen aufgeführt. Ein noch offener Teil aus dem früheren Audit ist die Verlaufsprojektion.

**Quellen:** [HistoryDashboard.razor:10](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/HistoryDashboard.razor:10), Zeilen 79–82 und 247–261. `Filtered` bewertet jede gefilterte Session bei jedem Render erneut. Auf einer einzelnen Spotseite wird der `_spotId`-Filter erst nach dieser Bewertung angewendet. In der Spotübersicht durchsucht anschließend jeder der 43 Spotkartenpfade die ganze gefilterte Liste und baut/sortiert seine Metriken. [TrackerComponentBase.cs:25](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Components/TrackerComponentBase.cs:25) fordert bei jeder Trackeränderung einen Render an.

Der Harness ruft die **echte kompilierte private `Filtered`-Eigenschaft** auf und danach die echten `HistoryPresentation.CalculateMetrics`-Methoden für die 43 Profile, wie die Übersicht. Synthetische Sessiondaten, 20 bepreiste Items je Session, drei Warmups und 20 Iterationen pro Größe, vollständiger Verlauf und keine Such-/Klassenfilter. `PreviewTrackerSession(empty:true)` ist nur In-Memory-Datenlieferant. Die Messung enthält weder Blazor-DOM/HTML noch WebView, Bildverarbeitung, Dateipersistenz oder Netzwerk. Sie ist eine Teilpfadmessung mit einer Debug-App-DLL. Die lokale Browser-Vorschau lief unabhängig weiter; Unterschiede der Messblöcke sind deshalb keine kontrollierte End-to-End-Appmessung.

| Sessions | Median Bewertung | Median Gruppierung + Spotmetriken | Mittel gesamter Teilpfad | Verwaltete Allokation je Durchlauf |
| ---: | ---: | ---: | ---: | ---: |
| 100 | 0,45 ms | 0,18 ms | 0,69 ms | 0,155 MB |
| 1.000 | 5,40 ms | 1,12 ms | 9,04 ms | 0,887 MB |
| 5.000 | 24,56 ms | 3,91 ms | 32,59 ms | 4,134 MB |
| 10.000 | 47,79 ms | 6,72 ms | 53,99 ms | 8,195 MB |

MB hier dezimal; Allokationen sind `GC.GetAllocatedBytesForCurrentThread`. Die Prozess-CPU-Werte stehen im Rohdatensatz, sind bei kleinen Blöcken durch die 15,625-ms-Zählerauflösung begrenzt und werden nicht als App-CPU-Prognose verwendet. Die Tabelle zeigt Kosten bei unveränderten Eingaben, keine gemessene Einsparung durch einen bereits implementierten Ersatz.

Es gab zwei Harnessläufe gegen dieselbe App-DLL. Die Tabelle und `results.json` zeigen den zweiten Lauf nach Ergänzung des unabhängigen synthetischen Abbruchrepros. Die erste Messung ist aus dem ursprünglichen Tooloutput als [first-run-summary.json](D:/Projects/bdo-grind-tracker/artifacts/audit-2026-09-27/perf-capture/first-run-summary.json) erhalten: Bei 10.000 Sessions waren es 58,76 ms Gesamtwandzeit statt 53,99 ms im zweiten Lauf, bei 5.000 Sessions 32,51 statt 32,59 ms. Diese Streuung bestätigt die Grenzen kurzer lokaler Proben. Es ist kein kontrollierter Vorher-/Nachher-Vergleich einer Produktoptimierung.

**Empfehlung, hohe Priorität bei wachsendem Verlauf:** Bewertete Sessionprojektionen an Verlauf-/Preis-/Steuerrevision binden, Daten einmal nach Spot gruppieren und unveränderte Metriken wiederverwenden. Spotfilter vor der Bewertung anwenden. Filterzeitgrenze einmal pro Projektion bestimmen. Preiswechsel, manuelle Korrekturen, Steuerwechsel, neue Sessions und Datumsfilter müssen richtig invalidieren. Die vorhandene Pagination begrenzt nur die dargestellten Sessionzeilen, nicht diesen Rechenpfad.

## Weitere Optimierungskandidaten und Features

Die folgenden Punkte beruhen auf Quellenprüfung, nicht neuen CPU-Vergleichsmessungen.

1. **Overlay verschieben ohne vollständiges Neuzeichnen.** [NativeOverlayForm.cs:147](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Overlay/Native/NativeOverlayForm.cs:147) ruft bei jedem MouseMove während Verschieben und Vergrößern `Render()` auf. Reines Verschieben ändert die Bitmapinhalte nicht. Position nativ aktualisieren; Repaint bei Größen-/Inhaltsänderung und bei Abschluss. Resize-Neuzeichnen bei hoher Ereignisfrequenz auf jeweils den neuesten Pointerzustand bündeln. Erst Interaktionslatenz und native Renderzeit messen; keine Einsparprozente ableiten.
2. **Unbekannter Spot: gemeinsame Rotations-OCR.** [RotationMonitor.cs:160](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Analysis/RotationMonitor.cs:160) startet vorläufige Profilerkennung; Hermesia, Event Horizon und Magaia verwenden denselben Bannerbereich und weitgehend dieselbe OCR-Aufbereitung. Der neue `RotationStartWatcher` gruppiert Regionen bereits, die laufenden BufferedRotationProfileMonitor-Instanzen haben weiter getrennte Bild-/OCR-Arbeit. Ein gemeinsames unveränderliches Leseresultat pro Capture/Region kann jeweils getrennte Parser und Suchhistorien versorgen. Namebar und Aphrodon-SingleLine bleiben eigenständig. Dieser Kandidat war bereits im alten Audit offen und muss wegen Worker-/Budget-/Diagnosereihenfolge end-to-end geprüft werden.
3. **Verlaufsspeicherung skalieren.** [TrackerSessionService.History.cs:68](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.History.cs:68) schreibt weiterhin im 15-Sekunden-Checkpointpfad den ganzen Verlauf; [LootHistoryStore.cs:55](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Persistence/LootHistoryStore.cs:55) normalisiert und serialisiert alle Sessions. Abgeschlossene unveränderte Daten getrennt vom aktiven Checkpoint halten oder deren Serialisierung wiederverwenden. Crashwiederherstellung und atomare Speicherung erhalten. Kein aktueller Dateizeitbenchmark; bereits als offener Kandidat dokumentiert.
4. **Pipelineanzeige für Nutzer und Diagnose.** Capturemetadaten enthalten bereits CaptureInterval/Backpressure/QueueDelay. [TrackerSessionService.cs:537](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Application/TrackerSessionService.cs:537) misst nur Analyzerzeit, vor Diagnose-PNG und HUD-Scheduling. Eine kleine Diagnoseansicht mit tatsächlicher Framekadenz, Queuealter, OCR-/Reviewlaufzeit, Cachetreffern, verwalteter/nativer Speichernutzung und separatem Diagnoseaufwand würde langsame Erkennung, unlesbares HUD und langsame Aufnahme unterscheidbar machen. Reale Gesamtzeiten erfassen und schmale Telemetrie sammeln; keine Warnungen allein aus theoretischen 5-Hz-Obergrenzen erzeugen.
5. **Begrenzte Diagnoseaufzeichnung als Produktoption.** [DiagnosticRecordingSession.cs:13](D:/Projects/bdo-grind-tracker/src/BdoGrindTracker.App/Diagnostics/DiagnosticRecordingSession.cs:13) dokumentiert absichtlich unbeschränkte öffentliche Aufnahme; PNG-Kompression und Journal-Flush liegen synchron im Framepfad (342/366). Nutzbarer neuer Funktionsumfang: einstellbares Zeit-/Speicherlimit, sichtbarer belegter Speicher und geordneter Diagnoseexport. Eine spätere Hintergrundqueue muss begrenzt sein, Dateireihenfolge/Framevollständigkeit erhalten und Abschluss sicher drainieren. Unbegrenztheit ist derzeit kein heimlicher Bug und keine behauptete gemessene Lastquelle.
6. **Threadbudget erst instrumentieren.** Primäre Lootpipeline, mehrere HUD-Worker, Rotationsworker und zwei Paddleworker können gleichzeitig rechnen. OpenCV-globales Threadlimit und ONNX-Spinning waren im alten Bericht bewusst offen. Aktuell weiterhin keine pauschale Empfehlung zur globalen Reduktion: CPU, Latenz, Queuealter, Zeitbudgets und Resultatparität unter realem Gameplay gemeinsam prüfen. Ein automatischer Ressourcen-/Energiesparmodus ist ein möglicher späterer Nutzermehrwert, benötigt aber messbare Qualitätsgrenzen.

## Bereits solide Schutzmechanismen

- PassiveCaptureSession verwendet eine geordnete, auf vier wartende Frames begrenzte Queue mit Backpressure statt stiller Frameverluste. Worker besitzen Bitmaps bis zum tatsächlichen Abschluss; Watchdogabbrüche geben aktive Eingaben nicht vorzeitig frei.
- GraphicsWindowFrameSource verwirft alte WGC-Frames über QPC-Zeitstempel, prüft HWND/Geometrie/HDR vor und nach Readback. WindowCaptureDevice nutzt einen wiederverwendeten Stagingpuffer und einen abbrechbaren, begrenzten GPU-Readbackwait.
- OCR-Eingabekopien sind von Framelebensdauer getrennt; `OcrOperationLease` erhält native Inputs nach Timeout. Windows-OCR verhindert weitere parallele Erkennung auf derselben noch laufenden Engine.
- Exakte OCR-/Buffcaches und skalierte Templatecaches sind begrenzt, berücksichtigen Pixel/Typ/Größe und besitzen ihre Daten. Mat-Ansichten bleiben nach Cacheeviction gültig. Kein Befund für einen unbeschränkten neuen Cache oder falsche Pixelgleichheit.
- Optionales Buff-/Combat-/XP-/Agris-/Lootscroll-Sampling hat jeweils höchstens einen aktiven Worker; Epoch-/Generationsprüfungen verwerfen Ergebnisse vorangegangener Sessions. Native Eingaben werden nach tatsächlichem Workerabschluss entsorgt.
- Native Overlays zeichnen bei Unsichtbarkeit oder unveränderten Renderwerten bereits nicht dauernd neue Bitmaps. Eine pauschale Behauptung permanenter Overlayzeichnungs-CPU wäre falsch.

Nicht durchgeführt: Live-Gameplay-Profilerhebung, native Faultinjektion in WGC/DXGI, kontrollierte Release-vs-Release-Vergleiche, GPU-/Energieverbrauch oder Visualtests echter nativer Overlayfenster. Die konkrete Comparatorlücke und der Bannerabbruchpfad sind unabhängig davon reproduziert.
