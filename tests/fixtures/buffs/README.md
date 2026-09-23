# Buff HUD regression captures

Captured locally from the user's running Black Desert client on 2026-09-21,
using Grindcrest's passive window capture at 3840 × 2160. Only small
buff-bar crops are retained; the full game images are not included.

The user enabled a Harmony Draught – Demihuman and a Simple Cron Meal for this test.
The Simple Cron Meal variant was explicitly confirmed by the user.
The automatic-reader regressions use the bundled client HUD artwork and item
mapping, with no personal profile. Separate calibrated-reader tests retain the
original captured icon crops to exercise the optional profile path.

- `bar-13m-114m.png`: 13 minutes and 114 minutes.
- `bar-9m-110m.png`: 9 minutes and 110 minutes.
- `bar-9m-109m.png`: 9 minutes and 109 minutes, after the meal timer rolled over.
- `harmony.png`, `cron.png`: timer-free icon crops from the first capture.
- `bar-edania-tenacity-8m.png`: user-supplied HUD screenshot from the local EXE
  test on 2026-09-21. Contains ordinary Harmony–Edania and Perfume of Tenacity,
  both at 8 minutes, plus Mystic Beasts All AP at 29 minutes and Simple Cron at
  28 minutes. This independently exercises transparent item-style HUD artwork,
  normal/Immortal discrimination, and native OCR of the closed digit 8.
- `bar-tenacity-harmony-17m.png`: later passive 4K capture cropped to the saved
  buff panel. Ordinary Harmony–Edania and Tenacity both show 17 minutes. Combined
  with the 8-minute capture, it covers independent renewal confirmation when
  Tenacity alone was unreadable for one intervening scan.
- `bar-cron67m-boon2h.png`: user-supplied comparison from the local EXE test.
  Simple Cron Meal shows 67 minutes and Adventure's Boon shows 2 hours. This
  covers native OCR of short hour labels and confirms that initially unreadable
  buffs do not create consumption tiles or costs when first recognized later.
- `bar-body20m-cron119m.png`: independent later capture, cropped to 160 × 135;
  automatically found Body Enhancement at 20 minutes and Cron at 119 minutes.
  This also tests the publisher-supplied Body Enhancement symbol on a real
  current game frame without a personal profile. The purchased Body Enhancement
  duration remains unknown because its variants share one symbol.
- `bar-tent-refresh-4h.png`: passive capture on 2026-09-22 after the user reported
  reapplying Body Enhancement and Adventure's Boon. Both show `4h`; Harmony–Edania
  and Tenacity show `4m`, Mystic Beasts All AP `47m`, and Simple Cron `32m`.
  Both existing OCR scales returned empty text for all four short `4h`/`4m` labels.
  A tightly cropped, repeated timer strip recovers them with three identical labels
  in each of two agreeing preparations. No full game frame is retained.

The original 48-pixel timer preparation read `13m` as `113m` and split `114m`
into `i 14m` on the installed Windows OCR engine. A second interpolation after
padding preserves the fine digit shapes. Native OCR regressions exercise all
three images; contradictory parsed preparations still remain unknown.

The later Edania/Tenacity capture also exposed `8m` being read as `3m` at that
48-pixel intermediate height. Timer preparation now uses 56 pixels; native OCR
regressions cover both automatic and calibrated crops, including `8m`, `28m`,
`109m`, and `110m`. Contradictory parses are still rejected.

On the later Boon fixture, both 56-pixel preparations return no text for `2h`.
Unreadable timers now try a smaller 32-pixel preparation; both grayscale and
thresholded results must agree. This fallback does not override contradictory
primary results or change timers already read successfully.

Completely empty results at both scales now allow one final repeated-label pass.
Only pixels from the original timer crop are used. The three copies must produce
three equal parsed timers, and grayscale and thresholded preparations must agree
on both duration and precision. Nonempty or contradictory original results cannot
be overridden. Native regressions cover `4h` and `4m` separately and verify that
each renewed tent buff counts once without charging its initial timer or
rebooking an unchanged hours label.

`bar-official-harmony-20240725.png` is an independent publisher HUD reference:
frame 85, crop `(0, 0, 280, 140)` from Pearl Abyss's
[Harmony introduction animation](https://s1.pearlcdn.com/KR/Upload/News/d258aa73b4120240719230950593.gif),
published in the [July 25, 2024 patch notes](https://blackdesert.pearlabyss.com/TR/en-US/News/Notice/Detail?_boardNo=18797).
It contains Body Enhancement, Harmony, and Cron family graphics with other
unassigned neighboring buffs. Its Korean countdowns are not English/German timer
OCR fixtures. See `data/ocr/buffs/SOURCES.md` for extraction coordinates and the
limits of price-variant attribution. Black Desert imagery © Pearl Abyss Corp.
