# Rotation Monitor

Im Overlay-Editor ist **Rotation Monitor** als frei platzierbares und skalierbares Modul verfügbar. Er übernimmt den erkannten Spot direkt aus dem Tracker ohne sichtbare Textbeschriftung. Für unbekannte Spots wartet er auf die Spot-Erkennung; für bekannte Spots ohne Profil bleiben die Timelines leer. Spot und Status stehen im Tooltip der Editor-Vorschau. Dabei werden keine Hermesia-Timelines oder Bestzeiten angezeigt.

Die Registrierung in `RotationProfiles` ordnet jedem unterstützten Spot ein eigenes Erkennungsprofil mit eigener Rotationslogik und Speicherung zu. Weitere Spot-Profile werden anhand der jeweils bereitgestellten Nachrichten und Referenzdaten ergänzt. Aktuell ist Hermesia verfügbar. Beim Wechsel wird die bisherige Erkennung beendet und eine laufende Teilrotation verworfen. Vorhandene Module mit dem früheren Schlüssel `hermesia-rotation` werden unter Beibehaltung ihrer Einstellungen auf `rotation-monitor` migriert.

- Obere Spur: persönliche Bestrotation oder ideale Rotation.
- Untere Spur: aktuelle Rotation. Weiße Linie: aktuelle Rotationszeit.
- Sechs gemeinsame Sektorflächen: Startup (fünf Porter), Drakania, erste Mine, zweite Mine, Drache, AFK. Beide Minen sind in zwei farbige Unterphasen aufgeteilt. Breite Bänder zeigen die jeweilige Dauer in min:sec; die laufende Phase zeigt ihre bisherige Dauer.
- Kleine Striche am oberen Bandrand: Mob-Pack-/Träger- und Opfergabe-Meldungen in allen Phasen. Die aktuelle Spur enthält nur bereits beobachtete Ereignisse.
- Start ist das **erste erkannte Ereignis nach Grind-Start oder nach der AFK-Phase**. Das AFK-Ende schließt die laufende Rotation ab; bis zum nächsten Ereignis bleiben Timeline und Playhead am Endpunkt stehen. Die Uhr zählt die AFK-Zeit mit und ist unabhängig von der aktiven Loot-Zeit.
- Der Spot steht erst nach dem ersten Loot fest, die erste Opfergabe-Meldung erscheint aber schon davor. Bis zur Spot-Erkennung lesen deshalb alle Rotationsprofile vorläufig mit. Das Profil des erkannten Spots übernimmt seine bis dahin erfassten Meldungen, die übrigen werden verworfen.
- Das erste Ereignis setzt die Zeit auf 0:00 und wird selbst dort eingetragen. Nur Durchläufe mit allen erforderlichen Mechaniken liefern eine Bestzeit.
- Drakania erscheint erst nach fünf `The overseer orders the Black Crystals to be offered up.`-Meldungen. Wurden bis zum Drakania-Spawn weniger als fünf davon erfasst, begann die Messung mitten im Startup: Die unvollständige Startphase verschwindet aus der aktuellen Spur, und die Rotation zählt weder als Bestzeit noch als abgeschlossene Session-Rotation. Ältere gespeicherte Rotationen ohne vollständigen Startup bleiben in der Datei, dienen aber nicht mehr als Referenz.
- Pausen, Spotwechsel und fehlende Bildbeobachtung verwerfen die laufende Messung. Das jeweils erste Ereignis nach dem Fortsetzen startet die Messung neu.
- `Intruder alert in effect. Valid authorization not confirmed.` gilt als **Rotation Failed**: Die aktuelle Spur endet mit einem roten Strich, die Uhr bleibt stehen und der Versuch wird weder als Bestzeit noch als abgeschlossene Session-Rotation gezählt. Alle weiteren Meldungen werden ignoriert, bis `The overseer orders the Black Crystals to be offered up.` erscheint; diese Meldung setzt die neue Rotation auf 0:00. Das gilt auch, wenn das Tracking zwischendurch pausiert oder das Bildsignal kurz fehlt.

