# Fresh-image Paddle/Windows comparison

This developer CLI reads every selected normal and rare PNG again through the **current production `CompanionLootFrameAnalyzer`**. It does not substitute recorded OCR text, quantities, decisions or events as analyzer input. Existing events are used only as comparison values in the report. It does not launch the app UI, start capture, read BDO configuration, or write user settings/history.

Both modes use the current item catalog, automatic spot lock, quantity policies, normal recovery, background Paddle review, alignment review, normal/rare reconciliation and shared ledger. `windows` supplies no primary Paddle reader; `paddle` adds the production `PaddlePrimaryLootReader` ahead of the same Windows fallback. For Magaia this uses the current minimum of 2. No counting logic is duplicated in this tool.

## Build and run

From the repository root, publish to an ignored artifact directory so a long-running replay does not lock normal app/test build outputs:

```powershell
dotnet publish tools/PaddlePrimaryReplay/PaddlePrimaryReplay.csproj -c Release -r win-x64 --self-contained false -o artifacts/paddle-primary-qa/runner

$source = 'C:\Users\marku\AppData\Local\BdoGrindTracker\diagnostics\loot-20260910-150334-c7555f5bbf8745829d035edd9248fbe1'
$runner = 'artifacts/paddle-primary-qa/runner/BdoGrindTracker.App.Tests.exe'

# Short geometry/runtime check; these are explicitly marked partial results.
& $runner --recording $source --output artifacts/paddle-primary-qa/windows-smoke --mode windows --data data --max-frames 40
& $runner --recording $source --output artifacts/paddle-primary-qa/paddle-smoke --mode paddle --data data --max-frames 40

# Full fresh OCR of all 4,655 frame pairs and the recorded final completion.
& $runner --recording $source --output artifacts/paddle-primary-qa/windows-full --mode windows --data data
& $runner --recording $source --output artifacts/paddle-primary-qa/paddle-full --mode paddle --data data
```

The assembly is named `BdoGrindTracker.App.Tests` to use the app's existing friend-assembly access, matching earlier diagnostic probes. This executable is a developer tool, not the test suite or the Grindcrest app.

Output directories must be new or empty and must be outside the original recording directory. Each run preserves its results; it never silently reuses old OCR or overwrites a previous replay. `Ctrl+C` cancels analysis and retains a failed/partial journal. Exit 0 means a completed replay with unchanged source files, not agreement with the game's true inventory. Exit 1 indicates a failure. `--help` lists all arguments.

## Exact crop geometry

The cited recording stores normal panels of **511×447** pixels and rare **bands** of **574×89** pixels. It does not store its full calibration or full screen. Defaults are the explicit assumptions `--scale 1.49 --font StrongSword --language en`; alternative recorded scales, fonts and languages may be supplied.

At scale 1.49 the production geometry gives a 447-pixel panel height and normal slot Y positions 373, 298, 224, 149, 75 and 0. The reduced normal width is reproduced with anchor `(123,223)` at the left screen edge. Its panel is `(0,0,511,447)`, with the production normal-row left inset of 60 pixels. A rare anchor at `(761,223)` places the already extracted rare band at `(575,179,574,89)`, separated by 64 blank pixels from the normal panel. The virtual frame is **1149×447**. This placement affects neither crop pixels nor the production relative row positions.

Every PNG is decoded to BGR and copied **unscaled** directly into the bitmap's locked pixel memory. This matches the production decoder and avoids GDI alpha blending of irrelevant DXGI alpha bytes. Both reconstructed normal/rare crops are compared pixel-for-pixel with their decoded source PNGs using OpenCV infinity norm on every frame, and any nonzero difference aborts. Metadata dimensions and every recorded normal-row Y coordinate must also match production geometry. A changed crop size or unsupported calibration fails explicitly. Original HDR and tone-mapped flags are passed verbatim; the tool does not tone-map the saved images a second time.

## Reproducibility and outputs

All analyzer calls use the original per-frame UTC capture timestamps, preserving the counter's temporal behavior. Recorded completion entries flush the production analyzer. A frame-limited run receives a final flush at its last selected capture timestamp. No artificial real-time waits are inserted.

- `manifest.json`: options, geometry assumptions, source header versions, current assembly/model SHA-256, and pre-run SHA-256/byte size for the input JSONL, optional count summary, and every selected PNG.
- `observations.jsonl`: all freshly recognized observations, finalized normal/rare events, decisions, recovery and review diagnostics, and linked reconciliation traces. This is a normal current-version counter-replay journal; PNG filenames refer to the **original source directory**, as stated in the manifest. The images themselves are neither copied nor altered.
- `frames.jsonl`: per-frame original timestamp/sequence, measured time, actual Windows OCR invocation count, original-versus-fresh observations/events, and primary Paddle diagnostics.
- `summary.json`: all original item keys including zero-valued missing detections, fresh/source quantity comparisons, accepted observations and booked quantities by source, review outcomes/errors, timing, completeness and source-integrity confirmation.

Progress is printed every 100 frames. All original file hashes are checked again after the final flush. Successful unchanged hashes confirm that the inspected source bytes stayed intact. Timings include the actual production pipeline and are recorded separately for frame preparation and analysis. Parallel Windows/Paddle runs compete for CPU; their elapsed/CPU times must not be described as isolated performance benchmarks.

The recorded 7,414 helmets / 64 Black Stones are a historical comparison, not ground truth. Fresh results also include current fixes and current quantity policies, so differences from that recording do not automatically measure the benefit of Paddle alone. Compare `windows-full` against `paddle-full` to isolate the configured primary-reader choice on the same current code and source images.
