# Quantity confidence regression fixtures

These four PNGs are lossless **385 × 50** row crops from the user's recording
`loot-20260914-210046-876e580f104e42d3961fe7f2b5892d69`, captured on 2026-09-14
at Aresion Temple with UI scale **1**, font **StrongSword**.

Each source is a 425 × 300 normal-loot capture. Crop **x = 40, width = 385,
height = 50**, at the y-coordinate in the filename. This matches the band's
actual production input: the icon is already excluded before
`PaddleLootRowPreprocessor` applies its further text crop. Cropping from x = 0
would test different pixels and fail to reproduce the bug.

| Fixture | Visible amount | Purpose |
|---|---:|---|
| `aresion-010320-y200.png` | 10 | Bright scenery is falsely read as a third digit, producing 101 |
| `aresion-010324-y200.png` | 10 | Later view of the same persistent false 101 reading |
| `aresion-001355-y250.png` | 30 | A real larger amount must remain acceptable |
| `aresion-000655-y250.png` | 6 | Existing missing-quantity recovery must remain available |

The 10 fixtures' recorded baseline has the complete item name without a numeric
suffix, quantity 10, and native template score approximately **0.84153**. That
score is below the trusted-template threshold 0.90, so the normal background
review runs. Both native Paddle variants produce 101 with high whole-line
confidence; the trailing digit has low individual confidence.

The 6 fixture's recorded pre-review observation is `Scorched Belt Ornameht`,
canonical item `Scorched Belt Ornament`, name confidence 0.9545454531908035 and
quantity null. Two whole-line reads recover 6 despite a lower individual digit
score. The test distinguishes recovering a missing amount from replacing an
existing one.

The 30 test deliberately supplies an incorrect baseline of 3 to verify genuine
upward correction. This baseline is a test input, not a claim about the recording.
The tests also render explicitly **synthetic** Segoe UI rows for 101, 104 and 338
to ensure confident three-digit values can replace shorter incorrect baselines.
Those rendered controls are not native game captures.

`provenance.json` records the original journal hash, source PNG hashes, crop
rectangles and fixture hashes. Every saved pixel was compared with its source
pixel. Source PNGs were only read; there is no rescaling, masking, redrawing,
color adjustment or OCR annotation in the fixture images.

`RecordedQuantityConfidenceTests` exercises the bundled native Paddle model and
the actual `BackgroundLootRowReview`. It requires no original recording path,
installed Windows OCR language, Python package or external service.
