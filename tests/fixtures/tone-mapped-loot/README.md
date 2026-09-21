# Recorded tone-mapped HDR loot rows

Lossless 385 × 50 pixel extracts from the user-supplied loot recording dated
2026-09-19, application 1.7.3. Each extract uses rectangle `(40, 250, 385, 50)`
within a recorded 425 × 300 normal-loot panel. No rescaling, recoloring or text
replacement was applied. These fixtures are test-only and are not app assets.

- `helmet-four-hdr.png`: frame 110, visible `Elion Follower's Helmet x 4`.
- `helmet-fifty-six-hdr.png`: frame 1820, visible `Elion Follower's Helmet x 56`.
- `empty-scenery-hdr.png`: frame 73, pale scenery without a loot row.

The visible lettering peaks at brightness 227 after HDR tone mapping. A filter
requiring 230 erases it; retaining only its brightest cores at 225 still damages
the OCR result. The regression checks the complete native OCR output as well as
the absence of a row in the negative fixture.
