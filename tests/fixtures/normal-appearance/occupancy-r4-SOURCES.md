# Occupancy regression fixtures

These four files are byte-for-byte copies of the original Normal PNGs from diagnostic recording `loot-20260911-062317-3ddc64191eea4b8986c37055a963a869`. They retain the original 511×447 panel, background and fading text; no processing or OCR was applied.

The calibrated newest-first rectangles relative to the panel are `(60,373,451,74)`, `(60,298,451,75)`, `(60,224,451,75)`, `(60,149,451,75)`, `(60,75,451,75)`, `(60,0,451,75)`, with UI scale 1.49.

| Fixture | Original SHA-256 |
| --- | --- |
| occupancy-r4-000282.png | 6A3011754102E27493B70E42C1509DE25BC8E2DA7EE0BBC6D1D8F45E3A80CE32 |
| occupancy-r4-000283.png | ACFBA6252857FAF1B37699E1DB301F85E3D4F8127B11F4526F9D72B77364FBF4 |
| occupancy-r4-000290.png | 42A2BEF4C4808B1D69D30E28435F8E2AA17446EFE78D7FE4DB27EEFB49928F9A |
| occupancy-r4-000291.png | 4CE622BD064DF08D936911A888122530E2E8756631DC1C77824C0B0FC41F2215 |

Frames 283 and 291 have four visible rows and two empty upper places. In frame 291, the fourth row is fading and its current-image glyph mask fails admission, while a previous accepted glyph mask still confirms occupancy. Repeated equal item text matches more than one previous template, so these fixtures also demonstrate why glyph similarity must not select an event identity.
