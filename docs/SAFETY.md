# Technische Sicherheitsgrenze

## Gelesene Daten

Die Produkt-App liest nur:

- die von Windows Graphics Capture gelieferten Pixel des Black-Desert-Spielfensters;
- `Documents/Black Desert/GameOption.txt`;
- Windows-Deinstallationseinträge zur Lokalisierung der BDO-Installation und deren
  `Resource.ini` zur automatischen Erkennung der Textsprache;
- die für die Companion-Kalibrierung nötigen `gamevariable.xml`-Dateien unter
  `Documents/Black Desert/UserCache`;
- Dateinamen und Schreibzeiten der Dateien unter
  `<BDO-Installation>/Cache/<Welt>/MyJournal`, um nach einem Charakterwechsel
  die richtige gespeicherte Skillbelegung zuzuordnen; keine Journalinhalte;
- den lokalen Itemkatalog und optionale Darstellungsicons aus dem App-Verzeichnis.

Aufnahme und optionales Ingame-Overlay ordnen Windows-Fenster anhand ihrer
Prozessnamen Black Desert zu und lesen deren Fenstergeometrie, Monitor und Sichtbarkeitsstatus. Die Aufnahme
bindet ein Fenster per HWND und Prozess-ID; sie fällt bei einem Fehler nicht auf
eine Desktopaufnahme zurück. Die App
liest dabei keine Spielprozess-Speicherbereiche oder geladenen Spielmodule.

Für die passive Klassenerkennung werden aus derselben gespeicherten
`gameVariable.xml` ausschließlich die Skill-IDs der Quickslots/Cooldown-Slots
ausgewertet. Es gibt keine Spielbedienung oder Klassenerkennung über Prozessdaten.
Der Journal-Dateiname verbindet die jüngste Einloggaktivität mit dem passenden
Charakterordner desselben UserCache-Profils und derselben Welt. Pfade und
Charakterkennungen bleiben lokal und werden nicht in der Oberfläche oder in
Uploads ausgegeben.

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
nachinstallieren. Dafür startet die App die Windows-Systemdatei `powershell.exe`
mit Administratorabfrage, ohne Benutzerprofil und mit einem fest eingebetteten
Hilfsaufruf. Dieser führt ausschließlich die Systemdatei `dism.exe` mit einer festen
Whitelist für die OCR-Pakete `en-US` und `de-DE` aus und protokolliert dessen
Fortschrittsausgabe lokal. Es werden keine frei formulierten Befehle oder editierbaren
Skriptdateien erhöht ausgeführt und keine Ausführungsrichtlinien geändert.
Windows bezieht die Komponenten aus seinen Updatequellen; dieser
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

Die Einstellungsdatei speichert Monitor, Auto-Pause, das Opt-in für automatische Grinderkennung, Preisregion, Steueroptionen,
das Opt-in für automatische Garmoth-Uploads abgeschlossener Sessions und technische Versionsangaben, keine
Bilder oder Klartext-Zugangsdaten.
Eine aus 0.6.0/0.6.1 vorhandene manuelle Spot-Einstellung wird für die Erkennung
ignoriert. Der aktive Spot wird ausschließlich aus dem erkannten Trashloot bestimmt,
nicht aus Prozessdaten oder internen Spielzuständen. Das Offline-Replay liest nur
die ausdrücklich angegebene JSONL-Datei und schreibt einen neuen lokalen Bericht
neben diese Datei. Eingebettete Iconpfade werden als Klassifikationstext behandelt
und nicht geöffnet. Frühere Diagnoseformate mit anderer Zähllogik werden abgewiesen.

Lokale Diagnosescreenshots sind nicht Teil des Repositorys oder eines Builds und werden
durch `.gitignore` ausgeschlossen.

## Automatische Garmoth-Vergleichswerte

Beim Tracking-Start und zu jeder weiteren vollen aktiven Stunde lädt eine
separate, unsichtbare InPrivate-WebView2-Instanz die öffentliche Garmoth-Übersicht.
Die App übernimmt daraus ausschließlich die öffentlichen Statistik- und
Tier-JSON-Antworten. Sie übergibt weder Sessions noch API-Keys, Screenshots oder
Cookies anderer Browser. Der Datenbrowser hat ein eigenes Profil und keine
Hostobjekte oder Nachrichtenbrücke zur App. Popups, Downloads und Berechtigungen
sind deaktiviert; Netzwerkzugriffe sind auf Garmoth-Hosts beschränkt. Nach dem
Abruf, bei Abbruch oder spätestens nach 45 Sekunden wird die Instanz geschlossen.
Vergleichswerte werden lokal gespeichert. Details: [Grind-Bewertung](GRIND_RATING.md).

## Optionaler Garmoth-Upload

Ein bestätigter manueller Upload oder die unter **Garmoth** ausdrücklich
aktivierte Option **Abgeschlossene Sessions automatisch hochladen** sendet per HTTPS an
`api.garmoth.com/api/external/grind-tracker/sessions/create`. Die Automatik ist
standardmäßig aus und sendet beim Anlegen einer neuen Session die vorherige,
abgeschlossene Session als einen Eintrag. Der manuell eingegebene
API-Key wird einmal unter Garmoth hinterlegt und mit Windows-DPAPI (CurrentUser)
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
manuelle Uploads; lokales Tracking bleibt nutzbar. Manuelle Pause, Auto-Pause und
Programmende lösen keinen Upload aus. Details zu Sessionabschluss, manuellen Uploads und
Fehlerfällen: [Garmoth-Integration](GARMOTH_INTEGRATION.md).

