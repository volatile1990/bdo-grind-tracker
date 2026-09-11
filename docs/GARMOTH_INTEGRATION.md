# Optionaler Garmoth-Upload

## Umfang und Datensicherheit

Der eigene Menüpunkt **Garmoth** bündelt Verbindung, Stundenautomatik und sämtliche
Upload-Aktionen. Die aktuelle Session steht genau einmal in derselben Liste wie
gespeicherte Sessions; solange sie nicht abgeschlossen ist, trägt sie einen kleinen
Live-Hinweis. Für sie gilt die Vorschau des noch offenen Rests, auch wenn im Verlauf
bereits eine automatisch übertragene Stunde vermerkt ist.

**Alles hochladen** bereitet alle uploadfähigen Sessions unabhängig von Suchfilter
und Listenseite vor. Die Sicherheitsabfrage zeigt die Sessions mit Dauer und
Silberbetrag sowie die Zahl ausgelassener Einträge. Eine enthaltene laufende Session
wird vor der Vorschau sicher pausiert. Erst die Bestätigung startet den Versand.
Die bestätigte Liste wird eingefroren, vor Beginn und vor jedem einzelnen Upload
erneut geprüft und nacheinander übertragen. Beim ersten Fehler stoppt der Vorgang;
erfolgreiche Uploads bleiben bestehen, ein unklares Ergebnis wird nicht wiederholt.
Abbrechen verwirft die Bestätigung und sendet nichts; die Session bleibt pausiert.

Die Integration sendet nach Bestätigung mit **Jetzt hochladen** beziehungsweise
**N Sessions hochladen** oder
mit ausdrücklich aktivierter Stundenautomatik. Beim Öffnen der manuellen Vorschau wird die laufende
Aufnahme pausiert und ausstehender Loot abgeschlossen. Die Bestätigung zeigt den
tatsächlichen Rest mit Dauer, Mengen, ausgelassenen Items und dem eingefrorenen
Silberbetrag. Ändern sich Klasse, Dauer oder Mengen danach, ist eine neue Vorschau
erforderlich. Automatische Uploads lassen das Tracking weiterlaufen.
Die App meldet sich nicht selbstständig an und
liest weder Browser-Cookies noch gespeicherte Companion-API-Keys. Der eigene Key
wird im Bereich **Garmoth** hinterlegt und mit Windows-DPAPI für
CurrentUser plus App-spezifischer Entropie in `garmoth-api-key.dpapi` gespeichert.
Beim Upload steht er nur im HTTPS-Request-Header `apiKey`, niemals in URL, Payload,
Klartext-Einstellungen oder Diagnoseausgaben. Entfernen im Garmoth-Bereich löscht
die verschlüsselte Datei und deaktiviert automatische Uploads.
Die Option **Stündlich automatisch hochladen**
ist standardmäßig aus und wird getrennt vom Key in den normalen Einstellungen
gespeichert. Sie benötigt einen gültigen, nicht leeren Key.

Die HTTP-Komponente hat keine automatischen Wiederholungsversuche, Cookies oder
Weiterleitungen. Ein 30-Sekunden-Limit umfasst auch das Lesen des Antwortkörpers;
JSON-Antworten sind auf 64 KiB begrenzt. Fehlermeldungen enthalten weder den
Serverantworttext noch Exception-Nachrichten, die Zugangsdaten enthalten könnten.

## Stundenabschnitte und Doppelupload-Schutz

Ab Sitzungsbeginn werden die Lootstände an jeder vollen aktiven Stunde festgehalten,
auch wenn die Automatik ausgeschaltet ist. Pausen zählen nicht mit; eine noch durch
Auto-Pause abziehbare abschließende Leerlaufphase wird nicht vorzeitig als volle
Stunde gesendet. Aktivieren während einer bestehenden Sitzung überträgt bereits
vollständige, ungesendete Stunden nacheinander, jeweils als eigenen 60-Minuten-Eintrag.
Eine angefangene Reststunde wird erst mit der nächsten vollen Stunde oder bei einem
manuellen Upload gesendet. Neue Sitzung und Programmende lösen keinen Upload aus.

Jeder Abschnitt erhält eine eigene ID sowie die ID der übergeordneten lokalen
Sitzung in seiner Notiz. Erfolgreich gesendete Zeit und Mengen werden für weitere
Uploads abgezogen. Der manuelle Upload sendet die gesamte verbleibende Zeit und
Beute in einem Eintrag. Spätere negative Mengenkorrekturen bleiben als Verrechnung
gegen zukünftige Mengen erhalten: Ein Zähler, der unter den bereits gesendeten
Stand fällt und sich erholt, erzeugt dadurch keine erneute Buchung derselben Beute.
Bereits gespeicherte Garmoth-Einträge werden nicht nachträglich korrigiert.

