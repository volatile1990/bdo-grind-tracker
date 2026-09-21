# Lootpools – Prüfstand 2026-09-14

Der Tracker unterstützt alle 40 konkret angefragten Screenshot-Spots. Für die automatische Erkennung gibt es zusätzlich drei Sammelprofile bei nicht unterscheidbarem Trashloot. [Ergänzte Spots und Grenzen](SCREENSHOT_SPOTS.md). Die bisherigen zwölf Edania-Zonen bleiben erhalten. Inner Edania umfasst Aphrodon Temple,
Hermesia Inner Castle, Magaia Temple, Aresion Temple, Scales of Judgment und Event
Horizon. Outer Edania umfasst Aetherion Castle, Nymphamaré Castle, Orbita Castle,
Tenebraum Castle, Zephyros Castle und Dark Energy Floodlands.
Die [Outer-Edania-Quellen](OUTER_EDANIA_SOURCES.md) dokumentieren deren eigene
Hauptdrops und Trashloot. Beide Trashloot-Items von Dark Energy Floodlands lösen
dieselbe Spoterkennung aus; Tainted Armor Fragment ist der Haupttrash für die
Stundenkennzahl, Faded Dark Energy wird als zusätzlicher Loot mit Silberwert erfasst.
Der Spotfilter soll mögliche Beute durchlassen, nicht nur die prominenten
Hauptdrops. Eine geringe Dropchance ist kein Ausschlussgrund.

## Zusammensetzung

Jeder Spotpool ist die Vereinigung aus:

1. den bisherigen spotspezifischen Hauptdrops und dem jeweiligen Trashloot;
2. dem gemeinsamen HighestTier-Pool ausschließlich bei Inner-Edania-Spots;
3. den gemeinsamen Standard-/Worlddrops unten.

Die separate Eventliste bleibt über den vorhandenen Event-Schalter zuschaltbar.
Ein globaler Drop benötigt diesen Schalter nicht. Die erste passende Trashloot-Zeile
legt weiterhin nur den Spot fest; globale Items verändern den Spot nicht.
Die Ereigniszählung bleibt erhalten. Die Texterkennung berücksichtigt zusätzlich lange Artefaktnamen und verhindert, dass Wortteile in Dehkia oder Gavinya als Mengen gelesen werden.

## Gemeinsame Standard-/Worlddrops

| Englischer OCR-Name | Warum zugelassen / Evidenzgrenze |
|---|---|
| Ancient Spirit Dust | Vom Nutzer im unterstützten Grind beobachtet; zusätzlich als direkter Inner-Edania-Monsterdrop offiziell belegt. |
| Black Stone | Offiziell als direkter Inner-Edania-Monsterdrop belegt; bewusst als gemeinsames Material zugelassen. |
| Caphras Stone | Dieselbe Einordnung wie Black Stone. |
| Empty Picture Frame | Auf Nutzervorgabe vom 11.09.2026 global zugelassen; Drop über Allan Serbins Landschaftsgemälde. Minimum 1 und Maximum 10 pro Drop gelten an allen unterstützten Spots. |
| Intricately Patterned Mystical Shard | Nutzervorgabe und Spieltooltip vom 14.09.2026: seltener Monsterdrop in jeder Region; Minimum = Maximum = 1 an allen unterstützten Spots. |
| Laila's Petal | Pearl Abyss beschreibt Monsterbeute unabhängig von der Region. |
| Pure Black Stone | Historisch als sehr seltener Worlddrop beschrieben; vorsorglich als theoretisch mögliche Beute zugelassen. Kein neuer offizieller Nachweis für jede der sechs Zonen. |

