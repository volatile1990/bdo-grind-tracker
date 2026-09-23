# Buff-Erkennung und Kosten

Grindcrest erkennt unterstützte Buffsymbole und ihre Restzeiten automatisch in
den passiv aufgenommenen Spielbildern. **Die Erkennung läuft immer automatisch;
eine manuelle Einrichtung ist nicht vorgesehen.** Die Prüfung läuft alle zehn Sekunden.
Die Position der Buffleiste kommt aus der aktiven `UIData`-Sektion mit Index **119**
in derselben `gameVariable.xml`, die für das Item-Drop-Log (Index **159**) gebunden ist.
Relative Panelmitte, Sichtbarkeit, Auflösung und UI-Skalierung bestimmen den
Suchbereich. Innerhalb dieses Bereichs sucht die Erkennung die einzelnen Symbole;
OCR liest die zugehörige Restlaufzeit. Die Erkennung
verwendet weder Mausbewegungen noch Tooltips, Tastatureingaben oder Prozessspeicher.

Gespeicherte Verschiebungen werden beim nächsten Scan eingelesen. Versteckte,
fehlende oder widersprüchliche aktive Paneldaten liefern keinen Befund; gespeicherte
UI-Presets und Symbole außerhalb des Buffbereichs ersetzen sie nicht. Eine erst
nach der Aufnahme geänderte Konfiguration wird nicht auf das ältere Bild angewendet.
Der Ausschnitt umfasst alle drei Zeilen und die volle Breite von bis zu 20 Symbolen,
die über die nominelle Panelbreite hinausragen können. Quellen und Geometrie:
[`BDO-Buffbereich in der Config`](BUFF_HUD_CONFIG.md).

Der mitgelieferte Symbolkatalog enthält **37 Client-Symbolvorlagen für alle 58
Einträge der Kostenliste**: Cron-Mahlzeiten, Harmony, Parfüme, kostenpflichtige
Zeltbuffs und Mystic-Beasts-Schriftrollen. Die Zuordnung folgt den statischen
Clientdatensätzen vom Gegenstand über dessen Skill zum Buffsymbol. Die Anwendung
verwendet die mitgelieferten Vorlagen; sie benötigt keinen Extractor und liest
während der Erkennung keine Spielarchive. Die Herkunft und Zuordnung stehen in
[`client-mapping.json`](../data/ocr/buffs/client-mapping.json) und
[`SOURCES.md`](../data/ocr/buffs/SOURCES.md).

Die drei Cron-Mahlzeiten, zehn Harmony-Varianten (fünf normale und fünf
unsterbliche) und sechs Mystic-Beasts-Effekte besitzen jeweils eigene
Client-Symbole. Einige normale
und unsterbliche Parfüme, mehrere Zeltlaufzeiten und die Glücksstufen verwenden
dagegen nachweislich denselben Client-Symbolpfad. **Adventure's Boon und Body
Enhancement werden unabhängig von ihrer Restzeit ausschließlich der
300-Minuten-Variante zugeordnet und mit deren NPC-Preis bewertet.** Kürzere
Laufzeitvarianten werden automatisch nicht separat erkannt oder gezählt.
Damit führt das Unterschreiten ihrer Kaufdauern zu keinem Variantenwechsel.

Bei Turning Gates wird jede neue Anwendung der **kleinsten angebotenen Dauer
zugeordnet, die ihre gelesene Restzeit abdeckt**: etwa 280 Minuten zu 300 Minuten
und 160 Minuten zu 180 Minuten. Es gilt der hinterlegte NPC-Preis dieser Variante.
Die Variante bleibt während eines durchgängigen Countdowns erhalten und wird bei
einer neuen Anwendung erneut bestimmt. Grobe Stundenanzeigen werden als Intervall
ausgewertet: `2h` kann beispielsweise einen gerade angewendeten 180-Minuten-Buff
zeigen, `4h` die 300-Minuten-Variante. Passen mehrere Kaufdauern in dasselbe
Intervall, bleibt die Variante unbekannt. Diese Zuordnungen sind Annahmen zur
Bewertung und kein Beleg für die tatsächlich gekaufte Dauer.

