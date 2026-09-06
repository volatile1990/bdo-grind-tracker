# Grindcrest

Lokaler, passiver Loot-Tracker für Black Desert, Version 0.9.5 (bisher BDO Grind Tracker). Der erste Erkennungspfad und
die Zählung verwenden den Companion-basierten Stand 0.5.1. Die zusätzlichen
Bestätigungs- und Lebensdauerregeln aus 0.6.0/0.6.1 sind entfernt. Erhalten bleiben
der automatisch erkannte Spotfilter und die Verbesserungen der UI-Geschwindigkeit.

Der aktuelle Entwicklungsstand unterstützt alle sechs Inner-Edania-Zonen. Zu den
bisherigen Spots kommen Aresion Temple, Scales of Judgment und Event Horizon mit
automatischer Erkennung am jeweiligen Trashloot, vollständigem Hauptloot-Pool,
Silberbewertung, Originalicons und Garmoth-Zuordnung hinzu.

0.9.5 ergänzt den optionalen automatischen Garmoth-Upload. Unter **Optionen →
Garmoth-Key** aktivieren: Jede volle Stunde aktiver Grindzeit wird als eigener
Abschnitt übertragen, ausschließlich mit den noch nicht gesendeten Lootmengen
und deren Silberwert. Tracking läuft weiter, Pausen zählen nicht mit. Auch ein
manueller Rest-Upload lässt bereits übertragene Stunden aus. Die Option ist
standardmäßig aus; unklare Upload-Ergebnisse werden nicht automatisch wiederholt.

0.9.4 ergänzt ausschließlich für fehlgeschlagene normale Lootzeilen zwei zusätzliche
Lesewege: isoliertes Mengen-Nachlesen sowie Graustufen-/adaptive Bildaufbereitung
aus dem Originalausschnitt. Erfolgreiche bisherige Erkennungen bleiben unverändert;
keine zusätzliche Bestätigung, kein Warten auf weitere Frames, kein Mengenaufschlag.
Capture-Takt und Zählalgorithmus bleiben unverändert. [Details und Grenzen](docs/OCR_RECOVERY.md).

0.9.3 zieht bei einer automatischen Pause die gesamte abschließende Zeit ohne
neue Drops von der Sessiondauer ab. Manuelles Pausieren bleibt unverändert;
Anzeige und Garmoth-Upload verwenden dieselbe korrigierte aktive Dauer.

0.9.2 entfernt die Screenshot-Ausblendung, damit das Fenster auch in der
Snipping-Ansicht enthalten ist, und zentriert Itemname und Menge als kompakte
Textgruppe neben dem Icon. 20 fehlende Original-Icons sind ergänzt, einschließlich
des Trashloots aller drei Spots. Damit sind 42 Icons vorhanden; nur der mehrdeutige
Name „Pure Black Stone“ behält seinen generischen Platzhalter.
OCR und Zählung bleiben unverändert.

0.9.1 gibt der App den Namen **Grindcrest** und ein eigenes goldenes G-/Kristall-Logo
im Dashboard und als Windows-App-Icon. Klassen-Spezialisierungen heißen jetzt
**Awakening** und **Succession**. Technische Speicherpfade, der gespeicherte
Garmoth-Key und sämtliche Erkennungs-/Zählregeln bleiben unverändert.
[Logo, Icon und Designbrief](data/branding/README.md).

0.9.0 ersetzt Drop-/Itemarten-Kennzahlen durch Silber vor/nach Steuer. Hinzu kommen
regionale Marktpreise, steuerfreie Companion-Festwerte und Preis-/Steueroptionen.
Garmoth erhält per Einzelklick automatisch die Sitzungsdaten und den Netto-Silberwert;
der API-Key wird einmalig Windows-verschlüsselt hinterlegt. Nicht unterstützte Items
werden nur beim Upload ausgelassen. Die Erkennung und Zählung bleiben unverändert.

0.8.0 entfernt die Gesamtmengen-Karte und ergänzt eine konfigurierbare Auto-Pause,
passive Klassen-/Spezialisierungserkennung und einen ausdrücklich bestätigten
Garmoth-Upload. Loot-OCR und Zählalgorithmus sind unverändert.

0.7.1 korrigiert abgeschnittene Kennzahlen: Die Kartenhöhe folgt dem Textbedarf,
lange Werte passen ihre Schrift an die verfügbare Breite an. Die anfängliche
DPI-Skalierung erfolgt erst nach dem Aufbau der vollständigen Oberfläche.

