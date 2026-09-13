# Silver valuation provenance and limits

Audited on 2026-09-06. This is a read-only projection of the tracker's existing
item totals. Pricing, missing prices, tax options and Garmoth upload filtering do
not change OCR, the spot loot pool, deduplication or any counted quantity.

## Current market source

Updated on 2026-09-13. `MarketLootPriceProvider` uses Arsha as the primary source
and the public Pearl Abyss market endpoint as a fallback, with one shared price
cache. The app offers PC **EU (default)** and **NA**.

The primary request is an anonymous batched GET to
`https://api.arsha.io/v2/{region}/GetWorldMarketSubList?id={catalogIds}&lang=en`.
It requests the market IDs in `LootPriceCatalog` and uses `basePrice` for
enhancement sub-ID `sid=0`, not `lastSoldPrice` or an enhanced accessory price.
The multi-ID response contains an array of enhancement-row arrays. Arsha
requests use the current application user agent from `AppBranding.UserAgent`.
After a batch HTTP 500, bounded individual Arsha requests remain available,
with at most four in flight and within the same Arsha deadline. A 403 or 429
stops further queued individual requests to that source.

If Arsha fails or returns only some requested prices, the provider requests
the remaining IDs in one anonymous POST to
`https://eu-trade.naeu.playblackdesert.com/Trademarket/GetWorldMarketSearchList`
or the fixed NA equivalent at `na-trade.naeu.playblackdesert.com`. Its JSON body
contains only `searchResult`, a comma-separated list of those catalog IDs, and
its user agent is `BlackDesert` as required by the documented endpoint. The
response envelope must contain integer `resultCode=0` and string `resultMsg`;
each pipe-separated row has `id-stock-basePrice-totalTrades` fields. The
fallback fills missing prices without replacing prices already returned by
Arsha. No screenshot, session quantity, class, API key, account or other session
data is sent to either source. Cookie handling and redirects are disabled.

[Velia's documentation](https://developers.veliainn.com/) describes the Pearl
Abyss endpoints; Velia is not a separate price-data host. The
[Arsha repository](https://github.com/guy0090/api.arsha.io) identifies Arsha as
a community-operated proxy/cache for this same upstream API and credits Velia
for documenting it. Direct fallback can bypass an Arsha-specific failure, but
both paths still depend on Pearl Abyss. There is no reliability or SLA claim.
The displayed timestamp is **when this application fetched the quote**, not
the underlying market-change time, and the appraisal is not a guaranteed sale
price. The [Arsha API documentation](https://www.postman.com/bdomarket/arsha-io-bdo-market-api/documentation/qpavrc8/bdo-market-api-v2)
describes its request and response formats.

Construction and cached-snapshot reads do not make HTTP calls. Active refreshes
are serialized; successful refreshes suppress further requests for 10 minutes.
Each source has an independent 8-second deadline over both headers **and body**,
giving a refresh at most 16 seconds of active network-request budget. Waiting
for another refresh and local processing are outside that network budget.
An Arsha timeout does not cancel the fallback; caller cancellation cancels the
operation. Response/cache size is limited to 1 MiB, response rows to 5,000, and
JSON depth to 32 for Arsha or 8 for Pearl Abyss. Invalid field types,
contradictory duplicates, nonpositive prices, unexpected IDs and nonzero Arsha
enhancement sub-IDs cannot create a quote. Pearl Abyss also rejects malformed
envelopes, duplicate envelope fields and malformed four-field rows.

Each region tracks failure backoff separately for Arsha and Pearl Abyss. It
starts at 30 seconds, doubles and is capped at 15 minutes; a source's
`Retry-After` may extend its wait up to one hour. A successful source waits
10 minutes, also respecting a longer bounded `Retry-After`. One source's
cooldown does not prevent use of the other source when a refresh is due.
There is no unbounded retry loop. Response bodies and exception messages are
not shown or logged. A 403 marks that source unavailable; it is not retried
through different credentials or redirects.

The unchanged version-1 region-separated cache, shared by both sources, is
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

Added on 2026-09-11: **Empty Picture Frame** has a fixed NPC sell value of
**15,348 silver**, verified against the [English item record](https://bdocodex.com/us/item/767249/)
and [German item record](https://bdocodex.com/de/item/767249/). This addition does
not introduce a market-price request. Five frames are worth 76,740 silver,
consistent with the user's rounded 77K screenshot.

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

`ArshaLootPriceProviderTests` exercises `MarketLootPriceProvider`'s nested Arsha
response, integer dust appraisal, malformed JSON types, enhancement filtering, immutability, region
isolation, partial responses, stale/offline cache, retry delays, caller
cancellation, full-response timeout and response limits.
`MarketLootPriceFallbackTests` covers the anonymous regional POST contract,
primary failure/timeout and partial responses, independent fallback deadlines,
shared cache persistence, source-specific cooldowns, cancellation and invalid
Pearl Abyss responses. These tests use mock HTTP, not internet calls. Native tax
behavior has separate `SilverValuationTests`.
Public anonymous GET checks on 2026-09-05 returned all 24 then-requested base market
prices, and the six additions were verified on 2026-09-06. A transient
non-success response was also observed and correctly
produced fixed-only/incomplete valuation rather than fabricated prices.

Anonymous live checks on 2026-09-13 observed an Arsha EU Black Stone request
return HTTP 500 while the direct Pearl Abyss request returned HTTP 200 in about
275 ms. Full-catalog direct search batches then returned HTTP 200 in about
425 ms (EU) and 293 ms (NA). Each returned 118 of the 120 IDs requested at that
time; IDs `980115` and `980116` were absent, and all returned rows matched the
four-field grammar. These are point-in-time observations, not an availability
benchmark or a guarantee that either source supplies every catalog item.