Für die ersten drei Materialien nennt die offizielle NA/EU-Änderung vom
[3. September 2026](https://www.naeu.playblackdesert.com/en-US/News/Detail?countryType=en-US&groupContentNo=10550)
eine gemeinsame Drop-Ratenänderung bei Monstern in Event Horizon. Das belegt
regionale Kampfbeute, aber keine technisch identische Tabelle jedes Monsters
in allen sechs Zonen. Die gemeinsame Zulassung ist eine
bewusst auf mögliche Beute ausgerichtete Produkteinstellung.

Die [offizielle Fairy-GM-Note](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=844)
belegt den regionsübergreifenden Erwerb von Laila's Petal.

Intricately Patterned Mystical Shard heißt auf Deutsch
[Kompliziert gemusterter mystischer Splitter](https://bdocodex.com/de/item/9776/)
(Item 9776). Der vom Nutzer bereitgestellte Spieltooltip nennt 50.000 Silber als
Verkaufswert; dieser ist als fester NPC-Wert hinterlegt. Der vorhandene
Garmoth-Metadatenstand enthält den Itemschlüssel `9776_0`, aber keine Spotzuordnung;
der lokale globale Pool erweitert daher keine Garmoth-Uploadliste.

Der [offizielle Guide zu Allan Serbins Landschaftsgemälde](https://blackdesert.pearlabyss.com/Console/th-TH/Game/Wiki?_masterWikiNo=577)
beschreibt Empty Picture Frame als zusätzlichen Drop bei aktiviertem Gemälde.
Die Aufnahme in den PC-Lootpool und die Grenzen 1–10 beruhen auf der direkten
Nutzervorgabe; der Guide ist kein Beleg dieser Mengen. Der deutsche Itemname
lautet [Leerer Rahmen](https://bdocodex.com/de/item/767249/).

Pure Black Stone ist in einem
[Reprint der damaligen NA/EU-Patchnotes](https://www.blackdesertfoundry.com/14092016-patch-notes-euna-sorc-awakening/)
als Worlddrop beschrieben. Das ist eine historische Sekundärquelle, keine
aktuelle vollständige Inner-Edania-Tabelle. Die Varianten
[11](https://bdocodex.com/us/item/11/),
[12](https://bdocodex.com/us/item/12/),
[13](https://bdocodex.com/us/item/13/) und
[14](https://bdocodex.com/us/item/14/) teilen den sichtbaren Namen.
Deshalb gibt es einen OCR-/Summeneintrag und kein willkürlich gewähltes Variantenicon.

## Hauptdrops, Sonderbedingungen und Ausschlüsse

Die [Inner-Edania-Einführung vom 13. August 2026](https://www.naeu.playblackdesert.com/en-US/News/Detail?groupContentNo=10451)
kennzeichnet ihre Spottabellen ausdrücklich als Main Loot. Diese Tabellen sind
kein Vollständigkeitsnachweis. Die bestehenden Haupt-/HighestTier-Einträge bleiben
erhalten; die neue gemeinsame Liste ergänzt sie.

Die drei neu ergänzten automatischen Erkennungsanker sind die Junkloots aus
denselben offiziellen Tabellen:

| Junkloot | Spot |
|---|---|
| Scorched Belt Ornament | Aresion Temple |
| Elion Follower's Mark | Scales of Judgment |
| Broken Gloves of the Void | Event Horizon |

Der vorhandene Eintrag `[Event] Mysterious Ore` wird an allen Spots mitgezählt.
Das bedeutet nicht, dass dieses Event derzeit aktiv ist. Eine Ereignisliste kann
nicht im Voraus alle zukünftigen Eventnamen enthalten.

Quest-/Bosskisten, NPC-Tausch, Verarbeitung, Sammeln und normale Monsterbeute sind
verschiedene Quellen. Beispielsweise beweisen Caphras Essence oder Gold Bars in
einer wöchentlichen Bosskiste keinen regulären Monsterdrop am Grindspot.
Solche Items wurden nicht allein aufgrund einer Erwähnung in derselben Patchnote
zu Globaldrops erklärt.

`Black Gem Fragment` bleibt entsprechend dem gemeldeten Fehlzählungsfall außerhalb
aller unterstützten Spotpools, aber als Vergleichskandidat im globalen Namensmatcher.
Ein solcher Treffer darf nicht zu Black Crystal Fragment umgedeutet werden.
Der Trashloot eines anderen unterstützten Spots bleibt ebenfalls ausgeschlossen.

## Pflege und Grenzen

Alle Namen in `SharedGlobalItems`, den Spotpools und der Eventliste müssen in
`data/items.en.txt` vorhanden sein. Umgekehrt darf kein gebündelter Name ohne
Poolzuordnung oder ausdrücklich getesteten Ausschluss bleiben.
Die [feste Kanalzuordnung](LOOT_SOURCES.md) legt zusätzlich fest, aus welchem
Droplog ein Item gezählt werden darf. Neue Namen brauchen eine Pool- und
Kanalzuordnung. Historische Tests ohne Kanalfilter prüfen weiterhin die früheren
Mengen- und Poolregeln; eigene Tests sichern die Live-Quellentrennung ab.

Dieser Stand deckt die recherchierten und bisher beobachteten möglichen Drops ab.
Eine vollständige, aktuelle serverseitige Drop-Tabelle war nicht verfügbar;
eine Garantie aller theoretisch möglichen oder künftigen Eventdrops wäre daher
nicht belegt. Neue bestätigte Drops müssen im Vokabular und im passenden Pool
ergänzt werden. Die Windows-OCR-Trefferquote wird durch diese Datenkorrektur
nicht garantiert.

## Direkte Silberdrops

Direkte Währungsdrops (`Silver` / `Silber`) sind global vom Tracking ausgeschlossen.
Sie gehören weder zum OCR-Wortschatz noch zu den Spot-Lootpools, Dropmengen,
Festpreisen oder Garmoth-Dropzuordnungen. Der Silberwert der erfassten Items wird
weiterhin wie bisher berechnet.
