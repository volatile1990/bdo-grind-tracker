#requires -Version 5.1
<#
.SYNOPSIS
Creates a local, read-only Windows OCR installation diagnostic report.
.DESCRIPTION
Reads Windows version, boot time, selected language capabilities, servicing
processes and bounded existing DISM/CBS log excerpts. An explicitly supplied
InstallationLogPath also includes that log and its optional .progress.txt file.
Does not install features,
change services or policies, elevate itself, restart Windows or send data.
Existing output files are never overwritten. Use Windows PowerShell as an
administrator for capability queries and protected logs; missing access is
recorded in the report instead of triggering an elevation prompt.
#>
[CmdletBinding()]
param(
    [ValidateSet('en-US', 'de-DE')]
    [string]$LanguageTag = 'en-US',

    [string]$OutputPath,

    [string]$InstallationLogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$LanguageTag = if ($LanguageTag -ieq 'de-DE') { 'de-DE' } else { 'en-US' }
$report = [System.Collections.Generic.List[string]]::new()
$recordedAt = Get-Date

function Add-ReportLine {
    param([AllowEmptyString()][string]$Text = '')
    $report.Add($Text)
}

function Add-LogMatches {
    param(
        [string[]]$Lines,
        [string]$Pattern,
        [string]$Label,
        [int]$MaximumMatches = 6,
        [int]$Context = 2
    )
    Add-ReportLine $Label
    $matchingIndices = [System.Collections.Generic.List[int]]::new()
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -match $Pattern) { $matchingIndices.Add($index) }
    }
    if ($matchingIndices.Count -eq 0) {
        Add-ReportLine '  Keine passenden Eintraege im begrenzten Dateiende.'
        return
    }
    $selected = [System.Collections.Generic.SortedSet[int]]::new()
    $firstMatch = [Math]::Max(0, $matchingIndices.Count - $MaximumMatches)
    for ($matchIndex = $firstMatch; $matchIndex -lt $matchingIndices.Count; $matchIndex++) {
        $lineIndex = $matchingIndices[$matchIndex]
        $firstLine = [Math]::Max(0, $lineIndex - $Context)
        $lastLine = [Math]::Min($Lines.Count - 1, $lineIndex + $Context)
        for ($index = $firstLine; $index -le $lastLine; $index++) {
            [void]$selected.Add($index)
        }
    }
    $previousIndex = -2
    foreach ($index in $selected) {
        if ($index -gt $previousIndex + 1) { Add-ReportLine '  [...]' }
        $line = $Lines[$index]
        if ($line.Length -gt 1000) { $line = $line.Substring(0, 1000) + ' [gekuerzt]' }
        Add-ReportLine ('  ' + $line)
        $previousIndex = $index
    }
}

