# Zusätzliche OCR-Leseversuche (seit 0.9.4, Mengenübernahme korrigiert in 0.9.6-test.1)

Der Companion-basierte erste Erkennungsweg bleibt unverändert. Erst nachdem alle
normalen Baseline-Zeilen und das Rare-Band gelesen wurden, werden unvollständige
oder nicht erkannte normale Zeilen nachgelesen. Es gibt keine neue erforderliche
Zweitbestätigung, keinen Lebensdauerfilter und keine pauschale Mengenkorrektur.

## Zwei zusätzliche Lesewege

1. Bei erkanntem Item ohne Menge wird der äußerste rechte, durch Hintergrundabstand
   getrennte Zeichenblock isoliert und vergrößert mit Windows OCR gelesen. Der
   Parser akzeptiert ausschließlich ein vollständiges positives ASCII-Zahlentoken,
   optional mit vorangestelltem `x`/`×`. Keine Umdeutung von Buchstaben, Verkettung
   mehrerer Zahlen, Dezimal-/Tausendertrennzeichen oder Wahl der größeren Menge.
   Windows OCR selbst erhält dabei keine Ziffern-Whitelist; die Spezialisierung
   besteht aus dem engen Bildausschnitt und der vollständigen Tokenprüfung.
2. Gescheiterte Zeilen werden zusätzlich aus den Original-BGR-Pixeln vorbereitet:
   zuerst Graustufen ohne die ursprüngliche HSV-/Festschwellenmaske, danach lokal
   adaptive Binarisierung. Die bestehende Normalisierung auf 100 Pixel Höhe und
   Namensskalierung bleiben erhalten. Normale Namensgeometrie, Textpipeline und
   Katalogmatcher werden weiter verwendet. Die erste erfolgreiche zusätzliche
   Lesung gewinnt, nicht die mit der größten Menge. Auch Mengen aus diesen
   Gesamtzeilen benötigen ein vollständiges numerisches `x`-/`×`-Suffix; ein
   unvollständiges `x4O` wird nicht zu `4`, eine fünfstellige Zahl nicht abgeschnitten.

Auch ursprünglich als leer behandelte oder am oberen Rand aussortierte Zeilen
können so eine Lesechance erhalten. Die Ausschnitte selbst werden nicht verschoben;
es gibt keine neuen Zwischenbilder und keine höhere Aufnahmefrequenz.

## Bestehende Ergebnisse schützen, Zusatzarbeit begrenzen

- Ein erfolgreicher Name mit vorhandener Menge wird nicht erneut ausgewertet.
- Bei einem bereits erkannten Namen darf ausschließlich die fehlende Menge ergänzt
  werden. Ein anderer Name aus einem Zusatzversuch ersetzt ihn nicht.
- Beim Nachlesen eines zuvor nicht erkannten Namens hat eine vollständig gelesene
  numerische OCR-Endmenge Vorrang vor einer Template-Menge oder einer Menge aus der
  abgelehnten Zeile. So wird beispielsweise `x8` nicht mehr als Template-Wert `1`
  an die Zählung übergeben. Ohne vollständige Endmenge bleibt die vorhandene Menge
  der Rückfallwert. Bereits akzeptierte vollständige Baseline-Zeilen werden nicht geändert.
- Pro ursprünglichem Zeilenplatz bleibt genau eine Beobachtung mit derselben
  Position. Nur diese geht in den 10-Frame-Abgleich und das Ledger.
- Der zuerst aus Baseline-Trash bestimmte Spot hat Vorrang; gerettete Zeilen
  unterliegen weiterhin dem bisherigen Spotpool. Rare-Loot bleibt unverändert.
- Fehlende Mengen haben Vorrang vor fehlgeschlagenen Namen und leeren Zeilen.
  Gleichrangige Zeilen rotieren, damit nicht immer nur derselbe Platz nachgelesen wird.
- Zusatzarbeit ist pro Frame auf acht weitere OCR-Aufrufe und ein kooperatives
  Zeitbudget von 120 ms begrenzt. Ein bereits laufender OCR-Aufruf kann länger dauern;
  danach startet kein weiterer. Das Budget verwirft keine Baseline-Erkennung.
- Gleichförmige Originalbänder benötigen keine OCR-Wiederholung. Fehler im optionalen
  Leseweg behalten das Baseline-Ergebnis; echte Abbruchanforderungen werden beachtet.

## Ungültige Nullmengen

