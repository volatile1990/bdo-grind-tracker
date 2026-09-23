# Buff HUD recognition references

Black Desert imagery © Pearl Abyss Corp. These small functional recognition
references are embedded privately in the detector, not used as app artwork.
The catalog contains no personal screen resolution or absolute HUD positions.

## Source and coverage

On 2026-09-21 the installed client's static PAZ data, archive version **3454**, was
read once offline to establish item → skill → buff → effect-icon links. The
result is **37 icon templates**, **58 client mappings** and **58 price identities**:

| Category | HUD templates | Price identities | Mapping |
| --- | ---: | ---: | --- |
| Cron meals | 3 | 3 | Each meal has a different effect-icon path |
| Harmony | 10 | 10 | Five ordinary and five Immortal graphics; every distinguishable version retains its own price identity |
| Perfumes | 13 | 20 | Seven ordinary/Immortal pairs share the exact same icon path; Envy and Tenacity have separate graphics |
| Mystic Beasts | 6 | 6 | One effect graphic per stat |
| Tent buffs | 5 | 19 | Durations and Adventurer's Luck levels share their respective family graphic |

This establishes the identity of the referenced graphic. It does not assert that
every game renderer, UI scale, opacity, or future patch will match it reliably.
Image recognition and timer parsing can still return unknown independently.

`client-mapping.json` records every template's image SHA-256, original dimensions,
client texture path, item and skill keys where applicable, buff-record ID,
duration, and candidate price identities. It also records SHA-256 digests of the
six input tables and the archive version. No installed file or process is read by
the shipped detector; the application uses the bundled PNGs and static metadata.