Gleichbleibende oder sinkende Restzeiten erzeugen weiterhin keine weitere Buchung.
Eine höhere gelesene Restzeit zählt auch nach einer Pause oder Erkennungslücke
als neue Anwendung und erhält die oben beschriebene Laufzeitvariante.
Sekunden-, Minuten- und Stundenangaben behalten ihre jeweilige Anzeigeauflösung.
Ein Wechsel von einer groben Stundenanzeige zu einer genaueren Minutenanzeige
innerhalb desselben verbleibenden Zeitintervalls zählt nicht als Erneuerung.
Auch das bloße Unterschreiten einer angebotenen Kaufdauer erzeugt keinen Verbrauch.
Ein einzelner unplausibel schneller Timerabfall wird zunächst als unbekannt behandelt.
Erst eine dazu passende Folgelesung bestätigt den niedrigeren Wert; kehrt der Timer
zum erwarteten Countdown zurück, erzeugt das keine falsche Erneuerung.
Kurze Anzeigen wie `4h` und `4m`, die Windows OCR zunächst vollständig übersieht,
werden zusätzlich mit einem breiteren Streifen derselben Timerpixel geprüft.
Nur drei gleiche Timer in jeder von zwei übereinstimmenden Bildaufbereitungen
werden akzeptiert. Widersprüchliche oder nichtleere ursprüngliche OCR-Ergebnisse
werden dadurch nicht überschrieben. Ein wiederholt gleichbleibendes `4h` erzeugt
keine weitere Anwendung.
Frühere Buchungen behalten ihre Werte. In Live-Session, Verlauf und Overlay bleiben
unterschiedliche Laufzeitvarianten in getrennten Kacheln mit eigenen Mengen und
gespeicherten Kosten sichtbar. Haben mehrere Varianten dasselbe Symbol
**und dieselbe Dauer**, etwa Glücksstufen oder bestimmte normale/unsterbliche
Parfümpaare, bleiben sie als **(Variante unbekannt)** ohne Preis sichtbar.

Die Erkennung benötigt keine separate Einstellung. Frühere manuelle Profile
werden nicht mehr ausgewertet; vorhandene Profildateien bleiben erhalten.

**Normale und unsterbliche Harmony-Varianten werden bei eindeutigem Symbol
getrennt erkannt und zum jeweiligen Zentralmarktpreis bewertet.** Unsterbliche IDs
werden nicht auf normale IDs umgeschrieben. Bereits gespeicherte Verbrauchsbuchungen
behalten ihre ursprünglichen Namen, Preise und Zeitpunkte, einschließlich
früherer Buchungen zum normalen Preis.

**Beim Start bereits aktive Buffs zählen nicht als Verbrauch und erzeugen keine
Verbrauchskosten.** Ihre ersten zwei passenden Beobachtungen bestätigen nur den
Ausgangszustand und die beobachtete Laufzeit. Das gilt auch für zunächst
mehrdeutige oder unlesbare Buffs, sobald ihr Timer lesbar wird.

