# Loot-scroll gauge recognition references

These small crops are functional image-recognition references from Black Desert's
official publisher, Pearl Abyss. They are embedded privately in the detector,
not presented as Grindcrest artwork or exposed by the web frontend.

* `inactive.png`, `active-2.png`: official NA/EU UI demonstration published
  November 3, 2021, showing the gauge before and after entering a town.
  Original: <https://s1.pearlcdn.com/NAEU/Upload/News/ef3aa29877020211103060920801.gif>
  Frames 10 and 0 respectively; 56 × 56 crop at (377, 153).
* `active-1.png`: official Korean UI demonstration published September 3, 2021.
  Original: <https://s1.pearlcdn.com/KR/Upload/News/541769d0c2020210902220032044.gif>
  Frame 0; 56 × 56 crop at (70, 43). Frame 20 is the collapsed-menu test case.
* Independent 2024 inactive reference used only by tests:
  <https://s1.pearlcdn.com/bdo/brand/editor_template/2024/03/26/c041000679120240326075450639.png>
  Publisher guide: <https://blackdesert.pearlabyss.com/Asia/en-US/Game/Wiki?_masterWikiNo=224>
* `tests/fixtures/loot-scroll/inactive-user-20260910.png` and
  `inactive-expanded-user-20260910.png`: original-resolution HUD crops supplied
  by the Grindcrest user on September 10, 2026 after reporting a missing warning.
  They show the inactive gauge with the level menu collapsed and with level 0
  selected, respectively. These user-provided regression fixtures are not public
  publisher references and are not embedded in the shipped detector.
* `tests/fixtures/loot-scroll/active-2-user-20260910.png`: active level-2 HUD
  crop supplied in the same user conversation. The bag and both arrows are visible;
  the outer rim and plus button are cropped. These excluded pixels are not required
  by recognition, while a clipped bag/level glyph remains insufficient evidence.
* `tests/fixtures/loot-scroll/inactive-eight-hours-user-20260910.png`: user-provided
  original-resolution HUD crop from the same conversation, showing the level-0 menu
  and remaining time `8h 12m 10s`. Used only for locator and native timer OCR regression
  tests, including relocated Full HD, 1440p and 4K frames, UI scaling and HDR mapping.
  It is not a public publisher reference or an embedded detector template.

The active gauge has a bright bag and one/two upward chevrons. The inactive gauge
has a dim bag and a cross. The level menu's fill is cumulative (0; 0+1; 0+1+2),
and can be collapsed, so recognition uses the central symbol instead. A mask
excludes the surrounding world, remaining-time text, level buttons, and plus button.
Tooltips, scroll inventory items, and missing pixels are not status evidence.
At runtime, the visual gauge match only locates the adjacent timer. The composite
frame reader does not forward the symbol's status or level. Scroll activity and
level are inferred from the countdown's change across timed observations.

Original-resolution frames live under `tests/fixtures/loot-scroll`. Tests also
exercise resizing, relocation, missing/cropped symbols and contradictory gauges.
HDR tests pass the source pixels through the actual floating-point capture tone
mapper at different white levels. Verification normalizes the masked symbol's
brightness and contrast, then requires both high correlation and low pixel residual.
Brightness alone is not evidence that a gauge is inactive.
These references do not establish support for every game UI/opacity configuration;
unrecognizable or ambiguous imagery yields Unknown rather than an inactive warning.

Black Desert imagery © Pearl Abyss Corp. Retrieved 2026-09-10.