Manuelle Inline-Korrekturen verwenden denselben lokalen Mengenzähler und wirken
auf zukünftige Deltas. Bereits eingefrorene Stundenstände und laufende HTTP-Anfragen
bleiben unverändert; bereits gesendete Mengen werden nie herabgesetzt. Auch während
eines Uploads kann lokal korrigiert werden. Die lokale Speicherung erfolgt vor dem
Commit der Korrektur, ohne künstliche Drop- oder Aktivitätsereignisse. Eine explizite
Nullmenge bleibt im Verlauf erhalten, einschließlich der bisherigen Uploadsperre
und des Übertragungszeitpunkts. Historische Korrekturen ändern ausschließlich den
jeweiligen gespeicherten Eintrag und niemals den Zähler der aktuellen Sitzung.

Der Verlauf speichert die vollständigen lokalen Sitzungssummen. Sobald mindestens
ein Abschnitt erfolgreich übertragen wurde oder sein Ergebnis unklar ist, wird
deshalb der Gesamt-Upload dieses Verlaufseintrags gesperrt. Diese Sperre bleibt
auch nach einer neuen Sitzung oder einem Programmneustart erhalten. Einen noch
nicht übertragenen Rest vor **Neue Sitzung** über **Garmoth → Session-Anteil hochladen**
senden; dieser berücksichtigt weiterhin ausschließlich das Delta. Die vollständigen
Sessionwerte dienen dort zur Orientierung. Die Liste **Gespeicherte Sessions** enthält
nur frühere Sessions; deren Uploads und Übertragungsvermerke werden ebenfalls dort verwaltet.

Ein erfolgreicher automatischer Upload sperrt die lokale Sitzung nicht. Nach einem
unklaren automatischen Ergebnis sind dagegen sämtliche weiteren Uploads dieser
Sitzung gesperrt, einschließlich manuellem Upload und erneutem Aktivieren der Option;
das lokale Tracking bleibt nutzbar. Bei Zeitüberschreitung, Abbruch nach Versand,
Weiterleitung, Serverfehler oder mehrdeutiger Antwort muss der Nutzer zuerst in
Garmoth prüfen: Der Server könnte den Abschnitt bereits gespeichert haben.

Eine eindeutig abgelehnte Anfrage oder fehlende lokale Voraussetzung setzt die
automatischen Versuche aus. Nach Beheben der Ursache kann der Nutzer die
Garmoth-Verbindung durch einen korrigierten Schlüssel aktualisieren, die Automatik
erneut einschalten oder **Automatik fortsetzen** wählen. Diese Einstellungen werden
automatisch gespeichert. Änderungen unter **Einstellungen** reaktivieren die
Versuche nicht. Die Freigabe betrifft nur eindeutig fehlgeschlagene Versuche,
niemals ein unklares Ergebnis.
Ein erfolgreicher oder unklarer manueller Upload sperrt wie bisher weitere Uploads
und das Fortsetzen dieser Sitzung; eine ausdrücklich abgelehnte Anfrage erlaubt
einen neuen manuellen Versuch.

Vor jedem HTTP-Versand wird die Uploadabsicht in `garmoth-upload-journal-v1.json`
atomar geschrieben und auf den Datenträger geflusht. Der Eintrag enthält eine
Versuchs-ID, Quellsession, eingefrorene Abschnittsmengen, Dauer, Klasse, Spot und
Silber; keine Zugangsdaten oder Serverantworten. Ohne erfolgreiche Speicherung
wird kein HTTP-Aufruf ausgeführt. Danach wird das Ergebnis atomar ergänzt.
Schlägt dies fehl, bleibt die offene Absicht erhalten und weitere Uploads werden
gesperrt. Ein nicht lesbares Journal wird nie als leer behandelt.

Beim Neustart sperren erfolgreiche, unklare und unvollständige Versuche den
Gesamt-Upload der jeweiligen Session, auch wenn ihr Verlaufsvermerk vorher nicht
gespeichert werden konnte. Eindeutige Ablehnungen erlauben einen neuen Versuch.
Die während des laufenden Prozesses verwendeten Stundenstände und Mengenabzüge
werden weiterhin im Speicher geführt; ein Rest nach einem vollständigen Neustart
wird konservativ nicht erneut gesendet. Die Notiz-ID ist kein serverseitiger
Idempotency-Key. Eine garantierte Einmalverarbeitung wird damit nicht behauptet;
unklare Ergebnisse werden nicht automatisch wiederholt.

