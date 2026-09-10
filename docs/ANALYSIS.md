# Erkennungsarchitektur 0.9.6-test.2

**Aktueller Entwicklungsstand:** Windows OCR bleibt primär; PP-OCRv6 Small
ersetzt die Tesseract-Zusatzprüfung. Der normale Laufzeitzähler ergänzt den
Companion-Abgleich um kalibrierte Zeilenindizes, innere OCR-Lücken und
Drop-IDs mit Mengenrevisionen. Die folgenden Versionsabschnitte beschreiben
teilweise frühere Stände; aktuelle Regeln und Aufnahmenergebnisse stehen in
[BACKGROUND_OCR_REVIEW.md](BACKGROUND_OCR_REVIEW.md).

## Passiver Loot-Scroll-Hinweis

`LootScrollMonitor` prüft das HUD während einer laufenden Session einmal pro
Minute in einem eigenen Hintergrundtask. `LootScrollGaugeDetector` vergleicht
die sichtbaren Beutel-, Kreuz- und Pfeilsymbole mit Bildvorlagen. Helligkeit und
Kontrast werden beim Vergleich angeglichen, damit die interne HDR-Tonemapping-
Darstellung dieselben Symbole wie ein SDR-Screenshot erkennt. Die Bildvorlagen
und ihre Quellen liegen unter `data/ocr/loot-scroll`.

`LootScrollFrameDetector` ergänzt die Restzeit aus der kleinen Beschriftung links
neben dem erkannten Symbol. `LootScrollTimerReader` verwendet dafür eine eigene,
verzögert angelegte Windows-OCR-Instanz mit zwei kleinen Aufbereitungsvarianten je
Minutenprüfung. Es gibt keine zusätzliche Vollbild-OCR und keinen Eingriff in
die Loot-OCR. Widersprechen sich zwei lesbare Ergebnisse, wird die Probe verworfen.

Die Prüfung übernimmt höchstens ein eigenes Frame und wartet nicht im
Loot-Auswertungspfad. Fehler lassen den Status unbekannt und verändern weder
Loot, Grindzeit noch die Tracking-Verfügbarkeit. Es werden nur Aufnahmen des
eingestellten Monitors berücksichtigt, wenn Black Desert dort im Vordergrund
ist. Die Sichtbarkeit wird vor und nach der Aufnahme geprüft und mit dem Frame
weitergereicht, damit eine verzögerte OCR keinen späteren Fensterwechsel als
Beleg verwendet. Die Erkennung benötigt eine sichtbare, passende Spielanzeige; ihre
Abwesenheit ist kein Nachweis einer inaktiven Scroll.

Nur die Änderung der Restzeit bestimmt den Status. Zwei aufeinanderfolgende,
plausible Abnahmen bestätigen Aktivität (bei Minutenprüfungen nach etwa zwei
Minuten). Der Verbrauch muss zur Zeit zwischen den Aufnahmen passen: etwa
ein- bis zweifache Geschwindigkeit mit Toleranz für Rundung und Teilintervalle.
Kleine OCR-Schwankungen und unplausible Zeitsprünge bestätigen keine Aktivität.
Eine wiederholt unveränderte Restzeit bestätigt Inaktivität; bei einer Anzeige
ohne Sekunden muss sie mindestens 62 Sekunden unverändert bleiben. Aufladen,
wechselnde Zeitgenauigkeit und unterbrochene Messungen beginnen den Vergleich
neu. Ohne lesbare Restzeit bleibt der Status unbekannt. Symbole dienen nur zum
Auffinden der Anzeige und zum Ablesen der Stufe, niemals als Ersatz für den
Zeitvergleich. Unbekannte Messwerte brechen die
Bestätigungsfolge ab; nach 90 Sekunden ohne frische Beobachtung verfällt der
Status. Beim Fensterwechsel bleibt ein frischer Hinweis deshalb kurz lesbar.
Neue Sessions, Tracking-Start und Pause verwerfen vorherige Ergebnisse,
einschließlich noch laufender Hintergrundarbeit. `TrackerState.LootScroll` ist
die einzige Statusquelle für Live-Session und Overlay.

## Positionsänderungen des Droplogs

