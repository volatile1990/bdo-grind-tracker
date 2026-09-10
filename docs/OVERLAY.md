# Ingame-Overlay

Unter **Overlay** kann ein eigenes Layout aus Modulen zusammengestellt werden.
Das Overlay ist standardmäßig ausgeschaltet. Es zeigt dieselben Sessiondaten wie
die Live-Ansicht und erzeugt keine eigenen Drops oder Zählentscheidungen.

## Einrichten

Die Vorlagen **Kompakt**, **Dashboard**, **Loot-Inventar** und **Loot-Leiste** bieten einen Ausgangspunkt.
**Loot-Inventar** ordnet Spot, Dauer und Silber, Silberverlauf sowie Pause und
Trash pro Stunde über einem großen Inventarraster an (336 × 640).
Module lassen sich aus der Bibliothek auf die Arbeitsfläche ziehen oder per Klick
hinzufügen. Auf der Fläche können sie verschoben, vergrößert und verkleinert werden.
Wird die gesamte Overlay-Fläche am Eckgriff oder über Breite und Höhe geändert,
passen sich Position, Breite und Höhe aller Module proportional an. Abstände
bleiben relativ zur Fläche erhalten, auch ohne ganzzahlige Rasterpositionen.
Beim Ändern einzelner Module oder der gesamten Fläche skalieren auch Schrift,
Icons, Abstände und Inhalte mit. Unterschiedliche Seitenverhältnisse verzerren
die Inhalte nicht: Die knappere Achse begrenzt die gemeinsame Skalierung, die
andere Achse bietet zusätzlichen Layoutplatz. Lange Texte werden passend
verkleinert. Schrift- und Icongröße bleiben als relative Gestaltung einstellbar.
Escape bricht eine laufende Größenänderung am Ziehgriff ab und stellt auch die
Inhaltsskalierung wieder her.
Die Moduleigenschaften erlauben genaue Positionen und Größen sowie Beschriftung,
Icons und Textgröße. Das Drop-Inventar unterstützt Liste und Iconraster.

### Eigene Vorlagen

**Als Vorlage speichern** legt das aktuelle Layout unter einem eigenen Namen ab.
Unter **Eigene Vorlagen** kann es später geladen, mit dem aktuellen Layout
überschrieben oder gelöscht werden. Laden, Überschreiben und Löschen benötigen
eine Bestätigung.

Eine eigene Vorlage enthält Module samt Itemauswahl und Gestaltung, die Größe der
Arbeitsfläche, Skalierung, Deckkraft, Rahmen und Raster. Beim Laden bleiben die
aktuelle Fensterposition, Sichtbarkeit, Ein-/Aus-Zustand, Mausbedienung,
Tastenkürzel und der Ausschluss aus Bildschirmaufnahmen erhalten.
Die Vorlagen liegen lokal in `overlay-templates.json`.

### Drops gestalten

Die Drop-Anzeigen verwenden genau die gesammelten Mengen aus der **Live-Session**.
Neue Drops, später bestätigte OCR-Ergebnisse und manuelle Mengenkorrekturen werden
über dieselben Sessiondaten übernommen. Es gibt keine zusätzliche Stundenabgrenzung
und keine Hochrechnung dieser Itemmengen. Erst eine neue Live-Session beginnt neue
Mengen; Pausen oder der Wechsel über 60 Minuten ändern diese Zuordnung nicht.

Vier Bausteine stehen oben in der Modulbibliothek zur Verfügung:

- **Drop-Raster:** Inventarkacheln mit Itemicon und Menge unten rechts.
- **Drop-Leiste:** Dieselben Kacheln in einer horizontalen Reihe.
- **Drop-Liste:** Icon, Itemname und Menge in Zeilen.
- **Einzelnes Item:** Eine eigene Kachel mit großer Menge für einen ausgewählten Gegenstand.

