# Grindcrest im Microsoft Store

Die Store-Ausgabe ist ein MSIX für Windows x64. Microsoft übernimmt nach erfolgreicher Zertifizierung die Signierung, Downloads und Updates. Der bestehende GitHub-Installer bleibt ein eigener Vertriebsweg.

## Zugeordnetes Produkt

Die Identität stammt aus dem vom Eigentümer bereitgestellten Partner-Center-Screenshot:

| Feld | Wert |
| --- | --- |
| Name | `Grindcrest.Grindcrest` |
| Publisher | `CN=BA212178-044A-478D-B577-2DC7A6D66CB9` |
| PublisherDisplayName | `Grindcrest` |
| Store ID | `9NQPWC1CMWS0` |
| Produktlink | <https://apps.microsoft.com/detail/9NQPWC1CMWS0> |

Der Produktlink ist erst nach der Veröffentlichung öffentlich verfügbar. Für die Paketierung werden keine Konto-Passwörter, MSA-App-Secrets oder Signierzertifikate benötigt.

## Paket erstellen

Windows, PowerShell 7.2+, .NET SDK 9 und das Windows SDK ab 10.0.19041.0 mit `MakeAppx.exe` und `MakePri.exe` sind erforderlich.

```powershell
./scripts/Build-StoreRelease.ps1 -Version 1.2.2
```

Das Skript führt alle Tests aus, veröffentlicht .NET samt Desktop-/Blazor-Laufzeit, erzeugt Logos aus der bestehenden Marke, erstellt den Shell-Ressourcenindex mit MakePri und das MSIX mit der Manifestprüfung des Windows SDK. Anschließend prüft es Identität, Inhalt, Icontransparenz und Ressourcen-Zuordnungen. Das Ergebnis liegt unter `artifacts/store/1.2.2/Grindcrest-1.2.2.0-x64.msix`, zusammen mit SHA-256-Prüfsumme sowie MakePri- und MakeAppx-Protokollen. Das Ausgabeverzeichnis muss leer sein; zum Wiederholen einen neuen `-OutputDirectory` angeben. `-SkipTests` ist nur für lokale Paketierungsdiagnosen gedacht.

Für Taskleiste, Start und Alt+Tab enthält das Paket transparente `Square44x44Logo.targetsize-*`-Icons in 15 Größen, jeweils als Standard-, `altform-unplated`- und `altform-lightunplated`-Variante. MakePri ordnet diese im mitgelieferten `resources.pri` dem Manifestlogo zu. `BackgroundColor="transparent"` allein verhindert die von Windows ergänzte farbige Hintergrundfläche nicht. Der PNG-Master und das EXE-Icon bleiben unverändert.

Alternativ nach dem Push auf GitHub: **Actions → Build Grindcrest for Microsoft Store → Run workflow**. Das Artefakt `grindcrest-store-package` enthält das fertige MSIX. Der Workflow lädt nichts zu Microsoft hoch und veröffentlicht keinen GitHub-Release.

Neue Store-Versionen haben vier Komponenten: `1.0.1` wird zu `1.0.1.0`; die letzte Stelle bleibt für Microsoft 0. Für Änderungen nach einer Einreichung eine höhere Version verwenden. GitHub-SemVer-Betabezeichnungen gehören nicht in die MSIX-Version.

## Einreichen

1. Partner Center → **Apps und Spiele → Grindcrest → Neue Übermittlung** öffnen.
2. Unter **Pakete** das `.msix` hochladen. Nicht den GitHub-EXE-Installer verwenden. Das lokal unsignierte Paket ist zur Store-Einreichung bestimmt, nicht zum direkten Installieren per Doppelklick.
3. Unter **Preise und Verfügbarkeit** kostenlos auswählen und gewünschte Länder/Veröffentlichungsoptionen einstellen.
4. Store-Eintrag mit Beschreibung, echten Screenshots, Support- und Datenschutzhinweisen sowie Altersfreigaben vervollständigen. Die App-Oberfläche ist Deutsch; das Paket deklariert `de-DE`.
5. Unter **Übermittlungsoptionen** die Begründung für `runFullTrust` und Prüferhinweise aus dem folgenden Abschnitt eintragen.
6. Paket auf einem Testsystem als tatsächlich installierte MSIX-App prüfen. Das Windows App Certification Kit kann zusätzliche lokale Prüfungen durchführen, ist laut Microsoft inzwischen veraltet und optional. Ein erfolgreicher lokaler Build ersetzt Microsofts Zertifizierung nicht.
7. Die vollständige Übermittlung zur Zertifizierung einreichen.