`LootPanelCaptureGuard` prüft vor jedem Frame die Änderungszeiten von
`gameVariable.xml` und `GameOption.txt`. Nach einer Dateiänderung wird die
BDO-Konfiguration sofort erneut gelesen, sonst höchstens alle zwei Sekunden.
Eine gültige Positionsänderung aktualisiert im vorhandenen Analyzer sowohl
Normal- als auch Rare-Panel und deren Zeilenbereiche vor dem nächsten Crop.
Noch wartende Aufnahmen von vor der gespeicherten Änderung verwenden weiterhin
die vorherigen Koordinaten; maßgeblich ist der Aufnahmezeitpunkt.
Reconciliation, offene Mengenrevisionen, Ledger und Spot-Lock bleiben erhalten;
nur die geometrieabhängige Alignment-Prüfung und der Recovery-Cursor beginnen neu.

Eine reine Verschiebung benötigt keinen App-Neustart. Fehlende oder ungültige
Konfigurationen, ein anderes Profil, geänderte Schrift/Skalierung/Auflösung sowie
Clipping mit geänderten Zeilenidentitäten halten das Tracking weiterhin an,
damit vorhandene Drops nicht mit falschen Koordinaten erneut gezählt werden.

## Historische Zähleränderungen

Die Zähleränderung aus test.1 ist zurückgenommen. Fortlaufende Tags führten im
gemeldeten Live-Test zu etwa 1.500 gezählten bei 5.000 tatsächlichen Trashloot.
Identische Beobachtungen können unterschiedliche neue Drops darstellen. Sie
dauerhaft zusammenzufassen war deshalb zu aggressiv. Der normale Abgleich
verwendet wieder den ursprünglichen Zyklus 1→2→3→1 aus 0.9.5; dieser ist eine
historische Heuristik und kein Nachweis der tatsächlichen Drop-Identität.
Beim Nachlesen eines zuvor fehlgeschlagenen Namens überstimmt eine vollständig
gelesene OCR-Endmenge eine widersprüchliche Template-Menge. Vollständige akzeptierte
Baseline-Zeilen bleiben geschützt. Capture-Takt und Rare-Zähler bleiben unverändert.

Seit 0.9.4 wird der folgende Companion-Basispfad durch ausschließlich additive
[Normal-Loot-Leseversuche](OCR_RECOVERY.md) ergänzt: fehlende Mengen nachlesen und
gescheiterte Zeilen aus Originalpixeln mit Graustufen/adaptiver Binarisierung lesen.
Erfolgreiche Baseline-Beobachtungen brauchen keine zusätzliche Bestätigung. Der
Capture-Takt und Rare-Pfad bleiben unverändert; der normale Zähler entspricht
wieder 0.9.5. Die vollständige
Erkennung ist damit ausdrücklich nicht mehr identisch zum wiederhergestellten Stand.

## Rückkehr zum Erkennungsstand 0.5.1

