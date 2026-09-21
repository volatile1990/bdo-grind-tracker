# Themes

Unter **Einstellungen → Erscheinungsbild** lassen sich **Theme für das Hauptfenster**
und **Theme für die Overlays** getrennt wählen. Beide Auswahllisten bieten sieben Themes:

- **Grindcrest:** das bisherige Design, weiterhin Standard bei neuen und
  bestehenden Installationen.
- **Black Desert:** dunkle Spielfenster mit bronzefarbener Titelleiste,
  elfenbeinfarbener Schrift und kantigen Buttons. Drops erscheinen in quadratischen
  Inventarplätzen mit hellen, kühlen Konturen und schmalen Mengenzahlen unten rechts.
  Dezente kantige Konturen und eine leichte Abdunklung grenzen die einzelnen
  Komponenten innerhalb des gemeinsamen Fensters ab.
- **Light:** helle Flächen, dunkle Schrift und blaue Akzente für eine ruhige,
  gut lesbare Oberfläche und helle Overlay-Module.
- **Katzen:** große Kitten-Illustrationen, eine gemütliche Katzenecke in der
  Seitenleiste, Pfotenspuren und Ziernähte auf warmen Espresso- und Leinenflächen.
  Das Overlay verwendet dieselbe Illustration hinter den gut lesbaren Modulen.
- **Obsidian:** fast schwarze Flächen, klare Kontraste und kühle blaue Akzente.
- **Kamasylvia:** dunkles Waldgrün, warme Elfenbeintöne und dezente Blattdetails.
- **Valencia:** warme Sandflächen, Terrakotta und gut lesbare dunkle Schrift.

Das Hauptfenster-Theme gestaltet die Seiten, Dialoge und Bedienelemente des
Overlay-Editors. Das Overlay-Theme gilt für alle nativen Overlay-Fenster und ihre
Vorschauflächen im Editor und im Browser. **Wie Hauptfenster** ist der Standard:
Die Overlays folgen dann jedem Wechsel des Hauptfenster-Themes. Eine ausdrücklich
gewählte Overlay-Variante bleibt davon unabhängig; beispielsweise lassen sich
ein helles Hauptfenster und dunkle Overlays kombinieren.

Jeder Wechsel gilt sofort, auch während einer Session. Layouts,
Transparenzeinstellungen, Lootmengen und der Zustand der Session bleiben erhalten.
Die Windows-App speichert beide Auswahlen in den bestehenden Einstellungen;
die Browser-Vorschau hält sie wie ihre anderen Demo-Einstellungen nur für die
aktuelle Verbindung im Arbeitsspeicher.

Beim Black-Desert-Overlay ergänzt **Rahmen anzeigen** eine Titelleiste mit dem
jeweiligen Overlay-Namen. Sie liegt außerhalb der gespeicherten Inhaltsfläche:
links/rechts/unten kommen 2 px, oben 32 px hinzu. Breite, Höhe und Modulkoordinaten
im Editor bleiben Inhaltsmaße. Skalierung, Verschieben und Größenänderung rechnen
diesen zusätzlichen Rahmen mit ein. Ohne Rahmen entfällt auch die Titelleiste.
Das Katzen-Overlay verwendet dieselben Rahmenmaße mit einer eigenen Katzen-Titelleiste.
Light, Obsidian, Kamasylvia und Valencia verwenden wie Grindcrest die bisherige
Inhaltsfläche ohne zusätzliche Titelleiste.
Die letzte Reihe eines Drop-Rasters wird mit dekorativen leeren Inventarplätzen
ergänzt; diese erhöhen keine Lootmenge und keine angezeigte Itemanzahl.

Das Modul **Uhrzeit & Tag/Nacht** zeigt in allen Themes Stunden und Minuten.
Der Countdown wird auf die nächste volle Minute aufgerundet. Die aktive
Sessiondauer zeigt weiterhin Sekunden.

## Umsetzung

`Theming/AppThemes.cs` definiert die stabilen IDs `grindcrest`, `black-desert`,
`light`, `cats`, `obsidian`, `kamasylvia` und `valencia`.
`ThemeId` speichert das Hauptfenster-Theme; fehlende oder unbekannte Werte fallen
auf `grindcrest` zurück. Das optionale `OverlayThemeId` ist bei **Wie Hauptfenster**
`null`. Auch fehlende oder unbekannte gespeicherte Overlay-IDs folgen dem
Hauptfenster. Damit bleibt das bisherige Verhalten alter Einstellungsdateien
erhalten. Neue ungültige Eingaben werden vor dem Speichern abgewiesen.

