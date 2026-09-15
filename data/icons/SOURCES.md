# Item icon sources

The local PNG files were converted without visual modification
from the corresponding BDO Codex WebP item icons. The original set was collected
on 2026-09-02; additions are dated below and in `catalog.json`. Item names and
source pages for the original set:

| Item | BDO Codex source |
| --- | --- |
| Ancient Spirit Dust | https://bdocodex.com/us/item/721002/ |
| Apeiron Earring | https://bdocodex.com/us/item/11898/ |
| Apeiron Ring | https://bdocodex.com/us/item/12144/ |
| BON Origin Shard | https://bdocodex.com/us/item/821431/ |
| BON Wandering Origin Crystal | https://bdocodex.com/us/item/15295/ |
| Black Crystal Fragment | https://bdocodex.com/us/item/980128/ |
| Black Gem Fragment | https://bdocodex.com/us/item/4999/ |
| Black Stone | https://bdocodex.com/us/item/16001/ |
| Broken Vestige of Ebonmere | https://bdocodex.com/us/item/980140/ |
| Caphras Stone | https://bdocodex.com/us/item/721003/ |
| Corrupt Oil of Immortality | https://bdocodex.com/us/item/1178/ |
| Embers of Ynix - Helmet | https://bdocodex.com/us/item/821461/ |
| [Event] Mysterious Ore | https://bdocodex.com/us/item/1000508/ |
| Fusion Shard | https://bdocodex.com/us/item/821471/ |
| Laila's Petal (added 2026-09-05) | https://bdocodex.com/us/item/54031/ |
| Nev's Fragment | https://bdocodex.com/us/item/821460/ |
| Silent Crystal of Origin | https://bdocodex.com/us/item/761803/ |
| Silent Fragment of Origin | https://bdocodex.com/us/item/821318/ |
| Sunset Primordial Luster - Edana | https://bdocodex.com/us/item/821459/ |
| Sunset Primordial Pigment - Edana | https://bdocodex.com/us/item/767353/ |
| Twilight of the End - Earring | https://bdocodex.com/us/item/821422/ |
| Twilight of the End - Ring | https://bdocodex.com/us/item/821423/ |

Black Desert and its game assets are property of Pearl Abyss. BDO Codex and
Pearl Abyss do not endorse this project. The icons are included only as local
visual identifiers in the tracker UI.

The Laila's Petal icon was added on 2026-09-05 using the exact-name BDO Codex
lookup for item 54031. Its original WebP was converted to PNG without visual
modification; its source URL, dimensions, and checksum are recorded in
`catalog.json`.

## Three-spot coverage additions — 2026-09-05

The following 20 missing icons were added using exact, case-sensitive English
item matches from BDO Codex's public item lookup. For every name, all exact
matches shared one icon; no approximate-name or arbitrary variant selection was
used. The existing 22 PNG files and their metadata were preserved. Downloads and
lossless WebP-to-PNG conversion used `tools/Download-ItemIcons.ps1` with a separate
filtered staging catalog; only the verified additions were merged into the main
catalog. Each added entry records its source page, original asset URL, SHA-256,
dimensions and collection timestamp. Images retain their authentic game
appearance; none were generated, recolored, redrawn or enlarged.

