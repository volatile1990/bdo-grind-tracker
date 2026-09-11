# Technische Sicherheitsgrenze

## Gelesene Daten

Die Produkt-App liest nur:

- die von Windows Graphics Capture gelieferten Pixel des Black-Desert-Spielfensters;
- `Documents/Black Desert/GameOption.txt`;
- Windows-Deinstallationseinträge zur Lokalisierung der BDO-Installation und deren
  `Resource.ini` zur automatischen Erkennung der Textsprache;
- die für die Companion-Kalibrierung nötigen `gamevariable.xml`-Dateien unter
  `Documents/Black Desert/UserCache`;
- den lokalen Itemkatalog und optionale Darstellungsicons aus dem App-Verzeichnis.

Aufnahme und optionales Ingame-Overlay ordnen Windows-Fenster anhand ihrer
Prozessnamen Black Desert zu und lesen deren Fenstergeometrie, Monitor und Sichtbarkeitsstatus. Die Aufnahme
bindet ein Fenster per HWND und Prozess-ID; sie fällt bei einem Fehler nicht auf
eine Desktopaufnahme zurück. Die App
liest dabei keine Spielprozess-Speicherbereiche oder geladenen Spielmodule.

Für die passive Klassenerkennung werden aus derselben gespeicherten
`gameVariable.xml` ausschließlich die Skill-IDs der Quickslots/Cooldown-Slots
ausgewertet. Es gibt keine Spielbedienung oder Klassenerkennung über Prozessdaten.

Der aus der untersuchten Companion-0.7.4-EXE rekonstruierte Ziffernkatalog ist als
unveränderliche PNG-Nutzlast in der OCR-Assembly eingebettet und wird nicht aus einem
externen Template-Ordner geladen.

Die BDO-Konfigurationsdateien werden ausschließlich lesend geöffnet. Sie liefern
Auflösung, UI-Skalierung, Schriftprofil, Textsprache und sichtbare UI-Positionen. Die Erkennung selbst
arbeitet anschließend nur mit Bildpixeln.

## Nicht verwendet

Der Produktcode verwendet kein `ReadProcessMemory`, keine DLL-
Injection, keine globalen Hooks, kein Packet-Capture und kein `SendInput`. Er liest keine
internen Spieldaten und steuert weder BDO noch BDO Companion. Die Paddle-Modelle und
Ziffernvorlagen sind lokal enthalten.

Ein fehlendes Windows-OCR-Sprachpaket kann der Nutzer ausdrücklich aus Grindcrest
nachinstallieren. Dafür startet die App die vertrauenswürdige Windows-Systemdatei
`dism.exe` mit Administratorabfrage und einer festen Whitelist für die OCR-Pakete
`en-US` und `de-DE`. Es werden keine frei formulierten Befehle oder Scripts erhöht
ausgeführt. Windows bezieht die Komponenten aus seinen Updatequellen; dieser
Vorgang überträgt keine Sessiondaten oder Bilder. Ohne Installationsklick wird kein
Paket installiert, und Windows wird nicht automatisch neu gestartet.

## Datenschutz und Speicherung

Grindcrest setzt seine Fenster auf die normale Windows-Aufnahmefreigabe (`WDA_NONE`),
statt sie global aus Screenshots auszublenden. Das behebt ihr Verschwinden in der
Snipping-Ansicht; es löst selbst keine Bildschirmaufnahme aus. Auch die beiden
Optionsdialoge bleiben aufnehmbar. Der API-Key ist dort weiterhin maskiert.
Die Aufnahme des Spielfensters verarbeitet dessen eigene Bildoberfläche. Andere
Desktopfenster gehören nicht zu dieser Oberfläche; spielinterne Meldungen schon.

Für das separat aktivierbare Ingame-Overlay gilt standardmäßig
`WDA_EXCLUDEFROMCAPTURE`, damit seine Metriken nicht selbst in der OCR landen.
Dieser Ausschluss betrifft nur das Overlay und lässt sich in dessen Editor
abschalten. Optionale globale Overlay-Tastenkürzel verwenden `RegisterHotKey`,
keine Tastatur-Hooks. Layout und Position werden unabhängig von Sessiondaten in
`overlay.json` gespeichert. [Bedienung und Grenzen](OVERLAY.md).

