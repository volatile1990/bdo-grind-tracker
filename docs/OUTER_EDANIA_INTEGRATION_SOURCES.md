# Outer Edania: Preis- und Garmoth-Zuordnung

Geprüft am 13.09.2026. Die bestehenden Inner-Edania-Referenzwerte und ihre
Provenienz bleiben erhalten.

## Garmoth-Metadaten

Die bereits in [GARMOTH_INTEGRATION.md](GARMOTH_INTEGRATION.md) dokumentierte Datei
`%LOCALAPPDATA%/com.iqon-digital-llc.bdo-companion/loot_drops` wurde ausschließlich
als öffentliche Metadatenquelle gelesen. Verwendet wurden Namen, Item-Keys,
Spot-IDs und die erlaubten Item-Keys der jeweiligen Spots. Die App liest diese
Datei nicht zur Laufzeit. Anmeldedaten oder private Sitzungen wurden nicht gelesen.

| Lokaler Spot | Garmoth-ID | Trash-Key |
| --- | ---: | --- |
| Aetherion Castle | [183](https://garmoth.com/grind-tracker/best-grind-spots/183) | `767244_0` |
| Nymphamare Castle | [184](https://garmoth.com/grind-tracker/best-grind-spots/184) | `767245_0` |
| Orbita Castle | [185](https://garmoth.com/grind-tracker/best-grind-spots/185) | `767247_0` |
| Tenebraum Castle | [193](https://garmoth.com/grind-tracker/best-grind-spots/193) | `767246_0` |
| Zephyros Castle | [194](https://garmoth.com/grind-tracker/best-grind-spots/194) | `767248_0` |

Die Metadatendatei nennt Zephyros noch „Zephyrus Castle“; die öffentliche
Garmoth-Seite bestätigt „Zephyros Castle“ für ID 194. Der erlaubte Zephyros-Pool
enthält `768160_0` (Sealed Black Magic Crystal).

Dark Energy Floodlands hat bei Garmoth drei eigene Orte:
[Great Red Sea / 208](https://garmoth.com/grind-tracker/best-grind-spots/208),
[Orbita / 209](https://garmoth.com/grind-tracker/best-grind-spots/209) und
[Zephyros / 210](https://garmoth.com/grind-tracker/best-grind-spots/210).
Alle drei Metadatenzeilen enthalten dieselben beiden Trash-Keys `767348_0`
(Tainted Armor Fragment) und `767349_0` (Faded Dark Energy) und denselben Lootpool.
Aus dem lokalen Loot lässt sich daher keiner dieser Orte bestimmen. Der generische
lokale Spot wird vollständig erfasst und bewertet, hat aber bewusst keinen
Garmoth-Upload und keine ortsspezifische Garmoth-Bewertung. Vorschau und Payload-Prüfung
erklären die fehlende Gebietszuordnung; es wird keine Orts-ID geraten.

Die fünf Castle-Pools teilen Black Stone, Ancient Spirit Dust, Caphras Stone,
Distorted/Silent Fragment of Origin, Distorted/Silent Crystal of Origin und die
vier Deboreka-Grundgegenstände. Aetherion ergänzt Primordial Fragment und WON,
Nymphamare BON und Crystallized Energy of Endtimes, Orbita JIN und Endtimes,
Tenebraum/Zephyros HAN, Endtimes sowie Herald's Crystal und Flawless Herald's Crystal.
Die Namens- und Preispipeline unterscheidet bisher keine Verstärkungsstufen.
Deboreka-Namen verwenden deshalb ausschließlich die bestätigten Grundstufen-Keys
`11882_0`, `12094_0`, `11653_0`, `12276_0` und Marktpreise mit `sid=0`.

## Feste Silberwerte

Die Trashwerte wurden gegen die offiziellen Edania-Updates und die einzelnen
aktuellen Itemeinträge geprüft. Insbesondere wurde der veraltete Metadatenwert
140.000 für Lightlost Core **nicht** übernommen: der aktuelle Wert ist 140.600.

| Item | Item-ID | NPC-Silber je Stück |
| --- | ---: | ---: |
| Chilled Soul Piece | [767244](https://bdocodex.com/us/item/767244/) | 105.640 |
| Contaminated Coral Piece | [767245](https://bdocodex.com/us/item/767245/) | 116.200 |
| Lightlost Core | [767247](https://bdocodex.com/us/item/767247/) | 140.600 |
| Ancient Soldier Fragment | [767246](https://bdocodex.com/us/item/767246/) | 147.630 |
| Hardened Lava Chunk | [767248](https://bdocodex.com/us/item/767248/) | 126.980 |
| Tainted Armor Fragment | [767348](https://bdocodex.com/us/item/767348/) | 100.507 |
| Faded Dark Energy | [767349](https://bdocodex.com/us/item/767349/) | 597.680 |

Weitere feste Metadatenwerte: Primordial Fragment (821246) 30.000.000;
WON/BON/JIN/HAN Crystal of Ruin (821253/821254/821319/821320)
5.000.000/7.000.000/8.000.000/10.000.000; entsprechende Crystal of Dusky Ruin
(15286/15287/15290/15291) 500.000.000/700.000.000/800.000.000/1.000.000.000.

Marktgegenstände verwenden ausschließlich aktuelle bzw. zwischengespeicherte
Arsha-Preise, keine mitgelieferten Marktpreise. Neu sind Crystallized Energy of
Endtimes (821252), Distorted Fragment of Origin (821317), Distorted Crystal of
Origin (761802), Herald's Crystal (821250), Flawless Herald's Crystal (821251),
die vier Deboreka-Items und Sealed Black Magic Crystal (768160).
Der aktuelle [Distorted-Fragment-Eintrag](https://bdocodex.com/us/item/821317/)
belegt die Handelbarkeit; das alte Companion-Steuerflag wird dafür nicht übernommen.

## Garmoth-Vergleichswerte

Es werden keine neuen Durchschnitts- oder High-/Top-Werte geschätzt. Der öffentliche
HTTP-Abruf von `grindMeta.list` war bei der Prüfung mit HTTP 403 blockiert; die
öffentlich lesbare Zephyros-Seite liefert im statischen HTML keine nutzbare Statistik.
Die fünf Castle-Zuordnungen können vom vorhandenen Live-Provider geladen und
gespeichert werden, sobald gültige öffentliche Antworten vorliegen. Die Auswahl
und der Cache funktionieren auch für Spots ohne mitgelieferten Referenzwert.
Bis dahin bleibt deren Grind-Bewertung ohne Referenz. Die neuen synthetischen
Provider-Tests prüfen Mapping, Cache und Offline-Neustart, sind keine Preis- oder
Benchmarkquelle.
