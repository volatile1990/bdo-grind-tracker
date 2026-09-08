# Grindcrest

Lokaler, passiver Loot-Tracker für Black Desert, Version **1.0.0** (bisher BDO
Grind Tracker), mit vollständig neuem **Blazor-Hybrid-Frontend** für Windows.
Live-Session, Verlauf, Garmoth, Lootkorrekturen, Bestätigungsdialoge und Einstellungen werden
als lokale Razor-Komponenten in WebView2 dargestellt. Das Dashboard bietet eine
responsive dunkle Oberfläche, Spotbilder, Silber- und Trash-Kennzahlen, durchsuchbare
Loot-Tabellen, Stundenwerte und Tastaturbedienung. Die Sitzungssteuerung ist von der
Darstellung getrennt; es wird kein Webserver gestartet und keine UI aus dem Netz geladen.

Der bestätigte Zählerstand aus **0.9.6-test.2** bleibt erhalten. Bestehende Einstellungen,
Verlaufseinträge und der verschlüsselte Garmoth-Key werden weiterverwendet.
[Architektur, Voraussetzungen und UI-Prüfung](docs/BLAZOR_HYBRID.md).

Der erste Erkennungspfad und die normale Zählung basieren wieder
auf dem Companion-Stand wie in 0.9.5.
Die zusätzlichen Bestätigungs- und Lebensdauerregeln aus 0.6.0/0.6.1 sind entfernt.
Erhalten bleiben der automatische Spotfilter und die Verbesserungen der UI-Geschwindigkeit.

0.9.6-test.2 nimmt die Zähleränderung aus test.1 zurück. Im Live-Test wurden dort nur
etwa 1.500 von 5.000 Trashloot gezählt: Gleiche OCR-Zeilen können neue gleiche Drops
darstellen und dürfen nicht dauerhaft zu einem einzigen Drop zusammengefasst werden.
Der ursprüngliche Drei-Bilder-Zyklus ist deshalb wieder aktiv. Test.1 wird ersetzt.

Erhalten bleibt ausschließlich die getrennte Mengenübernahme beim Nachlesen eines
zuvor nicht erkannten Itemnamens: Eine vollständig gelesene OCR-Endmenge hat Vorrang
vor einer widersprüchlichen Template-Menge (etwa Text `x8`, Template `1`). Bereits
erfolgreich erkannte Zeilen bleiben erhalten. Bitte mit einer neuen Sitzung und
Inventarmengen vor/nach einem kurzen Grind vergleichen. Der ursprüngliche Zähler
bleibt eine Heuristik; eine vollständige Beseitigung aller Zählfehler ist nicht belegt.

Der aktuelle Entwicklungsstand unterstützt alle sechs Inner-Edania-Zonen. Zu den
bisherigen Spots kommen Aresion Temple, Scales of Judgment und Event Horizon mit
automatischer Erkennung am jeweiligen Trashloot, vollständigem Hauptloot-Pool,
Silberbewertung, Originalicons und Garmoth-Zuordnung hinzu.

Der Bereich **Verlauf** speichert Grind-Sitzungen ausschließlich lokal. Er
zeigt sie wahlweise chronologisch mit aufklappbaren Lootdetails oder gesammelt in
kompakten Spot-Kacheln. Die Kacheln verwenden die jeweiligen Gebietsbilder und
zeigen empfohlenen AP/DP, AP-Limit, farbige Traits, empfohlenen Widerstandskristall
sowie Trashloot-Icon und Silberwert. Die maximierte Spotansicht ergänzt Kennzahlen,
offizielle Klassensymbole und eine horizontal scrollbare, vollständige Loot-Tabelle.

0.9.5 ergänzt den optionalen automatischen Garmoth-Upload. Unter **Garmoth →
Zugang & Automatik** aktivieren: Jede volle Stunde aktiver Grindzeit wird als eigener
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

### Installation und Updates

