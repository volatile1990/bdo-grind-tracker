# Agris HUD recognition references

The original HUD crops under `tests/fixtures/agris` were supplied by the
Grindcrest user on September 10, 2026. The user confirmed that Agris is active
only when its central brazier is golden and the surrounding golden arc is visible.
A gray brazier is inactive even when the arc is present; gold without the arc is
also inactive. No status is inferred from a hidden or unrecognizable HUD.

`glyph-gray.png` is the 56 × 56 crop at (2, 1) in `inactive-gray.png`.
`glyph-gold.png` is the 56 × 56 crop at (11, 6) in `active-gold-ring.png`.
Only the brazier's central shape is used for template matching. The shortcut
marker and rotating ring are masked out; color and angular ring coverage are
checked separately. Tests preserve the central glyph while rotating only the ring.

These functional recognition references are embedded privately and are not app
artwork. Original Black Desert imagery © Pearl Abyss Corp.; user-provided crops,
not publicly hosted publisher reference images.

`active-gold-ring-user-20260912.png` is the user's September 12 screenshot of an
active Agris icon. The three `active-gold-ring-hdr-20260912*.png` crops were read
from the same user's running game through Grindcrest's Windows window capture
backend that day (3840 × 2160, HDR, tone mapping enabled). Only the detected icon
and an eight-pixel margin were retained. They cover successive ring positions.
The bright glyph includes nearly neutral RGB (245, 243, 242) after HDR tone
mapping; these fixtures prevent its warm highlights from being counted as gray.
They are test fixtures only; they do not add new embedded recognition templates.
