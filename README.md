# Grindcrest

Grindcrest ist ein lokaler, passiver Loot-Tracker für Black Desert auf Windows.
Er erkennt Drops aus dem Spielfenster und zeigt Lootmengen, aktive Grindzeit,
Silber und Stundenwerte im Dashboard und in anpassbaren Ingame-Overlays.

**Version 1.7.0:** [Rotation Monitor für Aphrodon und aktualisierte Klassenangaben im Verlauf](docs/release-notes/1.7.0.md).

## Funktionen

- Automatische Spoterkennung für die [40 unterstützten Referenz-Spots](docs/SCREENSHOT_SPOTS.md), deutsche und englische Itemnamen sowie manuelle Lootkorrekturen.
- Lokaler Verlauf mit Sessiondetails, Spotansicht und Silberbewertung für EU/NA einschließlich Steuern und Boni.
- Mehrere unabhängige Overlays mit frei angeordneten Kennzahlen, Lootlisten, Silberverlauf, Uhr und Tracking-Steuerung.
- Automatische Pause, Klassenerkennung und pausierte Wiederherstellung der aktuellen Session nach einem Neustart.
- Optionaler Garmoth-Upload mit Vorschau, Bestätigung und Schutz vor doppelten Übertragungen; automatische Stundenuploads sind separat einschaltbar.
- Windows-OCR-Installation mit Fortschrittsanzeige, erneuter Verfügbarkeitsprüfung und lokaler Diagnose.

## Installieren und starten

Benötigt werden **Windows x64 ab Windows 10 Version 2004**, Black Desert, WebView2
Evergreen und die Windows-OCR-Sprache des Spiels: Deutsch (`de-DE`) oder Englisch
(`en-US`). Grindcrest erkennt die Spielsprache aus der BDO-Konfiguration; eine
manuelle Auswahl steht unter **Einstellungen → Spielsprache in Black Desert** bereit.

