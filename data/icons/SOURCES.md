# Item icon sources

The local 44 × 44 pixel PNG files were converted without visual modification
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
