# Zusätzliche OCR-Leseversuche (seit 0.9.4, Mengenübernahme korrigiert in 0.9.6-test.1)

Der folgende Abschnitt beschreibt den ersten Zusatzweg mit Windows OCR.
Zusätzlich gibt es jetzt eine [parallele Prüfung mit einer zweiten Engine](BACKGROUND_OCR_REVIEW.md),
die auch fragwürdige positive Mengen und unsichere Rare-Namen nachlesen kann.
Die Beschränkung auf fehlende Mengen gilt weiterhin für den hier beschriebenen
Windows-Zusatzweg, nicht für die neue Hintergrundprüfung.

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
der damaligen Testversion reproduziert. Für den konkreten
gemeldeten Vorfall lag keine aktuelle Diagnoseaufnahme vor.

Die Textauswertung akzeptiert jetzt nur positive Mengen. Bei ungültigem OCR-Wert
bleibt eine positive Template-Menge verwendbar; andernfalls ist die normale Menge
fehlend und kann über Nachlesen oder den bisherigen Zähler-Fallback
ergänzt werden. Der bestehende Rare-Ersatzwert 1 bleibt erhalten. Auch die
Zusatzlesungen dürfen keine Template-Nullmenge wieder einführen. Die interne
Duplikaterkennung und der Zähler für bereits gespeicherte Beobachtungen ändern
sich dadurch nicht. Das behebt den Fehlerstopp, belegt aber keine bestimmte
OCR-Genauigkeit im Spiel.

## HDR-Bildaufbereitung und zusätzliche Engine

Seit 1.0.2-test.1 erhält der erste Windows-OCR-Durchlauf bei bereits tonemapped
HDR-Aufnahmen eine eigene Schriftmaske und die volle Zeilenbreite. Damit können
helle Bodenstrukturen den Namensausschnitt nicht mehr über falsche
Zifferntemplates verkürzen. Der ursprüngliche SDR-Pfad bleibt erhalten.

Nach dem hier beschriebenen Nachlesen kann PP-OCRv6 Small deutliche Grenzfälle
prüfen. Vollständige primäre Mengen bleiben maßgeblich; leere Zeilen und
Hintergrundtext ohne Itemhinweis erhalten keine solche Prüfung. Die getrennte
Engine erzeugt keine zusätzlichen Drop-Ereignisse. Regeln, Grenzen und der
Aufnahmevergleich stehen unter [Hintergrundprüfung](BACKGROUND_OCR_REVIEW.md).

## Diagnose und Nachweisgrenze

Nur bei ausdrücklich aktivierter lokaler Diagnose enthalten die vorhandenen
JSONL-Frame-Einträge zusätzlich kompakte `recovery`-Zähler (versuchte Zeilen,
OCR-Aufrufe, gerettete Mengen/Katalogzeilen, Fehler). Kein wachsendes Dashboardlog,
keine zusätzlichen Screenshots für das normale Nachlesen und keine Uploads.
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
