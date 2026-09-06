# Optionaler Garmoth-Upload

## Umfang und Datensicherheit

Die Integration sendet die Sitzung ausschließlich nach Klick auf **Garmoth-Upload**.
Eine laufende Aufnahme wird dabei pausiert und abgeschlossen; kein weiterer Dialog
oder Bestätigungsschritt ist nötig. Die App meldet sich nicht selbstständig an und
liest weder Browser-Cookies noch gespeicherte Companion-API-Keys. Der eigene Key
wird einmal unter **Optionen → Garmoth-Key** hinterlegt und mit Windows-DPAPI für
CurrentUser plus App-spezifischer Entropie in `garmoth-api-key.dpapi` gespeichert.
Beim Upload steht er nur im HTTPS-Request-Header `apiKey`, niemals in URL, Payload,
Klartext-Einstellungen oder Diagnoseausgaben. Entfernen im Einstellungsdialog löscht
die verschlüsselte Datei erst nach Speichern; Abbrechen verändert nichts.

Die HTTP-Komponente hat keine automatischen Wiederholungsversuche, Cookies oder
Weiterleitungen. Ein 30-Sekunden-Limit umfasst auch das Lesen des Antwortkörpers;
JSON-Antworten sind auf 64 KiB begrenzt. Fehlermeldungen enthalten weder den
Serverantworttext noch Exception-Nachrichten, die Zugangsdaten enthalten könnten.

Nach erfolgreichem oder unklarem Upload wird dieselbe lokale Sitzungs-ID nicht
erneut gesendet. Bei Zeitüberschreitung, Abbruch nach Versand, Weiterleitung,
Serverfehler oder mehrdeutiger Antwort muss der Nutzer zuerst in Garmoth prüfen:
Der Server könnte die Sitzung bereits gespeichert haben. Eine ausdrücklich
abgelehnte Anfrage lässt einen neuen manuellen Versuch zu. Dieser Schutz ist
sitzungs-/prozesslokal; es wird kein serverseitiger Idempotency-Key erfunden.

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

Der Tracker übernimmt den berechneten Netto-Silberwert aus der
[Companion-basierten Bewertung](SILVER_VALUATION.md) automatisch. Die Serializer-
Zugriffe `0x1401158BF`/`0x1401158E6` bestätigen `+0xE0=pre_tax`, `+0xE8=post_tax`.
Klasse/Spec, Spot, Loot und Dauer stammen aus der vorhandenen Sitzung. Ein noch
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

Diese IDs sind zusätzlich durch Garmoths öffentliche Seiten für
[Aphrodon](https://garmoth.com/grind-tracker/best-grind-spots/213),
[Hermesia](https://garmoth.com/grind-tracker/best-grind-spots/214) und
[Magaia](https://garmoth.com/grind-tracker/best-grind-spots/215) bestätigt.
Das statische Klassenmapping befindet sich außerdem in der EXE bei
`0x1414FC8D0` (31 Datensätze); der Metadatencache ergänzt Agent als ID 31.

Unbekannte oder beim Zielspot nicht unterstützte Items werden ausschließlich beim
Upload ausgelassen; ihre Namen stehen danach in der Rückmeldung. Insbesondere
haben `Pure Black Stone` und `[Event] Mysterious Ore` im untersuchten Metadatencache
keine bestätigte Garmoth-Zuordnung. Das beeinflusst **nicht** die lokale
Erkennung oder die vorhandenen Spot-Lootpools.

`Laila's Petal` hat einen bestätigten allgemeinen Item-Key `54031_0`, fehlt jedoch
in Garmoths untersuchten Spotlisten und wird daher nur beim Upload ausgelassen.
Die bestätigten Garmoth-Listen enthalten 25/27/29 Itemkeys für Aphrodon/Hermesia/Magaia.
Diese Transportlisten schränken weder lokale Erkennung noch Bewertung ein. Der
übermittelte Netto-Gesamtwert entspricht weiterhin der Dashboard-Bewertung; ein
bekannter lokaler Festwert kann deshalb enthalten sein, obwohl seine Itemmenge in
Garmoth nicht unterstützt wird. IDs werden niemals aus Namen/Iconpfaden erraten.
Sind keine unterstützten Items übrig, wird keine leere Sitzung gesendet.

## Nachweisgrenzen

Der Companion-Entwickler bestätigt die Zusammenarbeit mit Garmoth und den
[manuellen Klick-Upload](https://chanchan.dev/work/bdo-companion).
Die Implementierung folgt dem statisch belegten externen Vertrag, nicht einer
öffentlich vollständig dokumentierten API-Spezifikation.

Geprüft werden lokale Payloads und HTTP-Verhalten ausschließlich mit synthetischen
Mock-Antworten: richtige IDs/Feldtypen, explizites Auslassen nicht unterstützter Items,
Minuten-/Silberarithmetik, keine Zugangsdaten in Fehlern, Abbruch/Zeitlimit,
begrenzte Antworten und Doppelupload-Schutz. **Es wurde keine reale Sitzung
hochgeladen und kein fremdes Konto aufgerufen.** Die Annahme eines echten
API-Keys und einer echten Sitzung durch den aktuellen Garmoth-Server bleibt ein
vom Nutzer auszulösender Integrationstest. Öffentliche HTTP-Metadatenanfragen
wurden in dieser Umgebung mit 403 abgelehnt; es wurde keine Sperre umgangen.