Ein später neu auftauchender Buff kann nach **zwei eigenen, zeitlich
zusammenpassenden Beobachtungen** als neue Anwendung zählen. Dafür muss die vorige
lesbare Prüfung seine Abwesenheit gezeigt haben und seine erste Restzeit nahe
der vollen erkannten Laufzeit liegen (höchstens 45 Sekunden plus Timerauflösung
darunter). Eine gültige, sichtbare Buffleiste ohne erkannte unterstützte Symbole
bildet einen leeren Ausgangszustand. Ein erst spät mit deutlich verkürzter
Restzeit lesbarer Buff bildet dagegen ebenfalls nur einen Ausgangszustand.
Wurde eine neue Anwendung nach lesbarer Abwesenheit bereits gesehen, bleibt dieser
Nachweis bei einer kurzen einzelnen Leselücke bis zu 45 Sekunden erhalten. Zwei
passende lesbare Befunde nach der Lücke können die Anwendung noch bestätigen;
die unlesbare Zeit erzeugt keine Laufzeitkosten. Das gilt auch, wenn zunächst nur
das neu erschienene Symbol und kurz darauf sein fast voller Timer erkannt wurde.
Sobald eine eindeutig gelesene Restzeit höher ist als der zuletzt
für diesen Buff gelesene Wert, zählt sofort eine weitere Anwendung zum aktuellen
Preis. Gleichbleibende oder sinkende Restzeiten erhöhen den Zähler nicht. Eine
zweite Bestätigungsaufnahme ist für die Erneuerung nicht erforderlich.

Vor der zweiten passenden Beobachtung bleibt eine neue Erscheinung ungezählt.
Pause und Erkennungslücken belegen keine neue Anwendung: erstmals danach
gelesene Buffs bilden einen neuen Ausgangszustand. Bekannte Timer bleiben für
den Vergleich auf Erneuerungen erhalten. Beim Wiederherstellen einer Session
bleiben historische Buchungen einschließlich früherer Erstanrechnungen und ihrer
Preise erhalten; neue Erstanrechnungen werden nicht mehr erzeugt. Ein neuer Grind
beginnt mit einem neuen, ungezählten Ausgangszustand.

Bei unveränderter Timerauflösung zählt ein eindeutig höherer Wert weiterhin sofort
als neue Anwendung. Eine bloße Verfeinerung einer Stundenanzeige wird dabei unter
Berücksichtigung der inzwischen verstrichenen Zeit ausgeschlossen.
Der Bildleser verwirft widersprüchliche OCR-Ergebnisse.
Ein kompletter Verbrauch zwischen zwei Messungen kann unerkannt bleiben, wenn
die neue gelesene Restzeit bereits gleich oder kleiner als der alte Wert ist.

Fehlt ein zuvor beobachtetes Symbol in einer folgenden Prüfung, ist es mehrdeutig
oder ist seine Restzeit unlesbar beziehungsweise widersprüchlich, verliert nur
dieser Buff seine durchgehende Laufzeitbeobachtung. Andere eindeutig erkannte
Buffs werden weiter ausgewertet. Die letzte gelesene Restzeit bleibt für den
Verbrauchsvergleich innerhalb der Session erhalten, auch nach einem Tod, einer
längeren Unterbrechung, Pause oder vorübergehend vollständig verschwundenen
Buffleiste. Nach der Rückkehr zählt ein höherer Timer sofort als neue Anwendung.
Die Unterbrechung selbst erzeugt keine Laufzeitkosten. Eine neue Session löscht
diese Vergleichswerte; beim Wiederherstellen dienen gespeicherte aktive Timer
nur als Vergleichswerte, ohne offline Laufzeit oder aktuell aktive Buffs zu behaupten.

## Automatisch starten und im Spiel prüfen

1. Eine Session starten und **Live-Session → Verbrauchte Items** im Kopfbereich
   beobachten. Die Standarderkennung benötigt weder die Auswahl eines Profils
   noch eine manuelle Markierung der Leiste.
2. Buffsymbole und Restzeiten sichtbar lassen. Bereits aktive Buffs bleiben
   ungezählt; nach zwei passenden Befunden wird ihre Laufzeit beobachtet.
3. Für einen Verbrauchstest einen bereits beobachteten Buff mit deutlich
   verkürzter Restzeit erneuern und die nächste lesbare Prüfung abwarten. Der Mengenbadge
   am Icon steigt; der Mouseover nennt die gespeicherten Preise, die Kostenzeile
   die Gesamtkosten.
   Stundenanzeigen können eine Erneuerung wegen ihrer groben Auflösung verbergen.
