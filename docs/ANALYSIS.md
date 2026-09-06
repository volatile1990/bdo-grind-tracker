# Erkennungsarchitektur 0.9.0

## Rückkehr zum Erkennungsstand 0.5.1

Die eigenständige Matching-/Lebensdauerlogik aus 0.6.0 und 0.6.1 hat im berichteten
Live-Einsatz zu viele Trashloot-Drops verworfen. Deshalb werden Namensauflösung,
Text-/Mengenverarbeitung und zeitliche Zählung wieder nach dem Companion-basierten
Stand 0.5.1 ausgeführt. Grundlage sind die erhaltenen, hashgeprüften 0.5.1-Assemblies,
deren eigener C#-Code mit ILSpy wiederhergestellt wurde. Die historische statische
Analyse des Originals ist in [COMPANION_0_7_4_PARITY.md](COMPANION_0_7_4_PARITY.md)
dokumentiert.

Die eigenen Anforderungen an Mehrfachbestätigung, separate Mengenqualität,
BON/JIN/WON-Rohtextpräfixe, zusätzliche Runner-up-Abstände, Bildfingerabdruck-
Identitäten und zeitliche Normal-/Rare-Ereignisfusion sind nicht mehr Teil des
produktiven Erkennungspfads. Es gibt keinen auswählbaren alten Lebensdauer-Tracker.
Diagnose-Scores sind beschreibende Werte, keine zusätzlichen Annahmegates.

Bewusst erhalten bleiben der nachgelagerte, automatisch erkannte Spotfilter und
die von der Erkennungslogik unabhängigen UI-/Log-Performancekorrekturen. Daher
wird keine vollständige Binär- oder Produktparität mit BDO Companion behauptet.

## Bildaufbereitung und Namensauflösung

`CompanionCalibrationReader` liest ausschließlich GameOption.txt und die sichtbaren
UI-Anker 159/161 im aktiven gamevariable.xml. Daraus folgen Auflösung, Skalierung,
Schriftprofil und feste Lootausschnitte. Ein optionaler Rare-Anker aktiviert dessen
mittleres 20-%-Band. Es gibt keine Vollbildsuche.

DXGI Desktop Duplication erfasst den ausgewählten Monitor. Der tatsächliche
HDR-Zustand wird je Frame an die getrennten Normal-/Rare-Zeilenworker weitergereicht.
Normal werden bis zu sechs Zeilenbänder vorbereitet. SDR-/HDR-Masken, Leergates,
Skalierungen, Namensausschnitte und die zehn originalen Ziffernvorlagen des gewählten
Schriftprofils bleiben erhalten. Windows.Media.Ocr liest die vorbereiteten Namensbilder.

Die normale Vorbereitung wird aufsteigend nach Y ausgewertet. Der ursprüngliche
Sentinel-Präfixfilter, Wortgeometrie-/Breitengates und `CompanionTextPipeline` sind
wieder aktiv. Erst vor dem zeitlichen Abgleich werden akzeptierte normale Einträge
in die absteigende Reihenfolge umgekehrt. Der Rare-Pfad verwendet keine
Ziffernvorlagen und ohne gelesenes Mengensuffix den bisherigen Mengenstandard 1.

`CompanionItemMatcher` verwendet erneut das vollständige Vergleichsvokabular und
den ursprünglichen bytebasierten Matcher mit Grenzwert 0,34, dem Mengen-1-Filter
sowie den ursprünglichen zusätzlichen Rare-Regeln. Der normale Pfad bekommt
keine nachträgliche strengere Runner-up-Prüfung. Die gleichen Textreparaturen wie
im Stand 0.5.1 bleiben erlaubt; native Mengen-/Lückenreparatur ist wieder aktiv.

## Automatischer Spotfilter

`LootSpotCatalog` enthält die Edania-Part-2-Hauptloot-Tabellen,
den gemeinsamen HighestTier-Pool, eine separate `SharedGlobalItems`-Liste und
eine explizite Event-Liste. Seit 0.6.3 ergänzen Ancient Spirit Dust, Black Stone,
Caphras Stone, Laila's Petal und Pure Black Stone jeden der drei Spotpools.
Die Quellen und deren Grenzen sind in [LOOT_POOLS.md](LOOT_POOLS.md) dokumentiert.
Eine fehlende Erwähnung in einer Main-Loot-Tabelle belegt kein Dropverbot. Die Zuordnung
verändert keine OCR- oder Matchingentscheidung: Zunächst bestimmt der globale
Companion-Matcher den Itemnamen, anschließend wird dessen Spot-Zulässigkeit geprüft.
Ein unerlaubter Treffer wird nicht in einen ähnlichen erlaubten Kandidaten umbenannt.

