# Grind-Bewertung

Die Live-Session und das auswählbare Overlay-Modul **Grind-Bewertung** verwenden
dieselbe Berechnung: gezählter Trash des erkannten Spots geteilt durch die gesamte
aktive Sessiondauer. Pausen zählen nicht zur Dauer. Änderungen an Lootmengen und
Spot werden sofort übernommen. Das Overlay erzeugt kein eigenes Zeitfenster.

Ab einer vorhandenen Schwelle gilt deren Stufe einschließlich Gleichheit:
**Unter Average**, **Average Tier**, **High Tier**, **Top Tier**. In den ersten
fünf Minuten steht ein vorläufiger Hinweis; der Stundenwert bleibt unverändert.
Ohne bekannten Spot, positive Dauer oder gültige Referenz erscheint keine Bewertung.
Bei Average-only-Spots werden keine fehlenden High-/Top-Schwellen erfunden.

Die Live-Session und das Overlay ergänzen die Stufe durch eine kontinuierliche
Skala mit einem Marker für die eigene Leistung. Vorhandene Referenzwerte sind
gleichmäßig angeordnet; dazwischen wird anhand der tatsächlichen Trashrate
linear interpoliert. Die Prozentangabe beschreibt den Weg von der erreichten
zur nächsten Referenz, keinen Spieler-Perzentilrang. Zusätzlich wird der noch
fehlende Trash pro Stunde angezeigt. Unter Average beginnt die Skala bei null;
oberhalb der höchsten Referenz läuft sie um ein weiteres Referenzintervall aus.
Ist nur Average vorhanden, zeigt die rechte Balkenhälfte den Bereich bis
**50 % über Average** statt bis 100 %. So bleibt ein üblicher Abstand zum
Durchschnitt sichtbar; das rechte Ende ist mit +50 % beschriftet.
Der Marker bleibt am Skalenende, während Text und Stundenwert auch darüber hinaus
die tatsächliche Leistung zeigen. Gleiche Schwellen teilen eine Markierung.

## Mitgelieferte Referenzen

Einheit: Trash pro Stunde, **Loot-Scroll Lv.2, ohne Agris und ohne Agris-Münze**.
Der Stand stammt aus dem erfolgreichen anonymen Live-Abruf vom 12.09.2026.
Magaia stimmt außerdem mit dem vollständigen
[Nutzer-Screenshot vom 12.09.2026](reference-evidence/magaia-average-2026-09-12.png)
überein: **12.803 Average, 14.000 High und 15.200 Top**. Er zeigt Average
Tier, 1.613 Gesamtstunden, Loot-Scroll Lv.2 und den Zeitraum **10.–17.09.2026**.
Dieser Zeitraum steht auch im Quellenlink; der Stand der Übernahme ist 12.09.2026.
Es werden die sichtbaren Garmoth-Werte verwendet.

