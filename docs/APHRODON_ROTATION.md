# Aphrodon Temple — Rotation Monitor

The tracker-selected spot activates this profile automatically. Recognition, crop,
buffering and records belong exclusively to the Rotation Monitor. Loot recognition
does not use these settings.

## Source and crop

Source: `Aphrodon Spot Events.drt`, supplied 2026-09-14; 60 fps timeline.
Video: `Black Desert 2026.09.14 - 18.40.03.05.mp4`, 2560 × 1440.
The video clip starts at timeline frame 216000 with source in-frame 10861.

Decoded Resolve crop fractions: left 0.40625, right 0.4072916667,
top 0.6166666667, bottom 0.3611111111. The retained rectangle is calculated
from the current capture dimensions with outward rounding: 478 × 32 pixels
at (1040, 888) on the source resolution. The crop was checked against the video.

The monitor buffers at most 21 cropped samples over ten seconds (500 ms cadence),
probes every three seconds, confirms new banners with two samples and backdates
them to the earliest matching buffered sample. Cached OCR is reused. Aphrodon
uses single-line OCR with one enlarged grayscale fallback; Hermesia retains its
existing multi-strip recognition.

## Events and rotation

| Kind | Message |
| --- | --- |
| setup | As the scarecrow falls, the workers of the golden fields gather around. |
| small-scarecrow | The harvest winds begin to blow. |
| restart | A golden fragrance rides the wind. |
| hog | You sense the energy of abundance. |
| agris | You sense an intoxicating energy of abundance. |
| big-scarecrow | A scarecrow blessed with abundance appears. |
| afk | Agris's blessing settles over the fields. |
| end | Agris's blessing fades from the fields. |
| failure | The scarecrow awakens as the commotion continues. |

Hog and rare Agris events share a nine-event total. Each is followed by a large
scarecrow. A complete reference requires nine Hog/Agris events and the AFK phase
through its end. Scarecrow messages are optional observations, not comparison
checkpoints. AFK end closes the old rotation and starts
the next one directly. Failure invalidates the active attempt; up to three small
scarecrows can awaken. Incomplete observations never become best times.

Activation after startup/failure follows the original-video Golden Fragrance
banner. Each Harvest Winds message counts one placed small scarecrow, as clarified
by the user. Golden Fragrance confirms that all three are active, including when
the final placement has no separate Wind banner in the recording.

While inactive, a setup hint and 0/3 counter replace the timeline, including after
failure. Placements increment the count and awakenings decrement it. Each Hog/Agris
and following large scarecrow share one timeline band; Hog/Agris spawns have small
markers. Golden Fragrance also recovers tracking if a failure banner was missed.
Rejected completions display a reason and save the latest event trace to
`aphrodon-rotations.json.last-rejected.json` for diagnosis.

Records are stored separately in `aphrodon-rotations.json`; completed rotations also
flow through the existing per-session persistence. Best/ideal/sector comparisons
use matching event checkpoint sequences, so different Hog/Agris arrangements do
not mix unrelated mechanic timings. The demo is based on the supplied recording
and never writes records.

## Recording notes

The Text+ title at 149.1 seconds calls the third message “harvest winds”, but the
actual video says “A golden fragrance rides the wind.” The video at 56.7 and
103.5167 seconds does say “The harvest winds begin to blow.” The other explicit
restart at 1194.55 seconds also says “A golden fragrance rides the wind.”

The demonstrated complete rotation runs from 149.1 to 913.5 seconds (764.4 seconds).
Its rare Agris event replaces Hog number six. AFK starts at 760.1167 seconds.