Aufgenommene Frames werden im Arbeitsspeicher verarbeitet und nach der Analyse
freigegeben. Ohne ausdrückliche Aktivierung der lokalen Diagnose werden keine
Screenshots exportiert. Bei aktiviertem Opt-in werden ausschließlich kalibrierte
Lootausschnitte (niemals ein Vollbild-Fallback)
sowie Rohtext und Zählentscheidungen
lokal gespeichert. Für die Aufnahme gibt es kein Gesamtlimit für Frames oder
Dateigröße; sie läuft bis zum Sessionende oder einem Aufnahmefehler. Die Prüfung
jedes Eintrags und der Schutz vor zu großen oder ungültigen Lootausschnitten bleiben erhalten.
Die Dateien verlassen den Rechner nicht; Aufnahmepfad siehe README.
Nach Neustart oder neuer Sitzung ist das Opt-in wieder aus. Alte Aufnahmen werden
nicht automatisch gelöscht; der Benutzer kann sie im angezeigten Ordner entfernen.
Auch ein Lootausschnitt kann bei überdecktem Spiel andere sichtbare Inhalte enthalten.
Deshalb Diagnose nur mit tatsächlich sichtbarem Lootpanel aktivieren.

Ein unerwarteter Tracking-Stopp speichert unabhängig von der Loot-Aufzeichnung
einen technischen Nachweis in `last-capture-error.json` im Einstellungsordner.
Die Datei enthält ausschließlich Zeitpunkt, App-/Engineversion, Fehlertyp,
Fehlercode, einen kurzen Parameternamen und begrenzt viele Methodennamen aus dem
Aufrufpfad. Sie enthält weder Bilder noch OCR-Texte, Exception-Nachrichten,
Dateipfade oder Parameterwerte. Ein neuer Fehler überschreibt diese eine Datei;
sie ist auf 8 KiB begrenzt. Normale Pausen erzeugen keinen Eintrag. Schreibfehler
beeinflussen weder den Tracking-Stopp noch die Sicherung der Sitzung. Die Datei
wird nicht hochgeladen.

Die Einstellungsdatei speichert Monitor, Auto-Pause, Preisregion, Steueroptionen,
das Opt-in für stündliche Garmoth-Uploads und technische Versionsangaben, keine
Bilder oder Klartext-Zugangsdaten.
Eine aus 0.6.0/0.6.1 vorhandene manuelle Spot-Einstellung wird für die Erkennung
ignoriert. Der aktive Spot wird ausschließlich aus dem erkannten Trashloot bestimmt,
nicht aus Prozessdaten oder internen Spielzuständen. Das Offline-Replay liest nur
die ausdrücklich angegebene JSONL-Datei und schreibt einen neuen lokalen Bericht
neben diese Datei. Eingebettete Iconpfade werden als Klassifikationstext behandelt
und nicht geöffnet. Frühere Diagnoseformate mit anderer Zähllogik werden abgewiesen.

Lokale Diagnosescreenshots sind nicht Teil des Repositorys oder eines Builds und werden
durch `.gitignore` ausgeschlossen.

## Optionaler Garmoth-Upload

Ein Klick auf **Garmoth-Upload** oder die unter **Optionen → Garmoth-Key** ausdrücklich
aktivierte Stundenautomatik sendet per HTTPS an
`api.garmoth.com/api/external/grind-tracker/sessions/create`. Die Automatik ist
standardmäßig aus und sendet jede volle aktive Stunde als eigenen, noch nicht
übertragenen Abschnitt; das Tracking läuft weiter. Der manuell eingegebene
API-Key wird einmal unter Optionen hinterlegt und mit Windows-DPAPI (CurrentUser)
in einer separaten lokalen Datei verschlüsselt. Beim Upload steht er ausschließlich
im Header `apiKey`, nicht in Payload/URL/Logs. Die App kann ihn unter demselben
Windows-Benutzer wieder entschlüsseln; andere Prozesse desselben Benutzers sind
dadurch nicht grundsätzlich ausgeschlossen. Keine
Browser-Cookies, keine übernommenen Companion-Zugangsdaten, keine Weiterleitungen,
keine automatischen Wiederholungen. Header- und Antwort-Lesezeit sind begrenzt;
Fehlertexte zeigen weder Serverantworten noch Schlüssel an.

