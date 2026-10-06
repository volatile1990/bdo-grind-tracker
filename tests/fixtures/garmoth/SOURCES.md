# Public Garmoth benchmark responses

Captured on 2026-09-12 at 15:46:29 UTC by the production
`GarmothWebViewBenchmarkReader` in a fresh, hidden InPrivate WebView2 profile.
The public overview loaded without a login or cookie-dialog interaction.

- `collective-20260912.json`: response from
  `https://api.garmoth.com/api/grind-tracker/collective/all`, reduced to supported
  spot IDs 213–218; all original fields and values of those rows are retained.
- `metadata-20260912.json`: response from
  `https://garmoth.com/api/trpc/grindMeta.list`, reduced to the same IDs and the
  `highTierTrash`, `topTierTrash`, and `dropRatios` fields used by the parser.

These are actual public responses, unlike the separate synthetic unit-test
payloads. They include the ongoing reporting window ending on 2026-09-17.
Magaia's raw 6401.4/h uses the default Lv.2 multiplier of 2 and rounds to
12,803/h. Moderator thresholds are 14,000/h and 15,200/h. The observed public
page and the user's full screenshot show the same values.

## Rare-drop references captured on 2026-10-02

Captured at 10:37:13 UTC in a fresh, hidden InPrivate WebView2 profile while
loading `https://garmoth.com/grind-tracker/best-grind-spots`. The browser observed
the overview's ordinary public GET requests, without login, subscription,
cookie-dialog interaction, or user-profile access. All three responses returned
HTTP 200. Direct HTTP requests still returned HTTP 403; no challenge was solved.

- `collective-20261002.json`: the same public collective endpoint, reduced to
  all 40 supported spot IDs, reporting windows, observed minutes, and drop
  `item_key`, `is_trash`, and `hourly_rate`. Normal non-trash drops and unsupported
  item keys are omitted. Decimal values are represented as invariant strings to
  retain their exact source precision; the parser supports both JSON numbers
  and numeric strings.
- `metadata-20261002.json`: the public `grindMeta.list` procedure envelope,
  reduced to the matching spot IDs and threshold/drop-ratio fields.
- `no-scroll-spots-20261002.json`: the exact IDs whose `specials` array included
  `no-scroll` in the public `getGrindSpots` response: 32, 89, 112, 113, 149, 150.
  Of these, only Winter Tree Fossil (280), ID 149, is a supported local spot.

The compact production resource `data/garmoth-benchmarks-20261002.json` derives
its spot thresholds and rare item-key rates from these responses. The loader
resolves keys through the canonical local catalog and its spot/source allowlists.
The original six Inner Edania trash references remain dated 2026-09-12; their
separately dated rare references and all other bundled spots use this capture.

The public client at
`https://assets.garmoth.com/_static/_nuxt/BVEzzpVX.js` was retrieved successfully
on 2026-10-02. An anonymous user defaults to the visible 100% bonus setting.
For regular non-trash drops it displays `hourly_rate * (100 + bonus) / 100`:
at the public 100% setting the unrounded hourly rate is therefore twice the raw
collective value. Spots marked `no-scroll` and Dehkia's Light ignore this bonus.
Dehkia's Light is not a supported local rare-channel item. The application keeps
full precision in references and rounds only the visible comparison text.
