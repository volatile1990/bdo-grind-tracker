# Grindcrest

Grindcrest ist ein lokaler, passiver Loot-Tracker für Black Desert auf Windows.
Er erkennt Drops aus dem Spielfenster und zeigt Lootmengen, aktive Grindzeit,
Silber und Stundenwerte im Dashboard und in anpassbaren Ingame-Overlays.

**Version 1.9.2:** [Übersichtlichere Einstellungen und zuverlässigere Rare-Drop-Erkennung](docs/release-notes/1.9.2.md).

## Funktionen

- Automatische Spoterkennung für die [40 unterstützten Referenz-Spots](docs/SCREENSHOT_SPOTS.md), deutsche und englische Itemnamen sowie manuelle Lootkorrekturen.
- Lokaler Verlauf mit Sessiondetails, Spotansicht und Silberbewertung für EU/NA einschließlich Steuern und Boni.
- Mehrere unabhängige Overlays mit frei angeordneten Kennzahlen, Lootlisten, Session-Timeline, Uhr und Tracking-Steuerung.
- Automatische Pause, Klassenerkennung und pausierte Wiederherstellung der aktuellen Session samt gespeicherter Dropzeiten nach einem Neustart.
- AP/DP aus der Spielanzeige mit farblich erkannter Kategorie (Allgemein, Edania, Halbmenschen oder Kamasilvia), live und zur jeweiligen Session gespeichert.
- Automatische Buff-Erkennung ohne manuelle Einrichtung, mit Verbrauchsicons, Mengen und Gesamtkosten im Live-Header, Verlauf und Overlay.
- Optionale automatische Grinderkennung mit sparsamer Bereitschaft und kurzer Lootprüfung bei einem Startverdacht.
- Optionaler Garmoth-Upload mit Vorschau, Bestätigung und Schutz vor doppelten Übertragungen; abgeschlossene Sessions können auf Wunsch automatisch hochgeladen werden.
- Windows-OCR-Installation mit Fortschrittsanzeige, erneuter Verfügbarkeitsprüfung und lokaler Diagnose.
- Deutsche und englische Oberfläche einschließlich Overlays, Zahlen- und Datumsformaten; die Auswahl wird lokal gespeichert.

## Installieren und starten

Benötigt werden **Windows x64 ab Windows 10 Version 2004**, Black Desert, WebView2
Evergreen und die Windows-OCR-Sprache des Spiels: Deutsch (`de-DE`) oder Englisch
(`en-US`). Grindcrest erkennt die Spielsprache aus der BDO-Konfiguration; eine
manuelle Auswahl steht unter **Einstellungen → Spielsprache in Black Desert** bereit.

