# Zeitliche Loot-Zählung und Capture-Messwerte

Diese Datei beschreibt den historischen Temporal-Zähler. Ab Testbuild 3 läuft
der [Lifetime-Zähler mit bisheriger OCR](LIFETIME_LOOT_TRACKING.md) im Live-Pfad.

**Nachtrag nach den realen Tests vom 10.09.2026:** Die unten beschriebene
v1-Architektur unterzählt. Insbesondere die bloße Altersstrafe und irreversible
Veröffentlichung sind keine Anforderungen an einen Nachfolger. Die Prüfung
beider Aufnahmen, des visuellen v2-Zwischenstands und des tatsächlichen
Garmoth-Modells steht in [Unterzählung: Befunde und Ersatz](LOOT_UNDERCOUNT_20260910.md).

Entwicklungsstand: 10. September 2026. Diese Beschreibung gilt für den aktuellen
Quellcode, nicht automatisch für bereits installierte Releases. Ausgangspunkt
war die [Garmoth-Analyse vom selben Datum](GARMOTH_REVERSE_ENGINEERING.md), deren
Vergleich den vorherigen Grindcrest-Zähler beschreibt. Die neue Implementierung
enthält keinen aus Garmoths Paket übernommenen Laufzeitcode.

## Aktiver Normalzähler

`FrameAnalyzerFactory` verbindet den normalen Live-Analyzer mit
`TemporalNormalReconciliationAdapter` und `TemporalLootReconciler`
(`temporal-v1`). Windows OCR, Zifferntemplates, Katalogmatching und die gezielte
lokale Paddle-Nachprüfung bilden weiterhin die Erkennungswege. Diese Änderung
macht Paddle nicht zur Haupt-OCR. Rare-Loot verwendet seinen bestehenden
Abgleichsweg.

Der `CompanionReconciliationAdapter` und `CompanionFrameReconciler` bleiben für
historische Replays und Vergleichstests erhalten. Auch der direkte
Analyzer-Konstruktor ohne expliziten Adapter verwendet weiterhin diesen
Vergleichspfad; für die Live-Auswahl ist deshalb die Factory maßgeblich.

## Wie normale Drops zugeordnet werden

Der neue Zähler verarbeitet zeitgestempelte Beobachtungen aus sechs kalibrierten
Zeilenplätzen in Aufnahme-Reihenfolge. Er bewertet mehrere Erklärungen dafür,
welche Zeilen weiterbestehen, nach oben rücken oder neu hinzukommen. Insgesamt
bleiben höchstens 24 Hypothesen im Speicher. Alternative Lebensdauern von
1.250, 1.350, 1.450 und 1.550 ms beeinflussen die Bewertung fehlender Zeilen.
Diese Werte sind weiche Modellannahmen: Das Alter allein verwandelt eine
weiterhin beobachtete Zeile nicht in einen neuen Drop.

Eine neue Ankunft benötigt eine tatsächlich gelesene Zeile. Leere Bilder oder
reine Ausrichtungsanker erzeugen keine erfundenen Drops. Unlesbare innere Zeilen
können ihren Platz behalten; ein Ausrichtungsanker bestätigt eine Zuordnung,
liefert aber keine zusätzliche Item- oder Mengenstimme. Wiederholte identische
Drops bleiben möglich, wenn die beobachtete Bewegung einen neuen Eintrag trägt.
Eine lange Beobachtungslücke ist zusätzliche Unsicherheit und kein Beweis für
Loot während der fehlenden Aufnahmezeit.

Für jede verfolgte Zeile werden mehrere gelesene Itemnamen und Mengen gesammelt.
Die häufigsten Lesungen bestimmen die Entscheidung. Das ist eine Abstimmung über
Beobachtungen, keine statistisch kalibrierte Wahrscheinlichkeit. Unklare Zeilen
können zunächst offenbleiben. Vorläufige Zuordnungen werden gegen konkurrierende
Verläufe geprüft; Abschluss und aus dem Bild verschwundene Zeilen werden gesondert
behandelt. Die genauen Bewertungs- und Freigabeschwellen bleiben im Core-Code
nachvollziehbar und müssen anhand von Replays bewertet werden.