| Item | BDO Codex source |
| --- | --- |
| Apeiron Belt | https://bdocodex.com/us/item/12298/ |
| Branch of Abundance | https://bdocodex.com/us/item/980127/ |
| Broken Vestige of Everlight | https://bdocodex.com/us/item/980141/ |
| Broken Vestige of Goldroot | https://bdocodex.com/us/item/980139/ |
| Crimson Primordial Luster - Sovereign | https://bdocodex.com/us/item/821341/ |
| Crimson Primordial Pigment - Sovereign | https://bdocodex.com/us/item/767293/ |
| Elion Follower's Helmet | https://bdocodex.com/us/item/980129/ |
| Embers of Ynix - Armor | https://bdocodex.com/us/item/821462/ |
| Embers of Ynix - Shoes | https://bdocodex.com/us/item/821464/ |
| JIN Origin Shard | https://bdocodex.com/us/item/821432/ |
| JIN Wandering Origin Crystal | https://bdocodex.com/us/item/15296/ |
| Refined Essence of Devouring | https://bdocodex.com/us/item/767338/ |
| Refined Origin of Hunger | https://bdocodex.com/us/item/767337/ |
| Twilight of the End - Belt | https://bdocodex.com/us/item/821424/ |
| Violet Primordial Luster - Edana | https://bdocodex.com/us/item/821343/ |
| Violet Primordial Luster - Sovereign | https://bdocodex.com/us/item/821342/ |
| Violet Primordial Pigment - Edana | https://bdocodex.com/us/item/767296/ |
| Violet Primordial Pigment - Sovereign | https://bdocodex.com/us/item/767294/ |
| WON Origin Shard | https://bdocodex.com/us/item/821430/ |
| WON Wandering Origin Crystal | https://bdocodex.com/us/item/15294/ |

Coverage is now **40 of 41 distinct items across Aphrodon, Hermesia and Magaia**,
plus the previously included Black Gem Fragment and event ore: **42 catalog
icons in total**. All three trash-loot names have their own exact icon. The sole
supported-spot exception is the intentionally ambiguous `Pure Black Stone`, as
explained below. `IconCoverageTests` verifies packaged files, every checksum and
dimension, unique names, expected item IDs and the actual UI repository lookup.

## Remaining Inner Edania additions — 2026-09-06

The remaining three zones added 14 new exact-name icons. They were downloaded
with the same script and converted by ImageMagick without recoloring, redrawing
or enlargement. BDO Codex serves `Broken Gloves of the Void` as 48 × 44 pixels;
that original aspect ratio is retained and the UI scales it into the standard
icon rectangle. Every other new source is 44 × 44 pixels.

| Item | BDO Codex source |
| --- | --- |
| Apeiron Necklace | https://bdocodex.com/us/item/11733/ |
| Broken Gloves of the Void | https://bdocodex.com/us/item/980132/ |
| Broken Vestige of Crimsonflare | https://bdocodex.com/us/item/980142/ |
| Broken Vestige of Voidreach | https://bdocodex.com/us/item/980143/ |
| Elion Follower's Mark | https://bdocodex.com/us/item/980130/ |
| Embers of Ynix - Gloves | https://bdocodex.com/us/item/821463/ |
| HAN Origin Shard | https://bdocodex.com/us/item/821433/ |
| HAN Wandering Origin Crystal | https://bdocodex.com/us/item/15297/ |
| Scorched Belt Ornament | https://bdocodex.com/us/item/980131/ |
| Twilight of the End - Necklace | https://bdocodex.com/us/item/821421/ |
| White Primordial Luster - Edana | https://bdocodex.com/us/item/821420/ |
| White Primordial Luster - Sovereign | https://bdocodex.com/us/item/821419/ |
| White Primordial Pigment - Edana | https://bdocodex.com/us/item/767344/ |
| White Primordial Pigment - Sovereign | https://bdocodex.com/us/item/767343/ |

Coverage is now **54 of 55 distinct items across all six Inner Edania zones**,
plus Black Gem Fragment and the event ore: **56 catalog icons in total**. The
sole supported-spot exception remains the ambiguous `Pure Black Stone` below.

