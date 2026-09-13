# Unreadable occupancy regression images

Byte-for-byte copies of original 511 x 447 Normal panel PNGs from recording `loot-20260913-180245-2da5f25adb1b45899d3003e631365747`. Original journal SHA-256: `52C74DF2D86B2183CFC996542D93E821CF20BAF27BEA481D924BDB857E6A575B`. No image editing or OCR rerun was applied.

UI scale: 1.49. Newest-first panel-relative rectangles: `(60,373,451,74)`, `(60,298,451,75)`, `(60,224,451,75)`, `(60,149,451,75)`, `(60,75,451,75)`, `(60,0,451,75)`.

| Fixture | SHA-256 |
| --- | --- |
| occupancy-unreadable-000427.png | D9E9684B814B0F66A7711422223C0CCF281AD2AB9FF52404BB6F5B9CD7E93344 |
| occupancy-unreadable-000428.png | C8BCA60085C042C4DCF93C30220D4F9D662C48EA4DC69619045C81AAF44535D2 |
| occupancy-unreadable-001385.png | 5733A038EC11DB89993D39BFAE234AFC37E5C019FC81CDD1BBFC594146D2008D |
| occupancy-unreadable-001386.png | BBE4989A1A235668E958DA8D0DA88FE34D8CEDE93E5DF30AD187300077183A66 |
| occupancy-unreadable-001387.png | E43572C86A13E54AF18EE224BEDDB3F9C63AC78F309A507013EEA24210ABD9ED |
| occupancy-unreadable-002383.png | 8BE03BF4CFDB4833E6FB69D54A2A931569BDAD0725D54A27D3821869535423EE |
| occupancy-unreadable-002384.png | 3CF45514F1BA646C46DCE5BD0F07A2B4D30503FEF6B6563D04349167748FE284 |

Frame 428 has five helmet rows while its OCR omits the oldest one. Frame 2384 also has five rows, but its oldest raw text is only `Elioti`. The preceding frames provide accepted glyph templates. Tests verify that these masks confirm occupancy without assigning item names, quantities, or identities to unreadable rows.

Frames 1385-1387 are consecutive, unmodified original images for a three-to-four
row arrival. Frame 1385 supplies three readable rows. In frame 1386 the oldest
row's original OCR is `Elion Heltnet x` (`native-catalog-miss`, no name or amount).
Frame 1387 has three readable rows plus a fourth physical row omitted by OCR.
`FadeAwareOccupancyImageTests` uses the exact original OCR fields and timestamps,
with previously derived occupancy annotations removed before recomputation.
It verifies occupancy of both unreadable rows while preserving their missing
names and quantities; no counter output or expected total supplies evidence.