## Vergleiche

Die Moduleinstellungen bieten unter **Timeline-Farben** die bestehende Darstellung **Eingefärbt** sowie **Grindcrest Gold**, **Schiefer** und **Dezent · Gold & Grau**. Die Auswahl wird pro Modul gespeichert und in Editor und nativem Overlay identisch verwendet. Die vereinfachten Varianten wechseln zwischen zwei Helligkeitsstufen; Minen-Unterphasen bleiben getrennt. Die allgemeinen Optionen Icon und Beschriftung werden für dieses Modul ausgeblendet.

Die Moduleinstellungen bieten die schnellste vollständige Rotation, die Bestrotation mit Mechanik-Bestzeiten sowie eine ideale Rotation aus Bestabschnitten. Mechanik-Abschnitte liegen zwischen den großen Ereignissen; Träger-/Opfergabe-Meldungen zerlegen sie nicht. Die ideale Rotation kombiniert nur Durchläufe mit derselben Mechanik-Reihenfolge. Ihre kleinen Zwischenmarkierungen werden proportional in die Bestabschnitte eingesetzt und sind keine gemessene vollständige Rotation.

Persönliche Bestzeiten liegen lokal in `hermesia-rotations.json` im App-Datenverzeichnis. Der laufende Playhead wird beim Neustart neu synchronisiert. Demo-Daten werden niemals als persönliche Rekorde gespeichert. Die Datei behält bis zu 200 schnelle Rotationen und zusätzlich erforderliche Spender von Mechanik-Bestzeiten.

Zusätzlich werden alle neu abgeschlossenen, gültigen Rotationen in der jeweiligen Session gespeichert: Spot-ID, Startzeitpunkt, Gesamtdauer und sämtliche Ereigniszeiten. Sie stehen in `Rotations` sowohl im Verlaufsdatensatz als auch im aktuellen Session-Checkpoint. Die reguläre automatische Sicherung (alle 15 Sekunden), Pausieren, Session-Wechsel und Beenden speichern diese Daten zusammen mit der Session. Die Sessionliste wird nicht auf Bestzeiten reduziert. Wiederherstellung übernimmt nur die Rotationen derselben Session; neue Sessions beginnen leer. Unterbrochene/unvollständige Rotationen sind weiterhin keine abgeschlossenen Rotationen. Alte Sessions erhalten keine nachträglich geschätzte Zuordnung aus der globalen Bestzeiten-Datei. Die Daten werden zunächst gespeichert; eine eigene Ansicht im Verlauf ist noch nicht enthalten.

## Erkennung und Grenzen

Die Erkennung ist auf die **englischen Systemmeldungen und die mittlere untere Meldungsposition der gelieferten Aufnahme** abgestimmt. Sie liest einen eigenen Ausschnitt (36,5–63,5 % der Bildbreite, 54–65 % der Bildhöhe), der genau den Stapel aus bis zu drei Meldungsbannern enthält; neue Meldungen erscheinen unten und rücken nach oben. Andere Sprachen oder verschobene Meldungsbereiche sind noch nicht kalibriert.

Ein separater Hintergrundauftrag beobachtet höchstens alle 500 ms. Zwei Beobachtungen bestätigen ein Ereignis; der erste beobachtete Zeitpunkt wird behalten. Das liefert ungefähr eine Abtastperiode Zeitauflösung, keine framegenaue Messung. Je Bild laufen zwei Texterkennungen: das Rohbild und ein vergrößertes Schwarzweißbild. Die Suchbegriffe sind kurz gehalten, weil Skill-Hinweise neben den Bannern mit dem ersten Wort verschmelzen können und die AFK-Meldung bis an den Ausschnittrand reicht. Gegenüber dem früheren Ausschnitt (25–75 % × 54–70 %) mit vier Erkennungsdurchgängen sinkt die Rechenzeit je Bild von etwa 55 auf 21 ms (2560 × 1440); im Referenzvideo werden weiterhin alle 37 Meldungen ohne Fehlalarm erkannt. Während des sichtbaren Banners verhindert eine Wiederholungssperre doppelte Ereignisse.