4. Einen bisher nicht aktiven Buff während des Grinds anwenden. Nach einer zuvor
   lesbaren Prüfung und zwei passenden Befunden mit frischem Timer zählt er einmal.
   Ein erst nach einer Pause oder mit bereits verkürztem Timer erkannter Buff
   darf keine nachträgliche Erstanrechnung erzeugen.

Die unterstützte Erkennung ist keine Garantie, jedes vorhandene Buffsymbol unter
allen Grafikeinstellungen zu lesen. Fehlende oder mehrdeutige Symbole und
unlesbare Timer bleiben unbekannt. Ein nicht unterstützter Buff wird nicht anhand
ähnlicher Effekte einem kostenpflichtigen Gegenstand zugeordnet.

Bei eingeschalteten automatischen Debuglogs enthält `buff-observation` jeden
tatsächlich ausgewerteten Buff-Befund mit Aufnahmezeit, Buff-ID, Restzeit,
Timerauflösung, unbekannten IDs und den daraus entstandenen Verbrauchsbuchungen.
Damit lassen sich fehlende oder zusätzliche Anwendungen gezielt nachverfolgen.

## Frühere manuelle Profile

Die Anwendung verwendet immer den mitgelieferten automatischen Symbolkatalog.
Die frühere Profilauswahl und der Screenshot-Assistent sind nicht mehr Teil der
App-Oberfläche. Ein gespeicherter `BuffRecognitionProfilePath` wird nicht mehr
für die Erkennung ausgewertet. Vorhandene Profildateien werden nicht gelöscht;
sie beeinflussen die automatische Erkennung nicht. Profilparser und historische
Beispieldateien im Repository dienen weiterhin der Kompatibilität und Tests.

Die automatische Kostenliste enthält **58 Einträge**: drei Cron-Mahlzeiten, fünf
normale und fünf unsterbliche Harmony Draughts, zwanzig Parfüme, neunzehn kostenpflichtige
Zelt-/Laufzeitvarianten und
sechs Mystic-Beasts-Schriftrollen. Die 39 Markt-IDs, Buff-IDs, Laufzeiten und festen
Zeltpreise sind in `BUFF_PRICE_SOURCES.md` dokumentiert. Frühere gespeicherte
Beast-/Giant-/Frenzy-Draught-Daten bleiben lesbar; die automatische Erkennung erfasst
diese Gegenstände nicht. Die Liste für historische Sessions enthält weiterhin
61 Einträge. Bereits gespeicherte Buchungen und frühere unbekannte Harmony-/Cron-
Gruppen werden nicht umgeschrieben oder nachträglich einer Preisvariante zugeordnet.

## Position aus der Spielkonfiguration

Die automatische Erkennung verwendet den aktiven, sichtbaren `UIData`-Eintrag
119 aus derselben `gameVariable.xml`, die für die Aufnahme ausgewählt ist.
Position, Auflösung und Skalierung werden bei jedem Scan geprüft. Presets,
versteckte oder doppelte Panels ersetzen keine gültige sichtbare Buffleiste.
Änderungen am Layout müssen in BDO gespeichert sein, bevor die Konfiguration
sie abbildet. Die vollständige Geometrie und die Quellen sind in
[BUFF_HUD_CONFIG.md](BUFF_HUD_CONFIG.md) dokumentiert.

## Umfang und Aussagekraft

Verfolgt werden nur eindeutig unterstützte Symbole. Alle Einträge der Kostenliste sind im Symbolkatalog abgedeckt;
das garantiert keine Erkennung unter allen Grafikeinstellungen oder bei
verdeckten beziehungsweise unlesbaren Symbolen. Skill-, Klassen-, Möbel-, Event- oder
kostenlose Buffs werden nicht anhand ähnlicher Effekte einem Marktgegenstand
zugeordnet. Wenn eine kostenlose/eventbezogene Variante dieselbe sichtbare Grafik
verwendet, kann ein Screenshot deren Herkunft nicht unterscheiden. Marktwerte sind geschätzte Gegenwerte,
keine belegten Kaufpreise.

