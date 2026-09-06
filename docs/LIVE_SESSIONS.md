# Öffentliche Live-Sessions

Desktop-App, Homepage und API liegen im selben Repository und werden unabhängig
ausgeliefert. Die Website läuft statisch auf GitHub Pages; die ASP.NET-API benötigt
einen separaten Host mit persistentem Speicher. GitHub Pages allein reicht dafür nicht.

## Benutzung

In der App unter **Optionen → Live-Freigabe** die API-Adresse, einen selbst gewählten
Anzeigenamen und den persönlichen Schreibschlüssel des Betreibers hinterlegen.
**Session öffentlich teilen** aktivieren und speichern. Die Freigabe ist bei neuen
und bisherigen Installationen standardmäßig aus. Es werden keine Spielkonten verbunden.

Die Homepage zeigt Anzeigename, Klasse/Spezialisierung, Spot, EU/NA, aktive Dauer,
Silber nach Steuer pro Stunde und aufklappbaren Session-Loot. Unbekannte Preise bleiben
unbekannt, Teilsummen tragen `≥`, alte Marktpreise einen Hinweis.

- Während laufender und pausierter Sessions sendet die App etwa alle 15 Sekunden
  einen vollständigen Stand. Start, Pause und Fortsetzen lösen ein sofortiges Update aus.
- Pause behält dieselbe öffentliche Session und friert die aktive Dauer ein. Eine
  Auto-Pause übernimmt die rückwirkende Zeitkorrektur des Desktop-Trackers.
- Neue Sitzung, abgeschlossener manueller Garmoth-Upload, deaktivierte Freigabe und
  Schließen beenden die Veröffentlichung. Stündliche Garmoth-Uploads beenden sie nicht.
- Ohne erfolgreiches Lebenszeichen verschwindet eine Session nach 90 Sekunden.
  Auch bei einem Ausfall der API entfernt die Homepage abgelaufene Einträge.
- Netzwerkfehler beeinflussen weder OCR noch Zählung oder Auto-Pause. Beim normalen
  Schließen werden höchstens ein laufender Request und eine Abmeldung abgewartet
  (jeweils maximal fünf Sekunden). Bei Abbruch/Ausfall greift der Ablauf auf dem Server.

## Lokal starten

Voraussetzungen: .NET 9 SDK, Node.js 22 oder neuer. Die Desktop-App benötigt Windows;
API und Website funktionieren auch unter Linux. Die Website hat keine npm-Abhängigkeiten.

Im Repository-Root:

```powershell
dotnet run --project src/Grindcrest.Api --no-launch-profile --urls http://127.0.0.1:5080
```

In einem zweiten Terminal:

```powershell
cd web
npm run dev
```

Die Vorschau ist unter `http://127.0.0.1:5173` erreichbar und verwendet lokal
`http://localhost:5080/` als API-Adresse. Für eine andere Adresse `API_BASE_URL`
setzen. Die ungebauten Dateien und der Entwicklungsserver sind nur für lokale Arbeit;
GitHub Pages erhält ausschließlich `web/dist`.

Einen lokalen Schreibschlüssel erzeugen (separater Terminalaufruf):

```powershell
dotnet run --project src/Grindcrest.Api -- --issue-key "Mein PC"
```

Dieser bewusst interaktive Betreiberbefehl gibt Publisher-ID und Schreibschlüssel
einmal aus. Den Schlüssel in der App bei `http://localhost:5080/` eintragen und nicht
in Git, Screenshots, Workflow-Logs oder Website-Konfiguration übernehmen. HTTP ist
ausschließlich für Loopback-Adressen zulässig. Die standardmäßige Datenbank liegt
ignoriert unter `src/Grindcrest.Api/data/live.db`.

## API mit Docker betreiben

