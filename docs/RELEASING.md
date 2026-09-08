# Grindcrest installieren und Updates veröffentlichen

GitHub übernimmt Builds und Downloads. Velopack erstellt den Windows-Installer und liefert Updates an installierte Tracker aus. Ein eigener Server ist nicht nötig.

Download-Seite: <https://github.com/volatile1990/bdo-grind-tracker/releases>

## Eine neue Version veröffentlichen

1. Alle Änderungen einschließlich `.github/workflows/release.yml`, `.config/dotnet-tools.json` und `scripts/` committen und nach GitHub pushen. Der Workflow muss auf dem Standardbranch vorhanden sein, damit GitHub den manuellen Start anzeigt.
2. Auf GitHub **Actions → Release Grindcrest → Run workflow** öffnen und den Branch wählen, dessen Stand veröffentlicht werden soll.
3. Eine neue Versionsnummer ohne `v` eingeben: beispielsweise `0.10.0-test.2` für einen Test oder `0.10.0` für eine stabile Veröffentlichung.
4. **Run workflow** starten. Nach erfolgreichen Tests und Paketprüfungen erscheint der fertige Release samt Installer auf der Download-Seite.

Die Versionsnummer aus dem Workflow wird an App-Build und Installer weitergegeben. Sie bestimmt außerdem den Tag und Updatekanal; sie muss nicht zusätzlich im Projekt geändert werden. Die Projektversion bleibt die Vorgabe für normale lokale Entwicklungsbuilds. Für jede öffentliche Ausgabe eine neue, höhere Nummer verwenden. Ein vorhandener Release wird auch bei erneutem Workflow-Start nicht überschrieben.

Versionshinweise können vorab in `docs/release-notes/<Version>.md` abgelegt werden.
Build und Veröffentlichung übernehmen diesen Text in das Paket und den GitHub-Release.
Ohne diese Datei erzeugt GitHub die Release-Beschreibung automatisch.

Alternativ löst ein neu gepushter `v`-Tag denselben Ablauf aus:

```powershell
git tag v0.10.0
git push origin v0.10.0
```

Ein manuell gestarteter Release erzeugt seinen Tag am ausgewählten Commit. Existiert der Tag bereits, muss er auf denselben Commit zeigen. Der Workflow läuft nicht für Pull Requests. Das Build-Job-Token darf das Repository nur lesen; ausschließlich der anschließende Publish-Job erhält `contents: write`. Repository- oder Organisationsregeln müssen GitHub Actions diese Berechtigung und die Veröffentlichung erlauben. Für dieses öffentliche Repository sind keine zusätzlichen Download-Tokens oder Server-Zugangsdaten nötig.

## Welche Datei bekommen Nutzer?

Nutzer laden die Datei **`Grindcrest-win-x64-stable-Setup.exe`** beziehungsweise beim Testkanal **`Grindcrest-win-x64-beta-Setup.exe`** aus dem Release herunter. Der genaue Dateiname wird von Velopack erzeugt. Die Einrichtung installiert die App für den Windows-Benutzer. Die .NET-Laufzeit ist enthalten; WebView2 und die benötigte Visual-C++-Laufzeit werden bei Bedarf durch den Installer nachinstalliert. Für diese Nachinstallation ist Internetzugriff nötig. Die Windows-OCR-Sprache wird weiterhin über die Windows-Spracheinstellungen bereitgestellt.

Auch nach späteren Updates bleiben Einstellungen, Session-Verlauf und der benutzergebunden geschützte Garmoth-Key unter `%LOCALAPPDATA%\BdoGrindTracker`. Der Installer verwendet bewusst die eigenständige Paket-ID `Grindcrest` und das Installationsverzeichnis `%LOCALAPPDATA%\Grindcrest`. Diese IDs und Speicherorte nach dem ersten Release nicht austauschen.

Wer bisher einen entpackten Entwicklungsbuild verwendet, installiert den neuen Setup einmal und startet anschließend **Grindcrest** über dessen Verknüpfung. Vorhandene Daten werden am bisherigen Ort weiterverwendet. Die integrierte Installation von Updates setzt eine Velopack-Installation voraus.

