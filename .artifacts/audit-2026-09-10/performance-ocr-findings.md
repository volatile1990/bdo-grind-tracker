# Teil-Audit: OCR, Capture, natives Overlay und Performance

Geprüft: aktueller Working Tree am 10.09.2026. Nur Code gelesen; isolierte Probe unter `.artifacts/audit-2026-09-10/performance-probe` angelegt. Keine getrackten Quellen oder Benutzereinstellungen verändert, keine Desktopaufnahme durchgeführt. Ergebnisse beziehen sich auf die vorhandenen Änderungen im Working Tree.

## Reproduzierte Befunde

### P2 – Änderungen am Rare-Droplog bleiben unbemerkt

`src/BdoGrindTracker.App/Analysis/LootPanelCaptureGuard.cs:49` vergleicht gespeicherte Kalibrierung mit aktueller Konfiguration, berücksichtigt aber weder `HasRareLootAnchor` noch `RareLootAnchorX/Y`. Der Analyzer berechnet Rare-Ausschnitte nur im Konstruktor (`CompanionLootFrameAnalyzer.cs:68`), nicht erneut im laufenden Betrieb. Der Reader liest diese Änderungen durchaus ein (`src/BdoGrindTracker.Ocr/CompanionCalibrationReader.cs:101`).

Repro: Bei unverändertem Hauptpanel Rare-X 800 auf 1100 ändern bzw. HasRareLootAnchor auf false setzen, nach zwei Sekunden Validate aufrufen. Beide Änderungen werden ohne Fehler akzeptiert; ein entsprechender Hauptankerwechsel wird korrekt zurückgewiesen. Nach Verschieben oder Ausblenden liest die App weiter den alten Rare-Bereich. War Rare bei Initialisierung deaktiviert, wird späteres Aktivieren ebenfalls nicht übernommen. Risiko: seltene Drops werden nicht mehr erfasst, ohne den vorgesehenen Hinweis auf geänderte BDO-Geometrie.

Abhilfe: Rare-Präsenz und Koordinaten in die Geometrieprüfung aufnehmen; alternativ im sicher angehaltenen Zustand neue Kalibrierung übernehmen. Regressionstests für Verschieben, Aus-/Einblenden des Rare-Panels ergänzen.

### P2 – OCR-Zeitbudgets brechen langsame Aufrufe nicht ab; Pause und Beenden haben keine Drain-Grenze

`BackgroundLootRowReview.cs:46` nennt ein Zwei-Sekunden-Budget, kontrolliert dieses aber nur vor `engine.Recognize` (`:167–170`). Wartezeit auf freie Worker (`:132`) gehört nicht zum gemessenen Budget. Der normale Recovery-Pfad hat denselben Mechanismus mit 120 ms (`NormalLootRecovery.cs:31`, `:43–50`). Windows OCR blockiert auf `.GetResult()` ohne eigene Deadline (`src/BdoGrindTracker.Ocr/CompanionWindowsOcrRecognizer.cs:167`). Die Live-Pipeline übergibt zur Analyse ausdrücklich `CancellationToken.None` (`Capture/PassiveCaptureSession.cs:180`). Pause (`Application/TrackerSessionService.cs:293`) und Shutdown (`:523`) warten auf das Ende.

Repro: Token-kooperativer Test-Recognizer wartet 2600 ms. Review läuft 2608,8 ms trotz Budget 2000 ms, Token.CanBeCanceled=false und Ergebnis no-consensus. Das ist kein gemessener Produktionshang, belegt aber, dass die deklarierte Grenze keinen Aufruf begrenzt. Ein hängender nativer/Windows-OCR-Aufruf kann damit das vollständige Pausieren und ordentliche Schließen blockieren; eine gefüllte Queue verlängert das Nachlaufen zusätzlich.

Abhilfe: eigene begrenzte Lifetime für Recognition-Aufrufe, getrennt vom normalen Stop-Drain; CancelAfter an unterstützte Backends weiterreichen und Timeout der optionalen Prüfung als fallback zur Baseline behandeln. Für Shutdown einen klaren Eskalationspfad mit Datenrettung vorsehen, ohne bereits erworbene Frames im normalen Pausefall still zu verwerfen. Kooperativen langsamen Recognizer und wartende Worker testen.