Der Container ist für einen einzelnen API-Prozess mit SQLite ausgelegt. Er enthält
keine Windows-/OCR-Abhängigkeiten und läuft als unprivilegierter Benutzer. Ein
persistentes Volume bewahrt Schlüssel und Zustand über Neustarts hinweg.

```sh
docker compose up -d --build
docker compose exec api dotnet Grindcrest.Api.dll --issue-key "Spieler oder Installation"
```

Der Compose-Port ist nur auf `127.0.0.1:5080` erreichbar. Einen HTTPS-Reverse-Proxy
auf demselben Host davor setzen und dessen Domain für Desktop und Website verwenden.
Beispiel für Caddy auf dem Host; Domain durch die tatsächlich eingerichtete Adresse ersetzen:

```caddyfile
api.example.com {
    reverse_proxy 127.0.0.1:5080
}
```

Die Domain muss auf diesen Server zeigen; Caddy benötigt Zugriff auf Port 80/443
für seine TLS-Einrichtung. Bei Proxybetrieb im selben Docker-Netz stattdessen
`api:8080` als Upstream verwenden. Caddy ist nicht Teil des gelieferten Compose-Stacks.

Serverkonfiguration über Umgebungsvariablen:

| Variable | Bedeutung |
| --- | --- |
| `Live__DatabasePath` | SQLite-Datei; im Container `/data/live.db` |
| `Live__AllowedOrigins__0` | Website-Origin, standardmäßig `https://volatile1990.github.io` ohne Repository-Pfad |
| `Live__AllowedOrigins__1` usw. | Weitere erlaubte Origins; bei eigener Domain ergänzen |
| `Live__TrustedProxies__0` usw. | Tatsächliche IP-Adressen vertrauenswürdiger Reverse-Proxys |
| `ASPNETCORE_HTTP_PORTS` | Container-Port, standardmäßig `8080` |

Für korrekte Limits je Besucher die tatsächliche Proxy-IP ausdrücklich konfigurieren.
Ohne diese Konfiguration werden weitergeleitete Client-IP-Header ignoriert und
Besucher hinter einem Proxy teilen dessen IP-Limit. Keine pauschale Freigabe beliebiger
Proxys setzen. CORS erlaubt ausschließlich öffentliche GET-Lesezugriffe von den
eingestellten Origins; die Desktop-App unterliegt nicht Browser-CORS.

Schlüssel sperren (Publisher-ID aus dem Erzeugungsbefehl):

```sh
docker compose exec api dotnet Grindcrest.Api.dll --revoke-key PUBLISHER_ID
```

Dadurch werden auch dessen Session und Sperrvermerke entfernt. Einen neuen Schlüssel
mit `--issue-key` ausgeben. Für Backups die SQLite-Backupfunktion benutzen oder den
Container stoppen und das komplette Volume sichern; die laufende `.db` ohne zugehörigen
WAL zu kopieren ist kein zuverlässiges Backup. Mehrere API-Replikate auf unabhängigen
Volumes sind nicht unterstützt.

## GitHub Pages veröffentlichen

1. Die API unter ihrer endgültigen HTTPS-Adresse betreiben und `/health` prüfen.
2. Im GitHub-Repository unter **Settings → Secrets and variables → Actions → Variables**
   die öffentliche Variable **`LIVE_API_URL`** setzen, beispielsweise `https://api.example.com/`.
   Sie enthält ausschließlich die Adresse, keinen Schreibschlüssel.
3. Unter **Settings → Pages → Build and deployment → Source** **GitHub Actions** auswählen.
4. Änderungen nach `main` pushen oder den Workflow **Homepage** manuell starten.

`.github/workflows/pages.yml` prüft die Website, baut `web/dist` und veröffentlicht
dieses Verzeichnis. Änderungen ausschließlich an der Desktop-App lösen keine neue
Website-Veröffentlichung aus. Ohne gültige HTTPS-API-Adresse schlägt der Build bewusst
fehl, damit keine lokale Testadresse veröffentlicht wird. Nach Änderung der Repository-
Variable den Workflow erneut ausführen.