Grindcrest wird ausschließlich über den
[Microsoft Store](https://apps.microsoft.com/detail/9NQPWC1CMWS0) installiert und
aktualisiert. Das Paket enthält .NET; WebView2 Evergreen ist ein eigener
Windows-Systembestandteil. Fehlt es, zeigt Grindcrest einen Installationshinweis.
[Details zur Store-Ausgabe](docs/MICROSOFT_STORE.md).

1. Black Desert öffnen und nicht minimieren. Den Haupt-Droplog in der BDO-Oberfläche sichtbar und eindeutig positionieren; die UI-Konfiguration speichern.
2. Grindcrest starten und unter **Live-Session** auf **Tracking starten** klicken. Falls angeboten, **OCR-Sprachpaket installieren** wählen und der Windows-Abfrage zustimmen. Der Fortschritt stammt von Windows; erst die erfolgreiche OCR-Prüfung gibt das Tracking frei.
3. Ab dem ersten neu gezählten Drop läuft die aktive Zeit. Aus dem Trashloot wird der Spot erkannt. Bei Varianten mit gleichem Trashloot das konkrete Gebiet vor einem Garmoth-Upload auswählen.
4. **Pausieren** erhält die Session und zieht die Zeit seit dem letzten erkannten Drop ab; **Fortsetzen** zählt ab dem nächsten neuen Drop weiter. Ohne neue Drops pausiert Grindcrest standardmäßig nach drei Minuten und zieht ebenfalls die abschließende Leerlaufzeit ab. Das Intervall ist einstellbar.
5. Vor einem Spot- oder Charakterwechsel **Neue Session** wählen. Beim nächsten Programmstart wird die zuletzt gespeicherte aktuelle Session pausiert geladen.

AP/DP werden während des Trackings aus der Anzeige links oben im Spielfenster
gelesen. Die Schriftfarbe bestimmt die Kategorie. Die Live-Ansicht zeigt bestätigte
Werte; im Verlauf bleibt der zuletzt bestätigte Stand der jeweiligen Session
erhalten. Dafür ist kein Garmoth-Build-Link nötig.
[Erkennung und Nachweisgrenzen](docs/COMBAT_STATS_HUD.md).

Die **Buff-Erkennung** liest Position und Sichtbarkeit der Buffleiste aus derselben
BDO-Konfiguration wie das Item-Drop-Log (`UIData`-Index 119 statt 159). Innerhalb
dieses Bereichs prüft sie Symbole und Restzeiten alle zehn Sekunden. Dafür ist kein persönliches Profil
und keine vorherige Screenshot-Kalibrierung nötig. Die **37 mitgelieferten
Client-Symbolvorlagen** decken alle 58 Einträge der Kostenliste ab: Cron-Mahlzeiten,
Harmony, Parfüme, kostenpflichtige Zeltbuffs und Mystic-Beasts-Schriftrollen.
Die drei Cron-Mahlzeiten, zehn Harmony-Varianten und sechs Mystic-Beasts-Effekte
haben jeweils eigene Symbole. **Adventure's Boon und Body Enhancement werden
unabhängig von der Restzeit ausschließlich als 300-Minuten-Variante erkannt und
zum entsprechenden NPC-Preis gezählt.** Das Unterschreiten kürzerer Kaufdauern
erzeugt dadurch keine andere Variante oder zusätzliche Buchung.
Bei Turning Gates wird die kleinste angebotene Kaufdauer angenommen, die die
zuerst gelesene Restzeit abdeckt: etwa 280 Minuten zu 300 Minuten und 160 Minuten
zu 180 Minuten. Grobe Stundenanzeigen berücksichtigen ihr Zeitintervall; passen
mehrere Laufzeitvarianten hinein, bleibt die Zuordnung unbekannt. Beim durchgängigen
Countdown bleibt diese Annahme bis zur bestätigten Erneuerung bestehen.
Identische Symbole mit gleicher Laufzeit, etwa bei einigen normalen und
unsterblichen Parfümen oder Glücksstufen, bleiben ohne eindeutige Zuordnung unbekannt.
Unter **Verbrauchte Items** im Kopf der Live-Session stehen kompakte Bufficons mit
der gezählten Menge in der Ecke und den Gesamtkosten daneben. Beim Überfahren
eines Icons erscheinen Name, Anzahl und gespeicherte Preise. Der Verlauf verwendet
dieselbe kompakte Anzeige für die gespeicherte Session; in der Sessiontabelle
eines Grindspots steht sie in der Spalte **Verbraucht**. Mengen und die beim
Erkennen gebuchten Kosten bleiben je Session erhalten. Das Overlaymodul
**Verbrauchte Items** zeigt dieselben Mengen und Kosten im Spiel. Fehlende Preise
bleiben als unbekannt oder Teilbetrag sichtbar. Die Angaben bleiben im Verlauf
gespeichert. Die fünf normalen und fünf unsterblichen
Harmony-Varianten werden bei eindeutigem Symbol getrennt erkannt und mit ihrem
jeweiligen Marktpreis bewertet. Gruppen-Harmony zählt bestätigte
Timer-Erneuerungen; das Symbol verrät nicht, welches Gruppenmitglied den
Gegenstand eingesetzt hat.

Die Kostenliste enthält 58 aktive Varianten (39 Markt-IDs) aus Cron-Mahlzeiten,
normalen und unsterblichen Harmony Draughts, Parfümen, kostenpflichtigen Zeltbuffs und
Mystic-Beasts-Schriftrollen. Das ist keine Zusage, jedes Symbol in jedem Layout
automatisch zu erkennen. Einzelne unlesbare Buff-Timer unterbrechen die Auswertung
anderer eindeutig erkannter Buffs nicht. **Anfangs aktive Buffs zählen nicht als
Verbrauch.** Ihre ersten passenden Beobachtungen bilden nur den Ausgangszustand.
Später neu auftauchende Buffs zählen nach zwei passenden Befunden, wenn die
vorherige lesbare Prüfung ihre Abwesenheit zeigte und der erste Timer nahe der
vollen erkannten Laufzeit liegt. Spät lesbare Teil-Timer sowie erstmals nach
Pause oder Erkennungslücke erkannte Buffs bleiben ungezählte Ausgangszustände.
Ein höherer erneut gelesener Timer zählt sofort als neue Anwendung. Die zugehörige
Laufzeitvariante folgt der obigen Zuordnung und wird in Live-Session, Verlauf und
Overlay getrennt gezählt. Nicht über Symbol oder Dauer zuordenbare Varianten
bleiben als unbekannte Gruppe ohne Preis sichtbar. Gespeicherte Buchungen und
Preise, einschließlich historischer Erstanrechnungen, bleiben unverändert.
Die Erkennung läuft immer automatisch; eine separate Buff-Einstellung oder
Profilauswahl ist nicht erforderlich. Frühere manuelle Profile werden nicht mehr
ausgewertet, vorhandene Dateien bleiben erhalten. Für gespeicherte Sessions bleiben 61 historische Einträge
lesbar; bereits gespeicherte Buchungen und ihre Preise bleiben unverändert.
[Automatische Erkennung und Grenzen](docs/BUFF_TRACKING.md).

In der **Live-Session** neben **Tracking starten** und **Neue Session** lässt sich
**Grind automatisch erkennen** ein- und ausschalten. Die Option ist standardmäßig aus. Während der
Bereitschaft wird der Lootbereich nur sparsam geprüft, solange Black Desert im
Vordergrund ist; ein möglicher Drop löst eine kurze Texterkennung aus. Bestätigter
Monsterloot startet sofort eine neue Session oder setzt eine automatisch pausierte
Session fort. Eine neue automatische Session wird nach fünf getrennten Drops
gespeichert; vorher wird sie nach einer Minute ohne neuen Drop verworfen.
Auch nach manuellem Pausieren bleibt die Automatik bereit; ausgeschaltet wird sie
nur über den Schalter. Kurz sichtbare erste Drops können wegen der sparsamen
Prüfung fehlen; die Sessionzeit beginnt mit dem ersten bestätigten Drop.
[Ablauf, Ressourcen und Grenzen](docs/AUTO_START.md).

Unter **Overlay** lassen sich Fenster erstellen, konfigurieren und am Desktop
vorab ansehen. Neue Overlay-Fenster sind zunächst ausgeschaltet.
[Bedienung der Overlays](docs/OVERLAY.md).

Unter **Einstellungen → Erscheinungsbild** lassen sich die Themes für das
Hauptfenster und alle Overlays getrennt wählen. Mit **Wie Hauptfenster** folgen
die Overlays automatisch der Oberfläche. **Grindcrest** behält das bisherige
Design bei und bleibt die Voreinstellung. **Black Desert** verwendet dunkle, kantige
Spielfenster, feine Rahmen, helle Schrift und eingelassene Inventarfelder.
**Light** bietet helle Flächen mit dunkler Schrift und blauen Akzenten.
**Katzen** zeigt große Kitten-Illustrationen auf warmen Espresso- und Leinenflächen,
mit Pfotenspuren und Ziernähten in Oberfläche und Overlay.
**Obsidian** kombiniert fast schwarze Flächen mit kühlen Akzenten,
**Kamasylvia** dunkles Waldgrün mit Elfenbein und **Valencia** warme Sandflächen
mit Terrakotta und dunkler Schrift.
Beide Auswahlen gelten sofort, auch während einer Session, und werden in der
Windows-App gespeichert. In der Browser-Vorschau gelten sie für die aktuelle Verbindung.
[Themes und Darstellung](docs/THEMES.md).

### Einführung beim ersten Start

Neue Installationen öffnen automatisch die Einführung. Sie führt in fünf Schritten
durch Erscheinungsbild und Sprache, sichtbare Droplogs in BDO, Spielsprache und
Erfassungsprüfung, Silberbewertung sowie den Start der ersten Session. Die
Oberfläche startet auf Englisch; Deutsch lässt sich direkt im ersten Schritt wählen.
Eine bereits gespeicherte Sprachwahl bleibt erhalten.

Das normale Droplog und das Special-Droplog müssen frei sichtbar bleiben: Menüs,
Inventar, Chat und andere Anzeigen dürfen die Lootzeilen nicht verdecken.
Die Einführung erklärt das Speichern der BDO-Oberfläche und bietet die vorhandene
Erfassungsvorschau zum Prüfen beider Bereiche an. Tracking wird anschließend in
der Live-Session manuell gestartet. Unter **Einstellungen** lässt sich die
Einführung jederzeit erneut öffnen.

Unter **Einstellungen → Erscheinungsbild → Sprache der Oberfläche** lässt sich
zwischen **Deutsch** und **English** wechseln. Die Änderung gilt sofort, auch
während einer Session, und bleibt nach einem Neustart erhalten. Englisch ist
die Voreinstellung; eine gespeicherte Auswahl von Deutsch bleibt erhalten.
Die BDO-Spiel-/OCR-Sprache und die dazu passenden Itemnamen
werden weiterhin separat eingestellt. In der Browser-Vorschau gilt die Auswahl
bis zum Neuladen. [Lokalisierung erweitern](docs/LOCALIZATION.md).

Grindcrest merkt sich beim Beenden Position, Größe und Maximierung des
Hauptfensters. Beim nächsten Start wird diese Anordnung wiederhergestellt;
ist der bisherige Bildschirm nicht mehr angeschlossen, bleibt das Fenster
auf einem verfügbaren Bildschirm sichtbar.

Updates werden in der App angeboten und setzen eine pausierte Session voraus.
Angeboten werden die von Microsoft für die Installation freigegebenen
Store-Updates. Bei Problemen mit dem Sprachpaket,
auch bei **Installed** und weiterhin wartender Anzeige, hilft die
[OCR-Installationsdiagnose](docs/OCR_INSTALLATION_TROUBLESHOOTING.md).

## Erkennung und Genauigkeit

Windows OCR liest die sichtbaren Lootzeilen. Auffällige Mengen werden zusätzlich
lokal mit einem mitgelieferten Paddle-ONNX-Modell geprüft. Rare-/Special-Lesungen
benötigen immer zwei übereinstimmende Paddle-Lesungen aus Farb- und Graustufenbild,
auch bei einem vermeintlich sicheren Windows-OCR-Treffer. Widersprüchliche oder
nicht bestätigte Rare-Lesungen werden nicht gezählt. Python und ein
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
[Dropmengen](docs/DROP_QUANTITIES.md) · [Droplog-Zuordnung](docs/LOOT_SOURCES.md) ·
[Spielsprachen](docs/GAME_LANGUAGES.md).

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

Einstellungen, aktuelle Session, Verlaufssessions, Overlay-Layouts und das
Garmoth-Uploadjournal werden lokal gespeichert. Für den Verlauf gibt es keine
künstliche Grenze für die Sessionanzahl oder Dateigröße. Ältere Einträge werden
nicht automatisch entfernt; eine große Verlaufsdatei wird weder gekürzt noch
allein aufgrund ihrer Größe mit einer Fehlermeldung abgewiesen.
Der optionale Garmoth-Key ist mit Windows-DPAPI an den aktuellen Windows-Benutzer gebunden.

| Ausgabe | Datenordner |
| --- | --- |
| Microsoft Store | `%LOCALAPPDATA%\Packages\<Paketfamilie>\LocalState` |
| Lokaler Entwicklungsbuild | `%LOCALAPPDATA%\BdoGrindTracker` |

Die Store-Ausgabe übernimmt beim ersten regulären Start vorhandene Tracker-Daten
aus dem bisherigen Ordner; die Quelldateien bleiben erhalten. Anschließend sind
Quelldaten und Store-Daten unabhängig. Vor einer Deinstallation
wichtige Daten bei geschlossener App durch Kopieren des Datenordners sichern.
Details zur Übernahme stehen in der [Store-Dokumentation](docs/MICROSOFT_STORE.md).

Die optionale **Loot-Diagnose** speichert Lootausschnitte und Beobachtungen im
Unterordner `diagnostics` des tatsächlich verwendeten Datenordners. Den vollständigen
Pfad zeigt **Einstellungen**. Die Aufzeichnung ist beim Start und für jede neue
Session ausgeschaltet. Aktivierte Aufnahmen wachsen bis zum Sessionende ohne
Gesamtgrößenlimit. Ein kleiner lokaler Fehlernachweis `last-capture-error.json`
kann auch ohne Aufzeichnung entstehen; er enthält keine Bilder oder OCR-Texte.
Die ebenfalls optionale **Rotation-Monitor-Diagnose** speichert dort in einem eigenen
Ordner `rotation-…` die Meldungsausschnitte der Rotationserkennung
([Details](docs/HERMESIA_ROTATION.md#diagnose)).
[Aufzeichnung, Replay und historische Formatdetails](docs/IMPLEMENTATION_HISTORY.md#lokale-diagnose-und-replay).

Unter **Einstellungen → Diagnose** lässt sich außerdem das **automatische Debug-Logging**
aktivieren. Die Einstellung bleibt über neue Sessions und App-Neustarts erhalten und
kann während einer Session geändert werden. Die Textlogs enthalten Loot-Erkennungsdaten,
Session- und HUD-Zustände (einschließlich Buffs und Rotation) sowie Statusmeldungen und
Erfassungsfehler, aber keine Bilder oder API-Schlüssel. Sie liegen unter
`diagnostics/debug-logs/session-<Session-ID>` im angezeigten Datenordner: Jede Session
bekommt einen eigenen Ordner, auch wenn mehrere Sessions in dieselbe Stunde fallen.
Pausieren, Fortsetzen und Wiederherstellen derselben Session verwenden denselben Ordner.
Allgemeine App-Meldungen außerhalb einer Session liegen direkt unter `diagnostics/debug-logs`.
Die Aufbewahrung beträgt standardmäßig **3 Stunden** und ist von **1 bis 168 Stunden**
einstellbar. Einträge außerhalb dieses
rollierenden Zeitfensters werden in allen Sessionordnern beim Start, beim Ändern der Einstellung
und während die App läuft minütlich gelöscht. Leere Sessionordner werden ebenfalls entfernt,
auch während Pausen und nach dem Ausschalten der Aufzeichnung. Bei geschlossener App
erfolgt die nächste Bereinigung beim nächsten Start.
Manuelle Bilddiagnosen und gespeicherte Grind-Sessions bleiben davon unberührt.

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

Das Store-Buildskript führt die Lösungstests und `Test-Ui.ps1` aus und prüft die
Paketinhalte. Mit **`-RequireWindowsOcr`** schlägt es bei fehlenden nativen
OCR-Voraussetzungen fehl:

```powershell
./scripts/Build-StoreRelease.ps1 -Version 1.9.2 -RequireWindowsOcr
```

Das erzeugte MSIX wird anschließend im Partner Center eingereicht. Ein lokaler
Paketbuild veröffentlicht nichts. GitHub dient der Quellcodeverwaltung und CI;
die Verteilung und Updates übernimmt ausschließlich Microsoft Store.
[Store-Release erstellen](docs/RELEASING.md) · [Paketierung und Einreichung](docs/MICROSOFT_STORE.md).

Weitere Details: [Silberbewertung](docs/SILVER_VALUATION.md),
[Klassenerkennung](docs/CLASS_DETECTION.md), [HDR-Aufnahme](docs/HDR_CAPTURE.md) und
[Implementierungs- und Versionsgeschichte](docs/IMPLEMENTATION_HISTORY.md).
Die Geschichte bewahrt den ausführlichen README-Stand bis 1.4.0 einschließlich
historischer Zählverfahren, Messwerte und Versionsentscheidungen.
