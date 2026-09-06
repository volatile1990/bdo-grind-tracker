# Silver valuation provenance and limits

Audited on 2026-09-06. This is a read-only projection of the tracker's existing
item totals. Pricing, missing prices, tax options and Garmoth upload filtering do
not change OCR, the spot loot pool, deduplication or any counted quantity.

## Current market source

The application makes an anonymous batched GET to
`https://api.arsha.io/v2/{region}/GetWorldMarketSubList?id={catalogIds}&lang=en`.
It uses `basePrice` for enhancement sub-ID `sid=0`, not `lastSoldPrice` and not
the price of an enhanced accessory. Its own user agent is `Grindcrest/0.9.5`.
No screenshot, session quantity, class, API key, account or other session data
is sent. Cookie handling and redirects are disabled.

Arsha's [first-party API documentation](https://www.postman.com/bdomarket/arsha-io-bdo-market-api/documentation/qpavrc8/bdo-market-api-v2)
describes it as a community-operated cached wrapper, not a Pearl Abyss-supported
API. It documents a **30-minute server-side cache**. Therefore the displayed
timestamp is **when this application fetched the quote**, not the time of the
underlying market change, and the appraisal is not a guaranteed sale price.
The multi-ID endpoint returns an array of arrays, each inner array containing
one item's enhancement rows. This shape was confirmed by public GET and is
covered by parser tests. The app currently offers PC **EU (default)** and **NA**;
the API documents additional regions but these are deliberately not exposed yet.

One batch requests the 30 verified market IDs in `LootPriceCatalog`. The provider
does not make HTTP calls in its constructor. Requests are serialized; successful
refreshes suppress further requests for 10 minutes. Timeout is 8 seconds over
both headers **and body**; response/cache size is limited to 1 MiB, JSON depth
to 32 and flattened rows to 5,000. Invalid field types, contradictory duplicates,
nonpositive prices, unexpected IDs and nonzero enhancement sub-IDs cannot create
a quote. An error never triggers a hidden retry loop. Retry backoff starts at
30 seconds, doubles and is capped at 15 minutes; a server Retry-After may extend
this up to one hour. Response bodies and exception messages are not shown or
logged. A 403 is treated as unavailable, never bypassed.

The app's own region-separated cache is
`%LOCALAPPDATA%/BdoGrindTracker/market-prices-v1.json`. It contains only item IDs,
unit prices and retrieval timestamps. It contains no account/session data and
is unrelated to Companion's user settings. Cache quotes older than 10 minutes
are explicitly stale; failed/partial requests never renew the timestamps of
missing quotes. A partial successful response only replaces the returned IDs.
Corrupt, unsupported-schema, oversized and future-dated cached entries are
ignored. The file is saved atomically on a best-effort basis; an unwritable cache
does not prevent live tracking.

No historical market prices are bundled as a fictitious current offline price.
Without a prior app cache, offline valuation consists of known fixed appraisals
only. Unknown/unavailable prices remain **missing**, not silently zero. Totals
must be labelled incomplete when observed items have no quote. Only stale quotes
actually used in the session mark its valuation stale.

## Companion metadata and fixed appraisals

Static source: `C:/Program Files/BDO Companion/bdo_companion.exe`, version 0.7.4,
SHA-256 `8B75E114D3D33D227A01CDFE592F13AA65133A36363EAA2E6A82E5ADFC77EFCF`.
No game or Companion process was started, controlled, hooked or read in memory.

The public catalog cache `.../com.iqon-digital-llc.bdo-companion/loot_drops`
was read strictly as item/spot/class metadata, not a session log. It is 256,813
bytes; last-write UTC `2026-09-01 09:06:51`; SHA-256
`68A8A812D1D7C0E97B1AE700138BCBFE3D55C795BF73CBAE7A8AC9D42660699F`.
No `options.db`, browser storage, logs, tokens or credentials were read.

The bincode sequence begins with 1,236 item records. Each contains a UTF-8 name,
integer price/count, optional icon, integer tax flag, optional Garmoth drop key,
two flags and location IDs. The item section ends at offset 165,498; it is
followed by 200 spot definitions, then 32 class definitions at offset 248,976.
Only item mapping, taxability and explicitly fixed appraisals are used here.