`Work in the mine is suspended …` steht sowohl für einen Minenabschluss als auch für das AFK-Ende. Nur nach `… begins absorbing nearby Black Crystals` wird daraus eine Rotationsgrenze. Die Erkennung benötigt sichtbare Spiel-HUD-Bilder aus der vorhandenen Capture-Pipeline; sie liest keine Spielspeicher und greift nicht in das Spiel ein.

## Diagnose

Unter **Einstellungen → Diagnose** zeichnet **Rotation-Monitor-Diagnose aufzeichnen** die Rotationserkennung einer Session auf. Wie die Loot-Diagnose lässt sich der Schalter nur vor einer neuen Session ändern; nach einem App-Neustart und für jede neue Session ist er ausgeschaltet. Pausen setzen dieselbe Aufnahme fort.

Die Aufnahme liegt im Diagnoseordner unter `rotation-<Zeitpunkt>-<ID>`:

- `rotation.jsonl` enthält je Zeile einen Eintrag: `header` (Formatversion, App-Version, Abtast- und Prüfintervall), `probe` (Zeitpunkt, Bildgröße, Ausschnitt und Bilddatei der jeweils neuesten Probe), `read` (OCR-Text und erkannte Meldungsarten jedes gelesenen Pufferbilds), `event` (bestätigte Meldung mit erstem sichtbarem Zeitpunkt und Prüfzeitpunkt), `state` (Status, Synchronisierung und Ereignisse des Trackers nach jeder Meldung), `completed` (gezählte Rotationen) und `note` (vorläufige Erkennung vor der Spot-Erkennung, Spotwechsel, Unterbrechungen, Erkennungsfehler).
- `crops/` enthält den Meldungsausschnitt jeder Prüfung als JPEG, also etwa alle 3 Sekunden. Bei 2560 × 1440 sind das rund 55 MB pro Stunde. Nach 20.000 Bildern (rund 16 Stunden) werden Prüfungen nur noch als Text erfasst.

Die Aufnahme verändert die Erkennung nicht. Schreibfehler beenden nur die Aufnahme und erscheinen in der Statuszeile.

## Referenz und Validierung

Die Text+-Titel aus `Timeline 1.drt` wurden aus den komprimierten Fusion-Kompositionen gelesen. `hermesia-reference.json` enthält die Titelzeiten bei 60 fps und die Zuordnung zur Quellaufnahme. Diese Zeiten sind relativ zum Timeline-Anfang, der nicht als persönlicher AFK-Ende-Rekord verwendet wird. Die markierte AFK-Phase dauert 61,267 Sekunden.

Die Editor-Demo verwendet die annotierte Aufnahme als Beispielreferenz und bewegt den Playhead. Beispiel-Bestzeiten und ideale Zeiten dienen nur zur Demonstration.

Unit-Tests prüfen Synchronisierung, Doppelmeldungen, Unterbrechungen, Vergleichsmodi, Mechanik-Sektoren, Persistenz und native Skalierung. Mit `GRINDCREST_HERMESIA_FIXTURES` kann zusätzlich die lokale OCR-Prüfung an elf extrahierten Meldungsausschnitten ausgeführt werden. Die Originalaufnahme bleibt außerhalb des Repositorys.

Die Oberfläche zeigt zwei Timelines mit Phasendauern, Ereignisstrichen und Playhead. Phasennamen und weitere Details bleiben im Tooltip der Editor-Vorschau. Die Vergleichsauswahl bleibt in den Moduleinstellungen. Bestehende Bestzeiten ohne Zeitursprungs-Version 2 werden beim Laden anhand ihres ersten aufgezeichneten Ereignisses verschoben; die gespeicherten Zeitabstände bleiben erhalten.