Neben dem Installer erzeugt Velopack einen portablen ZIP-Build, vollständige `.nupkg`-Updatepakete und `releases.win-x64-*.json`-Feeds. **Alle erzeugten Release-Dateien gemeinsam veröffentlichen.** Ein Installer allein reicht für die automatische Updatefunktion nicht. Der Workflow übernimmt den vollständigen Upload. Das portable ZIP ist für manuelle Nutzung und Diagnose gedacht; für reguläre Installation und Updates den Setup verwenden.

## Stable und Beta

| Versionsnummer | Updatekanal | GitHub-Release |
| --- | --- | --- |
| `0.10.0` | `win-x64-stable` | Stable, als neuester Release markiert |
| `0.10.1-beta.1` oder `0.10.0-test.2` | `win-x64-beta` | Prerelease, ersetzt den neuesten Stable-Release nicht |

Der Tracker prüft beim Start im Hintergrund. Unter den Einstellungen kann der Nutzer Updates manuell prüfen und Beta-Updates auswählen. Downloads und Installation werden in der App angeboten. Eine laufende Grind-Session verhindert den Update-Neustart. Während GitHub offline ist oder eine Anfrage begrenzt wird, bleibt der Tracker nutzbar; später erneut prüfen.

Zum Wechsel zurück auf Stable muss eine neuere stabile Version vorhanden sein; ein Downgrade wird nicht automatisch durchgeführt. Den Kanal nicht durch ein nachträgliches Umbenennen veröffentlichter Paketdateien ändern.

## Lokal einen Installer erstellen

Voraussetzungen: Windows x64, .NET-9-SDK und PowerShell 7.2 oder neuer. `vpk` wird durch das eingecheckte Tool-Manifest exakt in Version `1.2.0` wiederhergestellt; die App verwendet dieselbe Velopack-Version. Beide Versionen bei einem späteren Tool-Update gemeinsam aktualisieren.

```powershell
./scripts/Build-Release.ps1 -Version 0.10.0-test.2
```

Das Skript führt alle Lösungstests aus, publiziert selbstständig für `win-x64`, prüft App-Dateien und Lizenzen, erstellt den Installer und kontrolliert Updatefeed, Paketversion sowie SHA-256-Prüfsummen. Das Ergebnis steht unter `artifacts/releases/0.10.0-test.2`. Arbeitsdateien verbleiben zur Diagnose unter `artifacts/release-work/`. Ein nicht leeres Ausgabeverzeichnis wird abgelehnt; zum Wiederholen einen anderen leeren Pfad mit `-OutputDirectory` wählen. `-SkipTests` ist für lokale Wiederholungen nach einem bereits erfolgreichen Testlauf verfügbar; der GitHub-Workflow verwendet es nicht.

Weitere Optionen:

```powershell
./scripts/Build-Release.ps1 -Version 0.10.1-beta.1 -Channel beta `
  -OutputDirectory ./artifacts/my-beta-release `
  -ReleaseNotes ./docs/my-release-notes.md
```

`-Channel` ist optional und wird sonst aus der Version abgeleitet. Widersprüche zwischen Version und Kanal werden abgelehnt. `-UpdateRepositoryUrl` kann beim Build eine andere **öffentliche** GitHub-Downloadquelle einbetten. Im aktuellen Workflow wird das Repository des Workflows verwendet; niemals einen privaten API-Token in die ausgelieferte App einbetten.

Beim ersten Release sind keine älteren Pakete erforderlich. Der GitHub-Workflow erzeugt vollständige Updatepakete; damit funktionieren auch Updates über mehrere ausgelassene Versionen hinweg. Optional erstellt ein lokaler Build kleinere Delta-Pakete, wenn die vorige Ausgabe desselben Kanals vollständig vorliegt:

```powershell
./scripts/Build-Release.ps1 -Version 0.10.1-beta.2 `
  -PreviousReleaseDirectory ./artifacts/releases/0.10.1-beta.1
