# Nachrichten des Rotation Monitors

Der Monitor unterstützt fünf konkrete Profile: **Hermesia, Aphrodon, Magaia und
Event Horizon sowie Zephyros**. Meldungen werden als Ereignisse an die gemeinsame
[Rotationsplattform](ROTATION_PLATFORM.md) übergeben. Der Textkatalog erweitert
die Erkennung auf EN, DE, FR und SP; er definiert keine zusätzliche Phasenfolge.

## Meldungsumfang

| Profil | Bannertexte pro Sprache | Zusätzliche Namen pro Sprache | Ereignisarten |
| --- | ---: | ---: | --- |
| Hermesia | 11 | 0 | Offering, Porter, Drakania, Drakania-Tod, Transfer, Mineneintritt, zweite Minenwelle, Mine beendet, Drache, AFK, Failure |
| Aphrodon | 9 | 0 | Setup, kleine/große Vogelscheuche, Neustart, Hog, Agris, AFK, Ende, Failure |
| Magaia | 16 | 2 | Start, Fragment, Prayer, Knight, Doubt, Sacred Power, AFK, Ende, Away, Back, Failure; Tears und Priest in der Namensleiste |
| Event Horizon | 11 | 0 | Anomaly, Halted, Reception, Debris, Distortion, Spacetime, Expansion, Boss, Boss-Tod, Ende |
| Zephyros | 7 | 0 | Aktivierung, Ritter/Wellen, Beelzebub, AFK, Bereitschaft |

54 Bannertexte plus zwei Namen, jeweils in vier Sprachen, ergeben **224 Regeln**.
Textvarianten derselben Ereignisart zählen einmal als Ereignis. Wiederholte
Meldungen werden nur dort gezählt, wo das bestehende Profil dies verlangt.

Die Laufzeitdaten stehen in `data/rotation-message-patterns.json`:

- `spot`, `kind`: registriertes Profil und Ereignis;
- `language`: `en`, `de`, `fr` oder `sp`;
- `text`: vollständiger Referenztext;
- `phrase`: Suchphrase bzw. Muster;
- `mode`: Teilphrase mit Wortgrenzen, vollständige Zeile oder mehrteiliges Muster.

Die englische Erkennung behält zusätzlich die bereits getesteten kurzen
OCR-Anker. Reihenfolge, zulässige Wiederholungen, Sonderereignisse und Timeouts
stehen weiterhin in den Profildefinitionen, nicht in der Reihenfolge der JSON-Zeilen.

## Bedienung und Verifikation

[Sprachwahl, Windows-OCR-Pakete und Erkennungsgrenzen](ROTATION_MESSAGES_RUNTIME.md)
beschreibt die Konfiguration. Die Sprache wird vor einer neuen Session gewählt
und mit dem Checkpoint gespeichert. Loot-Erkennung und Oberflächensprache sind
separate Einstellungen.

Für Hermesia, Aphrodon, Magaia und Event Horizon fehlen jeweils neue DE-/FR-/SP-
Spielaufnahmen. Die automatisierten Texttests bestätigen die Verarbeitung der
Referenztexte; tatsächliche Zeilenumbrüche, Crops, Timing und OCR-Lesbarkeit dieser
Sprachfassungen sind damit nicht bestätigt. Die bestehenden Crops und Phasenfolgen
wurden beibehalten.

Weitere Spots sind in der [Guide-Recherche](ROTATION_GUIDE_RESEARCH.md) bewertet.
Unsichere Zuordnungen aktivieren keine zusätzlichen Rotationsprofile.

Zephyros ergänzt den in einer englischen Stundenaufnahme belegten Normalablauf;
Details und noch ungeprüfte Sonderfälle stehen in [ZEPHYROS_ROTATION.md](ZEPHYROS_ROTATION.md).
