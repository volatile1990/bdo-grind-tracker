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

Die Werte sind ein fest hinterlegter, datierter Referenzstand. Die öffentlichen
Clients nennen `/api/grind-tracker/collective/all` auf `api.garmoth.com` und
`/api/trpc/grindMeta.list` auf `garmoth.com`; beide direkten anonymen Abrufe
lieferten bei der Prüfung HTTP 403. Die App verspricht deshalb keine automatische
Aktualisierung und benötigt für die Bewertung weder Verbindung noch API-Key.
Künftige Referenzänderungen müssen anhand der Quelle geprüft und in
`GarmothGrindBenchmarks` aktualisiert werden.

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