Mehrere Bausteine lassen sich unabhängig kombinieren, zum Beispiel eine große
Trash-Kachel neben einer kleinen Leiste ausgewählter seltener Drops. Je Modul sind
Darstellung, Itemauswahl, Reihenfolge, maximale Itemanzahl, Icon- und Schriftgröße
einstellbar. Die Filter bieten alle gesammelten Items, Trashloot, seltene Items oder
eine eigene Auswahl aus den unterstützten Gegenständen. Bei eigener Auswahl bleiben
Items mit Menge **0** sichtbar und behalten ihre gewählte Reihenfolge. So springt die
Anordnung beim ersten Drop nicht um. Alternativ kann nach Menge oder Name sortiert
werden. Alle Items bis zur gesetzten Begrenzung passen sich an den verfügbaren
Platz an; bei kleinen Modulen werden Icons und Mengen entsprechend kleiner.
**+ N weitere** erscheint nur für Items oberhalb dieser Begrenzung (bei einer
einzelnen Itemkarte maximal ein Item); die maximale Itemanzahl ist einstellbar.
Alle Einstellungen werden automatisch gespeichert. Bestehende Layouts bleiben erhalten.

Verfügbare Module: aktive Zeit, Grindspot, Silber netto, Silber pro Stunde,
Trashloot, Trash pro Stunde, Drop-Inventar, seltene Drops, Silberverlauf,
Tracking-Status, Loot-Scroll, Grind-Bewertung und Start-/Pause-Steuerung. Der Filter für seltene Drops ist eine
explizite Auswahl bekannter seltener Gegenstände, keine neue Klassifizierung durch
OCR. Das Drop-Inventar enthält weiterhin alle gezählten Gegenstände.

**Grind-Bewertung** vergleicht den Trash-pro-Stunde-Wert der vollständigen
Live-Session mit den Garmoth-Referenzen des erkannten Spots. Es zeigt Unter Average,
Average Tier, High Tier oder Top Tier. In den ersten fünf aktiven Minuten ist die
Bewertung vorläufig. Beschriftung, Icon, Textgröße und Modulgröße sind wie bei den
anderen Modulen einstellbar. Editor und natives Overlay verwenden dieselbe
Bewertung wie die Live-Session. Quellen, Vergleichsbedingungen, fehlende Stufen
und Datenstand sind in [GRIND_RATING.md](GRIND_RATING.md) dokumentiert.

Der Editor zeigt standardmäßig die Live-Session. Optional zuschaltbare
Beispieldaten helfen auch ohne laufende Session beim Anordnen.
**Desktop-Vorschau** blendet vorübergehend das echte Overlay ein. Änderungen an
Layout, Verhalten und Fensterposition werden automatisch lokal in `overlay.json`
gespeichert. Die Vorschau selbst ist vorübergehend und aktiviert das Overlay nicht
dauerhaft. Fehler beim Speichern werden angezeigt; die zuvor gespeicherte Datei
bleibt erhalten.

## Verhalten

- **Verschiebbar:** Das Overlay kann am Hintergrund mit der linken Maustaste
  gezogen und am Griff unten rechts in der Größe geändert werden. Die
  Tracking-Steuerung bleibt klickbar.
- **Position gesperrt:** Die Position bleibt fest, enthaltene Steuerungen sind
  weiterhin bedienbar.
- **Mausdurchlässig:** Sämtliche Mausklicks erreichen das darunterliegende Spiel.
  Zum Ändern des Modus steht der Editor weiterhin zur Verfügung.

Die Sichtbarkeit kann auf das aktive Black-Desert-Fenster beschränkt werden,
während einer vorhandenen Session gelten oder dauerhaft eingeschaltet bleiben.
Der Monitor wird über das Black-Desert-Fenster erkannt; ohne erkanntes Spiel dient
der eingestellte Tracking-Monitor als Rückfall. Positionen werden relativ zum
Monitor und Größen in logischen Pixeln gespeichert. Auflösung, Windows-Skalierung
und negative Monitorpositionen werden berücksichtigt; das Overlay bleibt innerhalb
des gewählten Bildschirms. **Position zurücksetzen** holt es an seine Ausgangsposition.

Deckkraft, Größe, Skalierung, Rahmen und Raster sind einstellbar.

### Tastenkürzel

Die globalen Tastenkürzel sind standardmäßig aktiv, solange Grindcrest läuft.
**Strg+Alt+O** schaltet das Overlay ein/aus; **Strg+Alt+L** wechselt zwischen
Verschieben und Mausdurchlässigkeit. Beide funktionieren auch bei ausgeblendetem
Overlay. Gedrückthalten löst die Aktion nur einmal aus.