0.7.0 überarbeitet ausschließlich die Oberfläche: große Loot-Karten mit Itemnamen,
Icons und Mengen, aktive Sitzungsdauer und eingeklappte Optionen. Screenshot-
Vorschau, OCR-Statistiken und das Live-Debuglog entfallen aus dem Dashboard.

Seit 0.6.3 ergänzen gemeinsame Drops alle drei Spotpools: Ancient Spirit Dust,
Black Stone, Caphras Stone, Laila's Petal und den theoretischen Worlddrop
Pure Black Stone. Die bisherigen Hauptloot-Listen waren keine vollständigen
Drop-Tabellen. Quellen, Einordnung und verbleibende Grenzen stehen in
[Lootpools](docs/LOOT_POOLS.md). OCR, Matchingregeln und Zählung bleiben unverändert.

## Benutzung

### Homepage mit Live-Sessions

Die neue Homepage unter `web/` zeigt öffentlich freigegebene Grind-Sessions mit
Spot, Klasse, aktiver Dauer, Silber/h und aufklappbarem Loot. Die separate API unter
`src/Grindcrest.Api` empfängt die Updates und entfernt Sessions ohne Lebenszeichen
nach 90 Sekunden. Website und API lassen sich unabhängig von der Desktop-App ausliefern.

In der App unter **Optionen → Live-Freigabe** API-Adresse, Anzeigename und persönlichen
Schreibschlüssel eintragen und **Session öffentlich teilen** aktivieren. Standardmäßig
bleibt die Freigabe aus. Pausen bleiben als solche sichtbar; neue Sitzung, Schließen
oder Deaktivieren beenden die Veröffentlichung. Der Garmoth-Upload bleibt unabhängig.

[Lokaler Start, Docker, Schlüsselvergabe und GitHub-Pages-Einrichtung](docs/LIVE_SESSIONS.md).

### Tracking

1. `Grindcrest.exe` starten und unter **Optionen** den Spielmonitor prüfen.
2. Dort optional **Event-Loot zulassen** aktivieren. Das ergänzt ausschließlich die
   explizite Event-Liste, keine beliebigen fremden Items.
3. Optional **Loot-Diagnose lokal aufzeichnen** aktivieren, dann **Tracking starten**.
4. Der Spot wird aus dem ersten passenden Trashloot automatisch erkannt und angezeigt:

   | Erkannter Trashloot | Spot |
   |---|---|
   | Branch of Abundance | Aphrodon Temple |
   | Black Crystal Fragment | Hermesia Inner Castle |
   | Elion Follower's Helmet | Magaia Temple |
   | Scorched Belt Ornament | Aresion Temple |
   | Elion Follower's Mark | Scales of Judgment |
   | Broken Gloves of the Void | Event Horizon |

5. **Pausieren** erhält die Session und stoppt die Sitzungsuhr. **Fortsetzen** zählt
   aktive Grindzeit weiter; Pausen zählen nicht mit. **Neue Sitzung** setzt Uhr,
   Summen, Zählzustand und Spot zurück. Vor einem Spotwechsel eine neue Sitzung anlegen.

Vor dem ersten erkannten Trashloot wird kein zusätzlicher Spotfilter angewendet;
es muss kein Spot manuell ausgewählt werden. Danach bleibt der erkannte Spot bis
zur neuen Sitzung gesperrt. Ein früher gespeicherter manueller Spot wird ignoriert.
Aufnahme- und Event-Optionen lassen sich vor Beginn einer neuen Session ändern.
Die Diagnose-Aufzeichnung ist beim Programmstart und nach jeder neuen Sitzung aus.

### Auto-Pause und Klasse

Nach **3 Minuten ohne neuen gezählten Drop** pausieren Aufnahme und Sitzungsuhr
automatisch. Unter **Optionen → Automatische Pause** sind 1–60 Minuten einstellbar,
auch während der Sitzung; der Wert wird gespeichert. Wiederholt sichtbare Zeilen
und negative Mengenkorrekturen setzen den Timer nicht zurück. **Fortsetzen** startet
ein neues Wartefenster. Bei der automatischen Pause wird die gesamte Zeit seit dem
letzten neuen Drop im aktuellen Laufabschnitt von der Sessiondauer abgezogen –
auch bei einer verspäteten Timerprüfung, nicht nur die eingestellte Minutenzahl.
Ohne Drop seit Start/Fortsetzen trägt dieser Abschnitt keine Zeit bei; zuvor
gesammelte aktive Zeit bleibt erhalten. Manuelles Pausieren zieht keine Zeit ab.
Der Garmoth-Upload verwendet ebenfalls die so korrigierte Sessiondauer.