Sind mehrere Preisvarianten anhand von Symbol und Dauer nicht unterscheidbar,
erscheint die erkannte Gruppe als **(Variante unbekannt)**. Restzeiten und
bestätigte Timer-Erneuerungen dieser Gruppe können sichtbar sein; ihre Preise
und Kosten bleiben unbekannt. Das betrifft nachgewiesene gemeinsame Symbole
normaler und unsterblicher Parfümvarianten mit gleicher Dauer sowie
unterschiedlich teurer Glücksstufen. Fehlende Preise erscheinen nie als null
Silber. Turning Gates verwendet die oben erläuterte Dauerannahme; Adventure's
Boon und Body Enhancement verwenden immer die 300-Minuten-Variante. Diese
Bewertung ist kein Beleg des ursprünglichen Kaufs.

Die Kosten verwenden den vollen Zentralmarktpreis der ausgewählten Marktregion,
ohne Verkaufssteuerabzug. Zeltbuffs verwenden den festen NPC-Kaufpreis der
gewählten Variante beziehungsweise der angenommenen Kaufdauer und werden nicht
am Zentralmarkt abgefragt. Historische Erstanrechnungen bleiben lesbar; neue Buchungen
erfassen nur während des Grinds erkannte Anwendungen mit Zentralmarkt- oder
NPC-Festpreis. Fehlende Preise bleiben unbekannt. Bereits bekannte
Preise behalten ihren Preiszeitpunkt und Cache-Status. Die Liste erfasst nur
bestätigte neue Anwendungen und Erneuerungen; sie ist keine vollständige Inventarhistorie.

Die Erfassung speichert zwei getrennte Werte, die nicht addiert werden:

- **Beobachtete Laufzeit:** Gegenstandspreis × bestätigte Beobachtungszeit / volle
  Wirkungsdauer. Das berücksichtigt auch einen bei seiner ersten Erkennung bereits aktiven
  Buff. Pausen, fehlendes Bildsignal und unlesbare Abschnitte werden ausgelassen.
- **Buffkosten:** Ein voller Gegenstands- bzw. NPC-Preis je bestätigter neuer Anwendung
  während des Grinds. Anfangs aktive Buffs kosten hier nichts. Art der Buchung, Zeitpunkt, Marktregion,
  Preis und Cache-Status bleiben mit der Buchung gespeichert. Die kompakte Anzeige
  verwendet diese Buffkosten.

Im Kopf der **Live-Session** fasst **Verbrauchte Items** die Buchungen als kompakte
Iconleiste zusammen. Jedes Bufficon trägt die gezählte Menge in der Ecke; daneben
stehen die Gesamtkosten aller Buchungen. Beim Überfahren eines Icons erscheinen
Name, Anzahl und gespeicherte Preise. Nur gespeicherte Verbrauchsbuchungen
erzeugen eine Kachel; ein aktiver Timer allein erhöht keine Menge.
Ohne Beobachtungen steht **—**, bei einem beobachteten Zustand ohne Buchungen
**0 Silber**. Sind alle Buchungen ungepreist, steht **Preis fehlt**; bei teilweise
bekannten Preisen bleibt der bekannte Betrag mit **\*** gekennzeichnet.