Die eigenständige Matching-/Lebensdauerlogik aus 0.6.0 und 0.6.1 hat im berichteten
Live-Einsatz zu viele Trashloot-Drops verworfen. Deshalb werden Namensauflösung,
Text-/Mengenverarbeitung und zeitliche Zählung auf dem Companion-basierten
Stand 0.5.1 aufgebaut. Aus 0.9.6-test.1 bleibt die oben beschriebene Änderung der
Mengenübernahme beim Nachlesen erhalten; die Zähleränderung ist zurückgenommen.
Grundlage sind die erhaltenen, hashgeprüften 0.5.1-Assemblies,
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
Nullmengen aus OCR oder Templates gelten jetzt als ungültig: Eine positive
Template-Menge bleibt verwendbar, sonst greift die Behandlung fehlender Mengen.
Damit erreicht keine Nullmenge mehr das positive-only Ledger und löst dort einen
Tracking-Stopp aus. [Fehlerfall und Abgrenzung](OCR_RECOVERY.md#ungültige-nullmengen).

`CompanionItemMatcher` verwendet erneut das vollständige Vergleichsvokabular und
den ursprünglichen bytebasierten Matcher mit Grenzwert 0,34, dem Mengen-1-Filter
sowie den ursprünglichen zusätzlichen Rare-Regeln. Der normale Pfad bekommt
keine nachträgliche strengere Runner-up-Prüfung. Die gleichen Textreparaturen wie
im Stand 0.5.1 bleiben erlaubt; native Mengen-/Lückenreparatur ist wieder aktiv.

## Automatischer Spotfilter

`LootSpotCatalog` enthält die Hauptloot-Tabellen aller sechs Inner-Edania-Zonen,
den gemeinsamen HighestTier-Pool, eine separate `SharedGlobalItems`-Liste und
eine explizite Event-Liste. Seit 0.6.3 ergänzen Ancient Spirit Dust, Black Stone,
Caphras Stone, Laila's Petal und Pure Black Stone jeden der sechs Spotpools.
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
| Scorched Belt Ornament | Aresion Temple |
| Elion Follower's Mark | Scales of Judgment |
| Broken Gloves of the Void | Event Horizon |

Vor dieser Erkennung bleibt der Spot unbekannt; es wirkt noch kein zusätzlicher
Spotfilter. Der erkannte Spot bleibt bis `Reset` gesperrt. Pause/Fortsetzen erhält
ihn; für einen Spotwechsel ist eine neue Sitzung nötig. Eine frühere manuelle
Spot-Einstellung wird nicht eingelesen. Bekannte Event-Gegenstände bleiben auch
nach Aktivierung des Spotfilters immer erlaubt. Die frühere Event-Loot-Einstellung
wird nicht mehr eingelesen oder gespeichert.

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

Im aktuellen normalen Laufzeitpfad teilen zugeordnete Zeilen eine Drop-ID.
Eine spätere echte Menge ersetzt einen gebuchten Mindestwert mit einer Revision
derselben ID und einem Mengendelta. Die UI verwendet zusätzlich die absolute
Dropmenge, damit wiederholte oder verspätete Revisionen nicht doppelt wirken.
Die Anzahl der Drops erhöht sich durch eine Mengenrevision nicht. Der getrennte
Rare-Zähler behält seine bisherigen signierten Korrekturen. Auch reine
Korrekturframes aktualisieren die Summen.

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
eine manuelle Pause. Nach Abschluss einer laufenden Analyse wird die gesamte
abschließende Inaktivitätsdauer atomar gelesen und von der aktiven Sessionzeit
abgezogen. Der Abzug ist auf den aktuellen Start-/Fortsetzen-Abschnitt begrenzt;
frühere Abschnitte bleiben erhalten und die Dauer wird niemals negativ. Ein noch
gezählter Drop aus einer laufenden Analyse aktualisiert vorher den Grenzzeitpunkt.
Manuelles Pausieren zieht nichts ab. Fortsetzen beginnt ein neues Wartefenster.
Anzeige und Garmoth-Upload verwenden dieselbe korrigierte Sessionuhr.

Die [passive Klassenerkennung](CLASS_DETECTION.md) liest gespeicherte Skill-Slots
unabhängig von der OCR. Der [optionale Garmoth-Upload](GARMOTH_INTEGRATION.md)
sendet auf manuellen Klick oder nach ausdrücklich gespeichertem Opt-in für
stündliche Uploads. Keine seiner Validierungen beeinflusst die lokale Erkennung
oder Zählung. Die Automatik sendet feste Abschnitte von 60 aktiven Minuten, ohne
das Tracking zu pausieren; Pausen und noch abziehbare abschließende Inaktivität
zählen nicht mit. Abschnittsstände werden ab Sitzungsbeginn unabhängig vom Opt-in
festgehalten. Bereits gesendete Zeit und Mengen werden für weitere Uploads abgezogen.

Seit 0.9.0 gilt der Klick auf **Garmoth-Upload** als Sendeauftrag und pausiert bei
Bedarf selbst. Der einmal gespeicherte Key liegt separat Windows-DPAPI-verschlüsselt.
Ein regionsgetrennter, asynchroner Preisprovider liefert ausschließlich öffentliche
Marktpreise; `SilverValuation` projiziert die vorhandenen Summen in Vor-/Nachsteuerwerte
mit der bestätigten Companion-Steuer-/Stückrundung. Es gibt keinen Datenpfad von
Preisen oder Upload-Itemlisten zurück in Aufnahme oder Zählledger. Der Upload
bewertet ausschließlich die neuen Abschnittsmengen zu aktuellen Preisen und
Steuereinstellungen und lässt nicht unterstützte Garmoth-Items aus. Frühere
Silber-Gesamtsummen werden nicht voneinander abgezogen. Preiswechsel während eines
Abrufs können niemals fremde Regionenpreise in die Sitzung übernehmen.

Ein unabhängiger Upload-Lock verhindert überlappende Sendevorgänge. Automatischer
Erfolg gibt die nächsten Stundenabschnitte frei; ein unklarer automatischer Ausgang
sperrt sämtliche weiteren Uploads dieser Sitzung, während das lokale Tracking
nutzbar bleibt. Der manuelle Upload sendet den gesamten verbleibenden Abschnitt
und behält bei Erfolg oder unklarem Ausgang die Sperre für Fortsetzen und erneuten
Versand bei. Einzelheiten zu Korrekturen, Wiederaufnahme nach eindeutiger Ablehnung
und den prozesslokalen Schutzgrenzen stehen in der
[Garmoth-Integration](GARMOTH_INTEGRATION.md).

## Diagnose und Offline-Replay

Nur bei ausdrücklichem Opt-in schreibt `DiagnosticRecordingSession` Loot-PNGs und
JSONL. Es gibt keine Gesamtgrenze für Frames, Aktionen, Bilddaten oder JSONL-Größe;
Fehler deaktivieren die Aufzeichnung, nicht die Erkennung. Eine Aufnahme beginnt mit einer neuen Session und umfasst auch
Pause/Fortsetzen. Der Header kann noch keinen Spot enthalten, weil die automatische
Erkennung erst während der Aufnahme erfolgt.

Neue Frame-Einträge enthalten mit `isHdr` auch die HDR-Information der Aufnahme.
Bei älteren Einträgen fehlt diese Angabe und gilt als unbekannt, nicht als SDR.
Replay liest JSONL weiterhin zeilenweise und ohne Gesamtgrößenlimit; die Prüfungen
pro Eintrag, für Sequenz und Zeitstempel sowie die Pixelgrenze pro Lootausschnitt bleiben erhalten.

Formatversion 2 trägt die Enginekennung `companion-0.7.4-minimum-quantity-v4`. Die vorherigen
Kennungen `companion-0.7.4-recovery-fix-v3`, `companion-0.7.4-restore-v1` und `companion-0.7.4-overcount-fix-v2` werden
als expliziter Vergleich mit dem aktuellen Zähler akzeptiert. Der Zähler entspricht
wieder restore-v1, solange keine Mindestmengen konfiguriert sind; die OCR-Aufbereitung wird im Replay nicht wiederholt. Alte Aufnahmen
des Lebensdauer-Trackers werden wegen der anderen Logik nicht akzeptiert.
Die Rare-Katalogmetadaten werden eingebettet, damit eine spätere Installation die
Klassifikation nicht unbemerkt ändert. Iconpfade sind dabei nur Klassifikationstext.
Ebenso wird die aktive Mindestmengen-Tabelle im Header eingefroren; das Replay
verwendet ausschließlich diese Werte. Alte Aufnahmen ohne Tabelle behalten ihren
Mengenersatz. Derzeit fehlen belegte Minima für alle sechs Spots, daher ist die
aktive Tabelle leer. [Quellen und Regeln](TRASH_MINIMUMS.md).

`LootDiagnosticReplay` liest nur JSONL, prüft Format, Sequenz, Größen und Zeitstempel
und wiederholt den aktuellen Companion-basierten Abgleich anhand akzeptierter
OCR-/Matching-Beobachtungen. Es öffnet keine Bildpfade und initialisiert weder
Bildschirmaufnahme noch BDO-Konfigurationsleser oder Windows OCR. Buchungen und
Korrekturen je Frame sowie Aufnahmesummen werden verglichen. Eine volle OCR-
Neuauswertung der PNGs und eine erneute Prüfung des Spotfilters sind nicht der
Umfang dieses Replays.

## Verifikation und Grenzen

Regressionstests prüfen den historischen Zyklus bei mehrdeutigen gleichen Zeilen,
sichtbar neu eingefügte gleiche Drops, Batch-/Pausengrenzen und widersprüchliche
Mengen beim Nachlesen. Die Zyklustests sichern Kompatibilität mit dem früheren
Zähler; ihre erwarteten Buchungen sind keine unabhängig gemessenen Inventarmengen.
UI-Tests prüfen insbesondere signed Korrekturen,
automatische Spotanzeige, ignorierte manuelle Alt-Einstellungen und begrenzte
Anzeigepuffer. Sie liefern keine reale Grind-Trefferquote.

Der bekannte kleinere Zählfehler des 0.5.1-Stands kann wieder auftreten. Überdeckte,
kurz sichtbare oder falsch gelesene Drops sind weiterhin nicht zuverlässig auflösbar.
Eine vollständige Beseitigung der Über- und Unterzählungen wird nicht behauptet;
manuell geprüfte Inventarmengen bleiben der Maßstab für den nächsten Livevergleich.

Die 0.6.3-Pooltests prüfen gemeinsame Drops nach einem bereits erfolgten Spotlock
in beiden OCR-Kanälen bis zur ausgegebenen Buchung. Außerdem
muss jeder Eintrag des gebündelten Vokabulars zu einem normalen Pool, der
Eventliste oder einem ausdrücklich ausgeschlossenen Vergleichskandidaten gehören.
Das verhindert erneut vergessene Poolzuordnungen, beweist aber nicht, dass alle
noch unbekannten Spielitems im Vokabular stehen.
