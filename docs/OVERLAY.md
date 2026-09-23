# Ingame-Overlay

Unter **Overlay** können mehrere unabhängige Fenster mit eigenen Layouts aus
Modulen zusammengestellt werden. Neue Fenster sind standardmäßig ausgeschaltet.
Alle Fenster zeigen dieselben Sessiondaten wie die Live-Ansicht und erzeugen
keine eigenen Drops oder Zählentscheidungen.

Unter **Einstellungen → Erscheinungsbild → Theme für die Overlays** lässt sich
das gemeinsame Design aller Overlay-Fenster unabhängig vom Hauptfenster wählen.
Zur Auswahl stehen Grindcrest, Black Desert, Light, Katzen, Obsidian, Kamasylvia
und Valencia. **Wie Hauptfenster** übernimmt automatisch das Hauptfenster-Theme.
Die Auswahl gilt sofort für die nativen Fenster sowie für die Vorschau im Editor
und im Browser und wird in der Windows-App gespeichert. Layouts, Modulkoordinaten
und Transparenzeinstellungen bleiben erhalten. Details stehen unter
[Themes und Darstellung](THEMES.md).

## Mehrere Fenster

Unter **Deine Overlay-Fenster** legt **Neues Overlay** ein leeres Fenster an.
**Duplizieren** kopiert das ausgewählte Layout in ein eigenes Fenster. Die Auswahl
**Fenster bearbeiten** wechselt zwischen den Fenstern, das Feld **Name** benennt
das ausgewählte Fenster um. Der Papierkorb entfernt es nach Bestätigung;
mindestens ein Fenster bleibt erhalten.

Jedes Fenster hat eigene Module, Position, Größe, Sichtbarkeit, Mausbedienung
und Einstellungen für Bildschirmaufnahmen. Die beiden Tastenkürzel steuern alle
Fenster gemeinsam. Mehrere aktive Fenster
werden gleichzeitig angezeigt; der Wechsel im Editor schaltet sie nicht um.
**Overlay aktiv**, **Auf Bildschirm testen** und **Position zurücksetzen** beziehen
sich auf das ausgewählte Fenster. Beim Verlassen des Editors enden alle temporären
Vorschauen; dauerhaft aktivierte Fenster bleiben eingeschaltet.

Die Fenster einschließlich der Auswahl werden in `overlay.json` gespeichert.
Ein bisheriges Einzel-Overlay wird mit seinem Layout, seiner Position und seinen
Einstellungen als **Overlay 1** übernommen. Eigene Layoutvorlagen lassen sich in
jedes Fenster laden.

## Einrichten

Die Vorlagen **Kompakt**, **Dashboard**, **Loot-Inventar** und **Loot-Leiste** bieten einen Ausgangspunkt.
**Loot-Inventar** ordnet Spot, Dauer und Silber, Silberverlauf sowie Pause und
Trash pro Stunde über einem großen Inventarraster an (336 × 640).
Module lassen sich aus der Bibliothek auf die Arbeitsfläche ziehen oder per Klick
hinzufügen. Auf der Fläche können sie verschoben, vergrößert und verkleinert werden.
Wird die Overlay-Fläche am Eckgriff oder über Breite und Höhe geändert,
bleiben Position, Breite und Höhe aller Module unverändert. So lässt sich freie
Fläche entfernen, ohne die Komponenten zu verkleinern. Inhalte außerhalb der
kleineren Fensterfläche werden abgeschnitten und bleiben gespeichert; beim
Vergrößern erscheinen sie wieder an derselben Position und in derselben Größe.
Beim gezielten Ändern einzelner Module skalieren deren Schrift,
Icons, Abstände und Inhalte mit. Unterschiedliche Seitenverhältnisse verzerren
die Inhalte nicht: Die knappere Achse begrenzt die gemeinsame Skalierung, die
andere Achse bietet zusätzlichen Layoutplatz. Lange Texte werden passend
verkleinert. Schrift- und Icongröße bleiben als relative Gestaltung einstellbar.
Escape bricht eine laufende Größenänderung am Ziehgriff ab. **Größe im Spiel**
ist derselbe Zoom, den der Eckgriff im Spiel verstellt: Er skaliert das gesamte
Overlay einschließlich seiner Module. Die Canvasgröße wird nur hier im Editor
geändert.
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