The six untaxed NPC trash prices are independently supported by the
[publisher's 2026-08-13 update](https://www.naeu.playblackdesert.com/en-US/News/Detail?groupContentNo=10451):

| Item | Silver per item |
| --- | ---: |
| Branch of Abundance | 155,127 |
| Black Crystal Fragment | 160,539 |
| Elion Follower's Helmet | 181,042 |
| Scorched Belt Ornament | 182,049 |
| Elion Follower's Mark | 186,458 |
| Broken Gloves of the Void | 196,501 |

Other untaxed **Companion appraisals** retained from that metadata are:

| Items | Silver per item |
| --- | ---: |
| WON / BON / JIN / HAN Origin Shard | 12m / 15m / 17m / 20m |
| WON / BON / JIN / HAN Wandering Origin Crystal | 1.2b / 1.5b / 1.7b / 2.0b |
| Broken Vestige of Goldroot / Ebonmere / Everlight / Crimsonflare / Voidreach | 3.0b / 3.1b / 3.2b / 3.3b / 4.0b |
| Laila's Petal | 500,000 |
| Embers of Ynix — Armor / Helmet / Gloves / Shoes | 0 |

These are identified as fixed catalog appraisals, not claimed to be live market
quotes or newly verified NPC cash-out prices for every non-trash item. In
particular, the three nonmarket embers have an explicit zero appraisal in
Companion; this is different from an unknown item being replaced by zero.
`Pure Black Stone` and `[Event] Mysterious Ore` lack verified appraisals/IDs and
remain unpriced. No conjectured conversion to premium items is applied.

## Ancient Spirit Dust

The [publisher's recipe](https://blackdesert.pearlabyss.com/Console/en-US/News/Notice/Detail?_boardNo=9741)
uses five Ancient Spirit Dust and one Black Stone to make one Caphras Stone.
The implemented unit appraisal is integer
`floor((Caphras basePrice - Black Stone basePrice) / 5)`.
This formula is **inferred from the documented recipe and verified against
Companion's metadata**: its cached prices 885,000 and 129,000 yield exactly the
stored dust value 151,200. The native instruction sequence that creates that
derived price has not been conclusively located; do not describe the conversion
formula as a fully traced native price-update routine.

Dust's stored tax flag is 2. Native valuation tests **nonzero**, so the complete
integer dust appraisal is taxed, exactly as an ordinary taxable unit price. It
does **not** separately tax Caphras revenue while deducting an untaxed Black
Stone cost. Both ingredient quotes are required, and the derived quote uses the
older input timestamp/stale state. If ingredient prices imply a negative dust
appraisal, it remains missing instead of inventing a negative or zero value.

## Before/after tax and native rounding

The native unit valuation sequence `0x1401B37D0..0x1401B383A` adds
`unitPrice * quantity` to gross. At `0x1401B37EF` it tests the tax flag; for a
nonzero flag it multiplies the unit price by the tax return factor, truncates
the **unit** result at `0x1401B3807`, and only then multiplies by quantity.
The correction path `0x1401BDAD5..0x1401BDB20` uses the same ordering. Therefore
rounding only the whole session or whole item line would not be equivalent.

The return factor is `0.65 * (1 + valuePackBonus + merchantRingBonus + fameBonus)`.
Value Pack contributes 0.30, Merchant Ring 0.05 and Family Fame contributes
0 / 0.005 / 0.010 / 0.015 at thresholds 0 / 1,000 / 4,000 / 7,000.
Defaults assume no Value Pack, no ring and zero fame until configured.
Fixed untaxed items, especially trash loot, do not use this factor. Decimal
arithmetic keeps currency exact; unrepresentable totals are flagged incomplete
instead of crashing the tracker or silently wrapping.

Native serialization at `0x1401158E6..0x1401158ED` labels session field `+0xE8`
as `post_tax`; `+0xE0` is `pre_tax`. The native Garmoth upload builder reads
`+0xE8` at `0x140648ED6`, so its upload `total` is **after-tax silver**, not item
count and not before-tax silver. No actual session was uploaded during testing.

## Verification

`ArshaLootPriceProviderTests` exercises the nested API response, integer dust
appraisal, malformed JSON types, enhancement filtering, immutability, region
isolation, partial responses, stale/offline cache, retry delays, caller
cancellation, full-response timeout and response limits. Tests use mock HTTP,
not internet calls. Native tax behavior has separate `SilverValuationTests`.
Public anonymous GET checks on 2026-09-05 returned all 24 then-requested base market
prices, and the six additions were verified on 2026-09-06. A transient
non-success response was also observed and correctly
produced fixed-only/incomplete valuation rather than fabricated prices.
