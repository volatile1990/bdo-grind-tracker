# Buff-Erkennung und Kosten

Grindcrest prüft die konfigurierte Buff-Leiste alle zehn Sekunden in den bereits
passiv aufgenommenen Spielbildern. Eine Bildvorlage identifiziert das Buff-Symbol;
OCR liest die dazugehörige Restlaufzeit. Symbolpositionen dürfen sich innerhalb
des erfassten Bereichs verschieben. Die Erkennung verwendet weder Mausbewegungen
noch Tooltips, Tastatureingaben oder Prozessspeicher.

Die Verbrauchserkennung benötigt wiederholte, zeitlich zusammenpassende Messungen.
Ein laufender Buff bei der ersten Beobachtung ist kein nachgewiesener Neukonsum.
Ein bestätigter Sprung der Restlaufzeit kann einen erneuten Verbrauch belegen;
Minuten- und Stundenanzeigen werden mit ihrer entsprechend gröberen Genauigkeit
behandelt. Aus verdeckten, fehlerhaften oder unterbrochenen Messungen wird kein
nachträglicher Verbrauch erfunden. Ein kompletter Verbrauch zwischen zwei
Messungen kann unerkannt bleiben.
Auch eine Leiste ohne ein erkanntes Symbol gilt als unbekannt: Der erste Buff
nach einer leeren, verdeckten oder unlesbaren Leiste bildet deshalb eine neue
Ausgangsmessung und wird nicht als neu konsumiert gezählt.

## Einrichten und später im Spiel testen

1. Vor einer neuen Session **Einstellungen → Buff-Erkennung → Buffs per Screenshot
   einrichten** öffnen. Ein vollständiges Spielbild bei der später verwendeten
   Auflösung und UI-Skalierung laden; ein bloßer Ausschnitt der Leiste genügt
   nicht zur Bestimmung ihrer Bildschirmposition.
2. Den Bereich der gesamten Buffleiste einschließlich der Timer markieren.
   Aus der Auswahlliste den tatsächlich verwendeten Gegenstand wählen. Bei
   Zeltbuffs auch die gekaufte Laufzeit, bei Arzneien/Parfümen die normale oder
   unsterbliche Variante beachten.
3. Das Symbol ohne Restzeit und möglichst ohne blinkenden Rand markieren; danach
   das Rechteck um seine Restzeit markieren. Die Zuordnung hinzufügen. Für weitere
   Buffs wiederholen; die Vorlagenliste lässt sich später ergänzen.
4. Mit dem Screenshot-Test prüfen, ob Symbole und Zeiten gelesen werden. Eine
   lesbare Einzelmessung prüft die Kalibrierung; sie bucht noch keinen Verbrauch.
   Die Ausgabe nennt auch Fehler wie einen zu kleinen Zeitbereich oder doppelte
   Symboltreffer. Danach speichern. Profil und PNGs liegen gemeinsam im lokalen
   Grindcrest-Datenordner unter `buff-profiles`; der ausgewählte Screenshot wird
   nicht dauerhaft mitgespeichert.
5. Im Spiel eine Session starten und **Buffs & Kosten** beobachten. Nach zwei
   aufeinanderfolgenden erfolgreichen Prüfungen wird ein bereits laufender Buff
   als Ausgangszustand angezeigt. Eine spätere, ebenfalls bestätigte Erneuerung
   des Timers kann einen Verbrauch buchen. Die Prüfung erfolgt alle zehn Sekunden.
6. Zum Testen eines Neukonsums erst einen Buff mit deutlich kürzerer Restzeit
   zweimal erfassen lassen, danach erneuern und zwei weitere Prüfungen abwarten.
   Bei Stundenanzeigen ist die Erneuerung möglicherweise erst erkennbar, wenn sie
   die grobe Timerauflösung überschreitet. Pause/Fortsetzen darf weder die Pause
   berechnen noch einen Verbrauch beim Wiedereinstieg erfinden.

Das Profil gleicht nur die ausgewählten Vorlagen ab, nicht den ganzen Katalog.
Geladene Icondateien und skalierte Vorlagen werden zwischengespeichert. Bei
geänderter Datei oder Zuordnung werden sie neu geladen. Die Symbolsuche bleibt
auf den kalibrierten Leistenbereich begrenzt; OCR läuft nur auf den zugehörigen
kleinen Zeitbereichen.

## Profilformat und manuelle Einrichtung

