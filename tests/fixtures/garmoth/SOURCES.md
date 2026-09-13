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