Die Klasse einschließlich Spezialisierung wird aus den gespeicherten Skill-Slots
ermittelt und neben dem Spot angezeigt. Unbekannt/mehrdeutig bleibt ausdrücklich
unbekannt. Vor dem Start oder während einer Pause lässt sie sich unter **Optionen**
korrigieren. Für einen Charakterwechsel eine neue Sitzung anlegen. Details und
Grenzen: [Klassenerkennung](docs/CLASS_DETECTION.md).

### Optionaler Upload nach Garmoth

Einmal unter **Optionen → Garmoth-Key** den eigenen API-Key aus den Garmoth-
Einstellungen speichern. Er wird mit Windows-DPAPI für den aktuellen Windows-Benutzer
verschlüsselt, nicht als Klartext in den Einstellungen gespeichert. Companion-/Browser-
Anmeldedaten werden nicht übernommen. Dort lässt sich der Key auch wieder entfernen.

Im selben Dialog kann **Automatisch jede Grind-Stunde an Garmoth senden** aktiviert
und gespeichert werden; standardmäßig ist die Option aus. Alle **60 aktiven
Grind-Minuten** wird nur der nächste ungesendete Stundenabschnitt übertragen,
während das Tracking weiterläuft. Pausen und eine noch durch Auto-Pause abziehbare
Leerlaufphase lösen keinen Stunden-Upload aus. Die Stunden werden ab Sitzungsbeginn
festgehalten: Wer die Option später aktiviert, lädt bereits vollständige, ungesendete
Stunden nacheinander hoch. Eine angefangene Reststunde bleibt bis zur nächsten vollen
Stunde oder zum manuellen Upload lokal. **Neue Sitzung** und Schließen senden sie
nicht automatisch.

**Ein Klick auf Garmoth-Upload** pausiert eine laufende Sitzung, schließt ausstehenden
Loot ab und sendet die gesamte noch nicht hochgeladene Zeit und Beute. Kein weiterer
Dialog, keine Silber- oder Klasseneingabe im Upload. Verwendet werden die bereits
erkannte/gewählte Klasse, Spot und der Netto-Silberwert des übertragenen Abschnitts
zu den aktuellen Preis-/Steuereinstellungen; keine Differenz alter Silber-Gesamtsummen.