## Vorlage für Prüferhinweise

> Grindcrest is an independent, free Windows desktop companion utility for Black Desert. It uses a WinForms/Blazor Hybrid desktop interface, Windows OCR and native OpenCV image processing. The runFullTrust capability is required to run this existing desktop application, capture the Black Desert game window for passive on-screen loot recognition, and save the user's tracker settings and session history. The app does not automate game input, inject code, modify game files, or read game process memory. Capture is started explicitly by the user. Updates to the Store-installed application are managed by Microsoft Store; its GitHub/Velopack updater is disabled.
>
> No account is required for the local tracker. Black Desert is required to exercise live loot tracking. Optional Garmoth integration is separate and requires the user's own API key; it is not required to launch the app or use local tracking. Microsoft Edge WebView2 Evergreen Runtime must be present. If it is absent, the app displays an installation message with Microsoft's download URL. Windows N editions may require the Media Feature Pack for native OpenCV dependencies.

Die Angaben sollten vor dem Absenden zusammen mit Store-Beschreibung, Datenschutzangaben und Rechten an mitgelieferten Fremdassets geprüft werden. Microsoft entscheidet über die Freigabe.

Das Manifest deklariert außerdem `uap11:Capability` mit
`graphicsCaptureWithoutBorder`, damit auch die gepackte App die vom Nutzer
gewünschte Fensteraufnahme ohne gelben Rahmen anfordern kann. Die Zustimmung
erteilt Windows; die Deklaration schaltet keine globale Sicherheitsvorgabe aus.
Der Paketvalidator erlaubt ausschließlich diese Capability und `runFullTrust`.

## Installation, Daten und Updates

Die App erkennt ihre tatsächliche Windows-Paketidentität. Nur eine unverpackte Installation darf Velopack starten und das GitHub-Update-Backend verwenden. Ab Version 1.0.1 verwendet die Store-Ausgabe `Windows.Services.Store.StoreContext` mit dem HWND des App-Fensters. Sie prüft beim Start, nach jeweils sechs Stunden weiterer Nutzung und auf Knopfdruck auf verfügbare Updates. Beta-Umschalter und GitHub-Installationsaktionen werden dort nicht angeboten.

Ab Store-Version **1.2.0** erscheint bei einer verfügbaren Aktualisierung automatisch ein Popup **Update verfügbar**. **Jetzt aktualisieren** startet Download und Installation über `RequestDownloadAndInstallStorePackageUpdatesAsync` in einem Schritt. **Später** schließt nur den Hinweis. Er öffnet sich bei Fortschrittsmeldungen und Seitenwechseln nicht erneut; über den Update-Banner bleibt er erreichbar. Unter **Einstellungen → App-Updates** steht derselbe direkte Update-Button zur Verfügung. Es wird weder ein Browser noch die Store-App geöffnet. Der zuvor lokal vorbereitete Build 1.1.1 wurde durch dieses Release abgelöst.

Die Installation setzt eine pausierte Session ohne laufende Vorgänge voraus; der Button zeigt andernfalls **Session zuerst pausieren**. Zuerst werden Verlauf, Einstellungen und Fensterposition gespeichert; während der Store-Operation sind Session-Aktionen gesperrt. Microsoft übernimmt Paketprüfung und Installation und kann einen eigenen Bestätigungsdialog anzeigen. Bei Abbruch, fehlender Verbindung oder Speicherfehlern kann der Nutzer weiterarbeiten und erneut versuchen. Ein fehlgeschlagener direkter Versuch wird nicht als bereits heruntergeladenes Paket angezeigt. Windows kann Grindcrest schließen und nach dem Update neu starten; falls kein Neustart erfolgt, die App erneut öffnen.

Ab Store-Version **1.2.2** wird zusätzlich die ausdrücklich aktuelle Session gespeichert und nach dem Öffnen pausiert wiederhergestellt. Loot, aktive Zeit, Klasse und erfasste XP-/Agris-Werte bleiben erhalten; erst **Fortsetzen** startet die Aufnahme. Die Diagnoseaufzeichnung bleibt ausgeschaltet, und der Schutz des Garmoth-Uploadjournals bleibt bestehen. Ältere Store-Versionen haben den dafür nötigen Sitzungsmarker nicht geschrieben: Ihr Verlauf bleibt erhalten, wird beim ersten Upgrade aber nicht als aktuelle Session geraten. Die [Änderungen in 1.2.2](release-notes/1.2.2.md) beschreiben den Umfang und diese Grenze.

