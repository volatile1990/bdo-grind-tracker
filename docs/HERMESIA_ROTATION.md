# Rotation Monitor

Im Overlay-Editor ist **Rotation Monitor** als frei platzierbares und skalierbares Modul verfügbar. Er übernimmt den erkannten Spot direkt aus dem Tracker und zeigt dessen Namen an. Für unbekannte Spots wartet er auf die Spot-Erkennung; für bekannte Spots ohne Profil erscheint ein Hinweis auf fehlende Rotationsdaten. Dabei werden keine Hermesia-Timelines oder Bestzeiten angezeigt.

Die Registrierung in `RotationProfiles` ordnet jedem unterstützten Spot ein eigenes Erkennungsprofil mit eigener Rotationslogik und Speicherung zu. Weitere Spot-Profile werden anhand der jeweils bereitgestellten Nachrichten und Referenzdaten ergänzt. Aktuell ist Hermesia verfügbar. Beim Wechsel wird die bisherige Erkennung beendet und eine laufende Teilrotation verworfen. Vorhandene Module mit dem früheren Schlüssel `hermesia-rotation` werden unter Beibehaltung ihrer Einstellungen auf `rotation-monitor` migriert.

- Obere Spur: persönliche Bestrotation oder ideale Rotation.
- Untere Spur: aktuelle Rotation. Weiße Linie: aktuelle Rotationszeit; schattierter Abschnitt: AFK.
- Große Markierungen: Mechanik-Ereignisse. Kleine Striche: Träger- und Opfergabe-Meldungen.
- Start und Ende sind das **Ende der AFK-Phase**. Die Uhr zählt die AFK-Zeit mit und ist unabhängig von der aktiven Loot-Zeit.
- Die erste Erkennung wartet auf AFK-Beginn und das anschließende AFK-Ende. Bereits laufende Teilrotationen liefern keine Bestzeit.
- Pausen, Spotwechsel und fehlende Bildbeobachtung verwerfen die laufende Messung. Der nächste erkannte AFK-Abschluss synchronisiert erneut.

## Vergleiche

Die Moduleinstellungen bieten die schnellste vollständige Rotation, die Bestrotation mit Mechanik-Bestzeiten sowie eine ideale Rotation aus Bestabschnitten. Mechanik-Abschnitte liegen zwischen den großen Ereignissen; Träger-/Opfergabe-Meldungen zerlegen sie nicht. Die ideale Rotation kombiniert nur Durchläufe mit derselben Mechanik-Reihenfolge. Ihre kleinen Zwischenmarkierungen werden proportional in die Bestabschnitte eingesetzt und sind keine gemessene vollständige Rotation.

Persönliche Bestzeiten liegen lokal in `hermesia-rotations.json` im App-Datenverzeichnis. Der laufende Playhead wird beim Neustart neu synchronisiert. Demo-Daten werden niemals als persönliche Rekorde gespeichert. Die Datei behält bis zu 200 schnelle Rotationen und zusätzlich erforderliche Spender von Mechanik-Bestzeiten.

## Erkennung und Grenzen

Die Erkennung ist auf die **englischen Systemmeldungen und die mittlere untere Meldungsposition der gelieferten Aufnahme** abgestimmt. Sie liest einen eigenen Ausschnitt (25–75 % der Bildbreite, 54–70 % der Bildhöhe). Andere Sprachen oder verschobene Meldungsbereiche sind noch nicht kalibriert.

Ein separater Hintergrundauftrag beobachtet höchstens alle 500 ms. Zwei Beobachtungen bestätigen ein Ereignis; der erste beobachtete Zeitpunkt wird behalten. Das liefert ungefähr eine Abtastperiode Zeitauflösung, keine framegenaue Messung. Rohbild sowie überlappende vergrößerte Schwarzweißstreifen berücksichtigen gleichzeitig sichtbaren Bossdialog und Systemmeldung. Während des sichtbaren Banners verhindert eine Wiederholungssperre doppelte Ereignisse.

`Work in the mine is suspended …` steht sowohl für einen Minenabschluss als auch für das AFK-Ende. Nur nach `… begins absorbing nearby Black Crystals` wird daraus eine Rotationsgrenze. Die Erkennung benötigt sichtbare Spiel-HUD-Bilder aus der vorhandenen Capture-Pipeline; sie liest keine Spielspeicher und greift nicht in das Spiel ein.

## Referenz und Validierung

Die Text+-Titel aus `Timeline 1.drt` wurden aus den komprimierten Fusion-Kompositionen gelesen. `hermesia-reference.json` enthält die Titelzeiten bei 60 fps und die Zuordnung zur Quellaufnahme. Diese Zeiten sind relativ zum Timeline-Anfang, der nicht als persönlicher AFK-Ende-Rekord verwendet wird. Die markierte AFK-Phase dauert 61,267 Sekunden.

Die Editor-Demo verwendet die annotierte Aufnahme als klar gekennzeichnete Beispielreferenz und bewegt den Playhead. Beispiel-Bestzeiten und ideale Zeiten dienen nur zur Demonstration.

Unit-Tests prüfen Synchronisierung, Doppelmeldungen, Unterbrechungen, Vergleichsmodi, Mechanik-Sektoren, Persistenz und native Skalierung. Mit `GRINDCREST_HERMESIA_FIXTURES` kann zusätzlich die lokale OCR-Prüfung an elf extrahierten Meldungsausschnitten ausgeführt werden. Die Originalaufnahme bleibt außerhalb des Repositorys.