Uploadbereitschaft und Versand nutzen dieselbe Payload-Prüfung für Klasse/Spec,
Spotzuordnung, mindestens eine volle **ungesendete** Minute, unterstützten Loot
und darstellbare Silberwerte. Der Verlauf auf der Garmoth-Seite zeigt die aktuelle
Bewertung für den Upload und den gespeicherten Silberwert getrennt.

## Statisch belegter Vertrag

Untersucht wurde dieselbe lokale `bdo_companion.exe` 0.7.4 wie im
[Paritätsnachweis](COMPANION_0_7_4_PARITY.md), SHA-256
`8B75E114D3D33D227A01CDFE592F13AA65133A36363EAA2E6A82E5ADFC77EFCF`.
Die Binärdatei wurde ausschließlich gelesen, niemals gestartet.

Die Funktion ab `0x1406483D0` baut einen POST an
`https://api.garmoth.com/api/external/grind-tracker/sessions/create` auf.
Die in der EXE verschleierten URL-Teile werden durch `0x1401FD840` dekodiert;
die beiden Upload-Callsites stehen bei `0x1406494A7` und `0x1406494C4`.
Die Header-Callsite `0x14064970C` verwendet den Namen `apiKey` und den vom Nutzer
konfigurierten Key. Es wird kein eingebettetes Service-Secret übernommen.

| Feld | Nachgewiesener Inhalt |
|---|---|
| `grindspot_id` | Numerische Garmoth-Spot-ID (`0x140648C4E`) |
| `minutes` | Ganze, abgeschnittene aktive Sekunden / 60 (`0x140648D2D`) |
| `total` | Gesamtwert in Silber **nach Steuer**, nicht Itemanzahl (`0x140648ED6`, Zugriff `LootTotals+0xE8`) |
| `hourly` | Abgeschnittener Silberwert / aktive Stunden (`0x140648DFA`) |
| `drops` | JSON-Objekt aus Garmoth-Itemkey und ganzzahliger Menge (`0x140648F31`) |
| `global` | `false`: keine Freigabe für Global-Auswertung (`0x140649202`) |
| `note` | Freitext (`0x140649258`), hier eigene Produkt-/Sitzungskennung |
| `class_id` | Garmoths Klassen-ID, **nicht** die Spiel-ID (`0x140649322`) |
| `spec` | `0` Awakening, `1` Succession (`0x1406493C2`) |

Die Spezialisierungswerte sind über `0x14064DC30` und die Fehlerzweige bei
`0x14064DD1E` (Succession nicht unterstützt) und `0x14064E11F` (Awakening
nicht unterstützt) zusätzlich belegt. Klassen mit nur einer Spielweise verwenden
das allein unterstützte Flag: Archer/Scholar/Wukong `0`,
Shai/Deadeye/Seraph/Agent `1`.

Der Tracker berechnet den Netto-Silberwert der jeweiligen Abschnittsmengen mit der
[Companion-basierten Bewertung](SILVER_VALUATION.md) und den aktuellen Preisen und
Steuereinstellungen. Er subtrahiert keine früheren Silber-Gesamtsummen; Preis- oder
Steueränderungen verfälschen dadurch nicht die Bewertung des neuen Loots. Die Serializer-
Zugriffe `0x1401158BF`/`0x1401158E6` bestätigen `+0xE0=pre_tax`, `+0xE8=post_tax`.
Klasse/Spec und Spot stammen aus der vorhandenen Sitzung, Loot und Dauer aus dem
jeweils ungesendeten Abschnitt. Ein noch
laufender Preisabruf wird abgewartet; nach Regionenwechsel nur die richtige Region.
Fehlende Preise ergeben eine gekennzeichnete bekannte Teilsumme, alte Preise einen
gekennzeichneten Cachewert. Ohne einen einzigen bekannten Preis wird kein erfundener
Nullwert gesendet. Die wirkliche aktive Sitzungsdauer bleibt lokal sekundengenau;
Garmoth erhält wie Companion volle Minuten. Eine Sitzung unter einer Minute wird
nicht als Null-Minuten-Sitzung gesendet. Das echte Startdatum steht in der Notiz;
es wird kein unbelegtes Datumsfeld für die API hinzugefügt.

## Datenzuordnung

