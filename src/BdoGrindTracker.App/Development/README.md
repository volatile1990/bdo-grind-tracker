# Offline-Demo-Sessions (nur Debug)

Die Daten wurden am 08.09.2026 aus den sichtbaren Garmoth-Tabellen erfasst: Average Tier, 320 % Droprate, 100 % Lootmenge, Zeitraum 20.08.–10.09.2026. Quelle: https://garmoth.com/grind-tracker/best-grind-spots/

Tracker schließen. Befehle aus dem Projektordner ausführen:

```powershell
# Zehn Stunden pro Spot ergänzen (60 insgesamt)
dotnet run --no-restore --project src/BdoGrindTracker.App -- --demo-sessions=10

# Vorhandene Demo-Sessions aller Spots entfernen und neu erzeugen
# Echte Sessions bleiben erhalten.
dotnet run --no-restore --project src/BdoGrindTracker.App -- --demo-sessions=10 --replace-demo-sessions

# Drei Stunden nur für Aphrodon ergänzen
dotnet run --no-restore --project src/BdoGrindTracker.App -- --demo-sessions=3 --demo-spot=aphrodon
```

Spot-IDs: aphrodon, hermesia, magaia, aresion, scales-of-judgment, event-horizon.

`--no-restore` vermeidet NuGet-Abfragen; vorausgesetzt, die Projektabhängigkeiten sind wie bei dieser Einrichtung bereits installiert. Der Generator selbst verwendet keinerlei Netzwerk und liest keine BDO-Konfiguration. Er erzeugt ausschließlich vollständige Stunden mit ganzzahligen Lootmengen. Die erwartete Gesamtmenge je Item wird einmal über die gewählte Stundenanzahl gerundet und dann auf Sessions verteilt. Sehr seltene Drops können bei kleinen Stichproben fehlen. Marktpreise aus der Quelle sind gerundete Momentaufnahmen; Festpreise stammen aus dem Anwendungskatalog. Die Verlaufsanzeige bewertet weiterhin mit den aktuell verfügbaren Preisen.

Einträge tragen den Klassen-Zusatz `Demo320` und sind für Garmoth-Uploads gesperrt. Eine Sicherheitskopie des bisherigen Verlaufs wird vor dem Schreiben angelegt. Maximal 500 Verlaufseinträge insgesamt; bei Überschreitung bricht der Generator ab, ohne echte Sessions zu verdrängen. `--replace-demo-sessions` ersetzt alle als Demo markierten Einträge, auch bei Auswahl eines einzelnen Spots.

Der Generator und Garmoth320.json sind durch Build-Bedingungen vom Release ausgeschlossen. Keine UI-, Startparameter- oder Datenverzeichnis-Sonderlösung ist erforderlich.
