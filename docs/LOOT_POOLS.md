# Lootpools – 0.6.3, Prüfstand 2026-09-05

Der Tracker unterstützt weiterhin Aphrodon Temple, Hermesia Inner Castle und
Magaia Temple. Der Spotfilter soll mögliche Beute durchlassen, nicht nur die
prominenten Hauptdrops. Eine geringe Dropchance ist kein Ausschlussgrund.

## Zusammensetzung

Jeder Spotpool ist die Vereinigung aus:

1. den bisherigen spotspezifischen Hauptdrops und dem jeweiligen Trashloot;
2. dem gemeinsamen HighestTier-Pool;
3. den gemeinsamen Standard-/Worlddrops unten.

Die separate Eventliste bleibt über den vorhandenen Event-Schalter zuschaltbar.
Ein globaler Drop benötigt diesen Schalter nicht. Die erste passende Trashloot-Zeile
legt weiterhin nur den Spot fest; globale Items verändern den Spot nicht.
Die OCR-, Matching- und Zählalgorithmen sind unverändert.

## Gemeinsame Standard-/Worlddrops

| Englischer OCR-Name | Warum zugelassen / Evidenzgrenze |
|---|---|
| Ancient Spirit Dust | Vom Nutzer im unterstützten Grind beobachtet; zusätzlich als direkter Inner-Edania-Monsterdrop offiziell belegt. |
| Black Stone | Offiziell als direkter Inner-Edania-Monsterdrop belegt; bewusst als gemeinsames Material zugelassen. |
| Caphras Stone | Dieselbe Einordnung wie Black Stone. |
| Laila's Petal | Pearl Abyss beschreibt Monsterbeute unabhängig von der Region. |
| Pure Black Stone | Historisch als sehr seltener Worlddrop beschrieben; vorsorglich als theoretisch mögliche Beute zugelassen. Kein neuer offizieller Nachweis für jeden der drei Spots. |

Für die ersten drei Materialien nennt die offizielle NA/EU-Änderung vom
[3. September 2026](https://www.naeu.playblackdesert.com/en-US/News/Detail?countryType=en-US&groupContentNo=10550)
eine gemeinsame Drop-Ratenänderung bei Monstern in Event Horizon. Das belegt
regionale Kampfbeute, aber keine technisch identische Tabelle jedes Monsters
in Aphrodon, Hermesia und Magaia. Die Zulassung für alle drei ist eine
bewusst auf mögliche Beute ausgerichtete Produkteinstellung.

Die [offizielle Fairy-GM-Note](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=844)
belegt den regionsübergreifenden Erwerb von Laila's Petal.

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

Der vorhandene Eintrag `[Event] Mysterious Ore` bleibt optional verfügbar.
Das bedeutet nicht, dass dieses Event derzeit aktiv ist. Eine Ereignisliste kann
nicht im Voraus alle zukünftigen Eventnamen enthalten.

Quest-/Bosskisten, NPC-Tausch, Verarbeitung, Sammeln und normale Monsterbeute sind
verschiedene Quellen. Beispielsweise beweisen Caphras Essence oder Gold Bars in
einer wöchentlichen Bosskiste keinen regulären Monsterdrop am Grindspot.
Solche Items wurden nicht allein aufgrund einer Erwähnung in derselben Patchnote
zu Globaldrops erklärt.

`Black Gem Fragment` bleibt entsprechend dem gemeldeten Fehlzählungsfall außerhalb
dieser drei Spotpools, aber als Vergleichskandidat im globalen Namensmatcher.
Ein solcher Treffer darf nicht zu Black Crystal Fragment umgedeutet werden.
Der Trashloot eines anderen unterstützten Spots bleibt ebenfalls ausgeschlossen.

## Pflege und Grenzen

Alle Namen in `SharedGlobalItems`, den Spotpools und der Eventliste müssen in
`data/items.en.txt` vorhanden sein. Umgekehrt darf kein gebündelter Name ohne
Poolzuordnung oder ausdrücklich getesteten Ausschluss bleiben.
Regressionstests prüfen globale Drops nach Spotlock im normalen und Rare-Kanal.

Dieser Stand deckt die recherchierten und bisher beobachteten möglichen Drops ab.
Eine vollständige, aktuelle serverseitige Drop-Tabelle war nicht verfügbar;
eine Garantie aller theoretisch möglichen oder künftigen Eventdrops wäre daher
nicht belegt. Neue bestätigte Drops müssen im Vokabular und im passenden Pool
ergänzt werden. Die Windows-OCR-Trefferquote wird durch diese Datenkorrektur
nicht garantiert.