Garmoth erhält Spot, Klasse/Spec, volle aktive Minuten, zugeordnete Lootmengen,
Silberwerte und eine Notiz mit Startzeit/Sitzungs-ID und eindeutiger Abschnitts-ID;
keine Screenshots, OCR-Texte
oder Spieldateien. Mindestens eine volle Minute, ein bekannter Spot und eine bekannte
Klasse sind nötig. Unbekannte oder beim Zielspot nicht unterstützte Items (z. B.
Laila's Petal/Pure Black Stone) werden beim Upload ausgelassen und danach genannt;
lokale Mengen bleiben erhalten. Bei fehlenden Preisen wird die gekennzeichnete
bekannte Silber-Teilsumme verwendet, bei alten Preisen der gekennzeichnete Cachewert.
Sind sämtliche Preise unbekannt, wird kein erfundener Silber-Nullwert hochgeladen.

Ein erfolgreicher automatischer Upload lässt die Sitzung weiterlaufen. Bei einem
unklaren automatischen Ergebnis werden alle weiteren Uploads dieser Sitzung gesperrt,
auch manuelle und nach erneutem Aktivieren der Option; lokales Tracking bleibt möglich.
Zuerst auf Garmoth prüfen. Eine eindeutige Ablehnung oder fehlende Upload-Voraussetzung
setzt die Automatik aus: Ursache beheben und die Garmoth-Optionen erneut speichern
oder manuell hochladen. Unklare Ergebnisse werden nicht automatisch wiederholt.
Nach einem erfolgreichen oder unklaren **manuellen** Upload bleibt die Sitzung wie
bisher gegen erneutes Hochladen/Fortsetzen gesperrt; danach **Neue Sitzung** wählen.

Spätere Mengenkorrekturen werden mit neuem Loot verrechnet, damit gesunkene und wieder
steigende Zähler keinen Doppelupload verursachen. Bereits angelegte Garmoth-Einträge
werden nicht nachträglich geändert. Stundenabschnitte und Doppelupload-Schutz gelten
für die aktuelle lokale Sitzung; Sitzungen werden nach einem Neustart nicht wiederhergestellt.
Der Vertrag ist statisch nachgewiesen und mit Mock-HTTP geprüft; ein echter Upload
mit deinem Konto wurde nicht durchgeführt. [Details](docs/GARMOTH_INTEGRATION.md).

### Silberbewertung

Die Karten zeigen **Silber vor Steuer** und **Silber nach Steuer** statt Drop- und
Itemartenanzahl. Unter **Preise / Steuer** neben Session-Loot lassen sich EU/NA,
Vorteilspaket, Handelsring und Familienruhm einstellen. Standard: EU, kein
Vorteilspaket/Handelsring, Familienruhm 0. Die tatsächlichen eigenen Boni einmal wählen.

Marktpreise werden anonym von Arsha abgerufen und getrennt je Region lokal gecacht;
der Abruf übermittelt keine Sitzung, Mengen oder Bilder. Arsha hat einen eigenen
30-Minuten-Cache, die angezeigte Zeit ist daher die Abrufzeit, keine Echtzeitgarantie.
Trashloot und belegte Companion-Festwerte bleiben steuerfrei. Die Steuerformel und
Stückrundung entsprechen Companion. Ancient Spirit Dust wird aus Caphras/Black-Stone-
Preisen bewertet. Fehlende Preise werden nicht als 0 erfunden: **≥** kennzeichnet
eine Teilsumme, **—** einen noch nicht bewertbaren Lootstand. Details per Mauszeiger
über den Silberwerten; [Preisquellen und Bewertungsgrenzen](docs/SILVER_VALUATION.md).

## Erkennung und Zählung

- Companion-Kalibrierung, Bildaufbereitung, Ziffernvorlagen, OCR, Textreparaturen
  und ursprünglicher globaler Katalogmatcher bilden den unveränderten ersten Erkennungsweg.
- Seit 0.9.4 erhalten fehlende Mengen und nicht erkannte normale Zeilen zusätzliche
  Leseversuche auf den Originalpixeln. Pro Zeilenplatz entsteht höchstens eine
  Beobachtung; erfolgreiche Namen/Mengen werden nicht überschrieben. Rare-Loot
  verwendet weiterhin ausschließlich seinen bisherigen Erkennungsweg.
- Normal- und Rare-Loot verwenden wieder den 10-Frame-Abgleich und das gemeinsame
  Korrektur-Ledger aus 0.5.1, einschließlich dessen Mengen- und Lückenreparaturen.
- Es gibt keine zusätzliche verpflichtende zweite Lesung, eigene Mengenbestätigung,
  Lebensdauer-ID-Zuordnung, BON/JIN/WON-Sperre oder neue Runner-up-Regel mehr.
  Die bereits im Companion-Matcher enthaltenen Regeln bleiben unverändert.
- Nach der Namensauflösung wird der automatisch erkannte Spotpool angewendet.
  Er umfasst den Spot-Hauptloot, den gemeinsamen HighestTier-Pool und die
  gemeinsamen Standard-/Worlddrops; letztere benötigen keinen Event-Schalter.
  Ein fremdes Item wird nicht in den nächstähnlichen erlaubten Namen umgedeutet.
  `Black Gem Fragment` gehört nicht zu den sechs Inner-Edania-Pools und wird nach
  Erkennung eines dieser Spots ausgefiltert.
- Negative Rare-Korrekturen ändern die Summen, zählen aber nicht als neue
  Logeinträge. Auf null korrigierte Itemarten verschwinden aus der Summenliste.

Der Rückbau basiert auf den erhaltenen, hashgeprüften 0.5.1-Assemblies; der eigene
frühere C#-Code wurde daraus mit ILSpy wiederhergestellt. Das ist kein neuer
Genauigkeitsnachweis für reale Spielszenen. Die bekannte kleinere Überzählung des
früheren Stands kann damit ebenfalls zurückkehren.

## Oberfläche und Aufnahme

Die Capture-Pipeline bleibt seriell mit 450 ms Mindestintervall. Es gibt keinen
Screenshot-Vorrat und keine Abhängigkeit von der UI-Antwortzeit. Alle ausgegebenen
Buchungen und Korrekturen werden in der Sitzungssumme übernommen; die UI erhält
nur den neuesten Anzeigezustand.

Das Dashboard zeigt Sitzungsdauer (HH:MM:SS) und Silber vor/nach Steuer.
**Session-Loot** nutzt den Großteil des Fensters: Karten mit Icon,
Itemname und Gesamtmenge, absteigend nach Menge sortiert. Bei Bedarf lässt sich
die Liste scrollen; sie wächst nur mit verschiedenen Itemarten, nicht mit jedem Drop.

Die Sitzungsuhr läuft unabhängig von neuen Frames und benutzt monotone Zeitmessung,
damit Änderungen der Systemuhr die Dauer nicht verfälschen. UI-Screenshot-Thumbnails,
OCR-Debuganzeigen und das Entscheidungslog werden im laufenden Dashboard nicht mehr
erzeugt. Die optionale lokale Diagnose bleibt unter **Optionen** verfügbar und ist
standardmäßig aus. Die Oberfläche verändert Aufnahmeintervall, Spotfilter und
Zählung nicht. Die zusätzlichen OCR-Leseversuche aus 0.9.4 sind oben beschrieben.

Das Trackerfenster und seine Optionen sind normale, aufnehmbare Fenster. Der frühere
Windows-Schalter `WDA_EXCLUDEFROMCAPTURE` ließ sie aus der eingefrorenen Ansicht des
Snipping Tools verschwinden; die App verwendet jetzt `WDA_NONE`. Beim aktiven Tracking
den Tracker nicht über das kalibrierte Lootpanel legen, idealerweise auf einem zweiten
Monitor verwenden: Die Aufnahme sieht auch überlagernde Fenster, nicht verdeckte
Spielpixel. Es wurden keine zusätzlichen Erkennungsfilter oder Verwerfungsregeln eingeführt.

## Lokale Diagnose und Replay

Bei aktivierter Aufzeichnung werden ausschließlich die kalibrierten Lootausschnitte
als PNG sowie OCR-Beobachtungen und Entscheidungen als JSONL gespeichert:
`%LOCALAPPDATA%\BdoGrindTracker\diagnostics\loot-...\observations.jsonl`.
Maximal 2.000 Frames bzw. 250 MiB pro Aufzeichnung. Ein Aufnahmefehler stoppt nur
die Diagnose, nicht das Tracking. Es gibt keine automatische Übertragung.

Nach dem Pausieren lässt sich die Aufzeichnung offline wiederholen:

```powershell
.\Grindcrest.exe --replay "C:\Pfad\zur\Aufzeichnung\observations.jsonl"
```

Der Replay-Bericht wird als neue `replay-*.txt` neben der Aufnahme abgelegt.
Verglichen werden Itemmengen und Buchungen/Korrekturen je Frame. Das Replay führt
die wiederhergestellte Companion-Zählung mit den gespeicherten akzeptierten
OCR-/Matching-Ergebnissen aus; **es führt OCR nicht erneut aus**. Die PNGs dienen
zur visuellen Prüfung. Eine Übereinstimmung mit der Aufnahme ist kein Abgleich
mit dem tatsächlichen Inventarloot; dafür werden manuell überprüfte Sollwerte benötigt.

Neue Aufnahmen verwenden Formatversion 2 und die Enginekennung
`companion-0.7.4-restore-v1`. Frühere Lebensdauer-Tracker-Aufnahmen sind damit nicht
kompatibel und werden nicht stillschweigend mit den geänderten Regeln abgespielt.
Für deren ursprüngliches Replay bleibt die zugehörige ältere EXE erforderlich.

## Grenzen und Sicherheit

Sehr kurz sichtbare, überdeckte oder falsch gelesene Drops können fehlen. Auch der
wiederhergestellte Abgleich kann zu viel zählen. Insbesondere ersetzt ein erfolgreicher
automatisierter Test keinen gemessenen Vorher-/Nachher-Vergleich im Grind.

Der Tracker verarbeitet sichtbare Pixel und liest BDO-UI-Konfigurationsdateien.
Er verwendet keine Prozesseingriffe, Hooks, Netzwerkmitschnitte oder Spieleingaben.
Ohne ausdrücklich aktivierte Diagnose bleiben Bilddaten im Arbeitsspeicher.
Siehe [Sicherheitsgrenze](docs/SAFETY.md) und [Architektur](docs/ANALYSIS.md).

## Entwicklung

Windows 10 Version 2004 oder neuer, .NET 9 SDK und eine installierte Windows-OCR-
Sprache. Die Itemnamen sind englisch; bevorzugte OCR-Sprache ist en-US.

Die interne Assembly heißt aus Kompatibilitätsgründen weiter `BdoGrindTracker`.
Beim Publish wird zusätzlich der gleichwertige Starter `Grindcrest.exe` angelegt.
Der bisherige Dateiname bleibt als Kompatibilitätsstarter enthalten; beide verwenden
dieselben Dateien und Einstellungen. Nicht beide gleichzeitig für dieselbe Sitzung starten.

```powershell
dotnet restore BdoGrindTracker.slnx
dotnet test BdoGrindTracker.slnx -c Release
dotnet run --project src/BdoGrindTracker.App
```

Die Herkunft der eingebetteten 30 Ziffern-PNGs und die historische Untersuchung der
Companion-Version stehen im [Paritätsnachweis](docs/COMPANION_0_7_4_PARITY.md).
Darstellungsicons beeinflussen die Texterkennung nicht.
