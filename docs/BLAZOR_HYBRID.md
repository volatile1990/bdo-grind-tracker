# Blazor Hybrid in Grindcrest

Version 0.10.0-test.1 ersetzt das gesamte bisherige WinForms-Frontend durch lokale
Blazor-Komponenten. WinForms stellt noch das native Fenster, DPI-Skalierung,
Fenster-Lebenszyklus und den WebView2-Host bereit. Die OCR- und Zählerimplementierung
aus dem bestätigten Stand 0.9.6-test.2 bleibt unverändert.

## Aufbau

- `UI/HybridMainForm.cs`: nativer Host, Initialisierung, erste erfolgreiche Darstellung,
  Timer und geordnetes Beenden. Die Fenstergröße berücksichtigt die Monitor-DPI.
- `Application/TrackerContracts.cs`: Zustands-Snapshots, Einstellungen und asynchrone
  Befehle über `ITrackerSession`; der gespeicherte API-Schlüssel ist kein UI-Zustand.
- `Application/TrackerSessionService*.cs`: Capture-Lebenszyklus, Mailbox, Sitzungsuhr,
  Auto-Pause, Verlauf, Preisbewertung und Garmoth. Der Dienst kennt keine Controls.
- `Components/`: Live-Dashboard, Loot-Tabelle, Spot-/chronologischer Verlauf,
  Garmoth-Bereich, Einstellungen, Dialoge und gemeinsame Darstellung. Änderungen am Dienst werden
  über `Changed` auf den Blazor-Dispatcher übernommen.
- `wwwroot/`: lokale HTML-, CSS- und JavaScript-Dateien. JavaScript ist auf
  Dialog-Fokus und Scrollen beschränkt. Die vorhandenen Spiel- und Branding-Assets
  werden beim Build/Publish unter `wwwroot/assets/` mitgeliefert.

Das UI arbeitet ohne lokalen HTTP-Server, CDN oder JavaScript-Buildpipeline.
C# läuft im Desktop-Prozess, die Darstellung in WebView2. Technische Grundlage ist
[Microsofts Blazor-Hybrid-Host für Windows Forms](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/tutorials/windows-forms).

## Bedienung

**Live-Session** zeigt Spot und Klasse, aktive Zeit, Trash, Netto-Silber und Stundenwert.
Spotbild, Session-Status und Start/Pause bzw. neue Session bilden einen kompakten
Header mit integrierter Kennzahlenleiste. Direkt darunter stehen die Drops;
detaillierte Preishinweise sind bei den Session-Details zusammengefasst.
Die Loot-Tabelle unterstützt Suche, Sortierung und Gesamt-/Stundenwerte. Fehlende Preise
werden als fehlend oder Teilbetrag kenntlich gemacht. Start/Pause und neue Session
greifen auf dieselbe Sitzungssteuerung zu wie die automatische Pause.

Ein fehlender oder ungültiger Haupt-Droplog blockiert die Erfassung und erscheint
als roter Fehler in der Live-Ansicht.

Einstellungen werden automatisch gespeichert: Auswahllisten und Schalter sofort,
Zahlen und Garmoth-API-Schlüssel beim Verlassen des Feldes oder mit Enter. Die
früheren Speichern-/Verwerfen-Buttons entfallen. Jede Änderung übernimmt nur das
betroffene Feld ausgehend von den aktuellen Einstellungen. Ungültige Zahlen werden
nicht übernommen; fehlgeschlagene Speicherungen werden angezeigt. Die API-Schlüssel-
Eingabe wird nach erfolgreichem Speichern geleert, ein leeres Feld behält den
bisherigen Schlüssel. **Schlüssel entfernen** löscht ihn direkt und schaltet die
Stundenautomatik aus. Das separate **Automatik fortsetzen** ist eine bewusste
Freigabe nach einer eindeutig fehlgeschlagenen Übertragung.

Der Stift neben einer Lootmenge öffnet die Inline-Korrektur: ganze Gesamtmenge ab 0,
Enter oder Haken zum Speichern, Escape oder Kreuz zum Abbrechen. Auch in der
Stundenansicht wird ausdrücklich die Gesamtmenge bearbeitet. Das funktioniert
während der Erfassung, in der Pause und nach dem Abschluss. Drops, die während
der Eingabe dazukommen, bleiben erhalten: gespeichert wird die Differenz zum Wert
beim Öffnen. Korrekturen ändern Silberwerte, aber weder Dropzahl noch Inaktivitätsuhr.
Explizite Nullmengen bleiben zum erneuten Bearbeiten sichtbar und werden gespeichert.
Bei einem Speicherfehler bleibt die bisherige Menge erhalten und die Eingabe offen.

Die Zurück-/Vorwärts-Tasten einer Maus navigieren durch die besuchten Ansichten,
einschließlich Spot- und Sessiondetails.
Die Navigation verwendet den lokalen Browser-Verlauf; sie startet keine Aufnahme
neu und führt keine Session-Aktionen erneut aus. Eine neue Navigation nach „Zurück“
ersetzt den bisherigen Vorwärtsverlauf. Windows-Browserbefehle, die der WebView nicht
selbst verarbeitet, werden vom nativen Fenster an denselben Verlauf weitergereicht.

