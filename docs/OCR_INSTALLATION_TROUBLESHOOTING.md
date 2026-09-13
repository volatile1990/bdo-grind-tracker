# Windows-OCR-Sprachpaket wird nicht installiert

`State: NotPresent` bei `Language.OCR~~~en-US~0.0.1.0` bedeutet, dass Windows das englische OCR-Paket derzeit nicht als installiert meldet. Der Status erklärt nicht, weshalb ein vorheriger Versuch ausblieb, scheiterte oder beim Neustart noch lief. `DownloadSize: 0` und `InstallSize: 0` sind kein Nachweis eines erfolgreichen Downloads.

Die Diagnose muss auf dem betroffenen PC erfolgen. Der Zustand eines anderen Entwicklungs- oder Testrechners erklärt den Screenshot nicht.

## `Installed`, aber Grindcrest zeigt weiter „Wird installiert“

Windows kann das Paket bereits installiert haben, während Grindcrest noch auf das
Ende seines gestarteten Installationsprozesses wartet. In Version 1.2.2 ist während
dieses Wartens auch **Erneut prüfen** gesperrt. Starte zunächst nur Grindcrest neu.
Falls sich die App nicht schließen lässt, beende im Task-Manager ausschließlich
Grindcrest; DISM, TiWorker und TrustedInstaller weiterlaufen lassen. Beim nächsten
Start prüft Grindcrest die Texterkennung erneut. Das Paket nicht erneut installieren,
solange es als installiert gemeldet wird.

Im aktualisierten Code bleibt **Erneut prüfen** während des Wartens verfügbar.
Außerdem prüft die App regelmäßig die tatsächliche OCR-Initialisierung. Bei Erfolg
gibt sie das Tracking frei, auch wenn der Installationsprozess noch aussteht. Ein
Windows-Status `Installed` allein oder die Anzeige 100 % ersetzen diese Prüfung nicht.

Grindcrest zeigt während der Installation die von Windows/DISM gemeldeten Prozentwerte zwischen 0 und 100 an. Solange Windows keinen Wert liefert, bleibt die Anzeige unbestimmt. Die Prozentzahl kann länger unverändert bleiben; 100 Prozent allein bedeutet noch keinen OCR-Erfolg. Entscheidend ist die erfolgreiche OCR-Prüfung, die auch vor dem Ende des Windows-Prozesses möglich ist.

## 1. Vor einem weiteren Installationsversuch den Bericht sichern

Das Skript [`Get-OcrInstallationDiagnostics.ps1`](../scripts/Get-OcrInstallationDiagnostics.ps1) liest Windows-Version und letzten Start, die Basic- und OCR-Capability der ausgewählten Sprache, laufende Servicing-Prozesse sowie begrenzte Auszüge aus vorhandenen DISM- und CBS-Protokollen. Es installiert nichts, verändert keine Dienste oder Richtlinien, beendet keine Prozesse, startet den PC nicht neu und sendet nichts. Es öffnet auch keine Administratorabfrage.

Die Skriptdatei auf den betroffenen PC kopieren. Dort **Windows PowerShell als Administrator** öffnen und im Ordner der Datei ausführen:

```powershell
.\Get-OcrInstallationDiagnostics.ps1 -LanguageTag en-US
```

Für eine deutsche Spielsprache `de-DE` verwenden. Ohne Administratorrechte wird ebenfalls ein Bericht erstellt; fehlende Zugriffsrechte werden darin als Fehler vermerkt. Es wird kein Windows-Update-Reset oder eine Änderung der Ausführungsrichtlinie benötigt.

Der Bericht landet standardmäßig unter `Downloads` im aktuellen Benutzerprofil, sofern der Ordner existiert, sonst im aktuellen Ordner. Ein vorhandener Bericht wird nicht überschrieben. Ein anderer vorhandener Zielordner lässt sich ausdrücklich auswählen:

```powershell
.\Get-OcrInstallationDiagnostics.ps1 -LanguageTag en-US -OutputPath 'C:\Users\Public\OCR-Diagnose.txt'
```

Grindcrest nennt während der Installation den Pfad ihres eigenen Installationsprotokolls. Dieses konkrete Protokoll lässt sich zusätzlich in den Bericht aufnehmen:

```powershell
.\Get-OcrInstallationDiagnostics.ps1 -LanguageTag en-US -InstallationLogPath 'C:\Pfad\aus\Grindcrest\ocr-20260913-120000-beispiel.log'
```

Den Beispielpfad durch den in Grindcrest angezeigten vollständigen Pfad ersetzen. Das Skript liest ausschließlich die ausdrücklich ausgewählte Datei und, falls vorhanden, die danebenliegende Datei mit angehängtem `.progress.txt`. Es durchsucht keine App- oder Benutzerverzeichnisse. Auch ein manuell erstelltes DISM-Protokoll aus Schritt 3 kann so übergeben werden.

Den Bericht zusammen mit der ungefähren Uhrzeit des Installationsversuchs und der Grindcrest-Version aufbewahren. Vor dem Weitergeben die enthaltenen lokalen Pfade prüfen. Prozesskommandozeilen werden nicht erfasst; Logauszüge können Benutzernamen und Dateipfade enthalten.