Die Store-Schnittstelle liefert keine verlässliche Zielversionsnummer für diese Abfrage; der Hinweis nennt deshalb keine erfundene Versionsnummer. Verfügbar sind nur bereits freigegebene Updates, die der Store diesem Nutzer anbietet. Microsoft kann die Verfügbarkeit nach Veröffentlichung verzögert melden und begrenzt erneute Prüfungen (aktuell höchstens einmal pro 30 Minuten und zehnmal pro 24 Stunden). Neue Pakete müssen weiterhin über Partner Center eingereicht und zertifiziert werden. Ein lokaler Build veröffentlicht nichts.

Bereits installierte **1.0.0**-Ausgaben enthalten ausschließlich den Store-Verweis. Sie müssen zuerst auf normalem Store-Weg aktualisiert werden, automatisch sofern Store-Updates aktiviert sind. Die veröffentlichte **1.1.0** enthält bereits Download-/Installationsaktionen in den Einstellungen; das neue automatische Popup samt Ein-Klick-Ablauf benötigt die hier vorbereitete nächste Version. Es lässt sich nicht nachträglich in die installierte 1.0.0 einblenden.

### Prüfung eines echten Store-Upgrades

Die automatisierten Tests prüfen Kanalwahl, direkte und bereits vorbereitete Installation, parallele Klicks, Abbruch, Netzwerk-/Speicherfehler sowie den Schutz der Session. UI-Tests prüfen Popup, Aufschieben, Wiederöffnen, Fortschritt und die gesperrte Aktion während des Trackings. Sie verwenden ein simuliertes Store-Backend. Für den vollständigen Integrationstest müssen zwei passende, freigegebene Paketversionen über das echte Store-Produkt verfügbar sein: eine Ausgabe mit diesem Popup installieren, anschließend eine höhere Version veröffentlichen und **Jetzt aktualisieren** ausführen. Dabei gespeicherten Verlauf, Abbruch und tatsächliche neue Paketversion nach dem Neustart prüfen. Ein lokal entpackter oder lediglich selbst signierter Build ersetzt diesen Test nicht.

Die Store-Ausgabe nutzt den eigenen `LocalState`-Datenordner. Beim ersten regulären Start werden vorhandene Tracker-Daten aus `%LOCALAPPDATA%\BdoGrindTracker` übernommen; die Originale bleiben erhalten. Danach entwickeln sich GitHub- und Store-Daten getrennt weiter. Vor einem Wechsel oder der Deinstallation sollten wichtige Sessions zusätzlich exportiert werden. Details der tatsächlich ausgeführten Übernahme und Prüfungen stehen in der Implementierung und den zugehörigen Tests.

MSIX enthält die .NET-Laufzeit. Die aktuellen OpenCV- und WebView2-Loader-Binärdateien brauchen kein zusätzliches VCLibs-Store-Paket; ihre nativen Importtabellen wurden geprüft. WebView2 Evergreen ist ein eigener Systembestandteil. Das Store-MSIX installiert keine EXE-Bootstrapper und lädt keinen Programmcode über den GitHub-Updater nach.

## Referenzen

- [Microsoft: MSIX-Paketanforderungen und Signierung](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Microsoft: Pakete mit MakeAppx erzeugen](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
- [Microsoft: transparente Shell-Icons und Varianten](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-construction)
- [Microsoft: Unplated-Assets und Ressourcenindex für MSIX](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion#optional-add-target-based-unplated-assets)
- [Microsoft: Verhalten verpackter Desktop-Apps](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
- [Microsoft: eingeschränkte App-Berechtigungen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations#restricted-capabilities)
- [Microsoft: lokale Paketprüfung und Status des WACK](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps#validate-your-app-package-locally)
- [Microsoft: Store-Updates aus der App heraus herunterladen und installieren](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/package-updates-from-store)
- [Microsoft: Verfügbarkeit und Abrufgrenzen der Updateprüfung](https://learn.microsoft.com/en-us/uwp/api/windows.services.store.storecontext.getappandoptionalstorepackageupdatesasync)