### OCR-Tests: reproduzierbarer Textfehler, aber kein nachgewiesener Loot-Zählfehler

Root-Suite meldet zwei fehlgeschlagene Tests in `SecondaryLootOcrRecognizerTests.cs:39` und `:84`. Probe bestätigt dieselbe synthetische Segoe-UI-Zeile: `Bruchstück x 12` wird `Bruchstüukx12`, Confidence 0,9024. Die Tests benutzen dieselbe Rendering-Routine; das zweite Scheitern allein beweist keinen Parallelitätsfehler. Sowohl `Bruchstück` als auch `Bruchstüuk` werden vom echten Gesamtkatalog abgelehnt. Der synthetische Beispieltext bezeichnet dort kein vollständiges Item. Die optionale Review akzeptiert außerdem erst Confidence >=0,95.

Folgerung: rotes Qualitäts-/Release-Gate ernst nehmen, Referenzbild/Renderingabhängigkeit prüfen, echte vollständige deutsche Katalognamen und Spiel-Crops mit erwarteter Zählung abdecken. Den Test nicht einfach lockern und den Fehler nicht als bewiesenen Verlust echter Drops darstellen.

## Weiterer konkreter Codebefund (statisch, kein DXGI-Repro)

### P3 – Bitmap verliert Ownership bei Fehler in ReleaseFrame

`PassiveScreenCapture.cs:248` erstellt das zurückzugebende Bitmap mit `return CopyFrame(resource)`. Danach kann der finally-Block bei `ReleaseFrame` einen Fehler werfen (`:255–261`). Der Return wird dann verworfen; das frisch erzeugte Bitmap wird keinem Besitzer übergeben und in diesem Pfad nicht explizit disposed. Bei AccessLost wird im äußeren Capture-Loop neu initialisiert. Das betrifft insbesondere Fehler-/Displaywechselpfade, nicht jeden normalen Frame. Abhilfe: Ergebnis lokal halten, Ownership erst nach erfolgreicher Freigabe übertragen und im Ausnahmefall Dispose; Fehlerpfad über eine kleine native Schnittstellenabstraktion testbar machen. Größe/Rate eines tatsächlichen Leaks wurde nicht gemessen.

## Performance: belegte Struktur und Messungen

- Ganzer Monitor statt ausschließlich benötigter Loot-Crops: Start mit `monitor.Bounds` (`TrackerSessionService.cs:266`), pro Frame neue Staging-Texture (`PassiveScreenCapture.cs:312`), danach vollständiges 32-bpp-Bitmap (`DesktopPixelConverter.cs:26`) und vollständiges BGR-Mat (`CompanionFrameDecoder.cs:61`). Vier wartende Frames plus Analyseframe sind begrenzt (`PassiveCaptureSession.cs:16`). Bei 3840x2160 sind das rechnerisch bis zu 158,2 MiB reine Bitmap-Pixel plus etwa 23,7 MiB aktives BGR-Mat, ohne GPU-/OCR-Puffer. Dies ist eine Größenrechnung, keine gemessene Prozessspeichernutzung. Optimierung: Staging-Texture bei gleicher Geometrie wiederverwenden, früh auf notwendige Crop-Union/Teilbilder beschränken und Koordinaten sauber remappen.
- Die Queue ist absichtlich bounded und verliert bereits aufgenommene Frames nicht. Bei dauerhaft langsamer Analyse wartet die Aufnahme auf freie Kapazität (`PassiveCaptureSession.cs:212`), sie stellt keine verlorene Zeit wieder her. Daher Queue-Alter, effektive Capture-Rate, OCR-Zeit p50/p95, maximale Pause-Drainzeit und Recovery-Fehler messbar machen, bevor Workerzahl/Queue verändert werden.
- Native Overlay: alle 500 ms (`HybridMainForm.cs:25`) wird bei Sichtbarkeit `Present` und unbedingtes `Render` aufgerufen (`NativeOverlayHost.cs:75`, `NativeOverlayForm.cs:78`). Dabei entstehen Bitmap und HBITMAP jeweils neu (`NativeOverlayRenderer.cs:17`, `NativeOverlayForm.cs:95`). MouseMove rendert zusätzlich synchron (`NativeOverlayForm.cs:170`). Unveränderte Pausen-Snapshots könnten über Dirty-/Zeitänderungsprüfung Wiederholungen sparen, bei Drag per begrenzter Renderfrequenz. Kein belegter großer CPU-Engpass.
- Potenzial für bounded Wiederverwendung identischer Zeilen-OCR-Ergebnisse bei identischen Crop-Pixeln plus Sprache/Skala/HDR-Konfiguration; Counter muss weiterhin jeden Frame mit unveränderter Reihenfolge sehen. Das ist eine Optimierungsidee, keine Empfehlung, identische Frames aus dem Counter zu entfernen.
- Optionale Diagnose zeichnet ohne Gesamtlaufzeit-/Größenlimit auf (`DiagnosticRecordingSession.cs:48`), erzeugt PNGs synchron im Analysepfad und flushes je Journalzeile (`:298–335`). Bei langer Aufzeichnung kann Dateimenge/Datenträger wachsen; kein gemessener Engpass. Nützlich: laufende Größe, freier Platz, Export/Paketierung, Cleanup mit Vorschau und optionales Limit. Bestehende Aufzeichnung explizit opt-in und nur kalibrierte Ausschnitte sind positiv.

