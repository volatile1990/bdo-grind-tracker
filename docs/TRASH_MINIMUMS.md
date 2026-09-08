# Mindestmengen für Trashloot

**Weiterentwicklung:** Der [allgemeine Dropmengenkatalog](DROP_QUANTITIES.md)
erfasst jetzt alle Items je Spot. Die [neue vollständige Recherche](DROP_QUANTITIES_RESEARCH.md)
ergänzt unter anderem die Event-Horizon-Teilwerte vom 3. September. Alle aktuellen
Min-/Max-Grenzen sind inzwischen aus der ausgefüllten Nutzerdatei übernommen:
Aphrodon 4/1000, Hermesia 4/1000, Magaia 7/1000, Aresion 2/1000,
Scales of Judgment 2/2000 und Event Horizon 2/1000. Diese Werte werden über
`DropQuantityCatalog` pro Beobachtung verwendet. Der folgende historische Abschnitt
dokumentiert den bisherigen Trash-Fallback und das weiterhin unterstützte v4-Replay.

Recherche am **7. September 2026**, PC-Version, sechs hinterlegte Inner-Edania-Spots.
Für keinen dieser Spots ließ sich eine Mindestmenge aller normalen Gegner belegen.
Deshalb enthält `TrashLootMinimumCatalog` sechs Einträge mit `null`; die aktive
Minimumtabelle ist leer. **Es werden derzeit keine erhöhten Mindestmengen eingesetzt.**
Ohne belegten Eintrag bleibt der bisherige letzte Mengenersatz von 1 erhalten.

## Quellen und Ergebnis

Gesucht ist die kleinste positive Grundmenge eines einzelnen Drops über die
normalen Gegner des Spots, vor Loot-Scroll, Agris und anderen Mengenboni.
Silber pro Stunde, Verkaufspreis und Agris-Verbrauch sind keine Dropmengen.

| Spot | Trash-Item | Item-ID / öffentlicher Datensatz | Belegtes Minimum |
| --- | --- | --- | --- |
| Aphrodon Temple | Branch of Abundance | [980127](https://bdolytics.com/en/db/item/980127) | unbekannt |
| Hermesia Inner Castle | Black Crystal Fragment | [980128](https://bdolytics.com/en/db/item/980128) | unbekannt |
| Magaia Temple | Elion Follower's Helmet | [980129](https://bdolytics.com/en/db/item/980129) | unbekannt |
| Aresion Temple | Scorched Belt Ornament | [980131](https://bdolytics.com/en/db/item/980131) | unbekannt |
| Scales of Judgment | Elion Follower's Mark | [980130](https://bdolytics.com/en/db/item/980130) | unbekannt |
| Event Horizon | Broken Gloves of the Void | [980132](https://bdolytics.com/en/db/item/980132) | unbekannt |

Die öffentlichen BDOlytics-Itemdatensätze enthalten die Spotzuordnung, aber keine
Gegner-Droptabelle und keine Mengenfelder. Der lokale Recherchebeleg liegt in
`artifacts/trash-minimum-research/bdolytics-six-item-source-audit.json`; er enthält
URLs, Abrufzeiten, verfügbare Feldnamen und Antwort-Hashes für alle sechs Items.

Die [offizielle PC-Einführung vom 13. August](https://www.jp.playblackdesert.com/ja-JP/News/Detail?countryType=ja-jp&groupContentNo=12423)
beschreibt Gegner, Loot, Verkaufspreise und Agris-Verbrauch. Eine Mindestmenge
gewöhnlicher Trashdrops ist darin nicht ausgewiesen.

Die [offiziellen PC-Patchnotes vom 20. August](https://blackdesert.pearlabyss.com/Asia/en-us/News/Notice/Detail?_boardNo=19716)
nennen konkrete Mengenänderungen für bestimmte Gegner: etwa 30–40 Helme beim
Magaia-Priester und 10–15 Gürtelornamente bei den ausdrücklich mit `[Reforged]`
bezeichneten Aresion-Gegnern. Diese Angaben decken die normalen Gegner nicht ab
und sind deshalb keine Mindestmengen für den ganzen Spot.

Auch der geprüfte [BDO-Codex-Eintrag des Enslaved Miner](https://bdocodex.com/us/npc/28697/)
liefert keine entsprechende Droptabelle. Aus dem bereitgestellten Magaia-Chatbild
mit Mengen 4 und 6 lässt sich wegen unbekannter Mengenboni und der kurzen Stichprobe
ebenfalls kein Mindestwert ableiten.

## Vorbereiteter Fallback

Ein später belegter positiver Wert kann im Eintrag des jeweiligen Trash-Items
hinterlegt werden. Dafür müssen Quelle, Version, Gegnerumfang und Mengenboni hier
nachvollziehbar ergänzt werden. Keine automatische Übernahme von Elite-Werten,
keine Ableitung aus Stundenstatistiken und kein Raten fehlender Werte.

Die App übergibt ausschließlich belegte Einträge an den normalen Frame-Zähler.
Bereits gelesene Mengen einschließlich des Item-Chat-Fallbacks haben Vorrang;
sie werden auch dann nicht angehoben, wenn sie unter dem hinterlegten Minimum liegen.
Für konfigurierte Items werden zunächst tatsächliche Mengen aus passenden
Nachbarbildern im noch offenen Batch übernommen. Erst wenn der bestehende Zähler
eine fehlende Menge auf 1 schätzen würde, ersetzt das Minimum die ausgegebene Menge.
Die interne Vergleichsmenge für die Erkennung gleicher Zeilen bleibt erhalten.
Es entstehen dadurch keine zusätzlichen Lootereignisse allein aus der Tabelle.
Einzelne ungelöste Zeilen, für die bisher kein Ereignis entstand, werden weiterhin
nicht allein aufgrund eines Minimums gebucht.

Neue Diagnosen verwenden die Enginekennung `companion-0.7.4-minimum-quantity-v4`
und speichern eine unveränderliche Kopie der aktiven Tabelle im Header unter
`minimumTrashQuantities`. Geschätzte Buchungen tragen den Diagnosegrund
`companion-minimum-quantity-estimate`. Das Replay verwendet nur die aufgenommene
Tabelle; ältere Aufnahmen ohne Tabelle behalten ihren bisherigen Mengenersatz.
Tests verwenden ausdrücklich synthetische Mindestmengen und belegen keine
realen Dropmengen eines Spots.

Prüfung dieses Stands: 103 Core-, 218 OCR- und 775 App-Tests bestanden.
Der ältere v3-Mitschnitt mit 611 Frames wird mit identischen Ereignissen je Frame
und unveränderten Summen wiedergegeben (313 Helme, 2 Ancient Spirit Dust,
1 Fusion Shard). Das belegt die Kompatibilität des Replays, keine Übereinstimmung
mit dem tatsächlichen Inventarloot.