Übertragen werden ausschließlich Sitzungsmetadaten und neue Lootmengen samt dem
Netto-Silberwert dieses Abschnitts zu den aktuellen Preisen und Steuereinstellungen.
Nicht unterstützte Garmoth-Items werden nur
aus der Übertragung ausgelassen, lokale Mengen bleiben unverändert. Keine Datei, kein Screenshot, kein Roh-OCR,
kein Konten- oder Charakterverzeichnispfad. Nach unklarem automatischem Ausgang
verhindert eine Sitzungssperre im laufenden Programm weitere automatische und
manuelle Uploads; lokales Tracking bleibt nutzbar. Die Auto-Pause erzeugt keinen
zusätzlichen Reststunden-Upload. Details zu Intervallen, manuellen Uploads und
Fehlerfällen: [Garmoth-Integration](GARMOTH_INTEGRATION.md).

## Programmupdates

Über GitHub installierte Versionen prüfen beim Start und auf manuellen Wunsch die öffentlichen
Releases von `https://github.com/volatile1990/bdo-grind-tracker` per HTTPS. Die
Updateprüfung und der Paketdownload senden keine Sessions, Screenshots, OCR-Texte,
Garmoth-Schlüssel oder GitHub-Zugangsdaten. Metadatenanfragen sind zeitlich begrenzt;
ein Netzwerkfehler beeinflusst das Tracking nicht. Vorschau, UI-Prüfungen und
Offline-Replay führen keine Updateprüfung aus.

Downloads beginnen ausdrücklich per Klick. Velopack prüft die Paketintegrität.
Die Installation erfordert einen weiteren Klick bei pausiertem Tracking und ohne
laufenden Session-Vorgang. Vor dem Start des Updaters wird der Verlauf gespeichert
und das Tracking beendet. Scheitert die erste Sicherung, bleibt die App mit der
Session im Arbeitsspeicher geöffnet und die Installation kann erneut versucht werden.
Beim normalen App-Start werden heruntergeladene Pakete nicht automatisch angewendet.
Die Programmdateien liegen unter `%LOCALAPPDATA%\Grindcrest`, getrennt von den
bestehenden Nutzerdaten unter `%LOCALAPPDATA%\BdoGrindTracker`. Die Updateauswahl
wird dort in `update-settings.json` gespeichert. Beta-Versionen und stabile
Versionen verwenden getrennte Kanäle; es gibt keine automatischen Downgrades.

Die Microsoft-Store-Ausgabe verwendet ausschließlich die Windows-Store-Schnittstelle.
Ab 1.0.1 prüft sie beim Start, alle sechs Stunden weiterer Nutzung und auf Knopfdruck.
Download und Installation werden getrennt durch den Nutzer gestartet; Windows kann
eine Bestätigung anzeigen. Vor der Installation müssen Tracking und andere
Session-Vorgänge ruhen und die lokalen Daten erfolgreich gespeichert sein. Während
der Installation sind Session-Aktionen gesperrt. Bei Abbruch oder Fehler wird die
Sperre aufgehoben; Microsoft übernimmt Paketprüfung und Installation. Die unabhängig
von Grindcrest verwaltete automatische Updatefunktion des Stores bleibt verfügbar.
Vorschau und Prüfmodi verwenden auch hier kein Update-Backend.

## Öffentliche Marktpreise

Die App ruft beim Anzeigen und anschließend höchstens alle zehn Minuten gebündelt
öffentliche Itempreise per HTTPS-GET von `api.arsha.io` ab. Übertragen werden nur
Serverregion, feste öffentliche Markt-Item-IDs und die Sprache. Keine Lootmengen,
Klasse, Sitzungsdaten, Bilder, API-Keys oder Cookies. Antwortgröße, Lesezeit und
Wiederholungsrate sind begrenzt; Weiterleitungen sind aus. Preise werden regional
getrennt in `market-prices-v1.json` gespeichert und offline mit Altershinweis
weiterverwendet. Preisfehler greifen niemals in OCR oder Zählung ein.

## Analyse- und Lieferumfang

Die Untersuchung von BDO Companion war statisch und lesend. Der Tracker enthält keine
Companion-Binärdateien, keine dekompilierten Quelltextteile von BDO Companion und keine
internen Datenbanken. Für den Rückbau auf den eigenen Stand 0.5.1 wurde dessen
erhaltene, hashgeprüfte C#-Assembly mit ILSpy gelesen und eigener Code wiederhergestellt.
Er enthält die 30 byteidentisch rekonstruierten Ziffern-PNGs der drei UI-Schriften; ihre
Herkunft ist mit EXE-Hash, RVA, Abmessung und Einzelhash dokumentiert. Weder BDO noch BDO
Companion wurden für Analyse oder Tests per Eingabeautomation gesteuert.