Lokaler Release-Microbenchmark, 5 Warmups + 100 Samples, synthetische Bitmaps; keine Live-Capture-/GPU-Map-/UpdateLayeredWindow-Kosten, kein isolierter Maschinenzustand:

| Operation | Mittel | p95 |
| --- | ---: | ---: |
| BGRA zu BGR 1920x1080 | 0,685 ms | 0,737 ms |
| BGRA zu BGR 3840x2160 | 1,136 ms | 1,318 ms |
| Overlay compact, 360x260, offscreen | 0,557 ms | 0,686 ms |
| Overlay dashboard, 536x384, offscreen | 1,058 ms | 1,173 ms |
| Overlay loot, 360x424, offscreen | 0,830 ms | 0,929 ms |

Diese Teilschritte waren in dieser lokalen Stichprobe schnell. Aussagen über Gesamt-CPU, FPS-Verlust im Spiel, Langzeitspeicher oder ältere Hardware wären daraus nicht gedeckt.

## Gute Grundlage und offene Validierung

Positiv: begrenzte Framequeue, ein geordneter Counter-Consumer, unabhängige OCR-Engines pro Worker, immutable per-frame Policies, maximal aktuelle UI-Mailbox, lokales OCR-Modell, passive Capture, Ressourcenfreigabe auf Normalpfaden, HMAC-/Netzwerkfragen in diesem Teil nicht bewertet. Release-Workflow nutzt gepinnte Actions und getrennte Read-/Write-Jobs.

Noch erforderlich: echte BDO-Replays mit Ground Truth für Deutsch/Englisch, SDR/HDR, verschiedene UI-Skalierungen; Langzeitaufnahme plus GDI-/native Memory-/CPU-Messung; Displaywechsel/Lockscreen/Alt-Tab; stop während blockierender nativer OCR; zwei bis vier Stunden auf schwächerer CPU. Keine Live-Aufnahme oder Spielsteuerung wurde im Audit ausgelöst.

Verworfener Verdacht: `GetTensorTypeAndShape()` liefert in der tatsächlich eingebundenen ONNX Runtime 1.29.0 einen nicht-disposablen Value-Type. Kein fehlendes Dispose an diesem Aufruf behaupten; alte XML-Dokumentation ist hier irreführend.

Reproduktion: `dotnet run --project .artifacts/audit-2026-09-10/performance-probe/AuditProbe.csproj -c Release`; Microbenchmark mit `-- bench`. Konsolenausgaben in `results.txt` und `microbench.txt` neben dem Projekt.