Eine veröffentlichte Drop-ID und ihr Item werden nicht stillschweigend ersetzt
oder aufgrund eines später bevorzugten Verlaufs zurückgenommen. Mengen können
weiter korrigiert werden: dieselbe `EventId`, eine erhöhte `Revision`, die neue
`TotalDropQuantity` und die Differenz als `QuantityDelta`. So kann etwa eine
Berichtigung von 5 auf 6 genau +1 verbuchen. Die Abstimmung erfindet keine
Durchschnittsmenge, die nie gelesen wurde. Kataloggrenzen und als solche markierte
Mindestmengenschätzungen bleiben gesonderte Eingaben der Mengenentscheidung.

Diese irreversible Veröffentlichung ist eine bewusste Grenze des bestehenden
Buchungsprotokolls. Ein früh falsch festgelegtes Item lässt sich damit nicht
beliebig durch einen später bevorzugten Verlauf ersetzen. `Complete()` schließt
offene Evidenz ab, ohne sichtbare Identitäten beim Pausieren zu vergessen;
`Reset()` verwirft den Zustand für eine neue Sitzung.

## Spotfilter und Mengenprüfung

Der Live-Spotfilter verwendet die letzten zwölf bestätigten Trash-Drops. Drei
unterschiedliche Erstbuchungen desselben Trash-Typs und mindestens zwei Stimmen
Vorsprung sind erforderlich. Wiederholte OCR-Bilder, Mengenrevisionen, reine
Ausrichtungsanker und Mindestmengenschätzungen liefern keine zusätzlichen
Stimmen. Die letzten 128 verarbeiteten Trash-IDs werden dedupliziert. Das rollende
Fenster lässt anfängliche Fehl-Evidenz auslaufen; ein Zwischenstand von 3:2
blockiert einen später eindeutigen Spot deshalb nicht dauerhaft. Nach erfolgtem
Lock bleibt der Spot für die Sitzung fest.

Wird eine Mengenpolicy nach dem Lock enger, werden auch die bisherigen Stimmen
einer noch verfolgten Zeile unter dieser neuen Grenze bewertet. Eine bereits
gebuchte Menge kann dadurch eine reguläre Revision erhalten. Sicher auf 1..1
begrenzte Items benötigen keine lesbare Ziffer, um als feste Einzelmenge zu gelten.

Die bestehenden Windows-/Template-Recoverywege und die Paddle-Prüfung bleiben
aktiv. Der Trash-Anomaliedetektor lernt Erstbuchungen; eine spätere Revision
korrigiert einen noch vorhandenen Historieneintrag unter derselben ID, ohne
einen zusätzlichen Drop vorzutäuschen oder die Stichprobe nach hinten zu versetzen.

Der Rare-Zähler bleibt framebasiert mit seinem bestehenden Zwölf-Frame-Fenster.
Bei tatsächlich erreichtem 200-ms-Takt entspricht dieses Fenster etwa 2,4 Sekunden
statt etwa 5,4 Sekunden beim bisherigen 450-ms-Takt. Die Zählregeln sind erhalten,
ihre zeitliche Wirkung ist durch den schnelleren gemeinsamen Takt verändert.

## Aufnahme und Rückstau

`PassiveCaptureSession` verwendet standardmäßig einen Ziel-Takt von **200 ms**.
Der Konstruktor erlaubt ein anderes positives Intervall. Die historische
450-ms-Konstante bleibt für Vergleichstests erhalten und ist nicht der aktuelle
Live-Standard.

Der Termin für die nächste Aufnahme beginnt vor der aktuellen Aufnahme.
Benötigt diese 60 ms und entsteht kein weiterer Aufwand, verbleiben bei einem
200-ms-Ziel noch 140 ms Wartezeit. Dauert die Aufnahme länger als das Ziel,
wird keine zusätzliche Mindestwartezeit von 200 ms angehängt. Das ist keine
Garantie für fünf erfolgreich analysierte Bilder pro Sekunde.

Ein Producer nimmt Bilder auf, ein Consumer analysiert sie in derselben
Reihenfolge. Höchstens **vier Bilder warten zusätzlich zum gerade analysierten
Bild**. Der Producer reserviert Kapazität vor der Aufnahme. Ist die Queue voll,
wartet er; bereits aufgenommene Bilder werden nicht zugunsten neuerer Bilder
verworfen. Der tatsächliche Aufnahmeabstand kann dadurch größer werden. Es gibt
keinen Rückstau von Hunderten vollständigen Bildschirmbildern.