```

Die vorige Ausgabe muss ihren Kanalfeed und die darin referenzierten Pakete enthalten. Das Ergebnis enthält weiterhin ein vollständiges Paket als Rückfallmöglichkeit.

## Optionale digitale Signierung

Ohne zusätzliche Konfiguration entstehen unsignierte Builds. Windows kann dabei eine Warnung zum unbekannten Herausgeber anzeigen. Signierung läuft innerhalb von Velopack, damit auch Installer und Updater signiert werden. Ein Signierfehler bricht den Build ab.

Für eine vorhandene Signieridentität im Zertifikatspeicher `CurrentUser\My` lokal diese Umgebungsvariablen setzen:

```powershell
$env:GRINDCREST_SIGN_CERT_SHA1 = 'THUMBPRINT_DES_SIGNIERZERTIFIKATS'
$env:GRINDCREST_SIGN_TIMESTAMP_URL = 'http://timestamp.digicert.com' # optional
./scripts/Build-Release.ps1 -Version 0.10.0
```

Der private Schlüssel muss über Windows zugreifbar sein. Das Skript akzeptiert keine Passwörter als Kommandozeilenargumente. Alternativ setzt `GRINDCREST_AZURE_SIGN_METADATA` den Pfad zur Metadatendatei einer bereits authentifizierten Azure-Artifact-Signing-Identität.

Für optionale Azure-Signierung in GitHub Actions:

1. Azure Artifact Signing und eine Signieridentität mit passender Berechtigung einrichten.
2. Die Repository-Variable `GRINDCREST_AZURE_SIGN_METADATA_JSON` mit dem offiziellen Metadatenformat setzen:

   ```json
   {
     "Endpoint": "https://REGION.codesigning.azure.net/",
     "CodeSigningAccountName": "ACCOUNT",
     "CertificateProfileName": "PROFILE"
   }
   ```

3. `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` und `AZURE_CLIENT_SECRET` als **GitHub Actions Secrets** hinterlegen. Der Workflow installiert dann zusätzlich die von der Signierkomponente benötigte .NET-8-Laufzeit und übergibt Zugangsdaten ausschließlich über die Umgebung.

Signierzugangsdaten gehören nicht in App-Konfiguration, Quelltext oder Release-Dateien. Der Workflow benötigt diese Secrets nur, wenn die Metadatenvariable gesetzt ist. Details: [Velopack-Signierung](https://docs.velopack.io/packaging/signing).

## Release prüfen und Fehler behandeln

Vor dem ersten öffentlichen Stable-Release auf einer Windows-Testinstallation den vollständigen Weg prüfen:

1. Testversion A per Setup installieren und eine Session sowie Einstellungen speichern.
2. Höhere Testversion B über den Workflow veröffentlichen.
3. In A das Update prüfen, herunterladen und nach Ende der Session installieren lassen.
4. In B Versionsanzeige, Verlauf, Einstellungen und Garmoth-Verbindung prüfen. Während einer laufenden Session muss der Neustart gesperrt bleiben.

Das Packaging-Skript prüft Dateivollständigkeit und Paketintegrität automatisch; dieser Installationsdurchlauf prüft zusätzlich Windows, Verknüpfungen und echte Nutzerdaten. Bei fehlendem WebView2 auf der Testinstallation auch dessen Nachinstallation prüfen.

Wenn ein Test oder Paketcheck fehlschlägt, wird kein Release veröffentlicht. Testberichte stehen als Workflow-Artefakt `test-results` bereit. Nach erfolgreichem Build sind sämtliche Pakete außerdem als `grindcrest-release` gespeichert. GitHub-Releases werden zunächst als Draft angelegt und erst nach vollständigem Upload sichtbar. Bricht der Upload ab, bleibt der Draft zur Prüfung erhalten. Einen fehlgeschlagenen **unveröffentlichten** Draft bei Bedarf auf GitHub entfernen und den Workflow für denselben Commit erneut starten; öffentliche Releases nicht ersetzen. Eine fehlerhafte bereits veröffentlichte App mit einer neuen höheren Version korrigieren.

Grundlagen: [GitHub-Actions-Verteilung](https://docs.velopack.io/distributing/github-actions), [GitHub-Release-Hosting](https://docs.velopack.io/distributing/self-hosting#github), [Installer-Abhängigkeiten](https://docs.velopack.io/packaging/bootstrapping).