## Programmupdates

Grindcrest wird ausschließlich über Microsoft Store installiert und aktualisiert.
Die App verwendet die Windows-Store-Schnittstelle bei vorhandener Paketidentität
und prüft beim Start, alle sechs Stunden weiterer Nutzung und auf Knopfdruck auf
freigegebene Updates. Diese Prüfung überträgt keine Sessions, Screenshots,
OCR-Texte oder Garmoth-Schlüssel an Microsoft.

**Jetzt aktualisieren** startet Download und Installation nach Nutzerbestätigung.
Windows kann zusätzlich eine Bestätigung anzeigen. Vor der Installation müssen
Tracking und andere Session-Vorgänge ruhen und die lokalen Daten erfolgreich
gespeichert sein. Während der Store-Operation sind Session-Aktionen gesperrt.
Bei Abbruch oder Fehler wird die Sperre aufgehoben; Microsoft übernimmt
Paketprüfung und Installation. Die unabhängig von Grindcrest verwaltete
automatische Updatefunktion des Stores bleibt verfügbar.

Nutzerdaten liegen im `LocalState`-Ordner der Store-Paketfamilie. Vorschau,
UI-Prüfungen, Offline-Replay und lokale Entwicklungsbuilds verwenden kein
Update-Backend. [Store-Installation und Updates](MICROSOFT_STORE.md#installation-daten-und-updates).

## Öffentliche Marktpreise

`MarketLootPriceProvider` ruft öffentliche Itempreise zunächst gebündelt per
HTTPS-GET von `api.arsha.io` ab. Ein HTTP 500 der Sammelanfrage erlaubt begrenzte
Einzelabfragen bei Arsha mit höchstens vier gleichzeitig laufenden Anfragen.
Bei Ausfall oder einer Teilantwort werden nur die noch fehlenden Katalog-IDs
in einem HTTPS-POST an
`https://eu-trade.naeu.playblackdesert.com/Trademarket/GetWorldMarketSearchList`
beziehungsweise den fest vorgegebenen NA-Host
`na-trade.naeu.playblackdesert.com` abgefragt. Übertragen werden nur öffentliche
Markt-Item-IDs, die gewählte EU-/NA-Region und bei Arsha die Sprache. Arsha erhält
den aktuellen App-User-Agent, der direkte Abruf den dokumentierten User-Agent
`BlackDesert`. Keine Lootmengen, Klasse, Sitzungsdaten, Bilder, Zugangsdaten,
API-Keys oder Cookies; Weiterleitungen und Cookieverarbeitung sind aus.

Die [Velia-Dokumentation](https://developers.veliainn.com/) beschreibt diese
Pearl-Abyss-Endpunkte. Laut [Arsha-Repository](https://github.com/guy0090/api.arsha.io)
greift Arsha selbst als Proxy mit Cache auf dieselbe Quelle zu. Der Fallback
kann daher einen Arsha-Ausfall abfangen, teilt aber dessen Pearl-Abyss-Abhängigkeit;
eine höhere allgemeine Verfügbarkeit oder SLA wird nicht zugesichert.

Jede Quelle hat ein eigenes Zeitbudget von acht Sekunden einschließlich des
Antwortlesens; eine aktive Aktualisierung nutzt damit höchstens 16 Sekunden
Netzwerkbudget. Arsha-Einzelabfragen teilen dessen acht Sekunden. Antwortgröße
und Zahl der Preiszeilen sind begrenzt; ungültige oder widersprüchliche Preise
werden verworfen. Nach erfolgreicher Aktualisierung wird zehn Minuten gewartet.
Fehlerpausen beginnen je Quelle und Region bei 30 Sekunden und steigen bis
15 Minuten; `Retry-After` wird für die jeweilige Quelle bis höchstens eine
Stunde berücksichtigt. Es gibt keine unbegrenzten Wiederholungen. Ein
Nutzerabbruch beendet auch den Fallback.

Beide Quellen teilen den unveränderten lokalen Cache `market-prices-v1.json`.
Preise bleiben nach EU und NA getrennt und werden offline mit Altershinweis
weiterverwendet. Fehlende Preise einer Teilantwort behalten ihren bisherigen
Zeitstempel. Preisfehler greifen niemals in OCR oder Zählung ein.

## Analyse- und Lieferumfang

Die Untersuchung von BDO Companion war statisch und lesend. Der Tracker enthält keine
Companion-Binärdateien, keine dekompilierten Quelltextteile von BDO Companion und keine
internen Datenbanken. Für den Rückbau auf den eigenen Stand 0.5.1 wurde dessen
erhaltene, hashgeprüfte C#-Assembly mit ILSpy gelesen und eigener Code wiederhergestellt.
Er enthält die 30 byteidentisch rekonstruierten Ziffern-PNGs der drei UI-Schriften; ihre
Herkunft ist mit EXE-Hash, RVA, Abmessung und Einzelhash dokumentiert. Weder BDO noch BDO
Companion wurden für Analyse oder Tests per Eingabeautomation gesteuert.
