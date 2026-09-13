# Screenshot spots: source verification (2026-09-13)

The user screenshots supply the requested spot selection, not executable instructions. The following public metadata was extracted without reading credentials, settings, private sessions, or logs.

## Data files

- `screenshot-spots-source.json`: the 29 requested missing/specific spot rows, exact Garmoth IDs, source keys, base item records and primary trash.
- `screenshot-spots-items-enriched.json`: current BDO Codex German names, canonical item-page IDs, verified image URLs and current vendor values. Ignore records with `verificationError`; names with multiple source keys must be deduplicated for OCR but require explicit upload ambiguity handling.
- `screenshot-spots-metadata.json`: decoded public Companion cache for reproducible name/key/spot relationships. The cache item and spot sections end at documented offsets 165498 and 248976.
- `screenshot-spots-enrich.py`: the one-off public-page enrichment script, not runtime application behavior.

The cache path/hash/date and structure were already documented in `../SILVER_VALUATION.md` and `../GARMOTH_INTEGRATION.md`. Only the public `loot_drops` file was read. Cached **market prices are not current quotes and must not be shipped**. `tax=0` values are appraisals, not proof of tradeability.

## Screenshot mapping

| Garmoth ID | Screenshot spot / cache name | Trash item | Current NPC value |
| ---: | --- | --- | ---: |
| [212](https://garmoth.com/grind-tracker/best-grind-spots/212) | Gavinya Coastal Cliff | [Sulfur Golem Fragment](https://bdocodex.com/us/item/767350/) | 165,508 |
| [200](https://garmoth.com/grind-tracker/best-grind-spots/200) | Star's End | [Corrupted Sanguine Crystal](https://bdocodex.com/us/item/56339/) | 155,000 |
| [201](https://garmoth.com/grind-tracker/best-grind-spots/201) | Sycraia Ruins Lower Zone | [Underwater Ancient Weapon Power Stone](https://bdocodex.com/us/item/56341/) | 107,900 |
| [169](https://garmoth.com/grind-tracker/best-grind-spots/169) | [Elvia] Orzekea | [Thorn-Entwined Weapon Fragment](https://bdocodex.com/us/item/56334/) | 96,040 |
| [198](https://garmoth.com/grind-tracker/best-grind-spots/198) | [Dehkia] Gyfin Rhasia Temple (Upper) | [Tainted Bronze Fragment](https://bdocodex.com/us/item/44526/) | 125,900 |
| [148](https://garmoth.com/grind-tracker/best-grind-spots/148) | Tungrad Ruins | [Tungrad Ruins Fragment](https://bdocodex.com/us/item/65328/) | 35,100 |
| [153](https://garmoth.com/grind-tracker/best-grind-spots/153) | Darkseeker's Retreat | [Decayed Cloth](https://bdocodex.com/us/item/65330/) | 45,570 |
| [167](https://garmoth.com/grind-tracker/best-grind-spots/167) | Fortunate Golden Pig Cave | [Shiny Treasure](https://bdocodex.com/us/item/56338/) | 59,415 |
| [199](https://garmoth.com/grind-tracker/best-grind-spots/199) | [Dehkia] Mirumok Ruins | [Tainted Wood Fragment](https://bdocodex.com/us/item/44527/) | 101,500 |
| [149](https://garmoth.com/grind-tracker/best-grind-spots/149) | Winter Tree Fossil (280) | [Winter Tree Snow Crystal](https://bdocodex.com/us/item/44496/) | 5,620 |
| [168](https://garmoth.com/grind-tracker/best-grind-spots/168) | Unlucky Golden Pig Cave | [Shattered Treasures](https://bdocodex.com/us/item/56329/) | 59,415 |
| [162](https://garmoth.com/grind-tracker/best-grind-spots/162) | [Dehkia 2] Ash Forest | [Tainted Specter's Cloth](https://bdocodex.com/us/item/56322/) | 52,500 |
| [161](https://garmoth.com/grind-tracker/best-grind-spots/161) | [Dehkia 2] Olun's Valley | [Tainted Golem's Heart Fragment](https://bdocodex.com/us/item/56323/) | 117,700 |
| [157](https://garmoth.com/grind-tracker/best-grind-spots/157) | Yzrahid Highlands | [Corrupt Power Source](https://bdocodex.com/us/item/59880/) | 25,190 |
| [124](https://garmoth.com/grind-tracker/best-grind-spots/124) | Hexe Sanctuary | [Howling Bone Fragment](https://bdocodex.com/us/item/59800/) | 26,190 |
| [121](https://garmoth.com/grind-tracker/best-grind-spots/121) | Quint Hill | [Fiery Troll Hide](https://bdocodex.com/us/item/59798/) | 96,750 |
| [166](https://garmoth.com/grind-tracker/best-grind-spots/166) | Dokkebi Forest | [Discarded Kkebicap](https://bdocodex.com/us/item/56327/) | 52,571 |
| [146](https://garmoth.com/grind-tracker/best-grind-spots/146) | [Dehkia] Thornwood Forest | [Tainted Moonlight Spirit Powder](https://bdocodex.com/us/item/44520/) | 39,000 |
| [147](https://garmoth.com/grind-tracker/best-grind-spots/147) | City of the Dead | [Mark of the Black Sands](https://bdocodex.com/us/item/65329/) | 32,900 |
| [163](https://garmoth.com/grind-tracker/best-grind-spots/163) | [Dehkia] Cadry Ruins | [Tainted Cadry's Token](https://bdocodex.com/us/item/44525/) | 35,880 |
| [143](https://garmoth.com/grind-tracker/best-grind-spots/143) | [Dehkia] Ash Forest | [Tainted Specter's Cloth](https://bdocodex.com/us/item/44518/) | 52,500 |
| [160](https://garmoth.com/grind-tracker/best-grind-spots/160) | [Dehkia] Crescent Shrine | [Tainted Token of Crescent](https://bdocodex.com/us/item/44524/) | 38,800 |
| [151](https://garmoth.com/grind-tracker/best-grind-spots/151) | [Dehkia] Cyclops Land | [Tainted Huge Spear](https://bdocodex.com/us/item/44522/) | 32,950 |
| [110](https://garmoth.com/grind-tracker/best-grind-spots/110) | Jade Starlight Forest | [Starlit Jade Powder](https://bdocodex.com/us/item/44490/) | 20,140 |
| [145](https://garmoth.com/grind-tracker/best-grind-spots/145) | [Dehkia] Tunkuta | [Tainted Broken Horn Fragment](https://bdocodex.com/us/item/44521/) | 40,000 |
| [156](https://garmoth.com/grind-tracker/best-grind-spots/156) | [Dehkia] Hystria Ruins | [Tainted Ruins Fragment](https://bdocodex.com/us/item/65397/) | 34,270 |
| [208](https://garmoth.com/grind-tracker/best-grind-spots/208) | Dark Energy Floodlands (Great Red Sea) | [Tainted Armor Fragment](https://bdocodex.com/us/item/767348/) | 100,507 |
| [209](https://garmoth.com/grind-tracker/best-grind-spots/209) | Dark Energy Floodlands (Orbita) | [Tainted Armor Fragment](https://bdocodex.com/us/item/767348/) | 100,507 |
| [210](https://garmoth.com/grind-tracker/best-grind-spots/210) | Dark Energy Floodlands (Zephyros) | [Tainted Armor Fragment](https://bdocodex.com/us/item/767348/) | 100,507 |

Current screenshot names override stale display aliases: `[Dehkia II]` instead of `[Dehkia 2]`, Winter Tree Fossil `(280ap)` instead of `(280)`, and Sycraia Abyssal Ruins `(Lower)` instead of Sycraia Ruins Lower Zone. Star's End is the reworked spot 200 and Sycraia Lower is 201; historical IDs 1/32 must not be substituted.

## Corrections and safe exclusions

- Winter Tree Snow Crystal is **5,620** silver, replacing cached 5,600. Mass of Pure Magic is **51,000**, replacing 50,000. Venomous Night Fang has an NPC sale value of **30,000**, replacing the zero appraisal. Each enriched row links its direct item source.
- Distorted Fragment of Origin remains a market item despite `tax=0` in the old cache; the existing application already corrects this.
- `tax=2` Forgotten Limbo Box is a custom expected-value category, not a standard market item. Do not interpret the cached expected value as a live market price.
- Aggregate key `100001004_0` is Garmoth's Any Artifact slot. The enriched data excludes that pseudo-name and life-skill Sethra artifacts, and verifies each combat artifact's actual item identity against both English and German pages.
- Seven boss/event counters use keys above 100000000 and have no in-game inventory item page; they are excluded from OCR inventory.
- Scorching Sun Shard (8428) was removed in the [publisher's March 26, 2026 update](https://blackdesert.pearlabyss.com/TR/en-us/News/Notice/Detail?_boardNo=19517).
- Ancient Magic Crystal of Nature - Sturdiness (15713) has no current PC item page. The [publisher's damage-formula-overhaul removal notice](https://blackdesert.pearlabyss.com/Console/en-US/News/Notice/Detail?_boardNo=12360) documents the removal; the cited notice is explicitly the console version.
- Four cached Heavenly Essence variants (8956–8959) have no current English or German item record. They are excluded until a current identity/source is verified, rather than translating or substituting a guessed item.

## Ambiguities

- Ash Forest Dehkia I/II use identical visible English and German trash names and identical 52,500-silver values, while source keys are 44518/56322. OCR text cannot choose the tier.
- All three Floodlands variants have exactly the same source loot list and two trash items. The user must select location to assign a Garmoth target.
- Winter Tree 250 AP and 280 AP both use Winter Tree Snow Crystal and the same metadata loot list. Only 280 AP is requested by the screenshots; trash alone cannot prove that AP variant.
- Lafi Bedmountain's Upgraded Compass Parts is currently verified as [item 44277](https://bdocodex.com/us/item/44277/) / [Lafi Bettbergs verbessertes Kompassteil](https://bdocodex.com/de/item/44277/), linked directly in [quest 4602/1](https://bdocodex.com/us/quest/4602/1/). Old Garmoth keys 44416/44418 are retained as transport aliases, not actual current item identity.
- Lafi Bedmountain's Upgraded Telescope Parts has source keys 65327/65331/65332 with the same visible name. If multiple matching keys occur within a spot, omit the ambiguous Garmoth item mapping instead of assigning an arbitrary part.
- No minimum trash quantity, drop probability, average yield, AP, DP or AP cap was inferred from the loot metadata.