Den Windows-x64-Installer `Grindcrest-win-x64-stable-Setup.exe` aus den
[GitHub Releases](https://github.com/volatile1990/bdo-grind-tracker/releases) laden.
Version 1.0.0 verwendet den stabilen Updatekanal. Das Setup enthält .NET und installiert bei Bedarf WebView2 und
die benötigte Visual-C++-Laufzeit. Eine passende Windows-OCR-Sprache wird weiterhin benötigt.

Installierte Versionen suchen beim Start nach Updates. Unter **Einstellungen →
App-Updates** lassen sich Updates herunterladen und mit **Installieren und neu starten**
anwenden. Dazu muss die Session pausiert sein. Vor dem Neustart wird sie im Verlauf
gespeichert; nach dem Neustart beginnt eine neue Session. Es gibt keinen erzwungenen
Neustart während eines Grinds. Beta-Updates lassen sich dort ein- und ausschalten;
beim Wechsel zurück zu stabil wartet die App auf die nächste höhere stabile Version.

Für den Wechsel von **0.10.0-test.2** zu **1.0.0** unter **App-Updates → Beta-Updates
erhalten** den Schalter ausschalten. Die App sucht dann im stabilen Kanal und bietet
1.0.0 zum Download an. Alternativ den aktuellen stabilen Installer ausführen.

Einstellungen, Verlauf und Garmoth-Key bleiben unter `%LOCALAPPDATA%\BdoGrindTracker`.
Die Installation liegt getrennt unter `%LOCALAPPDATA%\Grindcrest`. Für eine bisher
entpackte Version den Installer einmal ausführen; die vorhandenen Daten werden
weiterverwendet. Vorschau und Prüfmodi rufen keine Updates ab.

[Neue Version über GitHub veröffentlichen und lokal bauen](docs/RELEASING.md).

Für die Microsoft-Store-Ausgabe ist ab **1.0.1** ein eigener Updateablauf vorbereitet:
Grindcrest sucht beim Start und alle sechs Stunden nach freigegebenen Updates und
zeigt einen Hinweis in der App. **Einstellungen → App-Updates** bietet Download
und **Update installieren**, ohne die Store-App öffnen zu müssen. Vor der Installation
muss das Tracking pausiert sein; die Session wird gespeichert. Windows kann eine
Bestätigung anzeigen und Grindcrest schließen. Die Freigabe neuer Pakete erfolgt
weiterhin über Microsoft. [Store-Paket erstellen und einreichen](docs/MICROSOFT_STORE.md).

### Tracking starten

1. `Grindcrest.exe` starten und unter **Einstellungen** den Spielbildschirm prüfen.
2. Dort optional **Event-Loot mitzählen** aktivieren. Das ergänzt ausschließlich die
   explizite Event-Liste, keine beliebigen fremden Items.
3. Optional unter **Loot-Diagnose → Diese Session aufzeichnen** die Diagnose aktivieren.
   Änderungen werden automatisch übernommen. Unter **Live-Session** auf **Tracking starten** klicken.
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
   aktive Grindzeit weiter; Pausen zählen nicht mit. **Neue Session** setzt Uhr,
   Summen, Zählzustand und Spot zurück. Vor einem Spotwechsel eine neue Sitzung anlegen.

Beim Pausieren, beim Anlegen einer neuen Sitzung und beim Beenden wird der aktuelle
Stand im Bereich **Verlauf** aktualisiert. Dort lässt sich zwischen **Alle Sessions**
(chronologisch) und **Grindspots** wechseln; ein Klick auf eine Sitzung oder Spot-Kachel zeigt die
zugehörigen Stunden und Lootdetails aus. Gespeichert werden höchstens 500 Sitzungen
im lokalen App-Konfigurationsordner, ohne Cloud-Synchronisierung.

Vor dem ersten erkannten Trashloot wird kein zusätzlicher Spotfilter angewendet;
es muss kein Spot manuell ausgewählt werden. Danach bleibt der erkannte Spot bis
zur neuen Sitzung gesperrt. Ein früher gespeicherter manueller Spot wird ignoriert.
Aufnahme- und Event-Optionen lassen sich vor Beginn einer neuen Session ändern.
Die Diagnose-Aufzeichnung ist beim Programmstart und nach jeder neuen Sitzung aus.

### Automatischer Mengen-Fallback aus dem Item-Chat

Der Haupt-Droplog ist Voraussetzung für die Erfassung. Fehlt seine sichtbare,
eindeutige Position in der aktiven BDO-UI-Konfiguration, bleibt der Start gesperrt
und die Live-Ansicht zeigt **Tracking nicht möglich** mit einer Anleitung.
Beim Start und während der Aufnahme wird die Position erneut geprüft. Verschwindet
sie oder ändern sich die zugehörigen Anzeigeeinstellungen, hält die Erfassung an.
Nach dem Speichern der korrigierten BDO-UI-Einstellungen Grindcrest neu starten.
Ein leeres Droplog ohne neue Drops ist kein Fehler.

Ein separates, sichtbares BDO-Chatfenster kann fehlende Mengen im normalen Lootpanel
ergänzen. Im Spiel unter **System** ausschließlich **Beute** (englisch: **Private Item**) aktivieren und
die normalen Chatkanäle ausschalten. Die gespeicherte UI muss dieses Fenster als
eingeblendet und vom Hauptchat getrennt enthalten. Der Tracker liest Position,
Größe und Filter automatisch aus der aktiven `gamevariable.xml`; Änderungen werden
während der Aufnahme alle zwei Sekunden geprüft. Das Fenster muss auf dem gewählten
Spielbildschirm sichtbar bleiben und am Ende des Chatverlaufs stehen.

Unterstützt werden vollständige deutsche Meldungen wie
`Ihr habt 6 x [Helm eines Anhängers Elions] erhalten.` sowie englische Meldungen wie
`You have obtained [Elion Follower's Helmet] x6.`. Der Chat erzeugt keine eigenen
Buchungen, sondern ergänzt ausschließlich eine noch fehlende Menge einer bereits
erkannten normalen Lootzeile. Vorhandene Mengen, einschließlich einer möglicherweise
falsch erkannten `1`, werden nicht überschrieben. Alte Chatzeilen beim Start oder
Fortsetzen, mehrdeutige Folgen und unklare Zuordnungen liefern keine Ersatzmenge.
Ohne geeignetes Chatfenster läuft die bisherige Erkennung weiter.
Die Live-Ansicht zeigt dann seitlich den optionalen Hinweis **Erkennung ergänzen**
mit einer aufklappbaren Anleitung. Er verschwindet, sobald ein passendes Fenster
erkannt wird; ein konfiguriertes, momentan leeres Item-Chatfenster gilt als vorhanden.
[Zuordnung, Diagnose und Grenzen](docs/OCR_RECOVERY.md#item-chat-als-mengen-fallback).

Fehlende Mengen erhalten den hinterlegten Mindestwert für Item und Spot. Die
Grenzen stammen aus der ausgefüllten Dropmengentabelle; bei festen 1/1-Drops entfällt
die Mengen-OCR. [Dropmengen und Korrekturen](docs/DROP_QUANTITIES.md).

### Auto-Pause und Klasse

Nach **3 Minuten ohne neuen gezählten Drop** pausieren Aufnahme und Sitzungsuhr
automatisch. Unter **Einstellungen → Automatische Pause nach** sind 1–60 Minuten einstellbar,
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
unbekannt. Vor dem Start oder während einer Pause lässt sie sich unter **Einstellungen**
korrigieren. Für einen Charakterwechsel eine neue Sitzung anlegen. Details und
Grenzen: [Klassenerkennung](docs/CLASS_DETECTION.md).

### Optionaler Upload nach Garmoth

Einmal unter **Garmoth → Zugang & Automatik** den eigenen API-Key aus den Garmoth-
Einstellungen einfügen. Beim Verlassen des Feldes oder mit Enter wird er automatisch
gespeichert und mit Windows-DPAPI für den aktuellen Windows-Benutzer
verschlüsselt, nicht als Klartext in den Einstellungen gespeichert. Companion-/Browser-
Anmeldedaten werden nicht übernommen. Dort lässt sich der Key auch wieder entfernen.

Im selben Bereich wird **Stündlich automatisch hochladen** beim Umschalten sofort
gespeichert; standardmäßig ist die Option aus. Alle **60 aktiven
Grind-Minuten** wird nur der nächste ungesendete Stundenabschnitt übertragen,
während das Tracking weiterläuft. Pausen und eine noch durch Auto-Pause abziehbare
Leerlaufphase lösen keinen Stunden-Upload aus. Die Stunden werden ab Sitzungsbeginn
festgehalten: Wer die Option später aktiviert, lädt bereits vollständige, ungesendete
Stunden nacheinander hoch. Eine angefangene Reststunde bleibt bis zur nächsten vollen
Stunde oder zum manuellen Upload lokal. **Neue Sitzung** und Schließen senden sie
nicht automatisch.

Der Menüpunkt **Garmoth** bündelt alle Uploads. **Session-Anteil hochladen → Jetzt
hochladen** pausiert eine laufende Sitzung, schließt ausstehenden
Loot ab und sendet die gesamte noch nicht hochgeladene Zeit und Beute. Der Dialog
bestätigt die Übertragung; Silber- und Klasseneingaben sind dort nicht nötig. Verwendet werden die bereits
erkannte/gewählte Klasse, Spot und der Netto-Silberwert des übertragenen Abschnitts
zu den aktuellen Preis-/Steuereinstellungen; keine Differenz alter Silber-Gesamtsummen.

Unter **Gespeicherte Sessions** lassen sich frühere Grinds nach Spot und Upload-Status
filtern und nachträglich hochladen. Die aktuelle Session erscheint ausschließlich
oben. Übertragungsvermerke können auch einzelne automatische Stunden betreffen;
ein erneuter Gesamt-Upload bleibt dann gesperrt.

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
setzt die Automatik aus: Ursache beheben und den Schlüssel korrigieren oder
**Automatik fortsetzen** wählen. Allgemeine Einstellungen reaktivieren die Automatik nicht.
Unklare Ergebnisse werden nicht automatisch wiederholt.
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
  Die versuchsweise dauerhafte Zuordnung aus 0.9.6-test.1 ist wegen starker
  Unterzählung zurückgenommen; der ursprüngliche Drei-Bilder-Zyklus ist wieder aktiv.
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

Das Dashboard zeigt aktive Sitzungsdauer (HH:MM:SS), Trashloot, Netto-Silber und
Silber pro Stunde. **Dein Loot** zeigt Itemicons, Mengen und Silberwerte als
durchsuchbare Tabelle. Sortierung nach Silber, Menge oder Name und der Wechsel
zwischen Gesamtmengen und Stundenwerten verändern nur die Darstellung.

Die Sitzungsuhr läuft unabhängig von neuen Frames und benutzt monotone Zeitmessung,
damit Änderungen der Systemuhr die Dauer nicht verfälschen. UI-Screenshot-Thumbnails,
OCR-Debuganzeigen und das Entscheidungslog werden im laufenden Dashboard nicht mehr
erzeugt. Die optionale lokale Diagnose bleibt unter **Einstellungen** verfügbar und ist
standardmäßig aus. Die Oberfläche verändert Aufnahmeintervall, Spotfilter und
Zählung nicht. Die zusätzlichen OCR-Leseversuche aus 0.9.4 sind oben beschrieben.

Das Trackerfenster und seine Optionen sind normale, aufnehmbare Fenster. Der frühere
Windows-Schalter `WDA_EXCLUDEFROMCAPTURE` ließ sie aus der eingefrorenen Ansicht des
Snipping Tools verschwinden; die App verwendet jetzt `WDA_NONE`. Beim aktiven Tracking
den Tracker nicht über das kalibrierte Lootpanel legen, idealerweise auf einem zweiten
Monitor verwenden: Die Aufnahme sieht auch überlagernde Fenster, nicht verdeckte
Spielpixel. Es wurden keine zusätzlichen Erkennungsfilter oder Verwerfungsregeln eingeführt.

## Lokale Diagnose und Replay

HDR-Aufnahmen können FP16-Pixel erhalten, bevor sie für die OCR umgewandelt werden.
Damit bleiben helle Abstufungen erhalten, die im bisherigen Aufnahmeweg verloren
gehen konnten. Die Aufzeichnung kennzeichnet dies mit `isHdr: true` und
`isToneMapped: true`. Technische Details, Fallback und Stand der Prüfung stehen in
[HDR_CAPTURE.md](docs/HDR_CAPTURE.md).

Bei aktivierter Aufzeichnung werden ausschließlich die kalibrierten Lootausschnitte
und gegebenenfalls das verwendete Item-Chatfenster
als PNG sowie OCR-Beobachtungen und Entscheidungen als JSONL gespeichert:
`%LOCALAPPDATA%\BdoGrindTracker\diagnostics\loot-...\observations.jsonl`.
Die Aufzeichnung läuft ohne Gesamtlimit für Frames oder Dateigröße bis zum Ende
der Session; Pause und Fortsetzen gehören zur selben Aufnahme. Ein Aufnahmefehler,
etwa ein voller Datenträger, stoppt nur die Diagnose, nicht das Tracking.
Es gibt keine automatische Übertragung.

Bei einem unerwarteten Tracking-Stopp schreibt die App außerdem einen kleinen
technischen Fehlernachweis nach `%LOCALAPPDATA%\BdoGrindTracker\last-capture-error.json`.
Er enthält Zeit, Versionen, Fehlertyp und aufrufende Methoden, keine Bilder,
OCR-Texte, Fehlermeldungstexte oder Parameterwerte. Es bleibt nur der letzte Fehler
mit höchstens 8 KiB erhalten; dafür muss die Loot-Aufzeichnung nicht aktiv sein.
Normales Pausieren erzeugt keinen Fehlernachweis.

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
`companion-0.7.4-minimum-quantity-v4`. Die aktive Mindestmengen-Tabelle wird im Header
eingebettet; ältere Aufnahmen ohne Tabelle behalten den bisherigen Mengenersatz.
Aufnahmen mit `companion-0.7.4-recovery-fix-v3`, `companion-0.7.4-restore-v1` (0.9.5)
oder `companion-0.7.4-overcount-fix-v2` (test.1) lassen sich zum ausdrücklich
gekennzeichneten Vergleich mit dem aktuellen Zähler öffnen. Dessen Verhalten
entspricht ohne Mindestmengen-Tabelle wieder 0.9.5; gespeicherte OCR-Mengen werden im Replay nicht repariert.
Für die ursprüngliche test.1-Zählung wäre die damalige EXE erforderlich.
Frühere Lebensdauer-Tracker-Aufnahmen bleiben inkompatibel.

## Grenzen und Sicherheit

Sehr kurz sichtbare, überdeckte oder falsch gelesene Drops können fehlen. Auch der
wiederhergestellte Abgleich kann zu viel zählen. Insbesondere ersetzt ein erfolgreicher
automatisierter Test keinen gemessenen Vorher-/Nachher-Vergleich im Grind.

Der Tracker verarbeitet sichtbare Pixel und liest BDO-UI-Konfigurationsdateien.
Er verwendet keine Prozesseingriffe, Hooks, Netzwerkmitschnitte oder Spieleingaben.
Ohne ausdrücklich aktivierte Diagnose bleiben Bilddaten im Arbeitsspeicher.
Siehe [Sicherheitsgrenze](docs/SAFETY.md) und [Architektur](docs/ANALYSIS.md).

## Entwicklung

Windows 10 Version 2004 oder neuer, .NET 9 SDK, die
[Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
und die Windows-OCR-Sprache des Spiels: Deutsch (`de-DE`) oder Englisch (`en-US`).
Die Textsprache wird standardmäßig aus der BDO-Konfiguration erkannt; unter
**Einstellungen → Spielsprache in Black Desert** ist eine manuelle Auswahl möglich.
Alle unterstützten Drops besitzen geprüfte deutsche Erkennungsnamen, siehe
[Spielsprachen und Quellen](docs/GAME_LANGUAGES.md). Das veröffentlichte Windows-x64-Paket enthält die
.NET-Laufzeit; WebView2 wird vom Betriebssystem bereitgestellt bzw. separat installiert.

Die interne Assembly heißt aus Kompatibilitätsgründen weiter `BdoGrindTracker`.
Beim Publish wird zusätzlich der gleichwertige Starter `Grindcrest.exe` angelegt.
Der bisherige Dateiname bleibt als Kompatibilitätsstarter enthalten; beide verwenden
dieselben Dateien und Einstellungen. Nicht beide gleichzeitig für dieselbe Sitzung starten.

```powershell
dotnet restore BdoGrindTracker.slnx
dotnet test BdoGrindTracker.slnx -c Release
dotnet run --project src/BdoGrindTracker.App
dotnet publish src/BdoGrindTracker.App -c Release -r win-x64 --self-contained true -o artifacts/v1.0.0
```

Die Herkunft der eingebetteten 30 Ziffern-PNGs und die historische Untersuchung der
Companion-Version stehen im [Paritätsnachweis](docs/COMPANION_0_7_4_PARITY.md).
Darstellungsicons beeinflussen die Texterkennung nicht.
