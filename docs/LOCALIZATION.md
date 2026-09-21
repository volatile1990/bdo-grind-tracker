# Oberflächensprache

Grindcrest unterstützt Englisch (`en`, Voreinstellung) und Deutsch (`de`).
Die Auswahl unter **Einstellungen → Erscheinungsbild → Sprache der Oberfläche**
wird als `UiLanguage` in `settings.json` gespeichert. Fehlende, leere oder
unbekannte Werte aus alten Dateien fallen auf Englisch zurück. Eine bereits
gespeicherte Auswahl von Deutsch bleibt erhalten. Ungültige Werte
bei einer Einstellungsänderung werden vor dem Speichern abgewiesen.

Die App-Sprache lässt sich während einer Session wechseln. Spielsprache,
OCR-Konfiguration, Itemnamen, Lootdaten, gespeicherte IDs und Garmoth-Daten bleiben
unabhängig davon. Die Browser-Vorschau hält ihre Auswahl pro Verbindung nur im
Arbeitsspeicher.

## Texte und Formatierung

`src/BdoGrindTracker.App/Localization/AppText*.cs` enthält die Übersetzungen.
Der ursprüngliche deutsche Text ist der Schlüssel; unbekannte Texte bleiben
unverändert. Komponenten verwenden `T("Text")` und für variable Inhalte
`F("{0} Sessions", count)`. Ganze Sätze übersetzen, keine Satzteile verketten.
Übersetzungen müssen dieselben nummerierten Platzhalter behalten.

`LocalizedComponentBase` und `TrackerComponentBase` lesen die aktuelle Sprache
der Session. `UiCulture` sowie die `Number`-/`Silver`-/Dauer-Helfer formatieren
die Anzeige. Statische Präsentationshilfen erhalten die Sprache als Argument.
Es werden keine globalen .NET-Kultureinstellungen geändert, sodass mehrere
Browser-Verbindungen unabhängig bleiben. Eingabewerte, CSS, JSON und andere
technische Formate bleiben invariant.

Native Overlays bekommen `UiLanguage` über ihren Snapshot. Ein Sprachwechsel
invalidiert den Rendercache. Der Browser aktualisiert außerdem `html.lang`.
Benutzerdefinierte Overlay-Namen und eigene Beschriftungen werden nicht übersetzt.

Bereits zusammengesetzte Statusmeldungen aus vorhandenen Diensten werden an der
Anzeigegrenze übersetzt. `AppText.MessageTemplates.cs` kennt dafür ausdrücklich
aufgeführte vollständige Meldungsformen und erhält externe Fehlerdetails und
Dateipfade. Neue Oberflächentexte sollten direkt `F` statt solcher Formen nutzen.

## Prüfen

```powershell
dotnet test tests/BdoGrindTracker.BrowserPreview.Tests
dotnet test tests/BdoGrindTracker.App.Tests
```

Die Tests prüfen Sprachwechsel in gerenderten Komponenten, Sprachisolation,
Formate und Ressourcenplatzhalter, Migration und Speicherung sowie native
Overlay-Aktualisierung. Zusätzlich beide Sprachen im Browser prüfen, da längere
Übersetzungen das Layout beeinflussen können.