function Add-LogExcerpt {
    param(
        [string]$Path,
        [int]$TailLines,
        [string]$LanguagePattern,
        [string]$MatchLabel = 'Neueste Sprachpaket-Treffer mit Kontext:',
        [switch]$IncludeGeneralErrors
    )
    Add-ReportLine ''
    Add-ReportLine ('Datei: ' + $Path)
    try {
        $file = Get-Item -LiteralPath $Path -ErrorAction Stop
        Add-ReportLine ('Letzte Aenderung: {0:o}; Groesse: {1} Bytes' -f $file.LastWriteTime, $file.Length)
        $lines = @(Get-Content -LiteralPath $Path -Tail $TailLines -ErrorAction Stop)
        Add-ReportLine ('Untersucht: letzte {0} Zeilen (Maximum {1}); Original-Zeitstempel bleiben erhalten.' -f $lines.Count, $TailLines)
        if ($lines.Count -gt 0) {
            Add-LogMatches -Lines $lines -Pattern $LanguagePattern -Label $MatchLabel
            if ($IncludeGeneralErrors) {
                Add-LogMatches -Lines $lines -Pattern '(?i)\berror\b|\bfailed\b|\bfailure\b|\bfehler\b|0x[89a-f][0-9a-f]{7}' `
                    -Label 'Neueste allgemeine Fehler (nicht automatisch dieser OCR-Installation zuzuordnen):' `
                    -MaximumMatches 4 -Context 1
            }
        }
    }
    catch {
        Add-ReportLine ('Nicht lesbar oder nicht vorhanden: ' + $_.Exception.Message)
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $downloads = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads'
    $outputDirectory = if (Test-Path -LiteralPath $downloads -PathType Container) {
        $downloads
    } else {
        $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath('.')
    }
    $reportName = 'Grindcrest-OCR-Diagnose-{0}-{1}.txt' -f $LanguageTag, $recordedAt.ToString('yyyyMMdd-HHmmss-fff')
    $OutputPath = Join-Path $outputDirectory $reportName
}
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$parentDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
if (-not (Test-Path -LiteralPath $parentDirectory -PathType Container)) {
    throw 'Der Zielordner fuer OutputPath muss bereits vorhanden sein.'
}
if (Test-Path -LiteralPath $OutputPath) {
    throw ('Die Ausgabedatei existiert bereits und wird nicht ueberschrieben: ' + $OutputPath)
}

Add-ReportLine 'Grindcrest - Windows-OCR-Installationsdiagnose'
Add-ReportLine ('Erstellt (lokal): {0:o}' -f $recordedAt)
Add-ReportLine ('Erstellt (UTC): {0:o}' -f $recordedAt.ToUniversalTime())
Add-ReportLine ('Ausgewaehlte OCR-Sprache: ' + $LanguageTag)
Add-ReportLine 'Nur Diagnose: keine Installation, Dienst-/Richtlinienaenderung, Prozessbeendigung oder Uebertragung.'
Add-ReportLine 'Logauszuege sind begrenzt und koennen lokale Pfade oder Benutzernamen enthalten.'
Add-ReportLine 'Ein fehlender Treffer beweist nicht, dass kein Installationsversuch stattgefunden hat.'

$isAdministrator = $false
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    finally { $identity.Dispose() }
}
catch { Add-ReportLine ('Administratorstatus nicht ermittelbar: ' + $_.Exception.Message) }
Add-ReportLine ('PowerShell: {0}; 64-Bit-Prozess: {1}; Administrator: {2}' -f $PSVersionTable.PSVersion, [Environment]::Is64BitProcess, $isAdministrator)
if (-not $isAdministrator) {
    $accessNotice = 'Fuer vollstaendige Capability-Abfragen Windows PowerShell selbst als Administrator oeffnen. Dieses Skript fordert keine Rechte an; Zugriffsfehler werden protokolliert.'
    Write-Warning $accessNotice
    Add-ReportLine $accessNotice
}

Add-ReportLine ''
Add-ReportLine 'Windows und letzter Start'
try {
    $operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem -Property Caption, Version, BuildNumber, OSArchitecture, LastBootUpTime -ErrorAction Stop
    Add-ReportLine ('System: {0}; Version: {1}; Build: {2}; Architektur: {3}' -f $operatingSystem.Caption, $operatingSystem.Version, $operatingSystem.BuildNumber, $operatingSystem.OSArchitecture)
    Add-ReportLine ('Letzter Windows-Start: {0:o}' -f $operatingSystem.LastBootUpTime)
}
catch { Add-ReportLine ('Windows-/Startabfrage fehlgeschlagen: ' + $_.Exception.Message) }
try {
    $windowsVersion = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop
    foreach ($name in @('DisplayVersion', 'CurrentBuildNumber', 'UBR', 'EditionID')) {
        $property = $windowsVersion.PSObject.Properties[$name]
        if ($null -ne $property) { Add-ReportLine ('{0}: {1}' -f $name, $property.Value) }
    }
}
catch { Add-ReportLine ('Ergaenzende Versionsabfrage fehlgeschlagen: ' + $_.Exception.Message) }

Add-ReportLine ''
Add-ReportLine 'Laufende Servicing-Prozesse (Momentaufnahme, keine Kommandozeilen)'
Add-ReportLine 'TiWorker/TrustedInstaller koennen auch andere Windows-Updates bearbeiten; ihre Anwesenheit belegt keinen OCR-Fortschritt.'
try {
    $servicingProcesses = @(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -in @('dism', 'DismHost', 'TiWorker', 'TrustedInstaller') })
    if ($servicingProcesses.Count -eq 0) { Add-ReportLine 'Keine der abgefragten Servicing-Prozesse aktiv.' }
    foreach ($servicingProcess in $servicingProcesses) {
        $startTime = 'nicht lesbar'
        try { $startTime = $servicingProcess.StartTime.ToString('o') } catch { }
        Add-ReportLine ('{0}; PID {1}; Start: {2}' -f $servicingProcess.ProcessName, $servicingProcess.Id, $startTime)
    }
}
catch { Add-ReportLine ('Prozessabfrage fehlgeschlagen: ' + $_.Exception.Message) }