Ein OCR-Ergebnis wie `x0` oder eine isoliert erkannte Ziffer `0` kann keine gültige
Lootmenge sein. Im bisherigen Stand gelangte sie dennoch in den normalen oder
Rare-Zähler; `CompanionLootLedger.Add` warf daraufhin eine Ausnahme mit Parameter
`count`, wodurch die laufende Aufnahme stoppte. Dieser Pfad wurde mit den DLLs
der Testversion `artifacts/chat-fallback/app` reproduziert. Für den konkreten
gemeldeten Vorfall lag keine aktuelle Diagnoseaufnahme vor.

Die Textauswertung akzeptiert jetzt nur positive Mengen. Bei ungültigem OCR-Wert
bleibt eine positive Template-Menge verwendbar; andernfalls ist die normale Menge
fehlend und kann über Nachlesen, Item-Chat oder den bisherigen Zähler-Fallback
ergänzt werden. Der bestehende Rare-Ersatzwert 1 bleibt erhalten. Auch die
Zusatzlesungen dürfen keine Template-Nullmenge wieder einführen. Die interne
Duplikaterkennung und der Zähler für bereits gespeicherte Beobachtungen ändern
sich dadurch nicht. Das behebt den Fehlerstopp, belegt aber keine bestimmte
OCR-Genauigkeit im Spiel.

## Item-Chat als Mengen-Fallback

Nach den normalen Zusatzlesungen kann `PrivateItemChatFallback` eine weiterhin
fehlende Menge aus dem separat eingeblendeten Item-Chat übernehmen. Die
`PrivateItemChatCalibrationReader` liest nur den direkten aktiven UI-Eintrag
`Index="32"` der bereits kalibrierten `gamevariable.xml`, keine gespeicherten
UI-Presets. Das Fenster muss sichtbar, verwendet und vom Hauptchat getrennt sein.
System muss eingeschaltet sein und von den Systemfiltern ausschließlich
`ChatSystemType_PrivateItem`. Normale Chatkanäle und unbekannte eingeschaltete
Filter schließen einen Kandidaten aus; die in BDO gespeicherten internen Chatflags
werden gesondert toleriert. Das ist keine Garantie, dass das Spiel niemals eine
andere Meldung in dieses Fenster schreibt.

Relative Positionen beschreiben den Mittelpunkt, Größen sind mit der UI-Skalierung
in Pixel umzurechnen. Die Konfiguration wird alle zwei Sekunden neu gelesen.
Fensteränderungen, fehlerhafte Konfiguration und verlorene Lesekontinuität setzen
den Chatverlauf für die Zuordnung zurück. Fenster außerhalb des aufgenommenen
Bildes werden übersprungen. Spielauflösung und UI-Skalierung stammen weiterhin aus
der beim Anlegen des Analyzers gelesenen Kalibrierung.

`PrivateItemChatOcrReader` liest nur diesen Ausschnitt desselben aufgenommenen
Frames: Graustufen, höchstens 1,5-fache Vergrößerung, Windows OCR in der gewählten
oder automatisch erkannten Textsprache (`de-DE` / `en-US`).
Falls keine vollständige Itemmeldung gelesen wird, folgt höchstens ein zweiter
Versuch mit invertierter Helligkeit. Jeder Treffer braucht den vollständigen
englischen Systemtext `You have obtained` oder das deutsche Format
`Ihr habt {count} x {item} erhalten.`, einen geklammerten Itemnamen und eine
vollständige positive Menge. Deutsch steht die Menge vor dem Item, Englisch danach.
Der deutsche Chatfilter heißt **Beute**. Mehrzeilige, abgeschnittene oder
anderweitig unklare Meldungen werden ausgelassen. Die Namen werden mit dem
vorhandenen Katalogmatcher aufgelöst.