Den Installer **`Grindcrest-win-x64-stable-Setup.exe`** aus den
[GitHub Releases](https://github.com/volatile1990/bdo-grind-tracker/releases) laden.
Das Setup enthält .NET und installiert WebView2 sowie die Visual-C++-Laufzeit bei
Bedarf nach. Die [Microsoft-Store-Ausgabe](docs/MICROSOFT_STORE.md) verwendet einen
eigenen Installations- und Updateweg.

1. Black Desert öffnen und nicht minimieren. Den Haupt-Droplog in der BDO-Oberfläche sichtbar und eindeutig positionieren; die UI-Konfiguration speichern.
2. Grindcrest starten und unter **Live-Session** auf **Tracking starten** klicken. Falls angeboten, **OCR-Sprachpaket installieren** wählen und der Windows-Abfrage zustimmen. Der Fortschritt stammt von Windows; erst die erfolgreiche OCR-Prüfung gibt das Tracking frei.
3. Ab dem ersten neu gezählten Drop läuft die aktive Zeit. Aus dem Trashloot wird der Spot erkannt. Bei Varianten mit gleichem Trashloot das konkrete Gebiet vor einem Garmoth-Upload auswählen.
4. **Pausieren** erhält die Session und zieht die Zeit seit dem letzten erkannten Drop ab; **Fortsetzen** zählt ab dem nächsten neuen Drop weiter. Ohne neue Drops pausiert Grindcrest standardmäßig nach drei Minuten und zieht ebenfalls die abschließende Leerlaufzeit ab. Das Intervall ist einstellbar.
5. Vor einem Spot- oder Charakterwechsel **Neue Session** wählen. Beim nächsten Programmstart wird die zuletzt gespeicherte aktuelle Session pausiert geladen.

Unter **Overlay** lassen sich Fenster erstellen, konfigurieren und am Desktop
vorab ansehen. Neue Overlay-Fenster sind zunächst ausgeschaltet.
[Bedienung der Overlays](docs/OVERLAY.md).

Unter **Einstellungen → Erscheinungsbild** lässt sich das Theme für die gesamte
Oberfläche und alle Overlays wechseln. **Grindcrest** behält das bisherige Design
bei und bleibt die Voreinstellung. **Black Desert** verwendet dunkle, kantige
Spielfenster, feine Rahmen, helle Schrift und eingelassene Inventarfelder.
**Light** bietet helle Flächen mit dunkler Schrift und blauen Akzenten.
**Katzen** zeigt große Kitten-Illustrationen auf warmen Espresso- und Leinenflächen,
mit Pfotenspuren und Ziernähten in Oberfläche und Overlay.
Die Auswahl gilt sofort, auch während einer Session, und wird in der Windows-App
gespeichert. In der Browser-Vorschau gilt sie bis zum Neuladen.
[Themes und Darstellung](docs/THEMES.md).

Grindcrest merkt sich beim Beenden Position, Größe und Maximierung des
Hauptfensters. Beim nächsten Start wird diese Anordnung wiederhergestellt;
ist der bisherige Bildschirm nicht mehr angeschlossen, bleibt das Fenster
auf einem verfügbaren Bildschirm sichtbar.

Updates werden in der App angeboten und setzen eine pausierte Session voraus.
Die GitHub-Ausgabe verwendet ihren gewählten Stable-/Beta-Kanal; die Store-Ausgabe
bietet von Microsoft freigegebene Updates an. Bei Problemen mit dem Sprachpaket,
auch bei **Installed** und weiterhin wartender Anzeige, hilft die
[OCR-Installationsdiagnose](docs/OCR_INSTALLATION_TROUBLESHOOTING.md).

## Erkennung und Genauigkeit

Windows OCR liest die sichtbaren Lootzeilen. Auffällige Mengen werden zusätzlich
lokal mit einem mitgelieferten Paddle-ONNX-Modell geprüft; Python und ein
Modell-Download beim Start sind nicht erforderlich. Wiederholte Bild- und
Textbeobachtungen bestätigen Drops. Noch offene Lesungen können später korrigiert
werden; manuelle Korrekturen bleiben getrennt erhalten.

Kurz sichtbare, überdeckte oder falsch gelesene Drops können fehlen oder zu viel
gezählt werden. Auf vier vollständig ausgewerteten Referenzaufnahmen wurden
**576 / 264 / 2.038 / 604 Helme** bei Sollwerten von **576 / 324 / 2.050 / 604**
gezählt: zwei exakte Endmengen, zwei Unterzählungen. Eine generell fehlerfreie
Zählung ist damit nicht belegt. Neue Sessions mit den Inventarmengen vergleichen;
automatisierte Tests und erfolgreiche Replays ersetzen diesen Abgleich nicht.
[Messwerte und Vergleichsgrenzen](docs/OCR_COMPARISON_20260911.md).

Die Aufnahme benötigt einen korrekt eingerichteten Haupt-Droplog. Änderungen an
Schrift, Skalierung, Auflösung oder abgeschnittenen Zeilen können die Erfassung
anhalten; die Live-Ansicht nennt die nötige Korrektur. Spielinterne Meldungen können
den Lootfeed überdecken. [Erkennungsverfahren](docs/LIFETIME_LOOT_TRACKING.md) ·
[Dropmengen](docs/DROP_QUANTITIES.md) · [Spielsprachen](docs/GAME_LANGUAGES.md).

Unter **Einstellungen → Erfassungsbereiche prüfen** zeigt eine Einzelaufnahme
die erfassten Bereiche: Türkis markiert das normale Droplog, Gold die Zeile des
Special-Droplogs. Beide Ausschnitte erscheinen zusätzlich einzeln. Zum Prüfen
das Tracking pausieren, die Lootanzeigen in BDO unter **UI bearbeiten** einblenden
und **Vorschau aktualisieren** wählen. Eine passende Auflösung allein bestätigt
noch nicht, dass die gespeicherte Position zur aktuellen Spieloberfläche passt.

Der goldene Rahmen zeigt den **Textbereich** des Special-Droplogs. Prüfe mit einer
tatsächlichen Dropmeldung, ob Gegenstandsname und Menge vollständig darin liegen.
Das Gegenstandssymbol und die Verzierung dürfen abgeschnitten sein; sie werden
für die Texterkennung nicht benötigt. Die graue Fläche aus **UI bearbeiten** muss
nicht genau zum goldenen Rahmen passen.

Die Konfigurationssuche listet alle `gameVariable.xml` unter dem BDO-`UserCache`,
einschließlich Unterordnern, mit Speicherzeit, Auflösung, UI-Skalierung,
Erfassungspositionen und Special-Droplog-Status. Die kompakte Tabelle zeigt
zunächst verwendbare Dateien. Suche, Statusfilter und Sortierung grenzen größere
Listen ein; vollständige Pfade und deutsche Fehlerhinweise stehen unter
**Details**. Der Filter **Beide Droplogs** zeigt Dateien mit beiden gespeicherten
Positionen. Eine Datei markieren oder über
**Datei auswählen…** öffnen, ihre Vorschau prüfen und mit **Verwenden** übernehmen.
Die feste Auswahl bleibt nach einem Neustart erhalten; **Automatische Auswahl**
verwendet wieder das zuletzt gespeicherte Profil. Eine andere Datei lässt sich
vor dem Start einer neuen Session übernehmen. BDO-Dateien werden nur gelesen;
Vorschaubilder werden nicht auf Datenträger gespeichert.

## Daten und Diagnose

Einstellungen, aktuelle Session, bis zu **500 Verlaufssessions**, Overlay-Layouts
und das Garmoth-Uploadjournal werden lokal gespeichert. Der optionale Garmoth-Key
ist mit Windows-DPAPI an den aktuellen Windows-Benutzer gebunden.

| Ausgabe | Datenordner |
| --- | --- |
| GitHub / entpackter Build | `%LOCALAPPDATA%\BdoGrindTracker` |
| Microsoft Store | `%LOCALAPPDATA%\Packages\<Paketfamilie>\LocalState` |

Die Store-Ausgabe übernimmt beim ersten regulären Start vorhandene Tracker-Daten
aus dem bisherigen Ordner; die Quelldateien bleiben erhalten. Anschließend sind
die Daten beider Ausgaben unabhängig. Vor einem Wechsel oder einer Deinstallation
wichtige Daten bei geschlossener App durch Kopieren des Datenordners sichern.
Details zur Übernahme stehen in der [Store-Dokumentation](docs/MICROSOFT_STORE.md).

Die optionale **Loot-Diagnose** speichert Lootausschnitte und Beobachtungen im
Unterordner `diagnostics` des tatsächlich verwendeten Datenordners. Den vollständigen
Pfad zeigt **Einstellungen**. Die Aufzeichnung ist beim Start und für jede neue
Session ausgeschaltet. Aktivierte Aufnahmen wachsen bis zum Sessionende ohne
Gesamtgrößenlimit. Ein kleiner lokaler Fehlernachweis `last-capture-error.json`
kann auch ohne Aufzeichnung entstehen; er enthält keine Bilder oder OCR-Texte.
[Aufzeichnung, Replay und historische Formatdetails](docs/IMPLEMENTATION_HISTORY.md#lokale-diagnose-und-replay).

Der Tracker verarbeitet sichtbare Pixel und liest die BDO-UI-Konfiguration.
Bilddaten bleiben ohne aktivierte Diagnose im Arbeitsspeicher. Die lokale
Oberfläche läuft in Blazor Hybrid/WebView2 ohne Webserver. Netzwerkzugriffe dienen
Updates, öffentlichen Referenz-/Preisdaten und ausdrücklich konfigurierten
Garmoth-Uploads; eine Cloud-Synchronisierung des Verlaufs findet nicht statt.
[Sicherheitsgrenze](docs/SAFETY.md) · [Garmoth-Integration](docs/GARMOTH_INTEGRATION.md).

## Entwickeln und prüfen

### Browser-Vorschau auf macOS, Linux und Windows

Die Oberfläche und der Overlay-Editor können mit Beispieldaten im Browser gestartet
werden. Benötigt wird nur das **.NET-SDK 9.0.318 oder neuer**; Windows, BDO und OCR
sind für die Vorschau nicht erforderlich.

```sh
dotnet run --project src/BdoGrindTracker.BrowserPreview
```

Auf macOS/Linux alternativ `./scripts/Start-BrowserPreview.sh`. Das Skript findet
auch ein lokal unter `.artifacts/dotnet` installiertes SDK.
Anschließend **http://127.0.0.1:5180** öffnen. Unter **Overlay → Im Browser ansehen**
erscheint das ausgewählte Overlay ohne Bearbeitungsgriffe, vor einem Spotbild oder
einem hellen, dunklen bzw. karierten Hintergrund. Der Editor unterstützt weiterhin
Module, Größen, Positionen, Vorlagen und mehrere Overlay-Layouts. Mit **Strg+C**
wird der Vorschauprozess beendet.

Dashboard, Verlauf, Einstellungen und Overlay-Widgets stammen aus denselben
Razor-, CSS- und JavaScript-Dateien wie die Windows-App. Änderungen und eigene
Vorlagen gelten nur für die aktuelle Browser-Verbindung und gehen beim Neuladen
verloren; andere Tabs starten unabhängig. Echte Tracker-Daten werden nicht geöffnet.
Aufnahme, OCR und Uploads sind simuliert, Updates deaktiviert. Die Overlay-Vorschau
verwendet die gemeinsame Layoutlogik, aber keine nativen Windows-Fenster: Schrift,
Rendering, globale Tastenkürzel und Klickdurchleitung müssen unter Windows geprüft
werden. [Technik und Prüfung](docs/BROWSER_PREVIEW.md).

### Windows-App

Zusätzlich zu den Laufzeitvoraussetzungen werden das **.NET-SDK 9.0.318 oder neuer**, **PowerShell
7.2+** und **Node.js** für die JavaScript-Interaktionstests benötigt. CI verwendet
Node.js 24. Ein Store-Build benötigt außerdem das Windows SDK ab 10.0.19041.0 mit
MakeAppx und MakePri.

```powershell
dotnet restore BdoGrindTracker.slnx
dotnet test BdoGrindTracker.slnx -c Release
./scripts/Test-Ui.ps1
dotnet run --project src/BdoGrindTracker.App
```

Die Lösungstests prüfen unter anderem Zählung, Speicherung, Sessionsteuerung und
Razor-Komponenten. `Test-Ui.ps1` prüft JavaScript-Interaktionen mit Node.js.
Fehlende Windows-OCR-Sprachen erscheinen bei betroffenen nativen Tests sichtbar als
übersprungen. Für eine Prüfung, die diese Voraussetzungen verbindlich verlangt,
vor dem Testlauf `GRINDCREST_REQUIRE_WINDOWS_OCR=1` setzen. Native Fensteraufnahme,
reale OCR-Genauigkeit und ein echtes Store-Upgrade benötigen zusätzlich praktische
Windows-Prüfungen. [Architektur und UI-Prüfung](docs/BLAZOR_HYBRID.md).

Die Release-Skripte führen die Lösungstests und `Test-Ui.ps1` aus und prüfen die
Paketinhalte. Mit **`-RequireWindowsOcr`** schlagen sie bei fehlenden nativen
OCR-Voraussetzungen fehl. Beispiel für den Store:

```powershell
./scripts/Build-StoreRelease.ps1 -Version 1.7.0 -RequireWindowsOcr
```

Das erzeugte MSIX wird anschließend im Partner Center eingereicht. Ein lokaler
Paketbuild veröffentlicht nichts. Für den GitHub-Installer dient
`./scripts/Build-Release.ps1 -Version 1.7.0 -RequireWindowsOcr`.
[GitHub-Releases](docs/RELEASING.md) · [Store-Paketierung](docs/MICROSOFT_STORE.md).

Weitere Details: [Silberbewertung](docs/SILVER_VALUATION.md),
[Klassenerkennung](docs/CLASS_DETECTION.md), [HDR-Aufnahme](docs/HDR_CAPTURE.md) und
[Implementierungs- und Versionsgeschichte](docs/IMPLEMENTATION_HISTORY.md).
Die Geschichte bewahrt den ausführlichen README-Stand bis 1.4.0 einschließlich
historischer Zählverfahren, Messwerte und Versionsentscheidungen.