Die gemeinsame Blazor-Oberfläche setzt das Theme auf dem HTML-Wurzelelement.
`wwwroot/themes.css` ergänzt eigene Regeln für die sechs weiteren Themes; die
bisherigen Styles bleiben die Grundlage von Grindcrest. Der Browser-Host lädt
dieselbe Datei. Eigenständige Theme-Bereiche um die Overlay-Vorschauen halten
deren Farben und Dekoration vom Hauptfenster unabhängig.
`EffectiveOverlayThemeId` löst die Auswahl samt **Wie Hauptfenster** auf und wird
über `OverlayMetrics` in jeden `OverlaySnapshot` übernommen.
`NativeOverlayRenderer` zeichnet die entsprechende Darstellung;
der Rendercache berücksichtigt Theme-Wechsel auch bei pausierten oder leeren
Overlays. Der Windows-Renderer und die Browser-Widgets teilen Layoutdaten und
eine abgestimmte Farbpalette, verwenden aber weiterhin verschiedene Zeichensysteme.
`OverlayWindowChrome` teilt die Rahmengeometrie zwischen Editor, Browser und Windows.
`NativeOverlayPalette` enthält die mit CSS abgestimmten Farben der drei neuen
Themes. EXP-Verluste in Liveansicht und Verlauf behalten unabhängig vom Theme ihre rote Bedeutungsfarbe;
auf dunklen Bildüberschriften bleiben auch im hellen Theme helle Texte erhalten.
Der native Black-Desert-Titel zeichnet die Georgia-Buchstaben als Vektorkonturen
direkt in der tatsächlichen Fensterauflösung. Das erhält die feinen Konturen
bei kleinen Schriftgrößen und skaliert mit Overlay-Größe und Monitor-DPI.

Die freigestellten Katzenillustrationen liegen unter `data/themes/cats/` und
werden lokal mit beiden Anwendungen ausgeliefert. Jeder Bereich besitzt sein
eigenes Motiv: `sidebar-napping-kitten.png` (schlafendes Kätzchen auf Büchern),
`workspace-playful-kitten.png` (spielendes Kätzchen mit Blättern),
`settings-ribbon-kitten.png` (Kätzchen mit Stoffbändern) und `kitten-lounge.png`
(Kittengruppe, ausschließlich im Overlay). Die Bilder werden nicht zwischen
diesen Bereichen wiederholt. Browser und Windows-Overlay
passen das Hintergrundmotiv an die Inhaltsfläche an; seine Deckkraft folgt der
eingestellten Hintergrundtransparenz. Das Bild verändert keine Modulpositionen
oder Klickflächen. Der native Renderer lädt es über seinen vorhandenen Bildcache.
Herkunft und Generierungsprompts stehen in [SOURCES.md](../data/themes/cats/SOURCES.md).

## Visuelle Referenzen

Das Overlay orientiert sich an zwei visuell geprüften PC-Referenzen:

- [Black Spirit's Scheduler aus den offiziellen Patchnotes vom 10.09.2026](https://www.naeu.playblackdesert.com/en-US/News/Detail?countryType=en-US&groupContentNo=10577),
  [Fensteraufnahme](https://s1.pearlcdn.com/NAEU/Upload/News/b509982e25820260908141838833.png):
  Bronze-Titelband, feiner heller Rand, dunkler Fensterkörper und dezentes Eckornament.
- [Inventaraufnahme vom März 2025 im offiziellen PC-Forum](https://s1.pearlcdn.com/NAEU/Upload/Community/5950eacae8220250313153105767.png):
  quadratische Slots, kühle helle Konturen, große Itembilder und rechtsbündige Mengen
  mit dunkler Schriftkontur. Die Quelle ist eine Spieleraufnahme des tatsächlichen
  Inventars; eine daneben diskutierte Designänderung wurde nicht übernommen.

Die übrige Black-Desert-Gestaltung orientiert sich an den offiziellen Beispielen für
[Spieloptionen](https://blackdesert.pearlabyss.com/Asia/en-US/Game/Wiki?_masterWikiNo=12),
[Inventar und Lager](https://blackdesert.pearlabyss.com/Asia/en-US/Game/Wiki?_masterWikiNo=11)
und [HUD/Interface](https://blackdesert.pearlabyss.com/Asia/en-US/Game/Wiki?_masterWikiNo=7).
Rahmen und Oberflächen werden mit CSS bzw. dem nativen Zeichenpfad erzeugt;
es werden keine Referenzscreenshots als UI-Texturen eingebunden. Die Darstellung
ist an das Spiel angelehnt; Schrift-Rasterung und native Fensterdetails hängen
weiterhin vom Betriebssystem ab.
Die goldene Hervorhebung verwendet weiterhin Grindcrests vorhandene Klassifikation
für seltene Drops; sie stellt keine vollständige Zuordnung der BDO-Itemqualitäten dar.

## Prüfung

Portable Tests prüfen Defaults, alte Einstellungsdateien, JSON-Roundtrips,
beide Theme-Auswahlen in den Einstellungen, **Wie Hauptfenster**, getrennte
Browser-Verbindungen und die Weitergabe an Overlays. Der native Rendercache wird
ebenfalls auf dem Mac geprüft.
Tests für Rahmengeometrie prüfen Größenänderung bei mehreren DPI-Stufen und das
Einpassen auf den Monitor. JavaScript-Tests prüfen Drop-Koordinaten, Größenänderung,
Abbruch und unverändertes Verhalten ohne Titelleiste.
Windows-Tests prüfen zusätzlich das Laden der gespeicherten Auswahl,
Theme-Wechsel während einer Session, Rückkehr zur ursprünglichen Darstellung,
Transparenz und unveränderte Klickbereiche bei mehreren DPI-Skalierungen.

```sh
dotnet test tests/BdoGrindTracker.BrowserPreview.Tests -c Release
# Unter Windows:
dotnet test tests/BdoGrindTracker.App.Tests -c Release
```