**Verlauf** bietet alle sechs Spotprofile sowie eine chronologische Gesamtliste,
Filter nach Zeitraum und Klasse, Kennzahlen und vollständige Lootdetails. Gespeicherte
Sessions lassen sich korrigieren und löschen. Einzelmengen sind direkt in der
Spotmatrix und in den Sessiondetails editierbar; in der Matrix lassen sich auch
bislang fehlende Items ergänzen. Die aktuelle Session verwendet dabei ihre
Live-Mengen. Nur das Löschen und der vollständige Bearbeitungsdialog bleiben für
die aktuelle Session gesperrt. Summen über mehrere Sessions sind reine Anzeigen.

**Garmoth** bündelt API-Schlüssel, Stundenautomatik, den manuellen Upload der aktuellen
Session und nachträgliche Uploads gespeicherter Sessions. Die Uploadliste lässt sich
nach Grindspot und Status filtern. Die aktuelle Session erscheint separat mit ihren
Gesamtwerten; gesendet wird nur ihr noch nicht übertragener Anteil. Bereits gesendete
automatische Stunden sperren einen späteren Gesamt-Upload des historischen Eintrags.
Der Schlüssel wird nie zurück in das Eingabefeld geladen. Entfernen und Speichern
schaltet auch die Automatik aus. Nur das Speichern im Garmoth-Bereich gibt eindeutig
fehlgeschlagene automatische Versuche wieder frei; unklare Ergebnisse bleiben gesperrt.

**Einstellungen** bündelt Monitor, Klasse, Auto-Pause, Event-Loot, Diagnoseaufzeichnung,
Marktregion und Steuern. Monitor, Lootfilter und Aufzeichnung werden vor einer
neuen Session festgelegt; die Klasse lässt sich vor dem Start oder während einer noch
nicht abgeschlossenen Pause korrigieren. Änderungen werden explizit gespeichert.

## Bestehende Daten und Voraussetzungen

Die bisherigen Dateien unter `%LOCALAPPDATA%\BdoGrindTracker` werden unverändert
weiterverwendet: Einstellungen, lokale Sessions, Preis-Cache und DPAPI-geschützter
Garmoth-Key. Die Diagnoseaufzeichnung bleibt ausdrücklich optional und startet nach
Programmstart und neuer Session ausgeschaltet.

Neu ist das Unterverzeichnis `webview2` für das Browserprofil des eingebetteten
WebView2. Es ist kein zweiter Session-Speicher; Sitzungen und Schlüssel verbleiben
in den bestehenden C#-Stores. Vorhandene Dateien werden beim UI-Umbau nicht migriert
oder zurückgesetzt.

Der Host benötigt die Microsoft Edge WebView2 Evergreen Runtime. Fehlt sie, erscheint
eine verständliche Startmeldung. Die Runtime ist separat erhältlich bei
[Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/).
Das selbstständige Windows-x64-Paket enthält .NET und ASP.NET Core; es braucht
keine separate .NET-Installation. Zum Entwickeln wird weiterhin das .NET 9 SDK benutzt.

## Build und Prüfung

```powershell
dotnet restore BdoGrindTracker.slnx
dotnet test BdoGrindTracker.slnx -c Release
dotnet publish src/BdoGrindTracker.App -c Release -r win-x64 --self-contained true -o artifacts/v0.10.0-test.1
& ./artifacts/v0.10.0-test.1/Grindcrest.exe --startup-smoke-test
& ./artifacts/v0.10.0-test.1/Grindcrest.exe --ui-smoke-test
```

`Grindcrest.exe` und `BdoGrindTracker.exe` sind gleichwertige Starter. Der gesamte
Publish-Ordner einschließlich `wwwroot`, `data` und DLLs gehört zur Anwendung.
Die EXE nicht allein aus dem Ordner kopieren.

Der Startup-Test prüft den echten OCR-Startpfad und die erste erfolgreiche
Blazor-Darstellung ohne Aufnahme. Der UI-Smoke-Test verwendet ausschließlich
Beispieldaten. Beide schließen nach erfolgreichem Rendern mit Exitcode 0.

Für interaktive UI-Prüfungen stehen isolierte Vorschauen bereit:

```powershell
& ./artifacts/v0.10.0-test.1/Grindcrest.exe --ui-preview
& ./artifacts/v0.10.0-test.1/Grindcrest.exe --ui-preview-empty
```

Diese Vorschauen nutzen `PreviewTrackerSession` und ändern keine Benutzerdaten.
Capture, Netzwerk und echte Uploads sind dort deaktiviert; Änderungen an Beispielen
leben ausschließlich im Arbeitsspeicher. Ein separates temporäres WebView-Profil
verhindert auch dort Änderungen am normalen UI-Profil.

Mit `--ui-debug-port=9225` lässt sich die eigene WebView zur UI-Prüfung über CDP
verbinden, etwa mit Playwright. `--ui-hidden` hält eine solche Vorschau unsichtbar.
Ohne expliziten Debug-Port wird kein Debug-Endpunkt eingerichtet.

Die Tests prüfen unveränderte Erkennungs-/Zählregeln, echte Service-Übergänge,
Stunden-Deltas, Fehler- und Shutdown-Fälle sowie Razor-Ausgabe und pure
Verlaufsberechnungen. Die früheren Tests für entfernte WinForms-Controls entfallen.
Die neue Darstellung wird zusätzlich in der echten WebView auf Navigation,
Formulare, Dialoge, Bilder und kompakte Fenstergrößen geprüft.
