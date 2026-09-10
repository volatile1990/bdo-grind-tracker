# Ingame-Overlay

Unter **Overlay** kann ein eigenes Layout aus Modulen zusammengestellt werden.
Das Overlay ist standardmäßig ausgeschaltet. Es zeigt dieselben Sessiondaten wie
die Live-Ansicht und erzeugt keine eigenen Drops oder Zählentscheidungen.

## Einrichten

Die Vorlagen **Kompakt**, **Dashboard**, **Loot-Inventar** und **Loot-Leiste** bieten einen Ausgangspunkt.
Module lassen sich aus der Bibliothek auf die Arbeitsfläche ziehen oder per Klick
hinzufügen. Auf der Fläche können sie verschoben, vergrößert und verkleinert werden.
Die Moduleigenschaften erlauben genaue Positionen und Größen sowie Beschriftung,
Icons und Textgröße. Das Drop-Inventar unterstützt Liste und Iconraster.

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
werden. Bei Platzmangel oder einer gesetzten Begrenzung zeigt **+ N weitere**, wie
viele Items fehlen; Modul vergrößern, Icons verkleinern oder die Begrenzung erhöhen.
Alle Einstellungen werden automatisch gespeichert. Bestehende Layouts bleiben erhalten.

Verfügbare Module: aktive Zeit, Grindspot, Silber netto, Silber pro Stunde,
Trashloot, Trash pro Stunde, Drop-Inventar, seltene Drops, Silberverlauf,
Tracking-Status und Start-/Pause-Steuerung. Der Filter für seltene Drops ist eine
explizite Auswahl bekannter seltener Gegenstände, keine neue Klassifizierung durch
OCR. Das Drop-Inventar enthält weiterhin alle gezählten Gegenstände.

Beispieldaten im Editor helfen auch ohne laufende Session beim Anordnen.
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

Deckkraft, Größe, Skalierung, Rahmen und Raster sind einstellbar. Optionale
Tastenkürzel: **Strg+Alt+O** schaltet das Overlay ein/aus, **Strg+Alt+L** wechselt
zwischen Verschieben und Mausdurchlässigkeit. Die Tastenkürzel sind zunächst aus;
bereits anderweitig belegte Kombinationen werden gemeldet.

## Daten und technische Grenzen

Der Silberverlauf stellt den bisherigen durchschnittlichen Stundenertrag über die
aktive Sessionzeit dar. Er wird während der geöffneten App mit begrenzter Historie
im Arbeitsspeicher gehalten. Es werden keine erfundenen Zwischenwerte oder aus
pausierter Zeit abgeleiteten Drops erzeugt. Fehlende Preise werden wie in der
Live-Ansicht als unbekannt beziehungsweise als Teilbetrag gekennzeichnet.

Das Overlay ist ein eigenes Windows-Fenster über dem Spiel, geeignet für
Fenstermodus und randlosen Vollbildmodus. Exklusiver Vollbildmodus wird nicht
garantiert. Es wird kein Code in Black Desert geladen. Zur Platzierung fragt die
App Windows nach sichtbaren Fenstern, deren Prozessnamen und Monitoren; weder
Spielprozess-Speicher noch Spielmodule oder Eingaben werden gelesen oder verändert.

Standardmäßig wird nur das Overlay aus Windows-Bildschirmaufnahmen ausgeschlossen,
damit seine eigenen Itemnamen und Zahlen nicht in der Loot-OCR auftauchen. Die
Hauptoberfläche bleibt normal aufnehmbar. Wird der Ausschluss abgeschaltet, muss
das Overlay außerhalb des Droplogs liegen. Das Verhalten anderer Aufnahmeprogramme
hängt davon ab, ob sie diese Windows-Funktion unterstützen.

Verwendete Windows-Schnittstellen: [Layered Windows und Mausdurchlässigkeit](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features),
[Fenster ohne Aktivierung](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles),
[Capture-Ausschluss](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity),
[optionale Tastenkürzel](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).
