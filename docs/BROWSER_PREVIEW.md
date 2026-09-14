# Browser-Vorschau

`src/BdoGrindTracker.BrowserPreview` startet die vorhandene Blazor-Oberfläche als
lokale interaktive Server-App auf `http://127.0.0.1:5180`. Sie läuft mit .NET 9
auf macOS, Linux und Windows. Ein Build der Windows-Lösung ist dafür nicht nötig.

```sh
dotnet run --project src/BdoGrindTracker.BrowserPreview
# macOS/Linux, auch mit SDK unter .artifacts/dotnet:
./scripts/Start-BrowserPreview.sh
```

Der Prozess bleibt während der Benutzung aktiv; `Strg+C` beendet ihn. Port 5180
muss frei sein. Die Vorschau bindet standardmäßig ausschließlich an die lokale
Loopback-Adresse und benötigt keine Anmeldung oder HTTPS-Zertifikatsfreigabe.

## Bedienung

- Live-Session, Verlauf, Garmoth und Einstellungen verwenden die vorhandenen
  Beispieldaten. Filter, Navigation und Mengenänderungen sind interaktiv.
- Unter **Overlay** lassen sich Module verschieben, skalieren und konfigurieren,
  Vorlagen anwenden und mehrere Layouts erstellen.
- **Im Browser ansehen** zeigt das ausgewählte Layout ohne Bearbeitungsgriffe.
  **Hintergrund** wechselt zwischen Spotbild, Dunkel, Hell und Transparenzraster.
  Tracking-Buttons simulieren Start/Pause. **Zurück zum Editor** oder Escape
  schließt die Ansicht und erhält das Layout.
- Jede Browser-Verbindung hat einen eigenen Demo-Dienst und eigene Overlay-Daten.
  Neuladen setzt beides zurück. Echte Sessions und Einstellungen werden nicht
  gelesen oder verändert. Auch eigene Overlay-Vorlagen sind nur temporär.

## Gemeinsame Oberfläche und Grenzen

Das Vorschauprojekt kompiliert die Razor-Komponenten und ihre Hilfsklassen direkt
aus `BdoGrindTracker.App`. CSS, JavaScript und Spielbilder werden beim Build aus
denselben Quellen ins Ausgabeverzeichnis kopiert. UI-Änderungen werden dadurch
beim nächsten Build in beiden Hosts übernommen. Die Vorschau enthält keinen
Nachbau des Dashboards oder des Editors.

`PreviewTrackerSession` und `OverlayService` sind pro Blazor-Circuit registriert.
Der Overlay-Dienst erhält keine Datei-Stores. `PreviewDataPaths` blockiert
versehentliche Zugriffe auf den echten Datenordner. Der Updater ist deaktiviert;
es werden keine Capture-/OCR-/Windows- oder nativen Overlay-Dienste registriert.
Links zu externen Websites bleiben normale, ausdrücklich anklickbare Links.

Die große Overlay-Ansicht verwendet `OverlayWidgetPreview`, `OverlayContentLayout`
und die vorhandene Größen-/Textanpassung aus `overlay-editor.js` ohne
Bearbeitungsgesten. Ein Timer aktualisiert die Uhr. Die Windows-App verwendet für
das tatsächliche Overlay weiterhin `NativeOverlayRenderer`: Schrift-Rasterung,
native Fenster, Bildschirmpositionen, Klickdurchleitung, Aufnahmeausschluss und
globale Hotkeys sind ausschließlich unter Windows real prüfbar. Im Editor bleiben
die entsprechenden Einstellungen zum Anschauen verfügbar.

## Prüfen

```sh
dotnet test tests/BdoGrindTracker.BrowserPreview.Tests
node --test tests/ui/*.test.cjs
dotnet publish src/BdoGrindTracker.BrowserPreview -c Release -o artifacts/browser-preview
```

Die portablen Tests prüfen direkte Seitenaufrufe, die Auslieferung gemeinsamer
Assets, unabhängige Demo-/Overlay-Daten sowie vorhandene Tests für Widget-Rendering
und Layoutskalierung. CI führt sie zusätzlich unter Linux aus. Die native
Windows-App wird weiterhin durch ihre vorhandenen Windows-Tests geprüft.
