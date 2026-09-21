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
dagegen nachweislich denselben Client-Symbolpfad. Adventure's Boon, Body Enhancement
und Turning Gates werden bei jeder neuen Buchung **immer als 300-Minuten-Variante**
bewertet, unabhängig von der gelesenen Restzeit. Damit zählen anfänglich aktive
Buffs und spätere Erneuerungen unter derselben Verbrauchsidentität. Verwendet
werden die hinterlegten NPC-Preise: 12.000.000, 10.000.000 bzw. 2.000.000 Silber.
Dies ist eine feste Bewertungsannahme; der Timer beweist die gekaufte Dauer nicht.

Gleichbleibende oder sinkende Restzeiten erzeugen weiterhin keine weitere Buchung.
Eine höhere gelesene Restzeit zählt auch nach einer Pause oder Erkennungslücke
als neue Anwendung und erhält wieder die 300-Minuten-Variante.
Frühere Buchungen behalten ihre Werte. In Live-Session, Verlauf und Overlay werden
auch alte kürzere Varianten je Zeltbuff in derselben Kachel zusammengefasst;
ihre gespeicherten Kosten bleiben unverändert. Haben mehrere Varianten dasselbe Symbol
**und dieselbe Dauer**, etwa Glücksstufen oder bestimmte normale/unsterbliche
Parfümpaare, bleiben sie als **(Variante unbekannt)** ohne Preis sichtbar.

Die Erkennung benötigt keine separate Einstellung. Frühere manuelle Profile
werden nicht mehr ausgewertet; vorhandene Profildateien bleiben erhalten.

**Normale und unsterbliche Harmony-Varianten werden bei eindeutigem Symbol
getrennt erkannt und zum jeweiligen Zentralmarktpreis bewertet.** Unsterbliche IDs
werden nicht auf normale IDs umgeschrieben. Bereits gespeicherte Verbrauchsbuchungen
behalten ihre ursprünglichen Namen, Preise und Zeitpunkte, einschließlich
früherer Buchungen zum normalen Preis.

Die Erstanrechnung benötigt für **jeden Buff zwei eigene, zeitlich zusammenpassende
Beobachtungen**. Ein dabei bereits aktiver, bisher ungebuchter Buff wird **einmal
mit seinem Preis angerechnet**. Das gilt auch, wenn er erst später in der Session
lesbar wird: Ein früh erkanntes Harmony-Symbol schließt eine erst später lesbare
Cron-Mahlzeit nicht aus. Diese Buchung ist als Erstbeobachtung gespeichert und im
Mouseover als **Bei erster Erkennung aktiv** gekennzeichnet; sie behauptet weder,
dass der Buff schon beim Grindstart sichtbar war, noch einen während der Session
beobachteten Kauf.
Sobald eine eindeutig gelesene Restzeit höher ist als der zuletzt
für diesen Buff gelesene Wert, zählt sofort eine weitere Anwendung zum aktuellen
Preis. Gleichbleibende oder sinkende Restzeiten erhöhen den Zähler nicht. Eine
zweite Bestätigungsaufnahme ist für die Erneuerung nicht erforderlich.

Vor der zweiten passenden Erstbeobachtung bleibt der Buff ungezählt. Vorheriges
Nichterkennen belegt keine Abwesenheit. Bereits vorhandene Buchungen verhindern
eine weitere Erstanrechnung desselben Buffs, auch bei Varianten derselben
Dauerfamilie. Pause, Erkennungslücke und eine Präzisierung der Timeranzeige zählen
bereits gebuchte Buffs deshalb nicht erneut. Beim Wiederherstellen einer Session
bleiben diese Buchungen und ihre Preise erhalten; bislang ungebuchte Buffs können
nach zwei neuen passenden Beobachtungen erstmals angerechnet werden. Ein neuer
Grind beginnt eine neue Inventur.