Ein normaler Stopp beendet weitere Aufnahmen und verarbeitet aktuelle sowie
wartende Bilder fertig. Schlägt der Consumer fehl, stoppt auch der Producer und
die verbliebenen Bitmaps werden freigegeben. Die vorhandene UI-Darstellung wird
für diese Instrumentierung nicht erweitert.

## Was die Diagnose tatsächlich misst

Neue Aufnahmen verwenden die Enginekennung `grindcrest-temporal-v1` bei
Formatversion 2. Im Header stehen `targetFrameIntervalMilliseconds` und
`maximumQueuedFrames` aus der tatsächlich verwendeten Capture-Session.
`recognitionVariant` kennzeichnet den aktiven Normalzähler pro Frame.

Die optionalen `captureTiming`-Felder verwenden monotone Zeitmessung:

| Feld | Bedeutung |
|---|---|
| `targetFrameIntervalMilliseconds` | Konfigurierter Ziel-Takt, normalerweise 200 ms. |
| `captureDurationMilliseconds` | Zeit für die lokale Aufnahme einschließlich des vorherigen HUD-Sichtbarkeitschecks. |
| `captureIntervalMilliseconds` | Abstand zwischen zwei erfolgreich abgeschlossenen Aufnahmen; beim ersten Bild unbekannt. |
| `backpressureDurationMilliseconds` | Warten auf freie Queue-Kapazität vor dieser Aufnahme. |
| `queueDelayMilliseconds` | Zeit zwischen Einreihen des Bildes und Beginn seines Analyse-Callbacks. |
| `analysisDurationMilliseconds` | Gemessene Dauer des Frame-Analyzers. |
| `captureToResultMilliseconds` | Summe aus Queue-Wartezeit und Analysedauer. |

`captureToResultMilliseconds` ist die Latenz ab Einreihen bis zum Analyseergebnis.
Sie enthält weder die vorherige Aufnahmezeit noch nachfolgendes Schreiben der
Diagnosedateien, HUD-Monitoring oder UI-Auslieferung. Die vollständige Dauer bis
zu einer sichtbaren aktualisierten Kennzahl ist damit nicht gemessen. Der
Aufnahmezeitstempel `CapturedAtUtc` bleibt unabhängig davon die zeitliche Eingabe
des Zählers; Mengen werden nicht auf den späteren OCR-Fertigstellungszeitpunkt
umdatiert. Die erste erfolgreiche Aufnahme einer Capture-Instanz wird einmal
an die UTC-Systemzeit gebunden. Weitere Aufnahmezeiten ergeben sich aus diesem
Anker plus der monoton gemessenen verstrichenen Zeit. Der Anker bleibt beim
Pausieren und Fortsetzen erhalten; die verstrichene Zeit enthält auch die Pause.
Ein Vor- oder Rücksprung der Systemuhr verändert dadurch das Zeilenalter auch
über eine Pause hinweg nicht. Nur bei identischen monotonen Zeitwerten greift
der bisherige Ein-Tick-Fallback. Eine neue Capture-Instanz setzt einen neuen
UTC-Anker.

Der Abschluss eines Aufnahmeabschnitts verwendet den letzten erfolgreich
analysierten Aufnahmezeitpunkt, weil der Flush keine neueren Bildbeobachtungen
hinzufügt. So bleiben auch Abschluss und spätere Fortsetzung in derselben
Zeitreihenfolge, wenn sich die Systemuhr verändert. Vor der ersten verarbeiteten
Aufnahme bleibt der übergebene Abschlusszeitpunkt maßgeblich.

Ein hoher `queueDelayMilliseconds`-Wert zeigt verzögerte Verarbeitung. Hoher
`backpressureDurationMilliseconds` zusammen mit verlängertem
`captureIntervalMilliseconds` zeigt, dass die begrenzte Queue bereits neue
Aufnahmen bremst. Für einen Durchsatzvergleich sind Verteilungen dieser Werte
über denselben Aufnahmefall sinnvoll; allein ein Zielwert von 200 ms ist kein
Leistungsnachweis. Fehlende Felder in älteren Aufnahmen bedeuten unbekannte
Messwerte, nicht null Millisekunden.

## Replay und Prüfgrenzen

Das Diagnose-Replay erkennt den Normalzähler aus der aufgezeichneten Variante
und führt gespeicherte OCR-/Matching-Beobachtungen mit ihren Zeitstempeln erneut
durch diesen Zähler. Historische Varianten bleiben getrennt vom neuen Verfahren.
Es findet keine erneute OCR der PNG-Dateien statt. Ein reproduzierbares Replay
zeigt daher deterministische Verarbeitung derselben Eingaben und keine
unabhängig bestätigte Lootmenge.