# Capture existing evidence before Get-WindowsCapability can append new DISM entries.
Add-ReportLine ''
Add-ReportLine 'Vorhandene Windows-Protokolle vor den Capability-Abfragen'
$escapedLanguage = [regex]::Escape($LanguageTag)
$languagePattern = '(?i)(Language\.(?:OCR|Basic)~~~{0}~|LanguageFeatures-(?:OCR|Basic)-{0}(?:-|~|\b))' -f $escapedLanguage
Add-LogExcerpt -Path (Join-Path $env:WINDIR 'Logs\DISM\dism.log') -TailLines 4000 -LanguagePattern $languagePattern -IncludeGeneralErrors
Add-LogExcerpt -Path (Join-Path $env:WINDIR 'Logs\DISM\dism.log.bak') -TailLines 4000 -LanguagePattern $languagePattern -IncludeGeneralErrors
Add-LogExcerpt -Path (Join-Path $env:WINDIR 'Logs\CBS\CBS.log') -TailLines 5000 -LanguagePattern $languagePattern

if (-not [string]::IsNullOrWhiteSpace($InstallationLogPath)) {
    Add-ReportLine ''
    Add-ReportLine 'Explizit ausgewaehltes Installationsprotokoll'
    try {
        $selectedLogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallationLogPath)
        Add-LogExcerpt -Path $selectedLogPath -TailLines 4000 -LanguagePattern $languagePattern -IncludeGeneralErrors
        $selectedProgressPath = $selectedLogPath + '.progress.txt'
        if (Test-Path -LiteralPath $selectedProgressPath -PathType Leaf) {
            Add-ReportLine 'Fortschrittsdatei: Werte stammen aus DISM; 100 Prozent allein belegt noch keine verfuegbare OCR-Engine.'
            Add-LogExcerpt -Path $selectedProgressPath -TailLines 400 `
                -LanguagePattern '(?i)\d+(?:[.,]\d+)?\s*%|\berror\b|\bfailed\b|\bfehler\b|0x[89a-f][0-9a-f]{7}' `
                -MatchLabel 'Neueste Fortschrittswerte oder Fehlermeldungen mit Kontext:'
        }
        else { Add-ReportLine ('Keine zugehoerige Fortschrittsdatei vorhanden: ' + $selectedProgressPath) }
    }
    catch { Add-ReportLine ('Zusaetzliches Installationsprotokoll nicht lesbar: ' + $_.Exception.Message) }
}

Add-ReportLine ''
Add-ReportLine 'Aktuelle Windows-Capabilities (Abfrage, keine Installation)'
foreach ($kind in @('Basic', 'OCR')) {
    $capabilityName = 'Language.{0}~~~{1}~0.0.1.0' -f $kind, $LanguageTag
    Add-ReportLine ('Abfragezeit: {0:o}; {1}' -f (Get-Date), $capabilityName)
    try {
        $capabilities = @(Get-WindowsCapability -Online -Name $capabilityName -ErrorAction Stop)
        if ($capabilities.Count -eq 0) { Add-ReportLine 'Kein Capability-Ergebnis erhalten.' }
        foreach ($capability in $capabilities) {
            Add-ReportLine ('Name: {0}; State: {1}' -f $capability.Name, $capability.State)
        }
    }
    catch {
        Add-ReportLine ('Abfrage fehlgeschlagen ({0}, HRESULT 0x{1:X8}): {2}' -f $_.FullyQualifiedErrorId, $_.Exception.HResult, $_.Exception.Message)
    }
}
Add-ReportLine ''
Add-ReportLine 'NotPresent bedeutet nicht installiert. Der Status allein nennt keine Fehlerursache.'
Add-ReportLine 'OCR hat eine Basic-Abhaengigkeit derselben Sprache; deren Status ist separat zu bewerten.'
Add-ReportLine 'DownloadSize/InstallSize 0 aus einer Statusabfrage sind kein Nachweis eines erfolgreichen Downloads.'
Add-ReportLine ('Bericht abgeschlossen: {0:o}' -f (Get-Date))

# CreateNew also prevents accidental overwrites if a file appeared during diagnosis.
$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
try {
    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($true))
    try { $writer.Write(($report -join [Environment]::NewLine) + [Environment]::NewLine) }
    finally { $writer.Dispose() }
}
finally { $stream.Dispose() }
Write-Output ('Diagnosebericht gespeichert: ' + $OutputPath)
