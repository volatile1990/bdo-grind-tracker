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

## Verifizierte Referenzen vom 10.09.2026

Einheit: Trash pro Stunde, **Loot-Scroll Lv.2, ohne Agris und ohne Agris-Münze**.
Die Garmoth-Anzeige hatte keinen eigenen Trash-Override oder Klassenfilter.
Es werden die auf ganze Stück/h gerundeten sichtbaren Garmoth-Werte verwendet.

| Spot und Quelle | Average | High | Top |
|---|---:|---:|---:|
| [Aphrodon Temple](https://garmoth.com/grind-tracker/best-grind-spots/213?startDate=2026-08-06&endDate=2026-09-10) | 12.144 | 13.500 | 16.300 |
| [Hermesia Castle](https://garmoth.com/grind-tracker/best-grind-spots/214?startDate=2026-08-06&endDate=2026-09-10) | 12.837 | 16.000 | 18.000 |
| [Magaia Temple](https://garmoth.com/grind-tracker/best-grind-spots/215?startDate=2026-08-13&endDate=2026-09-10) | 13.946 | 16.300 | 18.500 |
| [Aresion Temple](https://garmoth.com/grind-tracker/best-grind-spots/216?startDate=2026-08-20&endDate=2026-09-10) | 12.339 | 13.100 | 14.100 |
| [Scales of Judgment](https://garmoth.com/grind-tracker/best-grind-spots/217?startDate=2026-08-20&endDate=2026-09-10) | 13.535 | — | — |
| [Event Horizon](https://garmoth.com/grind-tracker/best-grind-spots/218?startDate=2026-09-03&endDate=2026-09-10) | 12.267 | — | — |

Garmoth bildet Average aus eingereichten Sessions. High und Top sind von
Moderatoren gepflegte Leistungsreferenzen, keine statistischen Perzentile.
Die Zeiträume gelten je Spot seit dessen Datenreset; siehe die Quellenlinks und
die FAQ auf [Garmoth Best Grind Spots](https://garmoth.com/grind-tracker/best-grind-spots).

Beim Start der ersten aktiven Grindstunde und jeder weiteren aktiven Stunde
fragt die App aktuelle Garmoth-Referenzen an. Pausen zählen nicht mit;
Fortsetzen innerhalb derselben aktiven Stunde löst keinen erneuten Abruf aus.
Der Abruf läuft im Hintergrund und blockiert weder den Start noch die
Loot-Erkennung. Ein Garmoth-API-Key ist dafür nicht erforderlich.

Während des Abrufs und bei fehlender Verbindung bleiben zuletzt gespeicherte
Referenzen nutzbar. Ohne gespeicherte Daten dient die obige, mitgelieferte
Referenz als Rückfall. Ihr ursprüngliches Referenzdatum bleibt dabei erhalten;
ein fehlgeschlagener Abruf macht alte Werte nicht zu aktuellen Werten.
Der Tooltip zeigt den Aktualisierungsstatus zusammen mit Quelle und Stand.

Die App liest anonym `https://api.garmoth.com/api/grind-tracker/collective/all`
und `https://garmoth.com/api/trpc/grindMeta.list`. Average wird aus der
Trash-Stundenrate und Garmoths spotabhängigem Lv.2-Multiplikator berechnet;
High und Top kommen aus den Moderatorenreferenzen. Beide Antworten müssen
gültig sein, bevor ein Spot einen neuen Stand erhält. Fehlende High-/Top-Werte
bleiben leer. Garmoth begrenzt vorhandene höhere Tiers mindestens auf Average;
bei identischen Schwellen gilt die höchste verfügbare Stufe.

Erfolgreiche Spotwerte werden atomar unter `garmoth-benchmarks-v1.json` im
App-Datenordner gespeichert. Fehlende oder ungültige Spots behalten ihren
bisherigen Stand. Neue Quellenlinks übernehmen den von Garmoth gelieferten
Zeitraum. Der angezeigte Stand ist bei abgerufenen Daten der Abrufzeitpunkt;
der Zeitraum der zugrunde liegenden Sessions steht im Quellenlink.

Die Endpunkte und Berechnungen wurden am 11.09.2026 anhand des öffentlichen
[Statistik-Clients](https://assets.garmoth.com/_static/_nuxt/B_Q4n5nK.js) und der
[Spot-Seite](https://assets.garmoth.com/_static/_nuxt/Ft2pq8LN.js) geprüft.
Die direkten Live-Abrufe lieferten dabei HTTP 403; die öffentliche Spot-Seite
zeigte außerdem einen Statistikfehler. Ein erfolgreicher Live-JSON-Abruf konnte
deshalb noch nicht verifiziert werden. Automatisierte Tests verwenden
synthetische Antworten nach dem beobachteten Client-Vertrag. Die App umgeht
keine Zugriffssperren und zeigt bei einem Fehler den letzten verfügbaren Stand.

## Vergleichbarkeit

Verglichen wird der tatsächliche Trash-pro-Stunde-Wert, ohne heimliche
Buff-Normalisierung. Bekannte abweichende Scroll-Stufen, eine inaktive Scroll
sowie aktive oder in dieser Session erfasste Agris-Nutzung erhalten einen Hinweis.
Unbekannte Erkennung wird nicht als abweichender Buff ausgegeben. Die Erkennung
kann nicht jede bisherige Buff-Änderung der Session belegen; eine Agris-Münze wird
nicht erkannt. Die Referenzbedingungen bleiben daher Teil des Vergleichs.
Garmoth verwendet teilweise spotabhängige Drop-Multiplikatoren; eine pauschale
Umrechnung wäre nicht für alle Spots gleichwertig.

Live- und Editor-Tooltips zeigen Bedingungen, Schwellen, Quelle und Datum. Das
native Overlay zeigt denselben Wert, dieselbe Farbe und kurze Zustands-Hinweise.
Die Bewertung greift nicht in Tracking, Lootzählung oder Uploads ein.
