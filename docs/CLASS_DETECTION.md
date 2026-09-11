# Passive Klassen- und Spezialisierungserkennung

Seit Version 0.8.0 erkennt der Tracker Klassen anhand der lokal gespeicherten
Skill-Slots in `Documents/Black Desert/UserCache/.../gameVariable.xml`.
Die Anwendung liest diese Datei beim Öffnen und vor dem Start/Fortsetzen.
Eine noch unbekannte automatisch gewählte Klasse wird alle 30 Sekunden erneut
geprüft. Auch beim Wechsel von einer manuellen Klasse zurück auf Automatik wird
frisch gelesen. Die Anwendung bedient das Spiel nicht und untersucht keinen
Spielprozess.

Die sichtbaren Spezialisierungsnamen sind überall **Awakening** und **Succession**,
auch bei deutscher Oberfläche: zum Beispiel `Maegu · Awakening` oder
`Corsair · Succession`. Klassen mit nur einer Spielweise behalten ausschließlich
ihren Klassennamen. Automatische Anzeige und manuelle Klassenliste verwenden
dieselbe Beschriftung; interne Klassen-IDs und Garmoth-Spezialisierungswerte
bleiben unverändert.

## Nachgewiesene Companion-0.7.4-Logik

Untersucht wurde ausschließlich die installierte Datei `bdo_companion.exe`.
SHA256: `8B75E114D3D33D227A01CDFE592F13AA65133A36363EAA2E6A82E5ADFC77EFCF`.

- Profil-/Charakterauswahl wie im bestehenden Kalibrierungsleser: zuletzt
  geändertes numerisches UserCache-Profil, Charakterdatei nach Zugriffszeit.
- Elemente `QuickSlotSkillData` und `SkillCoolTimeSlot`, Attribut `SkillNo`:
  positive uint32-Werte, Duplikate nur einmal berücksichtigen.
- Vergleich mit 56 Klassen-/Spezialisierungslisten (2.743 Skill-IDs).
  Der Kandidat mit den meisten passenden eindeutigen Skills gewinnt.
- Tabelle: VA `0x1414FA018`, 56 Einträge à 32 Byte.
  Parser: `0x1406EDD43..0x1406EE13F`, Vergleich: `0x1406EE5DD..0x1406EE683`,
  Stimmenzählung: `0x1406EE5BE`, Maximum: `0x1406EEB19..0x1406EEC44`.
- Keine zusätzliche Mindestanzahl oder Mehrfachbestätigung. Bei einem Gleichstand
  meldet der Tracker ausdrücklich „mehrdeutig“; Companion löst diesen durch die
  nicht deterministische Iterationsreihenfolge seiner HashMap auf.

Der Mapping-Regressionshash ist
`67B7D6C22AE3B41D74626E4CA76ABAA60E993C5D40E15E8A432EB4DB9EBFC903`.
Tests prüfen jedes automatische Profil, doppelte Slots, falsche XML-Strukturen,
Größenlimits und die Auswahl der gespeicherten Charakterdatei.

## Grenzen und Korrektur

Die aktuelle Dateiauswahl verwendet die letzte **Schreibzeit** der gespeicherten
Charakterdateien. Zugriffszeiten sind dafür ungeeignet: Schon das Lesen durch
Grindcrest, BDO oder ein Diagnoseprogramm kann sie ändern. Die allgemeine Datei
des Preset-Verzeichnisses wird nicht als Charakter ausgewählt, wenn darunter
echte Charakterdateien vorhanden sind. Bei gleichen jüngsten Schreibzeiten
müssen die Dateien dieselbe Klasse/Spezialisierung belegen; ansonsten bleibt
die Erkennung unbekannt oder mehrdeutig. Eine ältere bekannte Klasse wird nicht
als Ersatz für einen unbekannten aktuellen Charakter gewählt.

Die oben dokumentierte Companion-Auswahl nach Zugriffszeit ist damit eine
historische Vergleichsbeschreibung, nicht mehr die aktuelle Grindcrest-Auswahl.
Die 56 Skillprofile und deren Stimmenzählung bleiben unverändert.

Die gespeicherten Slots können einem noch nicht gespeicherten Charakter- oder
Spezialisierungswechsel hinterherhinken. In diesem Fall die UI-Einstellungen
im Spiel mit dem aktiven Charakter speichern, damit BDO seine aktuelle
Charakterkonfiguration schreibt. Eine beliebige Änderung anderer Spieleinstellungen
oder das Öffnen einer Datei ist kein Beleg für einen Charakterwechsel.
Fehlende Slots oder unbekannte Skills
liefern keine erfundene Klasse. Unbekannte/mehrdeutige Klassen verhindern das
Loot-Tracking nicht. Vor Beginn oder während einer Pause kann unter **Optionen**
eine Klasse korrigiert werden; beim Upload wird sie nochmals angezeigt.

Die Klassenwahl einer begonnenen Sitzung wird durch spätere automatische Lesungen
nicht überschrieben. Für einen Charakterwechsel eine neue Sitzung beginnen.
„Agent“ ist im Garmoth-Metadatensatz enthalten, jedoch nicht in der Skilltabelle
des untersuchten Companion: deshalb nur manuell auswählbar.

XML-DTDs/externe Entitäten sind deaktiviert, die Dateigröße ist begrenzt.
Konten-/Charakterpfade und XML-Inhalte werden weder angezeigt noch hochgeladen.
Die Loot-OCR, der Spotfilter und das Zählledger bleiben unverändert.
