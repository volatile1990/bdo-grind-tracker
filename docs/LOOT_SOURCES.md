# Feste Zuordnung zum Droplog

Die ausgefüllte `Droplog-Zuordnung.xlsx` vom 17. September 2026 legt für jeden
der 253 Namen im OCR-Vokabular genau einen Kanal fest: **164 normal, 89 rare**.
Die unveränderten Nutzereingaben stehen in `data/loot-sources.json`, einschließlich
des SHA-256 der Quelldatei. Die App lädt diese Tabelle als eingebettete Ressource;
Excel wird beim Tracking nicht benötigt.

Die aktualisierte Tabelle vom selben Tag ordnet alle acht Items mit `Luster`
im Namen dem normalen Droplog zu. Pigmente behalten ihre separat eingetragenen
Zuordnungen.

Alle Broken Vestiges sind dem Rare-/Special-Droplog zugeordnet. Ein Treffer auf
Broken Vestige of Everlight im normalen Droplog wird daher verworfen, selbst
wenn Name und Menge vollständig gelesen wurden. Umgekehrt werden normale Items
im Rare-Droplog verworfen. Der zusätzliche Spotfilter und die bestehenden
Einzel-Dropmengen gelten weiterhin. Ein falscher Kanal wird in den Diagnosen
mit `wrong-loot-source` gekennzeichnet.

Die Namenssuche vergleicht weiterhin den vollständigen Itemkatalog. Erst nach
der Identifikation wird der Kanal geprüft. Ein verbotenes Item wird dadurch
nicht auf ein ähnlich geschriebenes, im jeweiligen Kanal erlaubtes Item
umgedeutet. Die Prüfung umfasst primäre OCR, Recovery, zusätzliche OCR-Prüfung
und die spätere Interpretation von Rohtext. Sie gilt schon vor der Spoterkennung
und erneut nach einem Session-Neustart.

Neue Diagnoseaufnahmen verwenden `grindcrest-lifetime-v8` und speichern die
Kanalzuordnung im versionierten Parsing-Kontext. Replays verwenden ausschließlich
diese gespeicherten Regeln. Aufnahmen bis v7 ohne Quellenzuordnung behalten ihr
bisheriges Verhalten. Bereits gespeicherte Sessionmengen werden nicht geändert.

Neue Vokabeln benötigen ebenfalls eine Zuordnung. `Black Gem Fragment` bleibt
ein Vergleichskandidat ohne erlaubten Spotpool; seine Kanalzuordnung hebt diesen
Ausschluss nicht auf. Die Zuordnung betrifft die Erkennung, nicht die separate
Auswahl seltener Items für die Overlay-Anzeige.

Der Kanalfilter verhindert Fehlzählungen aus dem jeweils falschen Log.
Die zeitliche Zuordnung mehrerer Lesungen innerhalb des richtigen Logs bleibt
Aufgabe des bestehenden Zählers. Der gemeldete Achtfach-Fall ohne Diagnoseaufnahme
ist damit nicht nachträglich als konkrete Ursache nachgewiesen.
