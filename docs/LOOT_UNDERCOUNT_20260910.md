# Unterzählung: Prüfung beider realer Aufnahmen

**Umsetzung nach dieser Untersuchung:** Der reine Lifetime-Zähler mit der
bisherigen OCR ist inzwischen als [Teststand 3](LIFETIME_LOOT_TRACKING.md)
integriert. Die unten beschriebenen engen Paddle-Ausschnitte und Parserproben
bleiben getrennte Offline-Experimente.

Stand: 10. September 2026. Untersucht wurden die veröffentlichte Test-EXE 1
(`116e289`) und der uncommittete Zwischenstand der Test-EXE 2. Die nachfolgenden
Prototypen und OCR-Versuche sind **Offline-Experimente**, noch keine neue Live-EXE.

Die bisherige Kombination übernimmt Garmoths entscheidenden Mechanismus nicht
korrekt: Eine Lebensdauer als bloße Strafe für fehlende OCR ersetzt keine
endliche Zeilenlebensdauer. Zusätzlich verhindert unsere frühe Festlegung auf
öffentliche Drop-IDs die spätere Korrektur einer falschen Ereignisgeschichte.
Diese Regeln dürfen einen Ersatz nicht beschränken. Die zweite Aufnahme zeigt
außerdem einen unabhängigen Verlust bereits vor dem Zähler durch eine im Spiel
eingeblendete Bossmeldung und unsere OCR-Auswahlregeln.

## Referenzen und Messung

| Aufnahme | Referenz des Nutzers | Tatsächlich aufgezeichnet |
|---|---|---|
| `loot-20260910-194014-1c9636ebe38c4b449610bc98d1a14e12` | 576 Helme; Start bei 0 ausdrücklich bestätigt | 472 Helme, 3 Black Stones, 1 Caphras, 1 Ring |
| `loot-20260910-201448-414560d02cb445d0abc4dd50b96d4ae0` | 324 Helme, 7 Ancient Spirit Dust | 268 Helme, 4 Dust |

Die zweite Aufnahme enthält 660 Frames in 133,094 Sekunden. Der mediane
Aufnahmeabstand beträgt 201,775 ms, das Maximum 221,150 ms. Es gibt keinen
Abstand über 300 ms. Die maximale Wartezeit auf Queue-Kapazität beträgt
0,249 ms. Ein einzelner OCR-Auftrag benötigt 218,628 ms; das verursacht keine
längere Aufnahmelücke. Das 200-ms-Capture funktioniert; eine noch höhere Frequenz
behebt die nachgewiesenen Zuordnungs- und Erkennungsfehler nicht.

Unveränderte JSONL-Snapshots, Statistiken und Framezuordnungen befinden sich unter
`artifacts/temporal-undercount-20260910` beziehungsweise
`artifacts/temporal-redesign-20260910/recording2-analysis`.

## Nachgewiesene Fehler im Zählmodell

In Aufnahme 2 beginnt die Helm-ID `462ded6a-0f78-08df-7d05-000000000000`
in Frame 234 und wird 235 gebucht. Frame 240 zeigt das Ausblenden, 241 die
fast verschwundene alte Zeile, 242 einen neuen hellen Eintrag. Trotzdem verwendet
der Zähler bis Frame 248 dieselbe veröffentlichte ID: zwei physikalische
Lootmeldungen werden zusammengefasst. Das Muster wiederholt sich bei
533 → 540 → 541: alte Schrift verblasst, neue Helm- und Dustmeldung erscheinen,
der alte Helm-Track bleibt bestehen.

Der visuelle Zwischenstand betrachtet hauptsächlich das unmittelbar vorherige
Bild. Ist die vorherige Schrift schon zu schwach, gibt es keinen verifizierten
Glyphenvergleich und damit kein Freshness-Signal. Eine reine Anpassung der
Kontrastschwelle behebt diesen strukturellen Fall nicht.

`TemporalLootReconciler.Publish` bindet eine gebuchte ID an ihren Itemnamen und
entfernt Hypothesen ohne diese ID. Dadurch schränkt eine frühe Anzeigeentscheidung
die spätere Interpretation der Bilder ein. Mengenrevisionen reichen nicht aus:
Eine Erklärung muss auch Drops teilen, zusammenlegen oder zurücknehmen dürfen.