Die erwartete Projektadresse nach erfolgreicher Veröffentlichung lautet
`https://volatile1990.github.io/bdo-grind-tracker/`. Relative Assetpfade unterstützen
diesen Unterpfad. Diese Dokumentation behauptet keine bereits erfolgte Veröffentlichung.

## Vertrag und Sicherheit

| Endpunkt | Zugriff | Ergebnis |
| --- | --- | --- |
| `GET /health` | Öffentlich | Prozess erreichbar |
| `GET /api/v1/sessions` | Öffentlich | `{ serverTime, sessions: [{ session, updatedAt, expiresAt }] }` |
| `PUT /api/v1/session` | Persönlicher Bearer-Token | Vollständiger Stand; 204, 400, 401, 409 oder 429 |
| `DELETE /api/v1/session/{sessionId}` | Persönlicher Bearer-Token | Diese eigene Veröffentlichung beenden; idempotent 204 |

`session` entspricht `src/Grindcrest.Live/LiveSession.cs`. Die Sitzung enthält eine
zufällige öffentliche ID, eine steigende Sequenz, tatsächliche Startzeit und Zeitpunkt
der Freigabe. Die Desktop-ID wird nicht veröffentlicht. Erneutes Aktivieren der Freigabe
erzeugt eine neue öffentliche ID. Pro Schreibschlüssel ist eine Veröffentlichung aktiv.
Der Anzeigename ist selbst gewählt und kein verifizierter oder reservierter Kontoname.

Der Server speichert nur SHA-256-Hashes zufälliger 256-Bit-Schreibschlüssel. Schlüssel
werden ausschließlich auf dem Server über den Betreiberbefehl ausgegeben; es gibt
keine öffentliche Registrierung. Die App speichert ihren Schlüssel mit Windows-DPAPI,
an die exakt konfigurierte API-Adresse gebunden und getrennt vom Garmoth-Key sowie
von `settings.json`. Weiterleitungen und Cookies sind für Uploads deaktiviert.

Die API validiert Größen, Textlängen, Zahlen, Zeitstempel und JSON-null. Maximal 64 KiB
je Request, 128 Lootarten, 40 Zeichen Anzeigename; maximal 30 Tage Sessiondauer.
Ältere Sequenzen, beendete IDs und überholte Veröffentlichungen werden abgewiesen.
Sitzungsende wird auch vor einem verzögerten ersten PUT vermerkt. Request-Limits greifen
vor der Tokenprüfung; zusätzliche Limits gelten für Lese- und Besitzer-Schreibzugriffe.
Antworten haben `Cache-Control: no-store`. Die Website setzt alle Nutzerdaten als Text.

Abgelaufene Loot-Payloads werden beim nächsten Listenabruf gelöscht. Pro Publisher
bleiben ID, letzter Sequenz-/Freigabezeitpunkt und Token-Hash als kompakter Zustand
erhalten. Sperrvermerke für beendete IDs werden nach 31 Tagen beim Listenabruf entfernt.
Die API ist kein Sessionarchiv. Weder Bilder, OCR-Rohtext, lokale Pfade noch Garmoth-Keys
werden übermittelt. Die Homepage enthält keine Beispielsessions und keine Analytics.

## Prüfen

```powershell
dotnet test BdoGrindTracker.slnx -c Release
cd web
npm test
$env:API_BASE_URL = 'https://api.example.com/'
npm run build
```

Die Beispieladresse im Build-Befehl dient nur der Prüfung des statischen Builds.
Für die Veröffentlichung die tatsächlich erreichbare API verwenden. Für einen lokalen
Build mit HTTP zusätzlich `ALLOW_LOCAL_API=1` setzen; der Pages-Workflow setzt diese
Ausnahme nicht. `.github/workflows/checks.yml` prüft App und Website unabhängig vom Hosting.