Verfügbare Module: aktive Zeit, Uhrzeit, Grindspot, Silber netto, Silber pro Stunde,
Trashloot, Trash pro Stunde, Drop-Inventar, seltene Drops, Verbrauchte Items, Silberverlauf,
Rotations / h, Rotation Counter, Special Events, Special Events / h, Tracking-Status, Loot-Scroll, Grind-Bewertung und
Start-/Pause-Steuerung. Der Filter für seltene Drops ist eine
explizite Auswahl bekannter seltener Gegenstände, keine neue Klassifizierung durch
OCR. Das Drop-Inventar enthält weiterhin alle gezählten Gegenstände.

**Verbrauchte Items** zeigt die gebuchten Buffs der Live-Session als kompaktes
Iconraster mit Mengenbadge in der Ecke und einer gemeinsamen Kostenzeile. Es
enthält alle gebuchten Varianten, ohne Itemfilter oder Begrenzung der Itemanzahl;
bei wenig Platz werden die Kacheln entsprechend kleiner. Beschriftung, Icons,
Schriftgröße und Modulgröße sind einstellbar. Die Gesamtkosten bleiben auch ohne
Beschriftung oder Icons sichtbar. Fehlende Preise erscheinen als **Preis fehlt**;
bei teilweise bekannten Preisen steht der bekannte Betrag mit **\***. Ohne
Buffbeobachtungen steht **—**.

Die Mengen stammen ausschließlich aus bestätigten Erstanrechnungen und
Timer-Erneuerungen. Ein aktiver Buff ohne Buchung zählt nicht als verbrauchtes
Item. Bereits gespeicherte Preise bleiben erhalten; das Overlay bewertet sie
nicht neu und addiert keine zeitanteiligen Laufzeitkosten. Die Iconleiste im
Live-Header und im Verlauf zeigt dieselben Mengen und Kosten.
Die Mouseoverdaten nennen Name, Anzahl und gespeicherte Preise.
[Erkennung und Kostenregeln](BUFF_TRACKING.md).

**Silberverlauf** zeigt entweder den Session-Durchschnitt Silber / Stunde oder, als Standard
für neu hinzugefügte Module, **Silber je Zeitabschnitt als Kurve** (ganze Session,
10 Sekunden, logarithmische Höhe). Bestehende Module ohne gespeicherte Darstellung behalten
den Session-Durchschnitt. Die Zeitabschnitt-Kurve zeigt das netto verdiente
Silber je Abschnitt von 5, 10 (Standard) oder 30 Sekunden aktiver Grindzeit. Der
Zeitraum umfasst die letzten 10, 20, 30, 40, 50 oder 60 Minuten oder die ganze Session.
Jeder gezählte Lootzuwachs wird mit den aktuellen Marktpreisen und Steuereinstellungen
bewertet; ändern sich Preise, wird die ganze Kurve neu bewertet. Ein wertvoller Drop
erscheint dadurch unabhängig vom Zeitpunkt als eigene Spitze. Das Icon eines Rare Drops
steht zentriert über der Spitze seines Abschnitts; mehrere Rare Drops im selben Abschnitt
stehen dort nebeneinander. Als wertvoll gelten Favoriten und Items über 200 Mio. Silber.
Wie sie die Kurve formen, legt die Einstellung **Wertvolle Drops** fest:
**Spitzen kappen** richtet die Höhe nach den Abschnitten ohne wertvolle Drops;
höhere Abschnitte enden am oberen Rand und tragen zwei schräge Striche. Ohne solche
Abschnitte bestimmt der höchste Abschnitt die Höhe. **Aus der Kurve herausrechnen** lässt
das Silber wertvoller Drops weg, ihre Icons bleiben. **Logarithmische Höhe** (Standard) staucht große
Werte, ohne zu kappen; ein Hundertstel des höchsten Abschnitts erreicht noch die halbe Höhe.
Die Skala bezieht sich immer auf den sichtbaren Zeitraum. Lange Verläufe werden auf höchstens
800 Punkte verdichtet, ohne einzelne Spitzen zu verlieren. Die große Zahl bleibt der
Session-Durchschnitt. Die Zeitabschnitt-Kurve verwendet nach einem App-Neustart die
gespeicherten Drop-Zeitpunkte der Session. Die Wiederherstellung erfolgt pausiert;
Offlinezeit zählt nicht mit. Bei älteren Sessions ohne gespeicherte Dropzeiten
bleiben frühere Zeitpunkte unbekannt, bis neue Drops erfasst werden. Nachträgliche
Mengenkorrekturen nach unten verändern bereits gezählte Abschnitte nicht.