Der Mouseover eines Icons nennt auch den Preis je Buff oder die Preisspanne,
wenn sich der Preis während der Session geändert hat. Mehrere Erneuerungen
desselben Buffs erhöhen dessen Anzahl; unterschiedliche Varianten bleiben
getrennt. Die Zählung ergibt sich ausschließlich aus den gespeicherten Buchungen.
Der Verlauf und sein Bearbeitungsdialog verwenden dieselbe kompakte Anzeige
für die gespeicherte Session. In der Sessiontabelle eines Grindspots zeigt die
Spalte **Verbraucht** die Icons, Mengen und Gesamtkosten jeder Session direkt.
Die Anzeige verwendet ausschließlich deren gespeicherte Buchungen, auch nach
einem Neustart. Spätere Marktpreisänderungen oder Lootkorrekturen bewerten diese
Kosten nicht neu. Buchungsart, Zeitpunkt und Preisquelle bleiben in den
gespeicherten Daten erhalten. Ältere Sessions ohne Buffdaten zeigen **—**.

Das optionale Overlaymodul **Verbrauchte Items** verwendet dieselben Buchungen,
Icons, Mengen und gespeicherten Kosten. Es zeigt alle gebuchten Varianten und
passt das Raster an den verfügbaren Platz an. Seine Kostenzeile bleibt auch bei
ausgeschalteter Beschriftung oder ausgeschalteten Icons sichtbar. Einrichtung:
[Ingame-Overlay](OVERLAY.md).

Ein erst später verfügbarer Marktpreis bewertet folgende Beobachtungsintervalle.
Frühere unbekannte Kosten bleiben als Teilbetrag gekennzeichnet. Das erneute
Laden einer pausierten Session erhält die gebuchten Werte, zeigt alte Timer aber
nicht als aktuell aktiv an und berechnet keine Zeit während der Programmpause.
Beim Pausieren oder Beenden wartet Grindcrest höchstens fünf Sekunden auf eine
bereits laufende Buff-Prüfung. Noch rechtzeitig bestätigte Bilder von vor dem
Pausenklick werden gespeichert; die Wartezeit zählt weder als Grindzeit noch
als Buff-Laufzeit. Bei Zeitüberschreitung wird der unbestätigte Befund verworfen.
Die Buff-Werte stehen separat neben der Loot-Bewertung und ändern weder deren
Steuerberechnung noch den Garmoth-Upload. Empfangene Gruppenbuffs erlauben keinen
Nachweis darüber, welches Gruppenmitglied den Gegenstand konsumiert hat. Die
Gruppen-Harmony-Erkennung zählt bestätigte neue Anwendungen und Timer-Erneuerungen und bewertet die
eindeutig erkannte normale oder unsterbliche Variante ohne zusätzliche Zuordnung
zu eigenem Verbrauch. Ein bestätigter
Timerwechsel belegt die erneute Wirkung, nicht die Herkunft des Gegenstands.
Bei empfangenen Gruppenbuffs sind die angezeigten Kosten daher kein Nachweis eines
eigenen Kaufs oder Inventarverbrauchs.

Die automatisierten Prüfungen decken Profilvalidierung, aktive Konfigurationswahl,
Timerformate, synthetische Bildvorlagen, Mehrdeutigkeiten und Monitor-Lebenszyklen
ab; zusätzlich werden Whitelist, NPC-Preise, Quellenzuordnung und das Fortbestehen
historischer Buchungen geprüft. Drei echte Buffleisten-Ausschnitte vom 21.09.2026
prüfen zusätzlich die Windows-OCR mit Harmony/Halbmenschen und einer Cron-Mahlzeit:
13/114, 9/110 und 9/109 Minuten. Sie liegen unter `tests/fixtures/buffs`.
Eine zweite Bildskalierung nach dem Auffüllen des Zeitbereichs verhindert in
diesen Aufnahmen zusätzliche oder aufgeteilte Ziffern. Drei aufeinanderfolgende
passive Live-Aufnahmen bestätigten beide Buffs als bereits aktiv. Diese Erkennung
belegt den laufenden Effekt und erzeugt im Ausgangszustand keine Verbrauchsbuchung.
Das belegt dieses Layout, noch keine allgemeine
Erkennungsquote für andere Auflösungen, Buffsymbole oder UI-Skalierungen.