Der frühere `verified-stable-neighbor`-Fix liegt dagegen ausschließlich im
historischen `CompanionFrameReconciler`. Er wird im temporalen Live-Pfad nicht
aufgerufen. Die dort belegte Black-Stone-Doppelzählung bleibt ein sinnvoller
Gegenfall für den Ersatz; die alte Implementierungsregel muss dafür nicht
weiterverwendet werden.

## Was das Boss-Popup verdeckt

Die Bossmeldung fährt in Frame 78 ein, bleibt von 79 bis 176 vollständig
sichtbar, fährt 177 aus und ist 178 verschwunden. Der Nutzer bestätigt, dass sie
direkt aus Black Desert stammt. Eine andere Desktop-/Fenster-Capture-API würde
diese im Spiel gerenderte Meldung nicht entfernen.

- Frames 81–85, Slot 1: **Dust ×2** hinter dem transparenten Popup.
- Frame 158, Slot 0, anschließend 159–163, Slot 1: **Dust ×1**.
- Beide Gruppen fehlen vollständig in den akzeptierten OCR-Eingaben. Die späteren
  Dust-Gruppen 541–545 und 614–620 werden hingegen jeweils korrekt als ×2 gebucht.
  Somit sind alle drei fehlenden Dust bereits vor dem Zähler verloren.
- In den zwei manuell untersuchten Popup-Wellen 79–98 und 157–176 sind jeweils
  neun Helm-Ankünfte sichtbar, aber nur sechs gebucht: zusammen **24 fehlende
  Helme**. Von der gesamten Differenz 56 bleiben damit rechnerisch 32 Helme.
  Diese Restdifferenz ist nicht vollständig einzeln annotiert; die nachfolgenden
  Fälle 242 und 541 belegen zusätzliche Zählerfehler unabhängig vom Popup.

Die Transparenz ist wichtig: Fehlende OCR heißt hier nicht, dass die Pixel
grundsätzlich keine Information mehr enthalten. Garmoths eigener OCR-Helfer
erkennt auf diesen Bildern keine vollständigen Mengen der beiden verdeckten
Dust-Gruppen. Unser bereits mitgeliefertes PP-OCRv6-Small-Modell liest im
Standardband dagegen Text wie `Ancient Spirit Dust x 2intI anpneared×`.

Ein engerer, an der UI-Skalierung ausgerichteter Zeilenausschnitt liefert bei
denselben Bildern `Ancient Spirit Dust x 2X` beziehungsweise
`Ancient Spirit Dust x 1X` mit etwa 0,96 Modellkonfidenz. Das zusätzliche X stammt
vom Schließen-Symbol. Der Betrag der zweiten Gruppe wird erst nach dem Scrollen
in Slot 1 klar lesbar; der ursprüngliche untere Eintrag ist mit der Popup-Uhrzeit
überlagert. Diese Uhrzeit darf keinesfalls durch großzügiges Ziffernsammeln oder
bloßes Min-/Max-Clamping zur Lootmenge werden.

Der derzeitige Paddle-Review startet oft überhaupt nicht, wenn Windows OCR noch
keinen zum Katalog passenden Namenshinweis geliefert hat. Zudem fordert er einen
engen Mengensuffix am Textende und zwei bestandene Farbraum-Varianten. Das sind
Auswahlregeln unserer Pipeline, keine Grenzen des vorhandenen OCR-Modells.

## Vergleich mit dem tatsächlich extrahierten Garmoth-Modell

Der isolierte Harness führt den lokal extrahierten Original-Ledger aus. Er
übernimmt keine fremde Laufzeitdatei in Grindcrest. Akzeptierte Grindcrest-Zeilen
werden auf Garmoths fünf Slots abgebildet; zusätzliche Varianten verwenden dessen
Originalparser oder den tatsächlichen lokalen OCR-Helfer auf den gespeicherten
PNGs. Das ist kein Vergleich zweier parallel laufender vollständiger Apps.