**Rotations / h** zeigt mit einer Nachkommastelle, wie viele volle Rotationen beim
aktuellen Tempo in einer Stunde möglich sind: 60 Minuten geteilt durch die durchschnittliche Zeit der letzten bis zu drei
in dieser Session vollständig abgeschlossenen Rotationen am aktuellen Spot. Anders als die
Rotationsdauer im Rotation Monitor (ab dem Rotationsstart) enthält diese Zeit den Rückweg bis
zum Start der nächsten Rotation. Solange die nächste Rotation noch nicht begonnen hat, gilt für
die zuletzt beendete der durchschnittliche Rückweg dieser Session; ist noch keiner bekannt,
steht „ohne Rückweg“ in der Detailzeile. Lücken über zwei Minuten gelten als Pause und zählen
nicht als Rückweg. Die Detailzeile nennt den genauen Wert und die Durchschnittszeit. Aufbau und
abgebrochene Versuche zählen nicht. **Rotation Counter** zeigt die in der Session vollständig
abgeschlossenen Rotationen am aktuellen Spot und die Dauer der letzten. Beide Module
benötigen einen Spot mit Rotationsprofil (derzeit Hermesia, Aphrodon, Event Horizon und Magaia) und verwenden
dieselben gespeicherten Rotationen wie der Rotation Monitor.

**Special Events** sind zufällige Mechaniken, die eine volle Rotation nicht braucht und die zusätzlich
oder ersetzend auftreten: das Agris-Event in Aphrodon (ersetzt eine Hog-Welle), das Mini-AFK in
Event Horizon (zusätzlich nach herabfallenden Trümmern) und die Fragmente of Divinity in Magaia. Weil
Magaia-Fragmente in fast jeder Rotation vorkommen, vergleicht der Rotation Monitor dort nur Rotationen mit
gleicher Fragmentanzahl, unabhängig von der Einstellung unten. Das Modul **Special Events** zählt jedes in
der Session erkannte Special Event, auch in abgebrochenen oder laufenden Rotationen; **Special Events / h**
teilt diese Zahl durch die aktive Grindzeit. Im Rotation Monitor sind Special-Event-Phasen gestrichelt
goldgelb umrandet. Unter Einstellungen → Rotation Monitor legt „Rotationen mit Special Events werten“
fest, ob Rotationen mit Special Event für Bestzeit, Idealrotation, Bestabschnitte und Rotations / h
zählen (Standard: ja). Ausgeschaltet vergleicht der Rotation Monitor nur mit Rotationen ohne Special
Event, und Rotations / h nutzt nur solche Rotationen; der Rotation Counter zählt weiterhin alle.