Es gibt noch keine mit echten Spielbildern validierten Standardvorlagen für
Verbrauchsbuffs und keinen verifizierten allgemeinen `UIData`-Index ihrer Leiste.
Die Funktion benötigt deshalb ein eigenes Erkennungsprofil; der Assistent erzeugt
es ohne manuelle JSON-Bearbeitung. Die Beispielwerte
in `buff-recognition-profile.example.json` sind Platzhalter, keine bestätigte
BDO-Standardposition. Ohne gültiges Profil bleibt die Erkennung unbekannt.

1. Einen Screenshot des vollständigen Spielbilds bei der verwendeten Auflösung
   und UI-Skalierung aufnehmen. Ein zu verfolgender Buff muss sichtbar laufen.
2. Das reine Buff-Symbol ohne Restzeit, blinkende Ränder, Mouseover oder andere
   veränderliche Inhalte als PNG ausschneiden. Die Vorlage muss 8–256 Pixel breit
   und hoch sein. Echte HUD-Symbole verwenden: Inventar- oder Marktbildchen müssen
   nicht dem Buff-Symbol entsprechen.
3. Die Beispiel-JSON kopieren und `screenWidth`, `screenHeight`, `uiScale` sowie
   `region` anpassen. `region` umfasst alle relevanten Symbole **und** ihre Timer
   im vollständigen Spielbild; die Koordinaten beziehen sich nicht auf den Desktop.
4. Unter `templates` pro Buff eine bekannte `buffId`, den PNG-Pfad und
   `timerRegion` hinterlegen. Die Timer-Koordinaten sind Pixel relativ zur linken
   oberen Ecke des ausgeschnittenen Symbols. Negative X-/Y-Werte sind erlaubt,
   wenn die Zeit links oder oberhalb steht. Pfade sind relativ zur JSON-Datei oder
   absolut. Nur ein Symbol pro `buffId` ist zulässig.
5. Die JSON-Datei in den Einstellungen als Buff-Erkennungsprofil auswählen.
   Bei veränderter UI-Skalierung oder Auflösung die absolute Kalibrierung erneuern.

Pro `RecognitionGroup` des Katalogs ist nur eine Variante zulässig. Bei identischem
Symbol lässt sich beispielsweise der normale Preis nicht vom unsterblichen
ableiten. Zelt-Laufzeitvarianten derselben Familie benötigen ebenfalls eine
festgelegte Zuordnung, da ein laufender 300-Minuten-Buff später denselben Timer
wie ein kürzer gekaufter Buff anzeigen kann. Ein Gruppen-Harmony-Profil benötigt
zusätzlich `consumptionAttributionConfirmed: true`, entsprechend der Option
**Diesen Gruppenbuff verbrauche ich selbst** im Assistenten. Ohne diese Zuordnung
werden daraus keine eigenen Kosten oder Verbräuche abgeleitet.

`minimumSimilarity` ist optional (Standard `0.92`, erlaubter Bereich `0.85` bis
`0.999`). Ähnliche konkurrierende Vorlagen, mehrfach sichtbare Kopien desselben
Symbols und widersprüchliche Timer führen zu einer unbekannten Messung. Einen
unlesbaren Timer niemals durch einen fest eingetragenen Wert ersetzen.

Die Whitelist enthält **58 Einträge**: drei Cron-Mahlzeiten, zehn Harmony
Draughts, zwanzig Parfüme, neunzehn kostenpflichtige Zelt-/Laufzeitvarianten und
sechs Mystic-Beasts-Schriftrollen. Markt-IDs, Profil-IDs, Laufzeiten und feste
Zeltpreise sind in `BUFF_PRICE_SOURCES.md` dokumentiert. Frühere gespeicherte
Beast-/Giant-/Frenzy-Draught-Daten bleiben lesbar; neue Profile bieten diese
Gegenstände nicht mehr an.

## Position aus der Spielkonfiguration

Wenn der Index für das eigene Layout durch einen Vorher-/Nachher-Vergleich der
gespeicherten `gameVariable.xml` eindeutig verifiziert wurde, kann das Profil
zusätzlich `uiDataIndex` enthalten. Es wird ausdrücklich kein Index geraten.
In diesem Modus sind `region.x`/`region.y` Abstände vom gespeicherten Panelanker
bei der in `uiScale` angegebenen Kalibrierungsskalierung. Der Leser übernimmt
`RelativePosX`/`RelativePosY` aus dem aktiven, sichtbaren `UIData`-Eintrag und
berücksichtigt die aktuell gespeicherte Auflösung und Skalierung. Presets,
versteckte oder doppelte Panels gelten nicht als sichtbare Leiste. Speichern im
Spiel kann notwendig sein, bevor sich eine neue Position in der Datei zeigt.