Die Zuordnung ist bewusst vorsichtig: Der anfängliche Verlauf ist nur Referenz,
alte Meldungen werden nicht nachgebucht. Neue Meldungen brauchen nachvollziehbares
Aufrücken der bisherigen Zeilen. Im selben Frame muss auch das normale Lootpanel
neue Zeilen am unteren Ende zeigen: Bereits vollständig erkannte Zeilen müssen
eindeutig um entsprechend viele Plätze nach oben gerückt sein. Diese älteren
Anker müssen außerdem schon im vorherigen Bild mit dem Chat-Ende übereinstimmen,
damit gegeneinander verzögerte Anzeigen keine gleichnamigen Drops vertauschen. Nur die neuen
Zeilen werden direkt mit den ebenso vielen neuen Chatmeldungen verglichen.
Lücken oder abgelehnte Zeilen an neueren Panelpositionen sind Zuordnungsbarrieren.
Ein Chat-Update wird nur in diesem einen Frame verwendet, niemals später erneut.
Identische wiederholte Meldungen bleiben einzelne Positionen; bei mehreren
möglichen Scrollabständen wird keine Menge geraten. Ohne lesbaren älteren
Panelanker oder bei zeitversetzter Aktualisierung der beiden Anzeigen greift
dieser vorsichtige Fallback nicht.
Die normale Beobachtung behält Itemname, Position und Metadaten. Chat erstellt
keine zusätzliche Beobachtung und ändert keine bereits vorhandene Menge. Der
bestehende normale Zähler und der Rare-Pfad bleiben erhalten. Insbesondere eine
bereits akzeptierte falsche `1` wird durch diesen Fallback noch nicht korrigiert.

Bei aktivierter Diagnose wird das Chatfenster als dritter separater Crop `chat`
gespeichert. `chatRecovery` enthält Fensterindex, Zustand, Anzahl gelesener
Meldungen, Fehler und ergänzte Mengen samt Item/Zeilenposition. Diese Zähler
beschreiben OCR-Ergänzungen, keine unabhängigen neuen Drops. Ohne Diagnose werden
keine Chatbilder oder Chattexte auf Datenträger geschrieben. Das Replay verwendet
die tatsächlich an den Zähler übergebenen Beobachtungen einschließlich ergänzter
Mengen; es führt Chat-OCR und Zuordnung nicht erneut aus.

Der bereitgestellte Chat-Screenshot wird mit 12 von 12 Meldungen und den richtigen
Mengen (zusammen 64) gelesen. Das ist ein Bildtest, kein vollständiger Grindtest.
Scrollzuordnung und Fehlerfälle werden zusätzlich mit synthetischen Bildfolgen
geprüft; die tatsächlich erreichte Verbesserung braucht einen Inventarvergleich
in einer neuen laufenden Sitzung.

## Diagnose und Nachweisgrenze

Nur bei ausdrücklich aktivierter lokaler Diagnose enthalten die vorhandenen
JSONL-Frame-Einträge zusätzlich kompakte `recovery`-Zähler (versuchte Zeilen,
OCR-Aufrufe, gerettete Mengen/Katalogzeilen, Fehler). Kein wachsendes Dashboardlog,
keine zusätzlichen Screenshots für das normale Nachlesen und keine Uploads. Der
oben beschriebene Chat-Fallback ergänzt seinen eigenen Diagnoseausschnitt.
Gerettete Zeilen können weiterhin
am unveränderten Spotfilter scheitern; Rettungszähler sind keine echten Inventarmengen.

Die neue Erkennungsvariante heißt `companion-0.7.4+normal-recovery-v1`. Das
Diagnose-Replay bleibt ein Zählungs-Replay bereits gespeicherter Beobachtungen,
kein erneuter OCR-Lauf. In 0.9.6-test.2 ist der normale Zähler auf den Stand von 0.9.5
zurückgesetzt; nur die Mengenübernahme aus test.1 bleibt erhalten. Neue Aufnahmen
tragen die Enginekennung `companion-0.7.4-minimum-quantity-v4` und betten die aktive
Mindestmengen-Tabelle ein. Der letzte Mengenersatz pro Trash-Item ist vorbereitet,
aber mangels belegter Werte für alle sechs Spots noch ohne aktive Einträge;
siehe [Recherche und Fallback-Regeln](TRASH_MINIMUMS.md). Aufnahmen mit
`companion-0.7.4-recovery-fix-v3`, `companion-0.7.4-restore-v1` oder `companion-0.7.4-overcount-fix-v2` werden als
Versionsvergleich ausgewiesen; gespeicherte OCR-Mengen werden dabei nicht neu
erkannt oder repariert. Die Tests prüfen Auswahl,
negative Fälle, Bildaufbereitung, Mengenwidersprüche und erhaltene Baseline-Verträge.
Sie belegen keine bestimmte Genauigkeitssteigerung
im Spiel. Dafür sind nach Pausieren abgeglichene reale Lootfolgen erforderlich;
verpasste Drops und zusätzliche Fehlzählungen sind getrennt zu prüfen.