| Verfahren/Eingaben | Aufnahme 1: Helme | Aufnahme 2: Helme / Dust |
|---|---:|---:|
| Tatsächliche Grindcrest-Testversion | 472 | 268 / 4 |
| Visueller Zwischenstand auf alten OCR-Daten, offline | 508 | – |
| Garmoths Original-Ledger, fünf Slots, dieselben akzeptierten OCR-Daten | **576** | 264 / 4 |
| Garmoths Originalparser und Ledger, tatsächlicher Garmoth-Paddle-Output | nicht ausgeführt | 276 / 4 |
| Unabhängiger C#-Lebensdauer-Prototyp, fünf Slots, akzeptierte OCR-Daten | **576** | 264 / 4 |

Garmoth lässt Zeilen trotz fortbestehender gleichnamiger Lesung nach modellierten
1250/1350/1450/1550 ms auslaufen. Vier getrennte Modelle lernen altersabhängige
Beobachtungswahrscheinlichkeiten und behalten jeweils 24 Kandidaten. Die
Geburtszeiten liegen zwischen Captures. Der aktuelle Gesamtstand stammt aus der
gesamten besten Ereignisgeschichte und darf rückwirkend sinken oder seine
Itemzusammensetzung ändern.

Ein kontrollierter Versuch mit nur sechs Kandidaten pro Modell erhält in
Aufnahme 1 trotzdem die 576 Helme. Die Beamgröße allein ist hier also keine
nachgewiesene Fehlerursache. Auch eine pauschale Zwei-Lesungen-Regel erklärt den
Fehler nicht: Unser bisheriger Zähler kann bei Abschluss oder nach Retirement
bereits eine einzelne valide Lesung buchen. Entscheidend sind die zugelassenen
Ereignisgeschichten und ihre spätere Korrigierbarkeit.

Der eigene Prototyp wartet wie Garmoth mit dem Lernen bis zur ersten lesbaren
Dropmenge. Lernt er bereits aus sämtlichen leeren Startframes, sinkt sein zweites
Ergebnis auf 248 / 4. Auch eine synthetische Folge aus drei ×5-Ankünften wird
im 200-ms-Raster mit hart angenommener Sichtbarkeit von 1300 ms als 20 statt 15
gezählt. Der Treffer 576 ist damit ein wichtiger Vergleichsbeleg, aber keine
allgemeine Genauigkeitsgarantie oder Live-Abnahme.

## Gemeinsamer OCR-/Zähler-Versuch und Gegenbefunde

Die enge Paddle-Zeilenlesung wurde über alle 660 Frames der zweiten Aufnahme
ausgeführt, nicht nur auf den ausgewählten Dust-Bildern. Der unveränderte
Garmoth-Parser akzeptiert das zusätzliche Schluss-X in `Dust x 2X` nicht. Auf
kontaminiertem Text wie `Elion Follower's Helmotx 4 10 2115` liest er hingegen
den letzten Zahlenblock als Menge 2115. Ein unveränderter Parsertransfer liefert
dadurch völlig falsche Endstände. Die vom Popup überlagerte untere Dustzeile in
Frame 158 liefert roh `Arcient Spirit Dust x2b3  1021:15`; die Parserbereinigung
kann daraus unter anderem einen falschen Mengenwert 263 machen.

In einer ausdrücklich getrennten Parserprobe werden nur klar vorhandene
Multiplier/Ziffern-Tokens mit einem passenden Itempräfix isoliert; es werden
keine Ziffern ergänzt und keine Session-Sollwerte verwendet. Zusammen mit dem
unveränderten Fünf-Slot-Ledger ergibt dies **332 Helme / 7 Dust**, mit sechs Slots
**340 / 7**. Die Dust-Evidenz ist damit vollständig nutzbar, die Helmzählung aber
noch um 8 beziehungsweise 16 zu hoch. Dies ist kein fertiger Ersatz und kein
Grund, die Totals durch einen Korrekturfaktor auf 324 zu setzen.

