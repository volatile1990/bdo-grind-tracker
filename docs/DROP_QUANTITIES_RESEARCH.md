# Dropmengen: Recherche und bestätigte Nutzervorgaben

Stand: **8. September 2026, PC-Version**. Umfang: alle sechs Spots und alle
potenziellen Items aus `LootSpotCatalog`, einschließlich des optionalen Eventitems.

Gesucht sind das **kleinste positive Minimum** und das **höchste mögliche Maximum
einer einzelnen angezeigten Dropmenge** je Item und Spot. Das Maximum muss auch
Mengenboni und die normalen besonderen Spotmechaniken abdecken. Stundenmengen,
Null-Drops, Ausrüstungsanzahlen, Rezepte, Questbelohnungen und Kisteninhalte sind
keine solchen Grenzen.

**Alle 207 Item/Spot-Paare wurden am 8. September 2026 vom Nutzer in
`Dropmengen-Eingabe.xlsx` vollständig ausgefüllt.** Die Tabelle unten enthält
genau diese Angaben. 175 Paare haben Minimum = Maximum = 1.
Die daneben verlinkten Itemseiten dokumentieren die Recherche; sie sind kein
Beleg der vom Nutzer vorgegebenen Mengen. Die Quellenkritik weiter unten bleibt
als Recherchehistorie erhalten.

Die übernommene Excel-Datei hat SHA-256
`a5ba8409909fd7be454d1bbf085e1a9ecf2dcd5cc3781980d405ca461adf02ae`.
Der eingebundene Datensatz steht in
[`data/drop-quantities.json`](../data/drop-quantities.json), einschließlich
der jeweiligen Excel-Quellzellen. Kein zulässiges Paar wurde ausgelassen.

## Vollständige Tabelle der Nutzervorgaben

Spalten: **Ap** = Aphrodon Temple, **He** = Hermesia Inner Castle,
**Ma** = Magaia Temple, **Ar** = Aresion Temple, **Sc** = Scales of Judgment,
**EH** = Event Horizon. `—` bedeutet: kein Bestandteil des derzeitigen Spotpools.
Das Eventitem ist nur bei eingeschaltetem Eventloot verfügbar; die Matrix behauptet
keine aktuelle Aktivität dieses Events.

