# Shared rotation platform

`RotationPlatform` owns timing, recovery, reference samples and decision history for Hermesia, Aphrodon and Event Horizon. The old tracker names are thin compatibility constructors. New spots describe their messages/crop in `RotationMessageProfile` and their ordered steps, alternatives, optional branches, start/failure/AFK-end messages and setup counters in `RotationDefinition`. They register in `RotationProfiles`; they do not implement another tracker.

## Special events

Special events are mechanics a full rotation does not need. They appear at random, either in addition to the regular mechanics or in place of one. `RotationDefinition.SpecialMessages` lists all their messages and `SpecialStartMessages` the message that counts one occurrence: Aphrodon's `agris` (replaces a Hog wave) and Event Horizon's debris mini AFK (`debris`, `distortion`, `spacetime`; counted by `debris`). Hermesia has none.

- A step shared by a regular and a replacing special message records the special variant as its own section (`wave-3-special`), so its timeout and mechanic reference never mix with the regular wave. Extra branches already have their own steps.
- The snapshot carries the regular comparison as well (`WithoutSpecialEvents`: best, ideal, mechanic bests and count over rotations without a special event). The preference `IncludeSpecialEventRotations` (settings key `RotationIncludeSpecialEvents`, default true) selects which pool `RotationMonitor.Snapshot` shows, and whether Rotations / h uses rotations with special events.
- `SessionSpecialEvents` counts every special event of the session at the shown spot, including aborted and running rotations; superseded run versions do not count twice. A session that moved between spots keeps each count with its spot. `SessionRotationTiming.Special` marks completed rotations with a special event, and `SpecialEventSeconds` holds the seconds at which each one was recognized, so the session timeline can place them.
- `RotationPhase.Special` marks the phases; both overlay renderers outline them with `RotationPhases.SpecialColor`.

## Spot options

- `AmbientMessages` without `AmbientAfter` are recorded at any time during a run without moving the phase (Magaia's fragments, death and return).
- `AfkEndStartsRun`: the AFK-end banner also opens the next clean run (Magaia resets itself while the player stays). A repeated sighting within a minute of that start is ignored. The AFK-end message may also be a step (Magaia's cycle boundaries); only at the last step does it end the run, and a repeated sighting within a minute of entering that step is ignored.
- A message that belongs to exactly one step of the definition may repeat inside that phase (Elion's Tears' orbs) and is ignored. Where the definition models the repetition itself (Hermesia's five offerings, Aphrodon's nine waves, Magaia's three knights), one more than modelled is out of order: the run aborts and resynchronises.
- `SpecialComparison` decides how rotations with special events are compared:
  - `Separate` (Aphrodon): rotations with a special event form their own pool; `IncludeSpecialEventRotations` picks the pool shown.
  - `ByCount` (Event Horizon, every debris mini AFK lengthens the rotation): best, ideal and mechanic bests use only rotations with the current run's count so far, else the nearest higher, else the nearest lower count (`ComparedSpecialEvents`).
  - `Ignored` (Magaia, whose fragments fall into phases with a fixed timing): special events are counted and shown, but every rotation is compared.

  Only `Separate` marks rotations as special and excludes them.
- `RotationStep.Midpoint`: the step's message reliably marks the middle of its optional branch (Event Horizon's `distortion`); the value is the usual length of each half in seconds. A loading screen can swallow the branch's opening (`debris`) and closing (`spacetime`) banners. The middle alone fills in the unread opening half a branch earlier (the opening section's average, else `Midpoint`; never before the last recorded event), but only where it fits the order, so a late repetition cannot invent the next wormhole's mini AFK. The unread closing is filled in half a branch after the middle when the next phase begins, or when the middle's section runs past its timeout, instead of leaving the run incomplete or aborting it. Filled-in events carry `RotationEvent.Inferred`, count as special events and form phases, but are no checkpoints, so they never border a mechanic best.
- Ein Spot bleibt gewählt, bis ein *anderer* Spot erkannt wird. Ein Aufrufer ohne Spot sagt nichts über den bereits erkannten aus, deshalb behält der Monitor sein Profil samt laufender Rotation.
- `UnmistakableStartMessages`: the start messages the definition uses neither as a step nor as an ambient banner. Only these are watched for before a session (`RotationStartWatcher`) and handed to `RotationMonitor.ObserveRotationStart`, which replays them into the spot's provisional profile so the first rotation is measured from its banner. Hermesia's offering and Event Horizon's loot start qualify for nothing here.
- `RotationMessageProfile.CountedKinds`: stacked banners of one message. The search counts the lines per sample (`CountLines`); every increase that holds for a second sample is a new occurrence, single unreadable samples are bridged, and lines already visible at the buffer start continue the oldest known lines.

## Recovery contract

- A message can synchronize tracking in the middle of a run. This remains a partial observation and cannot become a best time or train timeout averages.
- A known AFK end opens a clean boundary and starts a fixed five-second lockout for **trashloot** starts. Loot neither extends nor renews it. A start message overrides the lockout. A consumed boundary cannot make a later failed attempt look complete.
- A definite failure or a backward/out-of-order transition closes an aborted run. The same unexpected message can open a partial observation immediately. Missing forward messages retain an explicitly incomplete observation; they never silently count as a complete run.
- Optional branches can require their closing message once their opening was observed. For example, a detected debris AFK without its ending is incomplete.
- For each section, the latest 20 valid measurements are kept in chronological order, separately per spot, across sessions. After three measurements, a duration strictly above twice their arithmetic mean, but never less than the mean plus 60 seconds (`MinimumTimeoutSlackSeconds`), aborts the run: twice a few seconds is no slack (Magaia's last knight falls on average seven seconds before the final phase, but half a minute is just as normal). After such a timeout, a message several cycles share resumes after the last known step instead of the first cycle, so the rotation's real end still opens the next rotation cleanly; a pause forgets that position. Incomplete, aborted, superseded and active runs never train this reference.
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