| Spot und Quelle | Average | High | Top |
|---|---:|---:|---:|
| [Aphrodon Temple](https://garmoth.com/grind-tracker/best-grind-spots/213?startDate=2026-09-10&endDate=2026-09-17) | 12.472 | 13.500 | 15.400 |
| [Hermesia Castle](https://garmoth.com/grind-tracker/best-grind-spots/214?startDate=2026-08-06&endDate=2026-09-17) | 12.893 | 16.000 | 18.000 |
| [Magaia Temple](https://garmoth.com/grind-tracker/best-grind-spots/215?startDate=2026-09-10&endDate=2026-09-17) | 12.803 | 14.000 | 15.200 |
| [Aresion Temple](https://garmoth.com/grind-tracker/best-grind-spots/216?startDate=2026-09-10&endDate=2026-09-17) | 13.323 | 14.000 | 15.500 |
| [Scales of Judgment](https://garmoth.com/grind-tracker/best-grind-spots/217?startDate=2026-09-10&endDate=2026-09-17) | 15.243 | — | — |
| [Event Horizon](https://garmoth.com/grind-tracker/best-grind-spots/218?startDate=2026-09-03&endDate=2026-09-17) | 12.590 | — | — |

Garmoth bildet Average aus eingereichten Sessions. High und Top sind von
Moderatoren gepflegte Leistungsreferenzen, keine statistischen Perzentile.
Die Zeiträume der jeweiligen Vergleichswerte stehen in den Quellenlinks.
Weitere Erläuterungen enthält die FAQ auf
[Garmoth Best Grind Spots](https://garmoth.com/grind-tracker/best-grind-spots).

Beim erfolgreichen Tracking-Start (auch vor dem ersten Drop) und bei jeder
weiteren vollen aktiven Stunde fragt die App aktuelle Garmoth-Referenzen an.
Pausen und die Wartezeit bis zum ersten neuen Drop zählen nicht mit;
Fortsetzen innerhalb derselben aktiven Stunde löst keinen erneuten Abruf aus.
Der Abruf läuft im Hintergrund und blockiert weder den Start noch die
Loot-Erkennung. Ein Garmoth-API-Key ist dafür nicht erforderlich.

Während des Abrufs und bei fehlender Verbindung bleiben zuletzt gespeicherte
Referenzen nutzbar. Ohne gespeicherte Daten dient die obige, mitgelieferte
Referenz für Inner Edania als Rückfall; weitere unterstützte Spots verwenden den
anonym abgerufenen Stand vom 02.10.2026. Das ursprüngliche Referenzdatum bleibt erhalten;
ein fehlgeschlagener Abruf macht alte Werte nicht zu aktuellen Werten.
Der Tooltip zeigt den Aktualisierungsstatus zusammen mit Quelle und Stand.

Die App lädt die öffentliche Garmoth-Übersicht automatisch in einer separaten,
unsichtbaren InPrivate-WebView2-Instanz und liest ausschließlich die von der
Seite selbst geladenen JSON-Antworten von
`https://api.garmoth.com/api/grind-tracker/collective/all` und
`https://garmoth.com/api/trpc/grindMeta.list` (einschließlich tRPC-Batches).
Es gibt keine Anmeldung und keine Verbindung zum Browserprofil des Nutzers oder
zur lokalen Blazor-Oberfläche. Andere Netzwerkhosts als Garmoth werden blockiert;
Popups, Downloads, Berechtigungen und Host-Bridges sind ausgeschaltet. Die
Daten-WebView wird nach beiden Antworten oder spätestens nach 45 Sekunden
geschlossen. Ein Abbruch beendet den Abruf auch beim Herunterfahren der App.
Average wird aus der
Trash-Stundenrate und Garmoths spotabhängigem Lv.2-Multiplikator berechnet;
High und Top kommen aus den Moderatorenreferenzen. Beide Antworten müssen
gültig sein, bevor ein Spot einen neuen Stand erhält. Fehlende High-/Top-Werte
bleiben leer. Garmoth begrenzt vorhandene höhere Tiers mindestens auf Average;
bei identischen Schwellen gilt die höchste verfügbare Stufe.

`start_date` und `end_date` bezeichnen die Grenzen des ausgewählten
Statistikzeitraums. Eine bereits begonnene Woche darf deshalb in der Zukunft
enden. Der frühere Import verwarf solche Antworten; er akzeptiert jetzt
laufende Wochen und bewahrt deren vollständigen Zeitraum im Quellenlink.
Umgekehrte Zeiträume oder vollständig in der Zukunft liegende Fenster werden
weiterhin verworfen. Das Abrufdatum bleibt davon getrennt.

Erfolgreiche Spotwerte werden atomar unter `garmoth-benchmarks-v1.json` im
App-Datenordner gespeichert. Fehlende oder ungültige Spots behalten ihren
bisherigen Stand. Neue Quellenlinks übernehmen den von Garmoth gelieferten
Zeitraum. Der angezeigte Stand ist bei abgerufenen Daten der Abrufzeitpunkt;
der Zeitraum der zugrunde liegenden Sessions steht im Quellenlink.

Die aktuellen Endpunkte und Berechnungen wurden anhand des öffentlichen
[Statistik-Clients](https://assets.garmoth.com/_static/_nuxt/BVEzzpVX.js) und der
[Spot-Seite](https://assets.garmoth.com/_static/_nuxt/B8FR-6Rf.js) geprüft.
Direkte HttpClient-Anfragen lieferten am 12.09.2026 weiterhin HTTP 403. Der
normale öffentliche Ladeweg funktioniert dagegen: Live-Tests der tatsächlichen
Reader-Implementierung um **17:46 und 17:50 MESZ** aktualisierten alle sechs Spots
in frischen, unsichtbaren InPrivate-Profilen ohne Anmeldung oder Cookie-Interaktion.
Geprüft wurden zusätzlich der Abbruch während des Kaltstarts, der erneute Abruf
aus einem Hintergrundthread, das Entfernen der Daten-WebView und die unveränderte
Weiterverwendung des Caches nach einem simulierten Verbindungsfehler.

Die [Testdaten aus echten Antworten](../tests/fixtures/garmoth/SOURCES.md)
ergänzen die synthetischen Fehler- und Grenzfalltests. Die gemeldete Session
(4.536 Trash in 20:44 Minuten, etwa 13.119/h) wird als **Average Tier** bewertet.
Der Browserweg löst keine Captchas und umgeht keine Zugriffssperren. Wenn auch
dieser Ladeweg fehlschlägt, bleibt der letzte Stand mit einem Fehlerstatus nutzbar.

## Vergleichbarkeit

Verglichen wird der tatsächliche Trash-pro-Stunde-Wert, ohne heimliche
Buff-Normalisierung. Das Rating zeigt die ungefähr gemessene Agris-Dauer der
Session, etwa „≈ 25 Min. Agris“. Bei gerade aktivem Agris ohne gemessene Dauer
steht „Agris aktiv“; Beobachtungslücken werden im Tooltip genannt. Der
Loot-Scroll-Status erhält keinen eigenen Hinweis im Rating. Die Erkennung kann
nicht jede bisherige Buff-Änderung der Session belegen; eine Agris-Münze wird
nicht erkannt. Die Referenzbedingungen bleiben daher Teil des Vergleichs.
Garmoth verwendet teilweise spotabhängige Drop-Multiplikatoren; eine pauschale
Umrechnung wäre nicht für alle Spots gleichwertig.

Live- und Editor-Tooltips zeigen Bedingungen, Schwellen, Quelle und Datum. Das
native Overlay zeigt denselben Wert, dieselbe Farbe und kurze Zustands-Hinweise.
Die Bewertung greift nicht in Tracking, Lootzählung oder Uploads ein.

## Seltene Drops und erwarteter Durchschnitt

Seltene Drops mit einer passenden öffentlichen Garmoth-Stundenrate zeigen den
erwarteten Durchschnitt für die aktive Sessiondauer sowie den Abstand dazu.
Der Vergleich gleicht außerdem dein tatsächliches Trash pro Stunde mit dem
Garmoth-Trash-Durchschnitt aus derselben Referenz wie die Rare-Drop-Raten ab.
Pausen zählen nicht zur Grindzeit. Die Droprate ist in der Live-Session sichtbar
und editierbar; neue Sessions verwenden standardmäßig **320 %**. Vergleichswerte
werden mit voller Präzision berechnet und mit zwei Nachkommastellen dargestellt.
Ein fehlender oder nicht eindeutig zum Spot passender Wert bleibt ohne Vergleich.

Wie in BDO und Garmoth bezeichnet die Eingabe den **Dropratenbonus** zusätzlich
zur Grundrate von 100 %. Der öffentliche, kostenlose Garmoth-Regler steht auf
100 % Bonus. Die rohe API-Stundenrate wird dort deshalb bereits mit 2 multipliziert.
Für 320 % Bonus lautet die Rechnung entsprechend:

`erwartete Menge = öffentliche Garmoth-Rate bei 100 % × (100 + 320) / 200 × aktive Stunden × (eigenes Trash/h / Garmoth-Trash/h)`

Beispiel: Garmoth zeigt bei 100 % Bonus ungerundet 2,00 Drops/h. Bei 320 % Bonus
sind das bei gleicher Trashleistung 4,20 Drops/h beziehungsweise durchschnittlich
2,10 Drops in 30 aktiven Minuten. Erreichst du statt 10.000 Referenz-Trash/h
selbst 12.000 Trash/h, steigt die Erwartung um 20 % auf 5,04 Drops/h beziehungsweise
2,52 Drops in 30 Minuten. **Über**, **unter** oder **im Durchschnitt** vergleicht die tatsächliche
Dropmenge mit dieser ungerundeten Erwartung. Das ist ein beobachteter
Community-Durchschnitt, keine Garantie für den einzelnen Grind.

Die Rechnung nutzt den gesamten gezählten Trash des aktuellen Spots. Suche und
Umschalten auf die Stundenanzeige verändern die Grundlage nicht. Änderungen an
Trashmengen, Dropratenbonus oder Referenz aktualisieren den Vergleich sofort.
Solange kein positiver Trash erfasst wurde, erscheint noch keine Erwartung.
Bei unveränderter Trashmenge steigt die erwartete Dropanzahl auch durch eine
längere aktive Dauer nicht: Die Rechnung entspricht der angepassten Garmoth-Rate
multipliziert mit eigenem Gesamt-Trash geteilt durch Referenz-Trash/h.
Der Tooltip zeigt beide Trash-Stundenwerte, ihren Faktor und die angepasste Rate.
Die Trash-Referenz verwendet Loot-Scroll Lv.2 ohne Agris; no-scroll-Spots behalten
ihre eigenen Bedingungen. Der tatsächliche Session-Trash erhält keine zusätzliche
Agris- oder Lootmengen-Normalisierung.

Garmoth-Spots mit `no-scroll` erhalten keinen Dropratenmultiplikator. Für den
derzeit unterstützten **Winter Tree Fossil (280)** bleiben sowohl seltene Drops
als auch Trash bei der öffentlichen Roh-Stundenrate. Die Referenz nennt hier
„Keine Loot-Scroll-Boni · ohne Agris“. Dehkia's Light wird als verbrauchtes Item
von Garmoth ebenfalls ausgenommen und gehört nicht zum lokalen Rare-Kanal.

Die mitgelieferten seltenen Drop-Raten stammen aus dem erfolgreichen anonymen
Live-Abruf vom **02.10.2026, 12:37 MESZ**, für alle 40 unterstützten Garmoth-Spots.
Die [Quellen und reduzierten Originaldaten](../tests/fixtures/garmoth/SOURCES.md)
belegen den tatsächlichen API-Vertrag und die Bedingungen. Garmoth-`item_key`
bestimmt das kanonische Item; übersetzte API-Namen werden nicht zur Zuordnung
verwendet. Nur lokal bekannte seltene Items, die zu diesem Spot gehören, erhalten
eine Vergleichsrate.

Seltene Drops tragen einen dazugehörigen Trash-Durchschnitt, ein eigenes
Referenzdatum und einen eigenen Quellenlink,
wenn ihr Stand vom Trash-Referenzstand abweicht. Die ursprünglichen sechs
Inner-Edania-Trashwerte bleiben dadurch unverändert datiert. Ein fehlgeschlagener
Abruf oder eine neue gültige Trashantwort ohne seltene Drop-Raten bewahrt die
letzte verfügbare Rare-Referenz einschließlich ihres zugehörigen Trash-Durchschnitts
mit ihrem ursprünglichen Datum und Zeitraum. Ältere Cache-Raten ohne diesen
zugehörigen Trashwert werden durch eine vollständige mitgelieferte Rare-Referenz
ersetzt; gültige Trashwerte für die Grind-Bewertung bleiben erhalten.
Cachewerte werden auf Spot, Itemkanal, nichtnegative Rate, positiven
Referenz-Trashwert, Quelle und Datum geprüft.