Die lokale Companion-Datei
`%LOCALAPPDATA%/com.iqon-digital-llc.bdo-companion/loot_drops` enthält reine
öffentliche Tracker-Metadaten im Bincode-Format. Ihr gelesener Präfix besteht aus
1.236 Loot-Definitionen, danach 200 Grindspots (Offset 165498), danach 32 Klassen
(Offset 248976). Es wurden ausschließlich Namen, Garmoth-Keys, IDs und
Spezialisierungsflags für die Kompatibilität übernommen, keine Nutzerprofile,
Preise, Sessiondaten oder Anmeldedaten. Die ausgelieferte App benötigt oder liest
diesen Cache nicht.

Bestätigte Spotzuordnungen:

- Aphrodon: `213`, Trash `980127_0`
- Hermesia: `214`, Trash `980128_0`
- Magaia: `215`, Trash `980129_0`
- Aresion: `216`, Trash `980131_0`
- Scales of Judgment: `217`, Trash `980130_0`
- Event Horizon: `218`, Trash `980132_0`

Diese IDs sind zusätzlich durch Garmoths öffentliche Seiten für
[Aphrodon](https://garmoth.com/grind-tracker/best-grind-spots/213),
[Hermesia](https://garmoth.com/grind-tracker/best-grind-spots/214) und
[Magaia](https://garmoth.com/grind-tracker/best-grind-spots/215) sowie
[Aresion](https://garmoth.com/grind-tracker/best-grind-spots/216),
[Scales of Judgment](https://garmoth.com/grind-tracker/best-grind-spots/217) und
[Event Horizon](https://garmoth.com/grind-tracker/best-grind-spots/218) bestätigt.
Das statische Klassenmapping befindet sich außerdem in der EXE bei
`0x1414FC8D0` (31 Datensätze); der Metadatencache ergänzt Agent als ID 31.

Unbekannte oder beim Zielspot nicht unterstützte Items werden ausschließlich beim
Upload ausgelassen; ihre Namen stehen danach in der Rückmeldung. Insbesondere
haben `Pure Black Stone` und `[Event] Mysterious Ore` im untersuchten Metadatencache
keine bestätigte Garmoth-Zuordnung. Das beeinflusst **nicht** die lokale
Erkennung oder die vorhandenen Spot-Lootpools.

Seit 11.09.2026 ist `Empty Picture Frame` lokal im globalen Pool enthalten.
Für dieses Item wurde keine Garmoth-Spotzuordnung übernommen; es bleibt beim
Upload als ausgelassenes Item sichtbar, während lokale Menge und NPC-Wert
erhalten bleiben.

`Laila's Petal` hat einen bestätigten allgemeinen Item-Key `54031_0`, fehlt jedoch
in Garmoths untersuchten Spotlisten und wird daher nur beim Upload ausgelassen.
Die konfigurierten Uploadlisten enthalten 25/27/29/35/35/38 Itemkeys für
Aphrodon/Hermesia/Magaia/Aresion/Scales of Judgment/Event Horizon.
Diese Transportlisten schränken weder lokale Erkennung noch Bewertung ein. Der
übermittelte Netto-Gesamtwert verwendet dieselben Bewertungsregeln wie das Dashboard,
bezogen auf die Mengen des übertragenen Abschnitts. Ein bekannter lokaler Festwert
kann deshalb enthalten sein, obwohl seine Itemmenge in
Garmoth nicht unterstützt wird. IDs werden niemals aus Namen/Iconpfaden erraten.
Sind keine unterstützten Items übrig, wird keine leere Sitzung gesendet.

## Nachweisgrenzen

Der Companion-Entwickler bestätigt die Zusammenarbeit mit Garmoth und den
[manuellen Klick-Upload](https://chanchan.dev/work/bdo-companion).
Die Implementierung folgt dem statisch belegten externen Vertrag, nicht einer
öffentlich vollständig dokumentierten API-Spezifikation.

Geprüft werden lokale Payloads und HTTP-Verhalten ausschließlich mit synthetischen
Mock-Antworten: richtige IDs/Feldtypen, explizites Auslassen nicht unterstützter Items,
Minuten-/Silberarithmetik, Stundenabgrenzung einschließlich Pausen und Leerlaufabzug,
Mengendeltas und spätere Korrekturen, gespeicherte Opt-in-Einstellung, keine
Zugangsdaten in Fehlern, Abbruch/Zeitlimit, begrenzte Antworten und Doppelupload-Schutz.
**Es wurde keine reale Sitzung
hochgeladen und kein fremdes Konto aufgerufen.** Die Annahme eines echten
API-Keys und einer echten Sitzung durch den aktuellen Garmoth-Server bleibt ein
vom Nutzer auszulösender Integrationstest. Öffentliche HTTP-Metadatenanfragen
wurden in dieser Umgebung mit 403 abgelehnt; es wurde keine Sperre umgangen.