Die erste passende Trashloot-Zeile in der Reihenfolge neueste normale Zeile zuerst
legt den Spot fest:

| Trashloot | Spot |
|---|---|
| Branch of Abundance | Aphrodon Temple |
| Black Crystal Fragment | Hermesia Inner Castle |
| Elion Follower's Helmet | Magaia Temple |

Vor dieser Erkennung bleibt der Spot unbekannt; es wirkt noch kein zusätzlicher
Spotfilter. Der erkannte Spot bleibt bis `Reset` gesperrt. Pause/Fortsetzen erhält
ihn; für einen Spotwechsel ist eine neue Sitzung nötig. Eine frühere manuelle
Spot-Einstellung wird nicht eingelesen. Event-Loot ist nach Aktivierung des
Spotfilters standardmäßig ausgeschlossen und nur per explizitem Opt-in erlaubt.

## Companion-Zählung und Summen

`CompanionFrameReconciler` und `CompanionRareFrameReconciler` verarbeiten wieder
getrennte 10-Frame-Folgen mit den ursprünglichen Frame-Tags, Präfix-/Suffix-
Überlappungen und Mengen-/Lückenreparaturen. Der Rare-Pfad behält seinen
12-Frame-Aktualitäts-/Supportzustand und Alias-/Konfliktkorrekturen. Beide Modi
arbeiten gegen dasselbe `CompanionLootLedger`.

Die 0.5.1-Korrekturen bleiben erhalten: Umkehr der akzeptierten normalen Einträge,
vollständiger Frame-Tag-Präfixvergleich, Fortschreiben von `lastY` nach regulären
Treffern und serieller 450-ms-Aufnahmetakt. Ein Sitzungsabschluss verarbeitet noch
ausstehende Blöcke nach diesen Regeln; er ist keine neue defensive Bestätigungsphase.

Ausgegebene Buchungen besitzen UI-Ausgabe-IDs, jedoch keine eigene Lebensdauer-
Identität. Die Oberfläche wendet jede Ausgabe höchstens einmal an. Signed
Rare-Korrekturen ändern die Summen; negative Deltas erhöhen den Ereigniszähler
nicht. Items mit Summe null werden entfernt. Auch reine Korrekturframes lösen
eine UI-Summenaktualisierung aus.

## Takt und Oberfläche

Die Aufnahme bleibt seriell: Capture, OCR/Analyse, optionale lokale Aufzeichnung,
Rest der 450-ms-Deadline. Keine Bitmap-Queue. Teure Analyse oder Aufzeichnung können
den Abstand verlängern. Die Capture-Schleife wartet nicht auf die Oberfläche.
`FrameUiMailbox` übernimmt jede Ausgabe in die threadgeschützte Aggregation und
hält nur den neuesten visuellen Zustand. Das Dashboard erzeugt keine Thumbnails.
Ein 500-ms-UI-Timer aktualisiert zusammengefasste Summen und die Anzeige.
Eine blockierte UI erzeugt keinen Event-/Frame-Rückstau und verzögert Capture nicht.

Seit 0.7.0 enthält das Dashboard keine Live-Debugansicht oder Entscheidungstextbox.
Entscheidungen werden für die Anzeige weder durchlaufen noch formatiert; die
optionale lokale Datei-Aufzeichnung bleibt davon unabhängig. Die virtuelle
Loot-Kartenliste zeichnet nur sichtbare Zeilen und verwendet keine Controls pro Drop.

`GrindSessionClock` misst aktive Sitzungsdauer mit monotonen Zeitstempeln. Start
setzt fort, Pause schließt Wartezeit aus, Reset löscht und stoppt die Uhr. Der
UI-Timer aktualisiert die Zeit auch ohne neue Frames. Bildschirmaufnahme, OCR,
Matcher, Spotfilter und Zählledger wurden für diesen UI-Umbau nicht verändert.

Seit 0.8.0 setzt `FrameUiMailbox.Publish` nur bei neu angewendeten positiven
Buchungen ein Aktivitätssignal. `GrindInactivityTimer` speichert dessen monotonen
Zeitpunkt im Capture-Thread; UI-Verzögerungen, wiederholte Zeilen und negative
Korrekturen verlängern das Wartefenster nicht. Nach dem einstellbaren Zeitraum
(Standard 3 Minuten) verwendet die UI denselben geordneten Pause-/Flushpfad wie
eine manuelle Pause. Fortsetzen beginnt ein neues Wartefenster.