**Grind-Bewertung** vergleicht den Trash-pro-Stunde-Wert der vollständigen
Live-Session mit den Garmoth-Referenzen des erkannten Spots. Es zeigt Unter Average,
Average Tier, High Tier oder Top Tier. In den ersten fünf aktiven Minuten ist die
Bewertung vorläufig. Beschriftung, Icon, Textgröße und Modulgröße sind wie bei den
anderen Modulen einstellbar. Editor und natives Overlay verwenden dieselbe
Bewertung wie die Live-Session. Quellen, Vergleichsbedingungen, fehlende Stufen
und Datenstand sind in [GRIND_RATING.md](GRIND_RATING.md) dokumentiert.

Beim Überfahren eines Moduls in der Liste erscheint neben der Liste eine Vorschau
des Moduls in seiner Standardgröße, immer mit den Beispieldaten und unabhängig
davon, ob die Fläche Live- oder Beispieldaten zeigt. Sie folgt derselben Tastatur-
und Mausbedienung: Auch der Fokus per Tabulator zeigt sie.

Der Editor zeigt standardmäßig die Live-Session. Optional zuschaltbare
Beispieldaten helfen auch ohne laufende Session beim Anordnen. Sie stammen aus
einer echten Hermesia-Session (Shai, 18.09.2026, 1:34 h, englischer Client):
Mengen, die Marktpreise und Steuereinstellungen dieses Tages sowie sieben
vollständige Rotationen sind aufgezeichnete Werte. Da diese ältere Beispielaufnahme
keine genauen Drop-Zeitpunkte enthält, verteilt die Vorschau jeden Drop auf die aktive Stunde,
in der die Session ihn gezählt hat. Loot-Scroll und Tagesziel sind Beispiele.
**Desktop-Vorschau** blendet vorübergehend das echte Overlay ein. Änderungen an
Layout, Verhalten und Fensterposition werden automatisch lokal in `overlay.json`
gespeichert. Die Vorschau selbst ist vorübergehend und aktiviert das Overlay nicht
dauerhaft. Fehler beim Speichern werden angezeigt; die zuvor gespeicherte Datei
bleibt erhalten.

### Uhrzeit und Tag/Nacht

Das Modul **Uhrzeit** zeigt die lokale Windows-Uhrzeit, die berechnete BDO-Weltzeit
und die reale Restdauer bis zum nächsten Wechsel zwischen Tag und Nacht. Die drei
Zeilen lassen sich separat einblenden, mindestens eine bleibt sichtbar. Sekunden,
Beschriftung, Icon und Schriftgröße sind einstellbar. Die Uhr läuft auch ohne
aktive Grind-Session und bei pausiertem Tracking weiter.

