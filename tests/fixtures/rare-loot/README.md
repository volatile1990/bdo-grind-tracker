# Recorded rare necklace regression

These unmodified 574 × 89 rare-banner crops were recorded in Aresion on
2026-09-22 at 16:54:40–44 CEST, with a 3840 × 2160 display, UI scale 1.49,
and HDR converted to SDR before OCR. They show one
`Twilight of the End - Necklace x 1`, plus scenery before and after it.

Windows OCR correctly identified the necklace, but Paddle verification rejected
every frame. Sending the whole decorated banner to its 48-pixel-high recognizer
shrunk the lettering too far. Frame 4347 reproduced the logged
`wiligth ohto t etex` with confidence 0.452. Cropping vertically around the
current frame's text boxes restores the Paddle read. Rare recognition now selects
the best usable result independently, so a weaker or failed view cannot veto a
correct primary reading. Full horizontal width is retained to preserve enhancement
prefixes and the amount.

The regression replays these crops through the actual Windows and Paddle engines,
the analyzer, and rare reconciliation. Repeated views must produce exactly one
necklace; the surrounding scenery must produce none.
After a 2.4-second interval of this recorded scenery, a second occurrence of
the same banner must count as a new necklace. Live and diagnostic replay totals
must agree in both cases.