Die [passive Klassenerkennung](CLASS_DETECTION.md) liest gespeicherte Skill-Slots
unabhängig von der OCR. Der [optionale Garmoth-Upload](GARMOTH_INTEGRATION.md)
arbeitet nur mit einer pausierten Summenkopie und ausdrücklicher Bestätigung;
keine seiner Validierungen beeinflusst die lokale Erkennung oder Zählung.

Seit 0.9.0 gilt der Klick auf **Garmoth-Upload** als Sendeauftrag und pausiert bei
Bedarf selbst. Der einmal gespeicherte Key liegt separat Windows-DPAPI-verschlüsselt.
Ein regionsgetrennter, asynchroner Preisprovider liefert ausschließlich öffentliche
Marktpreise; `SilverValuation` projiziert die vorhandenen Summen in Vor-/Nachsteuerwerte
mit der bestätigten Companion-Steuer-/Stückrundung. Es gibt keinen Datenpfad von
Preisen oder Upload-Itemlisten zurück in Aufnahme oder Zählledger. Der Upload
verwendet den Dashboard-Netto-Wert und lässt nicht unterstützte Garmoth-Items aus.
Ein unabhängiger Upload-Lock schützt auch bei gleichzeitigem Capture-Fehler vor
Reset/Fortsetzen/erneutem Versand. Preiswechsel während eines Abrufs können niemals
fremde Regionenpreise in die Sitzung übernehmen.

## Diagnose und Offline-Replay

Nur bei ausdrücklichem Opt-in schreibt `DiagnosticRecordingSession` Loot-PNGs und
JSONL. Grenzen: 2.000 Frames / 250 MiB; Fehler deaktivieren die Aufzeichnung, nicht
die Erkennung. Eine Aufnahme beginnt mit einer neuen Session und umfasst auch
Pause/Fortsetzen. Der Header kann noch keinen Spot enthalten, weil die automatische
Erkennung erst während der Aufnahme erfolgt.

Formatversion 2 trägt die Enginekennung `companion-0.7.4-restore-v1`. Alte Aufnahmen
des Lebensdauer-Trackers werden wegen der anderen Logik nicht akzeptiert.
Die Rare-Katalogmetadaten werden eingebettet, damit eine spätere Installation die
Klassifikation nicht unbemerkt ändert. Iconpfade sind dabei nur Klassifikationstext.

`LootDiagnosticReplay` liest nur JSONL, prüft Format, Sequenz, Größen und Zeitstempel
und wiederholt den wiederhergestellten Companion-Abgleich anhand akzeptierter
OCR-/Matching-Beobachtungen. Es öffnet keine Bildpfade und initialisiert weder
Bildschirmaufnahme noch BDO-Konfigurationsleser oder Windows OCR. Buchungen und
Korrekturen je Frame sowie Aufnahmesummen werden verglichen. Eine volle OCR-
Neuauswertung der PNGs und eine erneute Prüfung des Spotfilters sind nicht der
Umfang dieses Replays.

## Verifikation und Grenzen

Regressionstests und Vergleiche mit den erhaltenen Assemblies prüfen das Verhalten
des wiederhergestellten Algorithmus. UI-Tests prüfen insbesondere signed Korrekturen,
automatische Spotanzeige, ignorierte manuelle Alt-Einstellungen und begrenzte
Anzeigepuffer. Sie liefern keine reale Grind-Trefferquote.

Der bekannte kleinere Zählfehler des 0.5.1-Stands kann wieder auftreten. Überdeckte,
kurz sichtbare oder falsch gelesene Drops sind weiterhin nicht zuverlässig auflösbar.
Eine vollständige Beseitigung der Über- und Unterzählungen wird nicht behauptet;
manuell geprüfte Inventarmengen bleiben der Maßstab für den nächsten Livevergleich.

Die 0.6.3-Pooltests prüfen gemeinsame Drops nach einem bereits erfolgten Spotlock
in beiden OCR-Kanälen bis zur ausgegebenen Buchung, ohne Event-Opt-in. Außerdem
muss jeder Eintrag des gebündelten Vokabulars zu einem normalen Pool, der
Eventliste oder einem ausdrücklich ausgeschlossenen Vergleichskandidaten gehören.
Das verhindert erneut vergessene Poolzuordnungen, beweist aber nicht, dass alle
noch unbekannten Spielitems im Vokabular stehen.