Eine stärkere OCR kann verblassende Schrift länger lesen als der ursprüngliche
Erkennungsweg. Deshalb müssen OCR-Vorverarbeitung und das zeitliche
Sichtbarkeitsmodell gemeinsam geprüft werden. Das ist eine plausible Erklärung
für zusätzliche Zeilenzuordnungen, noch keine vollständig bewiesene Ursache der
neuen Überzählung. Die unveränderten Baselines und alle Parservarianten liegen
getrennt unter `garmoth-probe`; siehe dort `PPV6_NARROW.md`.

Der unverändert wiederholte Gegenversuch auf **allen 1012 Frames der ersten
Aufnahme** bestätigt diese Grenze: Alle sechs Kombinationen aus fünf/sechs Slots
und den drei festgehaltenen Parservarianten ergeben mit den engen Paddle-Bändern
**588 Helme, 3 Black Stones, 1 Caphras**. Die gewählte Lebensdauer ist dabei
1450 ms statt der 1350 ms auf den früheren OCR-Daten. Auch hier besteht also
eine Überzählung, um zwölf Helme. Nach diesem Gegenversuch wurden weder Regex
noch Schwellen auf die bekannten Sollzahlen nachjustiert.

## Konsequenz für den Ersatz

1. **Endliche Lebensphasen und korrigierbare Ereignisgeschichte** als neue
   Core-Komponente. Keine Veröffentlichung darf alternative Erklärungen aus dem
   Modell entfernen. Die Tests, die eine gleiche sichtbare Zeile selbst nach
   zwölf Sekunden oder einer zweiminütigen Lücke zwingend derselben ID zuordnen,
   sind kein akzeptabler Vertrag für dieses BDO-Modell.
2. **Paddle kann eigenständig lesen**, auch ohne erfolgreichen Windows-Vorpass.
   Den kalibrierten Textbereich enger erfassen; Belegung, unlesbare Schrift,
   fehlgeschlagene OCR und wirklich leere Zeilen getrennt behandeln. Über mehrere
   Bilder abstimmen. Mehrere Vorverarbeitungen desselben Bildes sind keine zwei
   unabhängigen Beweise.
3. **Menge gemeinsam mit Item und sauberem Textbereich prüfen.** Eindeutige
   Fremdglyphen dürfen erkannt werden; Datum/Uhrzeit und untrennbar vermischter
   Text dürfen keine erfundenen Mengen liefern. Die getesteten engen Ausschnitte
   sind noch keine über alle Fonts und UI-Skalen validierte neue Standardeinstellung.
4. **Summenkorrekturen vom Ankunftssignal trennen.** Ein korrigierter Gesamtstand
   ist kein neuer Pickup. UI-Aktualisierung, Aktivitätszeit, Spotlock, manuelle
   Änderungen und Normal-/Rare-Fusion brauchen dafür einen expliziten Vertrag.
   Einfach negative Deltas mit neuen Drop-IDs zu senden würde Nebenwirkungen
   erzeugen. Historische v1/v2-Replays bleiben gesonderte Vergleichspfade.

Der bereits untersuchte Garmoth-Mechanismus ist die bessere Grundlage für den
Zähler. Seine bloße Kopie würde jedoch die zweite Aufnahme nicht fehlerfrei
machen. Die eigenen PNG-Tests zeigen eine zusätzliche Stärke unseres vorhandenen
Paddle-Zeilenmodells, die durch die bisherigen Vorbedingungen ungenutzt bleibt.

Detailartefakte:

- `artifacts/temporal-redesign-20260910/garmoth-probe/README.md`: Originalregeln,
  Eingabeadaptionen, Quellenhashes, Vergleich und Ablationen.
- `artifacts/temporal-redesign-20260910/independent-architecture-review.md`:
  Schnittstellen, Rücknahmen, UI-/Rare-/Spotlock-Auswirkungen.
- `artifacts/temporal-redesign-20260910/LifetimeProbe`: unabhängiger C#-Prototyp.
- `artifacts/temporal-redesign-20260910/ppv6-row-band-probe.json`: enger/weiter
  Ausschnitt derselben realen Bilder, einschließlich Gegenbeispiel der Uhrzeit.
- `artifacts/temporal-redesign-20260910/PaddleRowsProbe`: reproduzierbare lokale
  OCR-Probe mit den unveränderten DLLs und dem Modell der Test-EXE 2.
