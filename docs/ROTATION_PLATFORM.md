# Shared rotation platform

`RotationPlatform` owns timing, recovery, reference samples and decision history for Hermesia, Aphrodon and Event Horizon. The old tracker names are thin compatibility constructors. New spots describe their messages/crop in `RotationMessageProfile` and their ordered steps, alternatives, optional branches, start/failure/AFK-end messages and setup counters in `RotationDefinition`. They register in `RotationProfiles`; they do not implement another tracker.

## Recovery contract

- A message can synchronize tracking in the middle of a run. This remains a partial observation and cannot become a best time or train timeout averages.
- A known AFK end opens a clean boundary and starts a fixed five-second lockout for **trashloot** starts. Loot neither extends nor renews it. A start message overrides the lockout. A consumed boundary cannot make a later failed attempt look complete.
- A definite failure or a backward/out-of-order transition closes an aborted run. The same unexpected message can open a partial observation immediately. Missing forward messages retain an explicitly incomplete observation; they never silently count as a complete run.
- Optional branches can require their closing message once their opening was observed. For example, a detected debris AFK without its ending is incomplete.
- For each section, the latest 20 valid measurements are kept in chronological order, separately per spot, across sessions. After three measurements, a duration strictly above twice their arithmetic mean aborts the run. Incomplete, aborted, superseded and active runs never train this reference.
- Capture drives timeout checks independently of the overlay. The buffered recognizer gets a ten-second confirmation allowance; the recorded timeout boundary remains the actual twice-average time. A late confirmation can repair this decision.
- The recognition gate suppresses repeated sightings. Event Horizon allows a longer duplicate window to bridge teleport blackouts. Repeated missing-image notifications do not flood the session journal.
- OCR failure, image loss, pause and spot changes preserve an aborted attempt. Recognition can resume automatically. Starting without any observation does not create a spurious history entry.

Event Horizon has no explicit failure message (confirmed by the user). Its failure-message list is intentionally empty; recovery uses the shared ordering, timeout and capture-recovery rules. This is a complete spot definition, not a missing message pattern. Hermesia and Aphrodon retain their known explicit failure phrases.

## Capture time and corrections

Confirmed messages carry their first visible capture time, not their OCR completion time. The platform replays ordered observations when new messages arrive. This also handles an AFK-end confirmation arriving after loot in its lockout window, or an earlier phase arriving after a later one. Active loot only buffers timestamps; replay work is performed for message/timeout changes. Section-reference queues avoid scanning all stored runs for each loot observation.

`RotationTimelineEntry` version 1 retains capture time (`At`), processing time (`RecordedAt`), spot, evidence/decision kind, run ID, detail and optional loot identity/quantity/revision. Recognized repeated banners are retained independently of whether the gate accepts another transition. Decisions that change reference the old entry through `Corrects`; retracted decisions append a correction. Original evidence is not deleted. Loot entries preserve the analyzer's reported drops/deltas and revisions; they do not invent unobserved drop times.

## Persistence

Session history and current-session checkpoints contain `RotationTimeline` plus all run outcomes. The timeline uses lossless `gzip-json-v1` JSON compression and can still read plain arrays. Capture checkpoints include the active run. Restoring it records an abort at its last observed time; the next observation resynchronizes. An unconsumed AFK-end lockout survives restoration.

Spot reference files use `<spot>-rotation-platform-v1.json`, with run timing version 3 and explicit section boundaries. Existing `<spot>-rotations.json` files are read without changing the original file. Structurally complete old runs are migrated for best-time comparisons; unverifiable runs are preserved but excluded. Old files did not record chronological measurement dates, so imported runs do not seed the *latest 20* timeout window. Fresh valid observations establish that window. Corrupt reference files are not overwritten, and recognition continues with an exposed storage error.

Overlay layout/presentation is intentionally a separate follow-up. The snapshot already exposes waiting/partial/confirmed tracking state, current section, last interruption reason and loot lockout boundary.

## Recorded regression evidence

`tests/fixtures/rotation/event-horizon-hour.messages.json` contains native-Windows-OCR message observations from the supplied 3,757.97-second, 2560×1440 recording, sampled every two seconds: 120 confirmed messages and eight AFK ends. It contains no inferred loot. Both message-only recovery and an explicitly synthetic loot-start integration scenario are tested. The original diagnostic's mid-rotation `spacetime` entry is covered separately.

Optional local tests in `RotationRecordingReplayTests` read the supplied video, diagnostic JSONL and ultrawide screenshot when `ROTATION_RECORDING_DIRECTORY` is set. `ROTATION_REPLAY_CROPS` can point to pre-extracted banner crops at two-second intervals, numbered `000001.jpg` onward; `ROTATION_REPLAY_OUTPUT` selects the evidence log. Large videos and screenshots stay outside the test fixtures. The ultrawide check verifies the lower banner location included by the expanded crop.
