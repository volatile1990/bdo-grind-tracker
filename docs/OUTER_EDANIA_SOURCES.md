# Outer Edania catalog sources

Verified on 2026-09-13. The tracker adds the five Outer Edania castles and the
three-player Dark Energy Floodlands alongside the six existing Inner Edania
spots. The Floodlands locations share one loot pool and two junk items.

## Loot pools

| Source | Catalog evidence |
| --- | --- |
| [Pearl Abyss, 2025-08-21](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=8017) | Launch tables for Aetherion, Nymphamaré, and Orbita: crystal tiers WON/BON/JIN, four Deboreka accessories, unique junk items. Aetherion has Primordial Fragment; Nymphamaré and Orbita have Crystallized Energy of Endtimes and the Silent/Distorted Origin fragments and crystals. |
| [Pearl Abyss, 2025-09-11](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=8063) | Tenebraum and Zephyros main loot: HAN crystals, Origin protection items, Endtimes energy, both Herald crystals, four Deboreka accessories, and unique junk items. |
| [Pearl Abyss, 2026-03-19](https://blackdesert.pearlabyss.com/TR/en-us/News/Notice/Detail?_boardNo=19077) | Floodlands main loot: four Deboreka accessories, Flawless Herald's Crystal, HAN Crystal of Dusky Ruin, Silent Crystal of Origin; two junk items. |
| [Pearl Abyss, 2026-07-02](https://blackdesert.pearlabyss.com/ASIA//Game/Wiki?_masterWikiNo=84) | Live update text adds Silent/Distorted Fragment of Origin and Silent/Distorted Crystal of Origin to Aetherion. This rolling wiki may later contain another update. |
| [Pearl Abyss, 2026-08-27](https://blackdesert.pearlabyss.com/TR/en-Us/News/Notice/Detail?_boardNo=19749) | Zephyros receives Sealed Black Magic Crystal and loses the `#HighestTier` exclusive loot. The Outer Edania pools therefore do not inherit the existing Inner Edania highest-tier pool. |

Existing shared global items and the supported event pool remain available at
each added spot. Source tables describe main loot and are not an exhaustive
proof of every possible world/event drop. Spot-specific main loot is kept
separate: for example, the Floodlands does not inherit HAN Crystal of Ruin,
Herald's Crystal, or Silent Fragment of Origin from Tenebraum/Zephyros.

## Junk items and language identity

The official launch/update tables above supply junk-item associations and vendor
prices. The English canonical names and German client names use the same item
IDs. Every new localized entry in `data/items.de.json` records its BDO Codex
item page and verification date. The new entries were read from those pages;
translations were not guessed.

| Spot | Canonical junk item | German name | Item ID | Vendor silver |
| --- | --- | --- | --- | ---: |
| Aetherion Castle | Chilled Soul Piece | Eisiges Seelenstück | [767244](https://bdocodex.com/de/item/767244/) | 105,640 |
| Nymphamaré Castle | Contaminated Coral Piece | Kontaminiertes Korallenstück | [767245](https://bdocodex.com/de/item/767245/) | 116,200 |
| Orbita Castle | Lightlost Core | Lichtloser Kern | [767247](https://bdocodex.com/de/item/767247/) | 140,600 |
| Tenebraum Castle | Ancient Soldier Fragment | Fragment eines vorzeitlichen Soldaten | [767246](https://bdocodex.com/de/item/767246/) | 147,630 |
| Zephyros Castle | Hardened Lava Chunk | Verhärteter Lavabrocken | [767248](https://bdocodex.com/de/item/767248/) | 126,980 |
| Dark Energy Floodlands | Tainted Armor Fragment | Besessenes Rüstungsfragment | [767348](https://bdocodex.com/de/item/767348/) | 100,507 |
| Dark Energy Floodlands | Faded Dark Energy | Verblasste dunkle Energie | [767349](https://bdocodex.com/de/item/767349/) | 597,680 |

Both Floodlands junk items identify the same spot for the automatic lock and
contribute to its confirmed-drop evidence. The user's 2026-09-13 workbook sets
the minimum to one for all seven Outer Edania junk items. These explicit bounds
override the historical quantity-one rejection for Chilled Soul Piece and
Contaminated Coral Piece. English, German, normalized names and matched OCR
typos therefore accept one consistently; the legacy filter remains available
for items without an explicit minimum of one.

## Quantity bounds

All 110 previously unverified pairs were supplied by the user in
[`Dropmengen-Outer-Edania-Eingabe.xlsx`](../data/Dropmengen-Outer-Edania-Eingabe.xlsx)
and imported on 2026-09-13. Official patch notes remain evidence for loot pools,
not for these user-supplied quantity limits. Empty Picture Frame retains the
existing explicit global user rule (1–10, 2026-09-11).

The imported Inner Edania workbook values and their source hash remain
unchanged. Its hash describes the original workbook, not the expanded JSON
catalog. The Outer Edania workbook has its own `additionalSources` record with
SHA-256 `546151963fd3173ea2ebdbb55c655fbe26816ea51fbeabe83b14ae75b21e1384`.
Before a spot is locked, a shared item uses the lowest minimum and highest
maximum across its supported spots. After a spot is locked, its own bounds apply.

The 110 imported item/spot pairs cover the six Outer Edania spots:
Aetherion 18, Nymphamaré 18, Orbita 18, Tenebraum 20, Zephyros 21, and Floodlands
15. Bounds apply to canonical item/spot identities, so each supplied pair is
used for both German and English. Limits describe one displayed drop, including
possible quantity bonuses, not the accumulated session total.
All 329 catalog pairs now have both bounds; 255 are fixed one-unit drops.

## Existing Inner Edania scope

This change adds the missing Outer Edania spots and retains the existing Inner
Edania pools. A separate current-data difference was found in the
[NA/EU update of 2026-09-10](https://www.naeu.playblackdesert.com/en-US/News/Detail?countryType=en-US&groupContentNo=10577):
Aphrodon loses the highest-tier loot and gains Sealed Black Magic Crystal. That
existing-pool update is not applied here, so the existing Aphrodon allowlist
still reflects the earlier catalog and workbook coverage.