Die Capture-Tests decken den 200-ms-Ziel-Takt einschließlich Aufnahmezeit,
konfigurierte Abweichungen, monotone Aufnahmezeiten und Queue-Latenz trotz
Systemuhrsprüngen sowie durch Rückstau verlängerte Aufnahmeabstände ab. Sie prüfen
zudem Reihenfolge, begrenzte Kapazität und das
Abarbeiten beim Stoppen. Synthetische Zeilenfolgen und diese Timingtests ersetzen
keinen Vergleich echter Grinds mit unabhängig ermittelten Sollmengen.

Eine bessere Fehlerquote gegenüber dem alten Zähler oder Garmoth wird hier nicht
behauptet. Dafür braucht es dieselben originalen Bildfolgen, annotierte neue
Drops und Mengen oder kontrollierte Inventarmengen vor und nach einem Grind.
Identische neue Meldungen ohne unterscheidbare Bewegung, Verdeckung,
Aufnahmelücken, falsch gelesene Namen und falsche Kataloggrenzen bleiben mögliche
Fehlerquellen.

Ein konkretes künstliches Grenzbeispiel: Drei identische Drops bei 0/600/1.200 ms
mit jeweils 1.300 ms Sichtbarkeit werden bei 200-ms-Abtastung korrekt als drei
Drops gelesen. Bei 450-ms-Abtastung erscheinen nie alle drei gleichzeitig;
die Ersetzung einer alten Zeile durch eine neue ist teilweise nicht vom
Weiterbestehen derselben Zeile unterscheidbar. Dieser konservative Zähler erkennt
in diesem Fall nur zwei. Er zählt keine zusätzliche Ankunft allein aufgrund
einer angenommenen Ablaufzeit. Der schnellere Solltakt und die Diagnose tatsächlicher
Aufnahmelücken sind deshalb Teil dieser Änderung; die verbleibende Mehrdeutigkeit
ist durch Software allein nicht vollständig auflösbar.

Auch bei 200 ms bleibt ein wichtiger Sättigungsfall: Sind alle sechs Plätze
dauerhaft mit demselben Item und derselben Menge gefüllt, kann eine identische
Ersetzung dieselben Beobachtungen wie ein unverändertes Panel liefern. Der Zähler
kann dann nach den ersten sechs Drops stehenbleiben und weitere Drops dauerhaft
unterzählen. Ein schnellerer Takt allein beseitigt diesen Fall nicht. Garmoths
Lebensdauerannahmen erlauben hier geschätzte Ersetzungen und nehmen dafür mögliche
Überzählung in Kauf. Diese Implementierung braucht für eine verlässliche
Unterscheidung zusätzliche visuelle Ankunftsevidenz; eine Erneuerung allein nach
Alter wird derzeit nicht gebucht. Die vorhandenen Tests mit drei identischen
beziehungsweise langen gemischten Dropfolgen belegen keine korrekte Zählung eines
solchen vollständig identischen, gesättigten Feeds.

## Code und Tests

- `src/BdoGrindTracker.App/Analysis/FrameAnalyzerFactory.cs`: Auswahl des Live-Zählers.
- `src/BdoGrindTracker.App/Analysis/CompanionLootFrameAnalyzer.cs`: Übergabe der Zeilen und Aufnahmezeitstempel.
- `src/BdoGrindTracker.Core/TemporalLootReconciler.cs`: Hypothesen, Identitäten, Mengenstimmen und Revisionen.
- `src/BdoGrindTracker.App/Capture/PassiveCaptureSession.cs`: Capture-Takt, Queue und Zeitmesswerte.
- `src/BdoGrindTracker.App/Application/TrackerSessionService.cs`: Analysedauer und Übergabe an die Diagnose.
- `src/BdoGrindTracker.App/Diagnostics/LootDiagnosticFormat.cs`: gespeicherte Timingfelder und Enginekennung.
- `src/BdoGrindTracker.App/Diagnostics/LootDiagnosticReplay.cs`: Auswahl und Prüfung des aufgezeichneten Zählers.
- `tests/BdoGrindTracker.App.Tests/PassiveCaptureTimingTests.cs` und `PassiveCaptureSessionTests.cs`: kontrollierte Capture-Prüfungen.