Die Timerauflösung beeinflusst die Laufzeitschätzung, nicht den Vergleich für
eine neue Anwendung. Der Bildleser verwirft widersprüchliche OCR-Ergebnisse.
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
2. Buffsymbole und Restzeiten sichtbar lassen. Jeder bisher ungebuchte Buff zählt
   nach zwei eigenen passenden Befunden einmal mit Preis, auch bei späterer
   Ersterkennung. Im Mouseover steht die Kennzeichnung **Bei erster Erkennung aktiv**.
3. Für einen Verbrauchstest einen bereits beobachteten Buff mit deutlich
   verkürzter Restzeit erneuern und die nächste lesbare Prüfung abwarten. Der Mengenbadge
   am Icon steigt; der Mouseover nennt die gespeicherten Preise, die Kostenzeile
   die Gesamtkosten.
   Stundenanzeigen können eine Erneuerung wegen ihrer groben Auflösung verbergen.
4. Bei erst später lesbaren Buffs ebenfalls zwei passende Prüfungen abwarten.
   Eine Pause, ein fehlendes Symbol oder ein verdecktes Bild darf bei bereits
   gebuchten Buffs keine weitere Erstanrechnung erzeugen.

Die unterstützte Erkennung ist keine Garantie, jedes vorhandene Buffsymbol unter
allen Grafikeinstellungen zu lesen. Fehlende oder mehrdeutige Symbole und
unlesbare Timer bleiben unbekannt. Ein nicht unterstützter Buff wird nicht anhand
ähnlicher Effekte einem kostenpflichtigen Gegenstand zugeordnet.

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
Silber. Bei unterschiedlichen Zeltlaufzeiten erlaubt dagegen die oben erläuterte
Dauerannahme eine Bewertung; sie ist kein Beleg des ursprünglichen Kaufs.

Die Kosten verwenden den vollen Zentralmarktpreis der ausgewählten Marktregion,
ohne Verkaufssteuerabzug. Zeltbuffs verwenden den festen NPC-Kaufpreis der
gewählten Variante beziehungsweise der angenommenen Kaufdauer und werden nicht
am Zentralmarkt abgefragt. Die gespeicherten Buchungen unterscheiden Erstanrechnung,
bestätigte Erneuerung, Zentralmarkt und NPC-Festpreis. Fehlende Preise bleiben unbekannt. Bereits bekannte
Preise behalten ihren Preiszeitpunkt und Cache-Status. Die Liste erfasst nur
bestätigte Erstbeobachtungen und Erneuerungen; sie ist keine vollständige Inventarhistorie.

Die Erfassung speichert zwei getrennte Werte, die nicht addiert werden:

- **Beobachtete Laufzeit:** Gegenstandspreis × bestätigte Beobachtungszeit / volle
  Wirkungsdauer. Das berücksichtigt auch einen bei seiner ersten Erkennung bereits aktiven
  Buff. Pausen, fehlendes Bildsignal und unlesbare Abschnitte werden ausgelassen.
- **Buffkosten:** Ein voller Gegenstands- bzw. NPC-Preis je bestätigter Erstanrechnung
  und je späterer bestätigter Erneuerung. Art der Buchung, Zeitpunkt, Marktregion,
  Preis und Cache-Status bleiben mit der Buchung gespeichert. Die kompakte Anzeige
  verwendet diese Buffkosten.

Im Kopf der **Live-Session** fasst **Verbrauchte Items** die Buchungen als kompakte
Iconleiste zusammen. Jedes Bufficon trägt die gezählte Menge in der Ecke; daneben
stehen die Gesamtkosten aller Buchungen. Beim Überfahren eines Icons erscheinen
Name, Anzahl und gespeicherte Preise. Nur gebuchte Erstanrechnungen und bestätigte
Erneuerungen erzeugen eine Kachel; ein aktiver Timer allein erhöht keine Menge.
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
Gruppen-Harmony-Erkennung zählt bestätigte Erstbeobachtungen und Timer-Erneuerungen und bewertet die
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
belegt den laufenden Effekt; die Erstanrechnung ist eine separate Bewertungsregel,
kein Nachweis eines neu beobachteten Kaufs. Das belegt dieses Layout, noch keine allgemeine
Erkennungsquote für andere Auflösungen, Buffsymbole oder UI-Skalierungen.
