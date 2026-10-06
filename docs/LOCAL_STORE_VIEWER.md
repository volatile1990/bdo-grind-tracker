# Lokale EXE mit Store-Session

Eine unverpackte Grindcrest-Ausgabe kann den Datenordner der installierten
Store-App im Lesemodus anzeigen. Beide Fenster dürfen gleichzeitig geöffnet
sein. Der Viewer startet keine Erfassung und schreibt weder Sessions noch
Einstellungen, Uploads, Fensterpositionen oder Overlaylayouts in den Quellordner.

Neben `Grindcrest.exe` aktiviert diese Datei den Lesemodus beim Doppelklick:

```json
{
  "DataDirectory": "C:\\Users\\<Nutzer>\\AppData\\Local\\Packages\\<Paketfamilie>\\LocalState"
}
```

Der Dateiname ist `store-session-viewer.json`. Der Quellordner muss absolut und
vorhanden sein. Eine ungültige Konfiguration führt zu einem Startfehler und
startet keinen normalen Tracker. Die Store-Ausgabe ignoriert diese Datei.

Alternativ nimmt `--view-store-session` den Quellordner aus der prozesslokalen
Umgebungsvariable `GRINDCREST_DATA_DIRECTORY` oder aus der Konfigurationsdatei.

Der Viewer zeigt ausdrücklich `current-session-v1.json`, zusätzlich den Verlauf
und gespeicherte Markt- und Garmoth-Referenzwerte. Historische Sessions werden
nicht als aktuelle Session ausgegeben. Änderungen werden alle zwei Sekunden
geprüft. Während des Trackings speichert die Store-App ihren Zwischenstand
ungefähr alle 15 Sekunden; die Ansicht kann entsprechend etwas hinterherliegen.
Die angezeigte aktive Zeit und der Zeitstempel stammen exakt aus dem gespeicherten
Zwischenstand und werden nicht zwischen zwei Speicherungen hochgezählt.

Lootbearbeitung, Tracking und Uploads erfolgen weiterhin in der Store-App.
Die Session-Controls bleiben im Lesemodus sichtbar, sind aber deaktiviert.
Droprate, der aufklappbare Store-Hinweis und die Übernahme stehen kompakt
am unteren Ende der Live-Ansicht.
Über **Store-Session übernehmen** kann die lokale EXE die gleiche Session mit
den normalen Controls fortsetzen: zuerst die Store-App über ihr Tray-Menü
**Beenden**, dann den Übernahmebutton wählen. Die lokale Ausgabe startet neu,
liest den letzten gespeicherten Stand derselben Session und bietet **Fortsetzen**.
Die Übernahme beginnt keine neue Session. Solange ein Tracker läuft, sperrt der
gemeinsame Instanzmutex den Start eines zweiten schreibenden Trackers.

`--take-over-store-session` startet diesen normalen Modus direkt; die
Quellenauflösung entspricht `--view-store-session`. Die Store-App darf auch bei
diesem Aufruf nicht mehr laufen. Die Quelle wird vor dem ersten Zugriff auf die
Appdaten für den neuen Prozess gesetzt.

Die Droprate und Anzeigeeinstellungen lassen sich im Viewer für den Vergleich
ändern. Diese Änderungen gelten nur für das offene Viewer-Fenster.
Bei vorübergehenden Lesefehlern bleibt der letzte gültige Stand sichtbar und
der Viewer versucht das Lesen erneut.

Für die lokale Ausgabe den gesamten Publish-Ordner behalten: Die EXE benötigt
die zugehörigen Laufzeitdateien und Assets. Ein normaler lokaler Tracker ohne
Viewer-Konfiguration bleibt durch denselben Instanzmutex wie die Store-App
gegen paralleles Tracking geschützt.