Die Berechnung verwendet den regulären EU/NA-Zyklus: Tag von 07:00 bis 22:00 BDO
entspricht 200 realen Minuten, die Nacht 40 Minuten. 00:20 UTC dient als Anker für
07:00 BDO; die Systemzeitzone und Sommerzeit verändern diesen Anker nicht.
Grundlage ist die veröffentlichte
[bdo-clock-Implementierung](https://github.com/markni/bdo-clock/blob/master/bdo-clock.js).
**BDO-Zeitkorrektur** verschiebt die Berechnung um reale Minuten, falls der Server
abweicht. Positive Werte stellen die BDO-Zeit vor, negative zurück. Sondergebiete
oder Ereignisse mit festgelegter Tageszeit können vom regulären Weltzyklus
abweichen. Die Uhr verwendet die Systemzeit und liest keine Daten aus dem Spiel.

## Verhalten

- **Verschiebbar:** Das Overlay kann am Hintergrund mit der linken Maustaste
  gezogen und am Griff unten rechts skaliert werden. Der Zug vergrößert oder
  verkleinert das ganze Overlay: Module, Schrift und Icons wachsen gemeinsam, das
  Seitenverhältnis bleibt erhalten. Beide Achsen steuern denselben Zoom über die
  Strecke entlang der Fensterdiagonale. Die gezogene Ecke bleibt an ihrem Platz;
  der Zoom wächst höchstens so weit, bis die gegenüberliegende Kante den
  Bildschirmrand erreicht, und bleibt zwischen 50 % und 200 %. Beim Loslassen
  wird der neue Zoom gespeichert, die Canvasgröße und alle Module bleiben
  unverändert. Bei eingeschaltetem 8-Pixel-Raster springt der Zoom in
  5-Prozent-Schritten, sonst in 1-Prozent-Schritten. Die Tracking-Steuerung
  bleibt klickbar. Die Fläche selbst (Breite und Höhe der Canvas) wird im Editor
  geändert.
- **Position gesperrt:** Die Position bleibt fest, enthaltene Steuerungen sind
  weiterhin bedienbar.
- **Mausdurchlässig:** Sämtliche Mausklicks erreichen das darunterliegende Spiel.
  Zum Ändern des Modus steht der Editor weiterhin zur Verfügung.

Die Sichtbarkeit kann auf das aktive Black-Desert-Fenster beschränkt werden,
während einer vorhandenen Session gelten oder dauerhaft eingeschaltet bleiben.
Der Monitor wird über das Black-Desert-Fenster erkannt; ohne erkanntes Spiel dient
der Windows-Hauptbildschirm als Rückfall. Positionen werden relativ zum
Monitor und Größen in logischen Pixeln gespeichert. Auflösung, Windows-Skalierung
und negative Monitorpositionen werden berücksichtigt; das Overlay bleibt innerhalb
dieses Bildschirms. **Position zurücksetzen** holt es an seine Ausgangsposition.

Deckkraft, Größe, Skalierung, Rahmen und Raster sind einstellbar.

### Tastenkürzel

Es gibt eine gemeinsame Belegung für alle Overlay-Fenster. Sie gilt unabhängig
von der Auswahl im Editor und wird von neuen oder duplizierten Fenstern übernommen.
**Strg+Alt+O** schaltet alle Overlays aus, sobald mindestens eines aktiv oder in
der Bildschirmvorschau ist.
Sind alle ausgeschaltet, aktiviert der nächste Tastendruck alle Fenster.
Temporäre Desktop-Vorschauen werden beim Ausschalten ebenfalls beendet.
**Strg+Alt+L** schaltet alle Fenster auf Mausdurchlässigkeit; sind bereits alle
mausdurchlässig, schaltet es alle zurück auf **Verschiebbar**. Beide Kürzel
funktionieren auch bei ausgeblendeten Overlays. Gedrückthalten löst die Aktion
nur einmal aus.

Die Hotkey-Einstellungen werden einmalig gemeinsam in `overlay.json` gespeichert.
Beim Übernehmen älterer Mehrfenster-Einstellungen wird die Belegung des ersten
Fensters mit aktivierten Hotkeys übernommen. Falls alle deaktiviert waren,
bleibt die Belegung des ersten Fensters deaktiviert erhalten.

Die Leiste **Tastenkürzel** zeigt die aktuellen Kombinationen. Über **Anpassen**
lassen sich die Haupttaste sowie **Strg**, **Alt** oder **Strg+Alt** wählen.
Mindestens eine dieser Zusatztasten ist für jedes Tastenkürzel Pflicht, auch für
**F1–F11**. Umschalt und Win werden nicht angeboten. F12 ist von Windows für
Debugger reserviert. Beide Aktionen müssen unterschiedliche Kombinationen
verwenden. Ältere Belegungen ohne Strg/Alt oder mit Umschalt/Win fallen beim Laden
auf die Standardkombinationen der jeweiligen Aktion zurück. Bereits gültige
Strg-/Alt-Belegungen bleiben erhalten.
Der Dialog bietet außerdem **Standard wiederherstellen** und einen
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

Jedes Overlay ist ein eigenes Windows-Fenster über dem Spiel, geeignet für
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