| Item / Quelle | Ap Min/Max | He Min/Max | Ma Min/Max | Ar Min/Max | Sc Min/Max | EH Min/Max |
| --- | --- | --- | --- | --- | --- | --- |
| [\[Event\] Mysterious Ore](https://bdocodex.com/us/item/1000508/) | 1 / 5 | 1 / 5 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Ancient Spirit Dust](https://bdocodex.com/us/item/721002/) | 1 / 50 | 1 / 50 | 1 / 50 | 1 / 50 | 1 / 50 | 1 / 50 |
| [Apeiron Belt](https://bdocodex.com/us/item/12298/) | — | — | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Apeiron Earring](https://bdocodex.com/us/item/11898/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Apeiron Necklace](https://bdocodex.com/us/item/11733/) | — | — | — | 1 / 1 | 1 / 1 | 1 / 1 |
| [Apeiron Ring](https://bdocodex.com/us/item/12144/) | — | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Black Crystal Fragment](https://bdocodex.com/us/item/980128/) | — | 4 / 1000 | — | — | — | — |
| [Black Stone](https://bdocodex.com/us/item/16001/) | 1 / 15 | 1 / 20 | 1 / 50 | 1 / 50 | 1 / 50 | 1 / 50 |
| [BON Origin Shard](https://bdocodex.com/us/item/821431/) | — | 1 / 1 | — | — | — | — |
| [BON Wandering Origin Crystal](https://bdocodex.com/us/item/15295/) | — | 1 / 1 | — | — | — | — |
| [Branch of Abundance](https://bdocodex.com/us/item/980127/) | 4 / 1000 | — | — | — | — | — |
| [Broken Gloves of the Void](https://bdocodex.com/us/item/980132/) | — | — | — | — | — | 2 / 1000 |
| [Broken Vestige of Crimsonflare](https://bdocodex.com/us/item/980142/) | — | — | — | 1 / 1 | — | — |
| [Broken Vestige of Ebonmere](https://bdocodex.com/us/item/980140/) | — | 1 / 1 | — | — | — | — |
| [Broken Vestige of Everlight](https://bdocodex.com/us/item/980141/) | — | — | 1 / 1 | — | 1 / 1 | — |
| [Broken Vestige of Goldroot](https://bdocodex.com/us/item/980139/) | 1 / 1 | — | — | — | — | — |
| [Broken Vestige of Voidreach](https://bdocodex.com/us/item/980143/) | — | — | — | — | — | 1 / 1 |
| [Caphras Stone](https://bdocodex.com/us/item/721003/) | 1 / 50 | 1 / 20 | 1 / 50 | 1 / 50 | 1 / 1 | 1 / 50 |
| [Corrupt Oil of Immortality](https://bdocodex.com/us/item/1178/) | 1 / 5 | 1 / 5 | 1 / 1 | 1 / 5 | 1 / 5 | 1 / 5 |
| [Crimson Primordial Luster - Sovereign](https://bdocodex.com/us/item/821341/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Crimson Primordial Pigment - Sovereign](https://bdocodex.com/us/item/767293/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Elion Follower's Helmet](https://bdocodex.com/us/item/980129/) | — | — | 7 / 1000 | — | — | — |
| [Elion Follower's Mark](https://bdocodex.com/us/item/980130/) | — | — | — | — | 2 / 2000 | — |
| [Embers of Ynix - Armor](https://bdocodex.com/us/item/821462/) | 1 / 1 | — | — | — | — | 1 / 1 |
| [Embers of Ynix - Gloves](https://bdocodex.com/us/item/821463/) | — | — | — | 1 / 1 | — | 1 / 1 |
| [Embers of Ynix - Helmet](https://bdocodex.com/us/item/821461/) | — | 1 / 1 | — | — | — | 1 / 1 |
| [Embers of Ynix - Shoes](https://bdocodex.com/us/item/821464/) | — | — | 1 / 1 | — | 1 / 1 | 1 / 1 |
| [Fusion Shard](https://bdocodex.com/us/item/821471/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [HAN Origin Shard](https://bdocodex.com/us/item/821433/) | — | — | — | 1 / 1 | 1 / 1 | 1 / 1 |
| [HAN Wandering Origin Crystal](https://bdocodex.com/us/item/15297/) | — | — | — | 1 / 1 | 1 / 1 | 1 / 1 |
| [JIN Origin Shard](https://bdocodex.com/us/item/821432/) | — | — | 1 / 1 | — | — | — |
| [JIN Wandering Origin Crystal](https://bdocodex.com/us/item/15296/) | — | — | 1 / 1 | — | — | — |
| [Laila's Petal](https://bdocodex.com/us/item/54031/) | 1 / 5 | 1 / 5 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Nev's Fragment](https://bdocodex.com/us/item/821460/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Pure Black Stone](https://bdocodex.com/us/item/13/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Refined Essence of Devouring](https://bdocodex.com/us/item/767338/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Refined Origin of Hunger](https://bdocodex.com/us/item/767337/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Scorched Belt Ornament](https://bdocodex.com/us/item/980131/) | — | — | — | 2 / 1000 | — | — |
| [Silent Crystal of Origin](https://bdocodex.com/us/item/761803/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Silent Fragment of Origin](https://bdocodex.com/us/item/821318/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Sunset Primordial Luster - Edana](https://bdocodex.com/us/item/821459/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Sunset Primordial Pigment - Edana](https://bdocodex.com/us/item/767353/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Twilight of the End - Belt](https://bdocodex.com/us/item/821424/) | — | — | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Twilight of the End - Earring](https://bdocodex.com/us/item/821422/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Twilight of the End - Necklace](https://bdocodex.com/us/item/821421/) | — | — | — | 1 / 1 | 1 / 1 | 1 / 1 |
| [Twilight of the End - Ring](https://bdocodex.com/us/item/821423/) | — | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Violet Primordial Luster - Edana](https://bdocodex.com/us/item/821343/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Violet Primordial Luster - Sovereign](https://bdocodex.com/us/item/821342/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Violet Primordial Pigment - Edana](https://bdocodex.com/us/item/767296/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [Violet Primordial Pigment - Sovereign](https://bdocodex.com/us/item/767294/) | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 | 1 / 1 |
| [White Primordial Luster - Edana](https://bdocodex.com/us/item/821420/) | — | — | — | 1 / 1 | 1 / 1 | 1 / 1 |
| [White Primordial Luster - Sovereign](https://bdocodex.com/us/item/821419/) | — | — | — | 1 / 1 | 1 / 1 | 1 / 1 |
| [White Primordial Pigment - Edana](https://bdocodex.com/us/item/767344/) | — | — | — | 1 / 1 | 1 / 1 | 1 / 1 |
| [White Primordial Pigment - Sovereign](https://bdocodex.com/us/item/767343/) | — | — | — | 1 / 1 | 1 / 1 | 1 / 1 |
| [WON Origin Shard](https://bdocodex.com/us/item/821430/) | 1 / 1 | — | — | — | — | — |
| [WON Wandering Origin Crystal](https://bdocodex.com/us/item/15294/) | 1 / 1 | — | — | — | — | — |

**Vollständigkeitskontrolle:** 56 unterschiedliche Items, 207 Item/Spot-Paare einschließlich optionaler Events.

## Itemgruppen

Die 56 Namen verteilen sich auf die folgenden Gruppen. Die genaue Spotzuordnung
und die individuellen Mengen stehen in der Matrix oben.

| Gruppe | Vollständig enthaltene Varianten / Items | Anzahl Namen |
| --- | --- | ---: |
| Trash | Branch of Abundance; Black Crystal Fragment; Elion Follower's Helmet; Scorched Belt Ornament; Elion Follower's Mark; Broken Gloves of the Void | 6 |
| Wandering Origin Crystal | WON, BON, JIN, HAN | 4 |
| Origin Shard | WON, BON, JIN, HAN | 4 |
| Embers of Ynix | Armor, Helmet, Shoes, Gloves | 4 |
| Apeiron | Earring, Ring, Belt, Necklace | 4 |
| Twilight of the End | Earring, Ring, Belt, Necklace | 4 |
| Broken Vestige | Goldroot, Ebonmere, Everlight, Crimsonflare, Voidreach | 5 |
| Origin-Materialien | Silent Fragment of Origin; Silent Crystal of Origin | 2 |
| Weitere Fragmente | Nev's Fragment; Fusion Shard | 2 |
| Devouring | Refined Origin of Hunger; Refined Essence of Devouring | 2 |
| Primordial Pigment | Crimson - Sovereign; Violet - Sovereign; Violet - Edana; Sunset - Edana; White - Sovereign; White - Edana | 6 |
| Primordial Luster | Crimson - Sovereign; Violet - Sovereign; Violet - Edana; Sunset - Edana; White - Sovereign; White - Edana | 6 |
| Öl | Corrupt Oil of Immortality | 1 |
| Standard-/Worlddrops | Ancient Spirit Dust; Black Stone; Caphras Stone; Laila's Petal; Pure Black Stone | 5 |
| Optionales Event | [Event] Mysterious Ore | 1 |

## Quellenprüfung und gesicherte Teilbefunde

### Offizielle PC-Veröffentlichungen

Die [NA/EU-Einführung vom 13. August 2026](https://www.naeu.playblackdesert.com/en-US/News/Detail?groupContentNo=10451)
enthält für alle sechs Spots Hauptlootlisten, jedoch keine vollständigen
Monster-Dropmengen. Beim BON Wandering Origin Crystal ist `Edania / x1` die
**maximale Ausrüstungsanzahl** der Kristallgruppe. Das ist kein Nachweis einer
Dropobergrenze. Auch die Rezeptkosten von 100 Origin Shards sind keine Dropmenge.

Die [PC-Patchnotes vom 20. August 2026](https://blackdesert.pearlabyss.com/Asia/en-us/News/Notice/Detail?_boardNo=19716)
belegen diese Mengen nach der Änderung:

| Spot | Item | Ausdrücklich genannter Gegner | Menge |
| --- | --- | --- | --- |
| Magaia Temple | Elion Follower's Helmet | [Witness] Priest of the End | 30–40 |
| Magaia Temple | Elion Follower's Helmet | Elion's Tear | 10–12 |
| Magaia Temple | Elion Follower's Helmet | Breath of Elion | 15–17 |
| Magaia Temple | Elion Follower's Helmet | Elion's Blessing | 20–22 |
| Aresion Temple | Scorched Belt Ornament | [Reforged] Curved Blade, Tempered Spear, Steadfast Arrow, Herald of Flame | 10–15 |

Diese Tabellen betreffen bestimmte Gegner und liefern kein vollständiges
Spotminimum. Die daneben genannten Rare-Änderungen sind **Dropchancen** in Prozent,
keine Mengenänderungen.

Die [PC-Patchnotes vom 3. September 2026](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=19773)
enthalten zusätzlich eine konkrete Trashmengentabelle für Event Horizon:

| Item | Ausdrücklich genannter Gegner | Menge nach Änderung |
| --- | --- | --- |
| Broken Gloves of the Void | Despair-Consumed Edana; Spacetime Debris | 2–4 |
| Broken Gloves of the Void | Despairbringer | 4–8 |
| Broken Gloves of the Void | Ibedor | 25–35 |
| Broken Gloves of the Void | Spacetime Debris (Special) | 30–35 |

Über die **genannten** Gegner ergibt sich damit 2–35. Diese Ergänzung war in der
älteren `TRASH_MINIMUMS.md` noch nicht enthalten. Sie ist kein Nachweis, dass 35
die höchste **angezeigte Menge einschließlich aller Mengenboni** wäre. Außerdem
muss bestätigt werden, dass keine weitere reguläre Variante einen anderen Bereich
hat. Der Nutzer hat für diesen Spot inzwischen 2/1000 vorgegeben; das ist eine
explizite Nutzervorgabe und wird nicht aus den genannten 2–35 abgeleitet.

Dieselben Patchnotes bestätigen direkte Event-Horizon-Drops zahlreicher
Katalogmaterialien, darunter Black Stone, Ancient Spirit Dust und Caphras Stone,
aber nennen nur geänderte Chancen und keine absoluten Rare-Dropmengen.

### BDO Codex: Itemseiten und nachgeladene Tabellen

Alle 56 in der Matrix verlinkten Itemseiten wurden abgefragt. Für neue
Inner-Edania-Materialien, Kristalle und Ausrüstung waren überwiegend Rezepte,
Quest-/Kistenbeziehungen und die Tabellensorte `nodes&type=nodedrop` vorhanden.
Letztere ordnet ein Item einem Gebiet zu; ihre Spalten sind Node, Zone,
Temperature, Humidity, Groundwater. Das liefert keine Mengen.

Beispiel: [BON Wandering Origin Crystal, Node-Tabelle](https://bdocodex.com/query.php?a=nodes&type=nodedrop&id=15295&l=us)
liefert Anemoi Mountains / Inner Edania mit Umweltwerten. Die `1` in generischen
Item-/Kistenlisten ist je nach Schema die Itemstufe; sie darf ebenfalls nicht als
Dropzahl interpretiert werden. Die BON-Seite bietet keine entsprechende
NPC-Mengentabelle. Der [Enslaved Miner (28697)](https://bdocodex.com/us/npc/28697/)
liefert ebenfalls keine verwertbare vollständige Droptabelle.

Die alten Standard-/Worlditems bieten dagegen `drop&type=npcdropgroups`-Tabellen
mit tatsächlichen Mengen. Geprüfte Resultate am 8. September:

| Item | NPC-Zeilen in der öffentlichen Tabelle | Vorhandene Mengen | Grenze der Aussage |
| --- | ---: | --- | --- |
| [Pure Black Stone, Variante 13](https://bdocodex.com/query.php?a=drop&type=npcdropgroups&id=13&l=us) | 1.138 | jeweils 1 | Kein Nachweis für die neuen Inner-Edania-Gegner |
| [Laila's Petal](https://bdocodex.com/query.php?a=drop&type=npcdropgroups&id=54031&l=us) | 1.454 | jeweils 1 | Kein Nachweis für die neuen Inner-Edania-Gegner |
| [Ancient Spirit Dust](https://bdocodex.com/query.php?a=drop&type=npcdropgroups&id=721002&l=us) | 774 | u. a. 1; 1–2; 3–9; 15–30 | Mehrfachmengen belegt, kein Inner-Edania-Gesamtmaximum |
| [Black Stone](https://bdocodex.com/query.php?a=drop&type=npcdropgroups&id=16001&l=us) | 503 | u. a. 1; 1–6; 6–8; 12–18 | Mehrfachmengen belegt, kein Inner-Edania-Gesamtmaximum |
| [Caphras Stone](https://bdocodex.com/query.php?a=drop&type=npcdropgroups&id=721003&l=us) | 518 | 1; 2–3 | Mehrfachmengen belegt, kein Inner-Edania-Gesamtmaximum |

Pure-Black-Stone-Varianten [11](https://bdocodex.com/us/item/11/),
[12](https://bdocodex.com/us/item/12/) und [14](https://bdocodex.com/us/item/14/)
wurden zusätzlich geprüft: ebenfalls je 1.138 Einträge mit Menge 1, jedoch ohne
den Enslaved Miner (28697). Die Namenssuche der Tabellen nach den neuen
Spot-/Gegnernamen ergab keine passende Inner-Edania-Abdeckung. Die alten globalen
Einermengen bleiben **Teilbelege**, keine automatisch gültigen Spotlimits.

### BDOlytics

Die öffentlichen Itemdatensätze wurden über den von der Seite verwendeten
`database.getItem`-Endpunkt geprüft, beispielsweise für
[BON Wandering Origin Crystal](https://bdolytics.com/en/db/item/15295).
Die erfolgreichen Antworten enthalten `itemDropsAtGrindspots`, Beschreibungen,
Rezepte und teilweise Kisten-/Questbeziehungen. Sie enthalten keine nutzbaren
Min/Max-Mengen pro NPC für die Inner-Edania-Items. Ein Sammelabruf wurde teilweise
mit HTTP 429 begrenzt; diese fehlenden Antworten wurden nicht als leere
Dropdaten interpretiert. Für alle Items wurde ergänzend die oben verlinkte
BDO-Codex-Seite geprüft. Die bereits vorhandene Prüfung der sechs Trashitems ist
in `artifacts/trash-minimum-research/bdolytics-six-item-source-audit.json` dokumentiert.

### Kisten und Eventgrenzen

Die Broken-Vestige-Beschreibungen belegen zum Beispiel Embers-Mengen 1–5 und
Twilight-Mengen 3–8; die Voidreach-Kiste nennt für Embers jeweils 2–3. Das sind
**Kisteninhalte**. Sie werden nicht in die regulären Monsterdrop-Grenzen übernommen.
Quellen: [Goldroot](https://bdocodex.com/us/item/980139/),
[Ebonmere](https://bdocodex.com/us/item/980140/),
[Everlight](https://bdocodex.com/us/item/980141/),
[Crimsonflare](https://bdocodex.com/us/item/980142/),
[Voidreach](https://bdocodex.com/us/item/980143/).

Die Beschreibung von [[Event] Mysterious Ore](https://bdocodex.com/us/item/1000508/)
verweist für Erwerbsdetails auf die jeweilige Eventseite; ihre Mengenbereiche
betreffen den Inhalt beim Öffnen. Eine aktuell gültige sichere Obergrenze des
Erz-Drops für alle sechs PC-Spots ließ sich daraus nicht gewinnen. Die
[PC-Korrektur vom 7. Mai 2026](https://blackdesert.pearlabyss.com/TR/en-us/News/Notice/Detail?_boardNo=17839)
belegt sogar, dass Erwerbsmengen dieses Eventnamens zwischen Zonen fehlerhaft
abweichen konnten. Weder diese historische Eventversion noch Console-Eventseiten
werden auf die aktuellen PC-Spots übertragen.

## Übernahme bestätigter Angaben

Jede spätere Angabe benötigt Item, Spot, Minimum, Maximum sowie Herkunft
(belegte Quelle oder ausdrückliche Nutzervorgabe). Ein angegebenes Minimum gilt
nur als letzter OCR-Mengenersatz; erfolgreich gelesene kleinere Mengen werden
nicht künstlich angehoben. Ein Maximum begrenzt ein einzelnes erkanntes
Lootereignis, keine Stundensumme und keine manuelle Gesamtkorrektur.

Die Recherche allein behebt keine Mehrfacherkennung desselben Ereignisses über
mehrere Frames: selbst ein Maximum von 1 pro Ereignis würde zwei irrtümlich
erzeugte Ereignisse weiterhin als 2 zählen. Mengenbegrenzung und
Ereignis-Deduplizierung müssen daher getrennt validiert werden.