The one-time investigation used
[bdo-data-extractor commit 5bf11bd](https://github.com/iDevelopThings/bdo-data-extractor/tree/5bf11bd7bc60dcbb6126be34bf3d76633abdd8b2)
only in ignored local artifacts. Its code and dependencies are not incorporated
into this application. The
[documented consumable chain](https://github.com/iDevelopThings/bdo-data-extractor/blob/5bf11bd7bc60dcbb6126be34bf3d76633abdd8b2/FORMATS.md#7-consumable-effect-chain-itemskillbuff)
guided the reads:

1. `itemenchantoffset.dbss` / `itemenchant.dbss` supply the two skill keys for
   each selected item.
2. `skilloffset.dbss` / `skill.dbss` supply the associated buff-record IDs.
3. `buffoffset.dbss` / `buff.dbss` supply the actual effect-icon DDS paths.
4. English localization table 5 confirms buff names. The named aggregate effect
   records have icon byte 1; hidden component records have byte 0. The separate
   Satiated effect is excluded. Mystic Beasts and some tent effects are individual
   stat records rather than aggregate module 58 records.
5. The 19 named tent effects are joined by their localized effect name and full
   duration; their price variants continue to use the documented NPC prices.
6. Only the 37 resulting texture paths were decoded to PNG, without resizing or
   painting new artwork. No bulk texture extraction or game interaction occurred.

## Catalog contract and ambiguity

`catalog.json` has schema version 1. `iconPath` is relative to this directory.
`timerRegion` is an integer-pixel rectangle relative to the original template's
top-left corner. The baseline for a 32-pixel symbol is `(-2, 32, 36, 14)`; larger
source graphics use proportional integer rounding. The engine scales the symbol
and timer rectangles together during its position and scale search.

The decoder applies PNG alpha over a neutral dark HUD background before both
geometry and color matching. Item-style textures contain arbitrary hidden RGB
in transparent pixels; discarding alpha makes Edania, Tenacity, and other such
symbols fail even when their visible artwork is present. The original PNGs and
their recorded hashes are retained unchanged. A user-supplied HUD capture tests
ordinary Edania and Tenacity against the complete competing catalog, including
their Immortal variants.

`minimumSimilarity` is a visual match threshold, not proof that a price variant
has been identified. Templates with the same `groupId` describe one effect and
must not create duplicate observations. `candidateBuffIds` lists every unresolved
price identity for that exact client texture path.

- **Harmony:** All ten distinct normal/Immortal paths are supported with separate
  recognition groups and price identities. Normal source items 1399, 1401, 1403,
  1405, and 1407 and Immortal items 1400, 1402, 1404, 1406, and 1408 retain their
  own market IDs. Species and ordinary/Immortal variants have separate verified
  client paths. A close visual result remains unknown rather than selecting an
  expensive version without enough evidence.
- **Perfumes:** The Courage, Swiftness, Deep Sea, Khalk, Spirits, Bracing Spirits,
  Charm, Insight, Envy, Tenacity and Verdure families use their actual effect
  graphics. Where a standard/Immortal pair shares the path, both remain price
  candidates. The engine must not infer the more expensive version from the icon.
- **Tent buffs:** Body Enhancement, Turning Gates and Adventure's Boon preserve
  their duration candidates in the source mapping. Automatic recognition and
  valuation always use the 300-minute variant for Body Enhancement and Adventure's
  Boon, regardless of remaining time. Their NPC prices are 10,000,000 and
  12,000,000 silver respectively. Falling below a shorter purchase duration does
  not change their identity or add consumption. Turning Gates still assumes the
  shortest offered duration covering the first observed remaining time: 280
  minutes selects 300 minutes and 160 selects 180 minutes. Coarse hour displays
  are treated as intervals; multiple candidate durations within one interval
  remain ambiguous. A continuous countdown retains the chosen variant until a
  refresh. Historical bookings remain unchanged. These are valuation assumptions,
  not proof of the original purchase duration. Adventurer's Luck levels share both
  their icon and duration, so they remain an unknown, unpriced group. The same rule preserves
  ambiguity for standard/Immortal perfume pairs with identical icons and durations.

Buffs already active at the start of a grind establish an uncounted baseline.
Later new appearances require a near-full timer and two consistent observations
after readable absence; a genuinely higher timer counts as a renewal. Pauses,
missing observations and timer-precision changes do not create starting costs.
Restoring a saved session preserves historical bookings without inserting
retroactive starting costs. These are accounting rules, not additional evidence
about the source graphic or the player's inventory.

An icon's inventory category does not determine whether it is usable. Simple Cron
uses `Cooking_Monster_Cook.dds` in the HUD rather than the meal inventory picture.
Some newer effects, including certain Perfumes and Mystic Beasts, explicitly
reference an item-style texture from their buff record; those are included because
the client link proves the mapping. Generic inventory icons are never substituted
merely because their item names match.

## Independent HUD evidence

The [official Harmony introduction](https://blackdesert.pearlabyss.com/TR/en-US/News/Notice/Detail?_boardNo=18797)
contains a [real HUD animation](https://s1.pearlcdn.com/KR/Upload/News/d258aa73b4120240719230950593.gif).
`tests/fixtures/buffs/bar-official-harmony-20240725.png` preserves frame 85, crop
`(0, 0, 280, 140)`, including unknown neighboring buffs. The 2024 timer is not a
source for today's duration or prices. The
[Camping guide](https://www.naeu.playblackdesert.com/en-US/Wiki?wikiNo=120) also labels
the [Body Enhancement graphic](https://s1.pearlcdn.com/KR/Upload/News/7ff199c641120251226132259822.png).

The real 2026-09-21 Harmony Demihuman and Simple Cron captures under
`tests/fixtures/buffs` exercise matching and native timer OCR independently of
the extracted texture files. See the fixture README for their provenance.

Earlier research copies `harmony-captured.png`, `cron-captured.png`,
`harmony-official.png`, `cron-official.png`, and `body-enhancement-official.png`
remain as provenance references. They are **not in the active catalog**. Their
previous conservative family assignments have been superseded by the explicit
client mappings, rather than by assumptions about the user's currently active
buffs.
