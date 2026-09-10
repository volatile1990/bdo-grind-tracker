# German OCR release reference check — 1.1.0

The unchanged recognition-only Paddle model reads the old 900×100 generated
image as `Bruchstüukx12` with confidence 0.9023931. This is a real model mistake
on that image, rather than a CTC decoder defect or cross-worker interference.
Both failed tests used exactly this same fixture.

The fixture included a 48-pixel font line inside a 100-pixel-high panel. The
recognizer scales the whole image to 48 pixels high, making the glyphs much
smaller than in an already located text line. Production supplies a calibrated
text-band crop through `PaddleLootRowPreprocessor.Prepare` before recognition.

A probe preserved the font, rendering mode, text, model, decoder, and exact
non-space text expectation, while sizing the image from `font.Height` plus
symmetric padding. The German text was correct at every checked padding:

| Padding each side | Height | German confidence | English confidence |
| ---: | ---: | ---: | ---: |
| 0 | 48 | 0.94521725 | 0.95615333 |
| 2 | 52 | 0.96345836 | 0.98122877 |
| 4 | 56 | 0.983686 | 0.9714582 |
| 8 | 64 | 0.9625459 | 0.9520679 |

The fixture now uses a font-derived line height and four pixels of padding.
The two exact German expectations, umlaut, English checks, concurrency check,
model hash checks, and confidence assertion remain unchanged. No production
OCR code or acceptance thresholds changed.

Validation in Release: targeted class 10/10 passed, complete OCR project
243/243 passed, zero skipped. TRX files are in `test-results/`.

This fixes the test-input contract; it does not improve model accuracy on the
original oversized image. In production, that specific erroneous reading would
be rejected by the 0.95 minimum model confidence before catalog matching, and
the existing primary observation would be preserved. The synthetic probe alone
does not establish accuracy for all real German screenshots or UI scales.
