# Spot artwork and guidance sources

## Screenshot zones — collected 2026-09-13

The added spot emblems use the primary trash item artwork shown in the user's
screenshots. See [screenshot-spots-sources.json](screenshot-spots-sources.json)
for exact image URLs, filenames, dimensions and SHA-256 checksums, and
[the presentation source notes](../../docs/SCREENSHOT_SPOT_PRESENTATION_SOURCES.md)
for verified regions, AP caps and optional recommendations. Missing scene art
uses a neutral interface background; the Floodlands variants share their
existing authentic Floodlands image.

## Outer Edania — collected 2026-09-13

The six added monster emblems and five castle banners are unmodified Pearl
Abyss assets from the official update articles below. The Dark Energy Floodlands
scene was converted from PNG to JPEG (ImageMagick, quality 94) at its original
1920 × 1080 resolution for the existing background packaging format. No artwork
was generated, redrawn, recolored, or copied from the Inner Edania spots.

Exact asset URLs, filenames, dimensions and SHA-256 checksums are recorded in
[`outer-edania-sources.json`](outer-edania-sources.json). The castle banners depict
their respective Demonlords; the emblems depict monsters from each zone.

| Zones | Official source |
| --- | --- |
| Aetherion, Nymphamaré, Orbita | [August 21, 2025 update](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=8017) |
| Tenebraum, Zephyros; Orbita DP correction | [September 11, 2025 update](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=8063) |
| Dark Energy Floodlands | [March 19, 2026 update](https://blackdesert.pearlabyss.com/TR/en-us/News/Notice/Detail?_boardNo=19077) |

The total AP/DP recommendations in `LootSpotPresentationCatalog` come from
these official sources. The September update lowered Orbita's total recommended
DP from 760 to 740. Floodlands' official AP limit is 1880, and its party design
does not offer Marni's Realm. Its primary junk is Tainted Armor Fragment;
Faded Dark Energy is also recognized and valued as vendor loot.

The five castle AP limits and resistance recommendations were cross-checked with
the [Outer Edania guide](https://www.blackdesertfoundry.com/edania-monster-zones-guide/)
and [Garmoth's Edania guide](https://garmoth.com/guides/post/edania-monster-zones).
The former still quotes Orbita's old DP, so the newer official correction takes
precedence. Resistance crystal suggestions use the same Giant, Adamantine and
Fighting Spirit assets as the existing spot guide.

The current September 10, 2026 update removes Zephyros from `#HighestTier`;
Outer Edania profiles therefore do not carry that tag. See
[`docs/OUTER_EDANIA_INTEGRATION_SOURCES.md`](../../docs/OUTER_EDANIA_INTEGRATION_SOURCES.md)
for the current loot-source audit.

Black Desert and the game assets are property of Pearl Abyss. Pearl Abyss,
Garmoth and BDO Codex do not endorse this project.