Optional legt `gameVariablePath` eine konkrete Konfigurationsdatei fest. Sonst
wird die bereits für die Aufnahme ausgewählte Datei genutzt; ohne diese Auswahl
wird das zuletzt gespeicherte gültige numerische Kontoprofil gewählt.
`blackDesertDirectory` kann den Stammordner mit `GameOption.txt` und `UserCache`
angeben. Pfade dürfen ebenfalls relativ zur Profil-JSON sein. Eine konkrete Datei
hat Vorrang vor einer automatisch gefundenen anderen Konto-Konfiguration.

Der Screenshot-Assistent erzeugt ein Profil mit absoluter Position. Ein geladenes
Profil mit `uiDataIndex` erfordert im Assistenten eine neue Markierung der Leiste
und wird als neues absolutes Profil gespeichert. Für die Nachführung aus der
Konfiguration bleibt das manuell verifizierte `uiDataIndex`-Profil nutzbar.

## Umfang und Aussagekraft

Nur explizit zugeordnete Symbole werden verfolgt. Skill-, Klassen-, Möbel-,
Event- oder kostenlose Buffs werden nicht anhand ähnlicher Effekte automatisch
einem Marktgegenstand zugeordnet. Wenn eine kostenlose/eventbezogene Variante
dieselbe sichtbare Grafik verwendet, kann ein Screenshot deren Herkunft nicht
unterscheiden: Das Profil muss zum tatsächlich eingesetzten Gegenstand passen.
Die Marktwerte bilden einen geschätzten Gegenwert, keinen belegten Kaufpreis.

Die Kosten verwenden den vollen Zentralmarktpreis der ausgewählten Marktregion,
ohne Verkaufssteuerabzug. Zeltbuffs verwenden den festen NPC-Kaufpreis der
gewählten Stufe/Laufzeit und werden nicht am Zentralmarkt abgefragt. Das
Verbrauchsprotokoll unterscheidet Zentralmarkt und NPC-Festpreis. Fehlende Preise bleiben unbekannt. Bereits bekannte
Preise behalten ihren Preiszeitpunkt und Cache-Status. Die Liste erfasst nur
beobachtete und bestätigte Verbräuche; sie ist keine vollständige Inventarhistorie.

Die Anzeige trennt zwei Werte, die nicht addiert werden:

- **Beobachtete Laufzeit:** Gegenstandspreis × bestätigte Beobachtungszeit / volle
  Wirkungsdauer. Das berücksichtigt auch einen beim Sessionstart bereits aktiven
  Buff. Pausen, fehlendes Bildsignal und unlesbare Abschnitte werden ausgelassen.
- **Konsumierte Gegenstände:** Ein voller Gegenstands- bzw. NPC-Preis pro erkanntem und
  erneut bestätigtem Verbrauch. Zeitpunkt, Marktregion, Preis und Cache-Status
  bleiben im Verbrauchsprotokoll gespeichert.

Ein erst später verfügbarer Marktpreis bewertet folgende Beobachtungsintervalle.
Frühere unbekannte Kosten bleiben als Teilbetrag gekennzeichnet. Das erneute
Laden einer pausierten Session erhält die gebuchten Werte, übernimmt aber keine
alten aktiven Timer und berechnet keine Zeit während der Programmpause.
Die Buff-Werte stehen separat neben der Loot-Bewertung und ändern weder deren
Steuerberechnung noch den Garmoth-Upload. Empfangene Gruppenbuffs erlauben keinen
Nachweis darüber, welches Gruppenmitglied den Gegenstand konsumiert hat. Die
Bestätigung im Profil ist deshalb eine Annahme über die eigene Nutzung, kein
visueller Nachweis. Bei fremden Gruppenbuffs diese Vorlage nicht aktivieren.

Die automatisierten Prüfungen decken Profilvalidierung, aktive Konfigurationswahl,
Timerformate, synthetische Bildvorlagen, Mehrdeutigkeiten und Monitor-Lebenszyklen
ab; zusätzlich werden Whitelist, NPC-Preise, Quellenzuordnung und das Fortbestehen
historischer Buchungen geprüft. Eine verlässliche Erkennungsquote für echte Buff-Leisten ist erst mit
repräsentativen Screenshots und einem Windows-OCR-Lauf messbar.