Die Logauswertung berücksichtigt höchstens die letzten 4.000 Zeilen je DISM-Datei, 5.000 Zeilen von `CBS.log` und 400 Zeilen einer ausgewählten Fortschrittsdatei, davon nur wenige neueste Treffer mit Kontext. Alte oder rotierte Einträge können fehlen. Allgemeine DISM-Fehler sind im Bericht ausdrücklich gekennzeichnet und gehören nicht zwangsläufig zur OCR-Installation. Ein laufender `TiWorker` oder `TrustedInstaller` kann andere Windows-Updates bearbeiten.

## 2. Basic und OCR getrennt prüfen

In derselben administrativen Windows PowerShell:

```powershell
Get-WindowsCapability -Online -Name 'Language.Basic~~~en-US~0.0.1.0' -ErrorAction Stop
Get-WindowsCapability -Online -Name 'Language.OCR~~~en-US~0.0.1.0' -ErrorAction Stop
```

Microsoft nennt **Basic derselben Sprache als Abhängigkeit von OCR**. Ein fehlendes Basic-Paket ist deshalb relevant; der Screenshot mit dem OCR-Status allein belegt diesen Fall jedoch nicht. Wenn Basic fehlt, zuerst das entsprechende Sprachfeature in den Windows-Spracheinstellungen ergänzen und seinen Status erneut prüfen. Die Windows-Anzeigesprache muss dabei nicht geändert werden. [Microsoft: Sprachfeatures und Abhängigkeiten](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/features-on-demand-language-fod?view=windows-11)

Wenn eine andere Installation noch läuft, deren Ergebnis abwarten und keinen zweiten OCR-Installationsversuch parallel starten. Bei unklarem oder lange unverändertem Zustand zunächst den Bericht auswerten; vorhandene Servicing-Prozesse nicht abschießen.

## 3. Einen manuellen Versuch mit sichtbarer Rückmeldung protokollieren

Dieser Schritt **installiert** das ausgewählte Windows-OCR-Paket. Den Diagnosebericht vorher sichern. Erst fortfahren, wenn kein vorheriger Installationsversuch mehr läuft und Basic verfügbar ist.

Microsoft beschreibt für die Installation von OCR-Sprachen `Add-WindowsCapability` in einer administrativen PowerShell. Der Download verwendet die für das Gerät verfügbaren Paketquellen beziehungsweise Windows Update. Eine bestätigte Administratorabfrage bedeutet, dass die Installation gestartet werden darf; sie belegt keinen abgeschlossenen Download. [Microsoft: Text Extractor und OCR-Sprachen](https://learn.microsoft.com/en-us/windows/powertoys/text-extractor), [Microsoft: Add-WindowsCapability](https://learn.microsoft.com/en-us/powershell/module/dism/add-windowscapability?view=windowsserver2025-ps)

```powershell
$ocrLog = Join-Path $env:TEMP ('Grindcrest-OCR-en-US-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$ocrLog
try {
    Add-WindowsCapability -Online -Name 'Language.OCR~~~en-US~0.0.1.0' -LogPath $ocrLog -ErrorAction Stop | Format-List *
}
catch {
    $_ | Format-List * -Force
    Write-Host ('Installationsprotokoll: ' + $ocrLog)
}
Get-WindowsCapability -Online -Name 'Language.OCR~~~en-US~0.0.1.0' -ErrorAction Stop
```

`Add-WindowsCapability` hat keinen Parameter `-NoRestart`. Für einen Installationsaufruf mit ausdrücklichem Neustartverbot stattdessen **diesen DISM-Befehl als Alternative verwenden; nicht beide Versuche gleichzeitig starten**:

```powershell
$ocrLog = Join-Path $env:TEMP ('Grindcrest-OCR-en-US-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
& "$env:WINDIR\System32\dism.exe" /Online /Add-Capability '/CapabilityName:Language.OCR~~~en-US~0.0.1.0' /NoRestart "/LogPath:$ocrLog"
$ocrExitCode = $LASTEXITCODE
Write-Host ('DISM-Exitcode: {0}; Protokoll: {1}' -f $ocrExitCode, $ocrLog)
Get-WindowsCapability -Online -Name 'Language.OCR~~~en-US~0.0.1.0' -ErrorAction Stop
```

`/NoRestart` verhindert den automatischen Neustart des DISM-Aufrufs. `/Quiet` wird hier bewusst nicht verwendet, damit die Konsole Rückmeldungen anzeigen kann. Das explizite Protokoll bewahrt die Diagnose dieses Versuchs. [Microsoft: Globale DISM-Optionen](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-global-options-for-command-line-syntax?view=windows-11)

Für Deutsch in diesen Befehlen `en-US` durch `de-DE` ersetzen. Wenn Windows `RestartNeeded: True` beziehungsweise DISM den Exitcode `3010` meldet, den Installationsabschluss abwarten und anschließend regulär neu starten. Danach Capability-Status und Grindcrest erneut prüfen. Ein anderer Fehlercode, `NotPresent` oder weiterhin fehlende Texterkennung erfordert die Auswertung der konkreten Rückmeldung und des Protokolls. Auch ein scheinbar erfolgreicher Aufruf ersetzt die abschließende Statusprüfung nicht.

Bei erneutem Stillstand das Konsolenfenster und die angegebene Logdatei für die Diagnose erhalten. Nicht mehrere Installer starten oder auf Verdacht Windows-Update-Dienste, Registry-Richtlinien oder den Komponentenspeicher zurücksetzen.