Die Leiste **Tastenkürzel** zeigt die aktuellen Kombinationen. Über **Anpassen**
lassen sich die Haupttaste sowie Strg, Alt, Umschalt und Win wählen. Buchstaben,
Ziffern, Navigationstasten und der Nummernblock benötigen mindestens eine dieser
Zusatztasten. **F1–F11** können auch allein verwendet werden; F12 ist von Windows
für Debugger reserviert. Beide Aktionen müssen unterschiedliche Kombinationen
verwenden. Der Dialog bietet außerdem **Standard wiederherstellen** und einen
Schalter zum vollständigen Deaktivieren der globalen Tastenkürzel.

Nach dem Speichern werden die alten Kombinationen freigegeben und die neuen bei
Windows registriert. Bereits belegte Kombinationen erscheinen als Hinweis in der
Tastenkürzel-Leiste. Wird die Belegung in einer anderen App aufgehoben, können die
Tastenkürzel zum erneuten Registrieren aus- und wieder eingeschaltet werden.
Bei älteren Installationen aktiviert das Update die Kürzel einmalig; eine danach
bewusst gespeicherte Deaktivierung bleibt bei weiteren Starts erhalten.

## Daten und technische Grenzen

Alle Kennzahlen verwenden dieselbe Berechnung und Darstellung wie die Live-Session.
Der Silberverlauf gehört zur Session und zeigt deren beobachteten durchschnittlichen
Stundenertrag über die aktive Grindzeit. Die gesamte laufende Session wird im
Arbeitsspeicher gehalten, mit Zeitstempeln im Abstand von zehn Sekunden plus dem
aktuellen Stand. Öffnen, Schließen oder Umgestalten des Overlays beginnt weder eine
neue Messung noch einen neuen Zeitraum. Pausen halten die Werte an; Korrekturen
aktualisieren den aktuellen Stand. Es werden keine erfundenen Zwischenwerte oder aus
pausierter Zeit abgeleiteten Drops erzeugt. Fehlende Preise werden wie in der
Live-Ansicht als unbekannt beziehungsweise als Teilbetrag gekennzeichnet.

Das optionale Modul **Loot-Scroll** zeigt denselben erkannten Status wie die
Session-Details: **Aktiv · Lvl. 1/2**, **Inaktiv** oder **Nicht erkannt**.
Während des Trackings wird die sichtbare Restzeit alle 30 Sekunden geprüft.
Zwei lesbare Messungen bestimmen den Zustand: ungefähr eine verbrauchte Sekunde
pro realer Sekunde bedeutet Level 1, zwei bedeuten Level 2. Steht die Zeit still,
erscheint der goldene Hinweis. Eine Anzeige ohne Sekunden benötigt eine längere
Vergleichszeit. Symbole dienen nur zum Auffinden der Anzeige.
Einzelne OCR-Ausfälle behalten den zuletzt bestätigten Zustand bei, verlängern
aber seine Gültigkeit nicht: Nach 120 Sekunden ohne Bestätigung wird er unbekannt.
Eine ausgeblendete, verdeckte oder nicht eindeutig erkennbare Anzeige gilt nicht
als ausgeschaltet. Das Tracking läuft weiter. Pausieren setzt den Status zurück.

Das Overlay ist ein eigenes Windows-Fenster über dem Spiel, geeignet für
Fenstermodus und randlosen Vollbildmodus. Exklusiver Vollbildmodus wird nicht
garantiert. Es wird kein Code in Black Desert geladen. Zur Platzierung fragt die
App Windows nach sichtbaren Fenstern, deren Prozessnamen und Monitoren.
Spielprozess-Speicher und Spielmodule bleiben unberührt; die App sendet keine
Eingaben an das Spiel.

Standardmäßig wird nur das Overlay aus Windows-Bildschirmaufnahmen ausgeschlossen,
damit seine eigenen Itemnamen und Zahlen nicht in der Loot-OCR auftauchen. Die
Hauptoberfläche bleibt normal aufnehmbar. Wird der Ausschluss abgeschaltet, muss
das Overlay außerhalb des Droplogs liegen. Das Verhalten anderer Aufnahmeprogramme
hängt davon ab, ob sie diese Windows-Funktion unterstützen.

Verwendete Windows-Schnittstellen: [Layered Windows und Mausdurchlässigkeit](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features),
[Fenster ohne Aktivierung](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles),
[Capture-Ausschluss](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity),
[globale Tastenkürzel](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).