`Pure Black Stone` deliberately has no single icon entry. The visible name
is shared by several buff variants with different icons (for example, BDO Codex
[11](https://bdocodex.com/us/item/11/),
[12](https://bdocodex.com/us/item/12/),
[13](https://bdocodex.com/us/item/13/), and
[14](https://bdocodex.com/us/item/14/)). The UI uses its generic fallback rather
than assigning one arbitrary variant's icon to every recognized stone.

`tools/Download-ItemIcons.ps1` can refresh icons from exact item matches returned
by BDO Codex. It rejects names with multiple distinct icons, including
`Pure Black Stone`, and replaces the target catalog. For selected additions,
use a filtered `-ItemsFile` and separate output/catalog paths, then merge only
the verified new metadata; do not replace this catalog with a partial refresh.

2026-09-08: Pure Black Stone uses the AP variant icon from https://bdocodex.com/us/item/13/ as representative for the shared item name.

2026-09-11: Empty Picture Frame (item 767249, German name Leerer Rahmen) was
added from https://bdocodex.com/us/item/767249/. The page's exact icon
https://bdocodex.com/items/new_icon/03_etc/03_quest_item/00066292.webp was converted
to PNG without changing its 44×44 pixels. The icon's filename refers to a reused
game asset and is not the new item's ID. The current catalog contains 58 icons.

## Outer Edania additions — 2026-09-13

The 26 additional icons were resolved by exact English name with
`tools/Download-ItemIcons.ps1`, downloaded into a separate staging directory,
and converted from the original BDO Codex WebP assets to PNG without visual
modification. Existing icon bytes and metadata were preserved. Every addition
records its source item ID, asset URL, checksum, original dimensions and date
in `catalog.json`. The catalog now contains 84 icons.

| Item | BDO Codex source |
| --- | --- |
| Ancient Soldier Fragment | https://bdocodex.com/us/item/767246/ |
| BON Crystal of Dusky Ruin | https://bdocodex.com/us/item/15287/ |
| BON Crystal of Ruin | https://bdocodex.com/us/item/821254/ |
| Chilled Soul Piece | https://bdocodex.com/us/item/767244/ |
| Contaminated Coral Piece | https://bdocodex.com/us/item/767245/ |
| Crystallized Energy of Endtimes | https://bdocodex.com/us/item/821252/ |
| Deboreka Belt | https://bdocodex.com/us/item/12276/ |
| Deboreka Earring | https://bdocodex.com/us/item/11882/ |
| Deboreka Necklace | https://bdocodex.com/us/item/11653/ |
| Deboreka Ring | https://bdocodex.com/us/item/12094/ |
| Distorted Crystal of Origin | https://bdocodex.com/us/item/761802/ |
| Distorted Fragment of Origin | https://bdocodex.com/us/item/821317/ |
| Faded Dark Energy | https://bdocodex.com/us/item/767349/ |
| Flawless Herald's Crystal | https://bdocodex.com/us/item/821251/ |
| HAN Crystal of Dusky Ruin | https://bdocodex.com/us/item/15291/ |
| HAN Crystal of Ruin | https://bdocodex.com/us/item/821320/ |
| Hardened Lava Chunk | https://bdocodex.com/us/item/767248/ |
| Herald's Crystal | https://bdocodex.com/us/item/821250/ |
| JIN Crystal of Dusky Ruin | https://bdocodex.com/us/item/15290/ |
| JIN Crystal of Ruin | https://bdocodex.com/us/item/821319/ |
| Lightlost Core | https://bdocodex.com/us/item/767247/ |
| Primordial Fragment | https://bdocodex.com/us/item/821246/ |
| Sealed Black Magic Crystal | https://bdocodex.com/us/item/768160/ |
| Tainted Armor Fragment | https://bdocodex.com/us/item/767348/ |
| WON Crystal of Dusky Ruin | https://bdocodex.com/us/item/15286/ |
| WON Crystal of Ruin | https://bdocodex.com/us/item/821253/ |

The numeric filename of a reused game asset can differ from the item ID.
For example, Chilled Soul Piece is item 767244 but uses asset 00065338,
and Tainted Armor Fragment is item 767348 but uses asset 00767250.
Both Floodlands junk items have their own verified icons.

## Screenshot spot additions - 2026-09-13

168 verified item icons were added from the current BDO Codex item pages recorded in `catalog.json`. The original WebP image was converted to PNG without resizing, recoloring or drawing changes; the original dimensions were checked for every file. Existing icon files and catalog records were preserved. Item identity and German names are documented in `../../docs/reference-evidence/screenshot-spots-items-enriched.json`.

Multiple source keys with the same visible canonical name share the first verified canonical icon, matching the application catalog. This visual choice does not assign a Garmoth upload key where the underlying item part or spot tier is ambiguous. In particular, the current compass item is 44277 even though its icon file is named 00044416. The aggregate Garmoth artifact key is not used as a real item ID.

| Item | BDO Codex source |
| --- | --- |
| Blessed Soul Fragment | https://bdocodex.com/us/item/8421/ |
| Abyssal Essence | https://bdocodex.com/us/item/9774/ |
| Lafi Bedmountain's Upgraded Compass Parts | https://bdocodex.com/us/item/44277/ |
| Faint Origin of Dark Hunger | https://bdocodex.com/us/item/767102/ |
| Faint Sycraia's Memory | https://bdocodex.com/us/item/56345/ |
| Mark of the Black Sands | https://bdocodex.com/us/item/65329/ |
| Trace of Nature | https://bdocodex.com/us/item/5960/ |
| Discarded Kkebicap | https://bdocodex.com/us/item/56327/ |
| Kabua's Fragment | https://bdocodex.com/us/item/59881/ |
| Marsh's Artifact - Extra AP Against Monsters | https://bdocodex.com/us/item/735207/ |
| Ah'krad | https://bdocodex.com/us/item/6399/ |
| Tungrad Ruins Fragment | https://bdocodex.com/us/item/65328/ |
| Ancient Spirit Light | https://bdocodex.com/us/item/56505/ |
| Tainted Ruins Fragment | https://bdocodex.com/us/item/65397/ |
| Forgotten Witch's Token | https://bdocodex.com/us/item/44799/ |
| Tainted Bronze Fragment | https://bdocodex.com/us/item/44526/ |
| Origin of Dark Hunger | https://bdocodex.com/us/item/65319/ |
| Turo Heart | https://bdocodex.com/us/item/44461/ |
| Kabua's Artifact | https://bdocodex.com/us/item/742269/ |
| Ouk Pill of Time and Tide | https://bdocodex.com/us/item/9070/ |
| Lesha's Artifact - Magic Evasion | https://bdocodex.com/us/item/735256/ |
| Ancient Relic Crystal Shard | https://bdocodex.com/us/item/40218/ |
| Hystria Ruins Paint | https://bdocodex.com/us/item/761717/ |
| Thorn-Entwined Weapon Fragment | https://bdocodex.com/us/item/56334/ |
| Imperfect Lightstone of Wind | https://bdocodex.com/us/item/766106/ |
| Sulfur Golem Fragment | https://bdocodex.com/us/item/767350/ |
| Life Spirit Stone | https://bdocodex.com/us/item/45302/ |
| Destruction Spirit Stone | https://bdocodex.com/us/item/45298/ |
| Embers of Frost | https://bdocodex.com/us/item/44498/ |
| Quturan's Black Leaf | https://bdocodex.com/us/item/45017/ |
| Iridescent Lightstone | https://bdocodex.com/us/item/766101/ |
| Al Yurad's Ring Piece | https://bdocodex.com/us/item/44271/ |
| Dawn Earring | https://bdocodex.com/us/item/11855/ |
| Magic Crystal of Infinity - Max HP | https://bdocodex.com/us/item/15136/ |
| Marsh's Artifact - Melee AP | https://bdocodex.com/us/item/735201/ |
| Winter Tree Snow Crystal | https://bdocodex.com/us/item/44496/ |
| Intense Sycraia's Memory | https://bdocodex.com/us/item/56347/ |
| Traveler's Map | https://bdocodex.com/us/item/16017/ |
| Decayed Cloth | https://bdocodex.com/us/item/65330/ |
| Tainted Huge Spear | https://bdocodex.com/us/item/44522/ |
| Tainted Specter's Cloth | https://bdocodex.com/us/item/56322/ |
| BON Dawn Crystal - Evasion | https://bdocodex.com/us/item/15274/ |
| Rare Treasures | https://bdocodex.com/us/item/56336/ |
| Lesha's Artifact - Ranged Damage Reduction | https://bdocodex.com/us/item/735253/ |
| Howling Bone Fragment | https://bdocodex.com/us/item/59800/ |
| Void Tainted Whispers | https://bdocodex.com/us/item/790694/ |
| Kehelle's Artifact - Max HP | https://bdocodex.com/us/item/735301/ |
| Ancient Magic Crystal of Nature - Adamantine | https://bdocodex.com/us/item/15616/ |
| Olun's Valley Paint | https://bdocodex.com/us/item/761722/ |
| Radiant Sycraia's Memory | https://bdocodex.com/us/item/56348/ |
| BON Dawn Crystal - Accuracy | https://bdocodex.com/us/item/15268/ |
| Tainted Wood Fragment | https://bdocodex.com/us/item/44527/ |
| Darkseekers' Retreat Paint | https://bdocodex.com/us/item/761725/ |
| Tainted Token of Crescent | https://bdocodex.com/us/item/44524/ |
| WON Dawn Crystal - Damage Reduction | https://bdocodex.com/us/item/15272/ |
| Black Distortion Earring | https://bdocodex.com/us/item/11853/ |
| WON Dawn Crystal - Evasion | https://bdocodex.com/us/item/15275/ |
| Essence of Devouring | https://bdocodex.com/us/item/65323/ |
| Manshaum Voodoo Doll | https://bdocodex.com/us/item/40383/ |
| Tainted Moonlight Spirit Powder | https://bdocodex.com/us/item/44520/ |
| Kehelle's Artifact - Max Stamina | https://bdocodex.com/us/item/735302/ |
| BON Dawn Crystal - Black Spirit's Rage | https://bdocodex.com/us/item/15265/ |
| Turquoise Primordial Luster - Sovereign | https://bdocodex.com/us/item/821417/ |
| Forest Fury | https://bdocodex.com/us/item/4917/ |
| Corrupted Gluttony Crystal | https://bdocodex.com/us/item/15741/ |
| Specter's Energy | https://bdocodex.com/us/item/721044/ |
| Krogdalo's Origin Stone | https://bdocodex.com/us/item/50801/ |
| Ancient Magic Crystal of Nature - Giant | https://bdocodex.com/us/item/15714/ |
| Embers of Despair | https://bdocodex.com/us/item/44475/ |
| Tainted Golem's Heart Fragment | https://bdocodex.com/us/item/56323/ |
| Corrupted Sanguine Crystal | https://bdocodex.com/us/item/56339/ |
| Tainted Broken Horn Fragment | https://bdocodex.com/us/item/44521/ |
| Thick Turo Blood | https://bdocodex.com/us/item/9778/ |
| Swaying Wind Shard | https://bdocodex.com/us/item/50802/ |
| Tungrad Ruins Paint | https://bdocodex.com/us/item/761724/ |
| Scroll Written in Ancient Language | https://bdocodex.com/us/item/40228/ |
| Clear Blackstar Crystal | https://bdocodex.com/us/item/44405/ |
| WON Dawn Crystal - All AP | https://bdocodex.com/us/item/15263/ |
| Tungrad Belt | https://bdocodex.com/us/item/12237/ |
| Crystal of Unyielding Spirit | https://bdocodex.com/us/item/15260/ |
| Elkarr | https://bdocodex.com/us/item/6393/ |
| Tungrad Earring | https://bdocodex.com/us/item/11828/ |
| Kehelle's Artifact - Black Spirit's Rage Max Increase | https://bdocodex.com/us/item/735303/ |
| Corrupt Power Source | https://bdocodex.com/us/item/59880/ |
| Fortunate Golden Pig King's Treasure Chest | https://bdocodex.com/us/item/761648/ |
| Dehkia's Artifact - All Damage Reduction | https://bdocodex.com/us/item/748021/ |
| Marsh's Artifact - Magic Accuracy | https://bdocodex.com/us/item/735206/ |
| Lesha's Artifact - Melee Damage Reduction | https://bdocodex.com/us/item/735251/ |
| Quturan's Ashen Leaf | https://bdocodex.com/us/item/45018/ |
| Tungrad Ring | https://bdocodex.com/us/item/12061/ |
| Essence of Dawn | https://bdocodex.com/us/item/820979/ |
| Turquoise Primordial Luster - Edana | https://bdocodex.com/us/item/821418/ |
| Sulfur Golem Power Core | https://bdocodex.com/us/item/980115/ |
| Forgotten Limbo Box | https://bdocodex.com/us/item/56284/ |
| Crystallized Despair | https://bdocodex.com/us/item/8411/ |
| Valtarra Eclipsed Belt | https://bdocodex.com/us/item/12236/ |
| Lesha's Artifact - Melee Evasion | https://bdocodex.com/us/item/735252/ |
| Ring of Crescent Guardian | https://bdocodex.com/us/item/12031/ |
| Crystal of Stalwart Fortitude | https://bdocodex.com/us/item/15258/ |
| Quturan's Right Lung | https://bdocodex.com/us/item/45014/ |
| Gluttony Crystal | https://bdocodex.com/us/item/821344/ |
| Lafi Bedmountain's Upgraded Telescope Parts | https://bdocodex.com/us/item/65332/ |
| Lesha's Artifact - Monster Damage Reduction | https://bdocodex.com/us/item/735259/ |
| Tungrad Necklace | https://bdocodex.com/us/item/11629/ |
| Gem of Void | https://bdocodex.com/us/item/821182/ |
| Rare Treasure Chest | https://bdocodex.com/us/item/761649/ |
| Dehkia's Fragment | https://bdocodex.com/us/item/767091/ |
| BON Dawn Crystal - All AP | https://bdocodex.com/us/item/15262/ |
| Sycraia Shard | https://bdocodex.com/us/item/821347/ |
| Underwater Ancient Weapon Power Stone | https://bdocodex.com/us/item/56341/ |
| Flame of Frost | https://bdocodex.com/us/item/44497/ |
| Golden Pigs Subjugation Token | https://bdocodex.com/us/item/56335/ |
| Marsh's Artifact - Magic AP | https://bdocodex.com/us/item/735203/ |
| Corrupted Gargoyle Claw | https://bdocodex.com/us/item/44523/ |
| Kuadir Fragment | https://bdocodex.com/us/item/820040/ |
| Gentle Sycraia's Memory | https://bdocodex.com/us/item/56346/ |
| Marsh's Artifact - Melee Accuracy | https://bdocodex.com/us/item/735204/ |
| Sycraia Underwater Ruins Paint | https://bdocodex.com/us/item/761718/ |
| Broken Horn Fragment | https://bdocodex.com/us/item/44454/ |
| Starlit Jade's Breath | https://bdocodex.com/us/item/9792/ |
| Flame of Resonance | https://bdocodex.com/us/item/65317/ |
| Old Warrior's Horn Decoration | https://bdocodex.com/us/item/44455/ |
| Dehkia's Artifact - All Evasion | https://bdocodex.com/us/item/748022/ |
| Imperfect Lightstone of Earth | https://bdocodex.com/us/item/766105/ |
| Corrupted Breath | https://bdocodex.com/us/item/8427/ |
| Ancient Magic Crystal - Addis | https://bdocodex.com/us/item/15649/ |
| Fortunate Golden Pig King Summon Scroll | https://bdocodex.com/us/item/66945/ |
| Origin of Corruption | https://bdocodex.com/us/item/56340/ |
| Ancient Magic Crystal - Carmae | https://bdocodex.com/us/item/15605/ |
| Crystal of Precise Destruction | https://bdocodex.com/us/item/15257/ |
| Turquoise Primordial Pigment - Edana | https://bdocodex.com/us/item/767342/ |
| Essence of Insight | https://bdocodex.com/us/item/9791/ |
| Sulfur Golem Power Core Fragment | https://bdocodex.com/us/item/980116/ |
| Marsh's Artifact - Ranged AP | https://bdocodex.com/us/item/735202/ |
| Turquoise Primordial Pigment - Sovereign | https://bdocodex.com/us/item/767341/ |
| Imperfect Lightstone of Fire | https://bdocodex.com/us/item/766104/ |
| Starlit Jade Powder | https://bdocodex.com/us/item/44490/ |
| Ring of Cadry Guardian | https://bdocodex.com/us/item/12032/ |
| Tattered Shadow | https://bdocodex.com/us/item/59802/ |
| Ominous Ring | https://bdocodex.com/us/item/12068/ |
| Quturan's Left Lung | https://bdocodex.com/us/item/45013/ |
| Embers of Resonance | https://bdocodex.com/us/item/65318/ |
| Fiery Troll Hide | https://bdocodex.com/us/item/59798/ |
| Mass of Pure Magic | https://bdocodex.com/us/item/752023/ |
| Venomous Night Fang | https://bdocodex.com/us/item/65770/ |
| Flame of Despair | https://bdocodex.com/us/item/44462/ |
| Gavinya Coastal Cliff Paint | https://bdocodex.com/us/item/761726/ |
| BON Dawn Crystal - Damage Reduction | https://bdocodex.com/us/item/15271/ |
| Turo's Belt | https://bdocodex.com/us/item/12257/ |
| WON Dawn Crystal - Black Spirit's Rage | https://bdocodex.com/us/item/15266/ |
| WON Dawn Crystal - Accuracy | https://bdocodex.com/us/item/15269/ |
| Lesha's Artifact - Ranged Evasion | https://bdocodex.com/us/item/735254/ |
| Marsh's Artifact - Ranged Accuracy | https://bdocodex.com/us/item/735205/ |
| Lesha's Artifact - All Damage Reduction | https://bdocodex.com/us/item/735257/ |
| Shiny Treasure | https://bdocodex.com/us/item/56338/ |
| Lesha's Artifact - Magic Damage Reduction | https://bdocodex.com/us/item/735255/ |
| Rumbling Earth Shard | https://bdocodex.com/us/item/50803/ |
| Lesha's Artifact - All Evasion | https://bdocodex.com/us/item/735258/ |
| Moonlight Spirit Powder | https://bdocodex.com/us/item/44451/ |
| Ash Forest Paint | https://bdocodex.com/us/item/761720/ |
| Crystal of Dogged Patience | https://bdocodex.com/us/item/15259/ |
| Shattered Treasures | https://bdocodex.com/us/item/56329/ |
| Ancient Magic Crystal of Nature - Fighting Spirit | https://bdocodex.com/us/item/15712/ |
| City of the Dead Paint | https://bdocodex.com/us/item/761723/ |
| Dragon Scale Fossil | https://bdocodex.com/us/item/44364/ |
| Guardian Spirit Stone | https://bdocodex.com/us/item/45300/ |
| Silver | https://bdocodex.com/us/item/1/ |
| Tainted Cadry's Token | https://bdocodex.com/us/item/44525/ |

## Global loot addition (2026-09-14)

| Item | BDO Codex source |
| --- | --- |
| Intricately Patterned Mystical Shard | https://bdocodex.com/us/item/9776/ |
