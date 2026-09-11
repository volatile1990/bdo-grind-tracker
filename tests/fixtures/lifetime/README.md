# Lifetime replay fixtures

`recording1.txt` through `recording4.txt` preserve the original accepted inputs for historical `lifetime-v1` replay. Their expected results are reproducibility baselines, not independent accuracy targets. `reference-sessions.json` keeps independent user-confirmed results separate.

The corresponding `.raw.txt` files contain original Normal observations, original timestamps, and changes to the recorded parsing context. Consecutive identical contexts are stored once. Fields unrelated to raw parsing or normal-row eligibility, such as UI events and tracing, are omitted. No parser results replace OCR text. Each file contains the SHA256 of its original observation journal. They are self-contained and do not require the original user's directories.

The `.occupancy.txt` files contain measured matches against previous accepted glyph masks, indexed by one-based recording sequence and current physical slot. Each record is `sequence|currentSlot,previousSlot,correlation;...`. A repeated current slot lists additional matching templates, not additional loot. The data carries no identity or quantity. Source feature JSON and observation journal hashes are included in each file. These measurements use the original image pixels and a correlation threshold of 0.90; no OCR was rerun for them.

The Core V3 regression uses the original validated name/quantity fallback and Core partial-text association, with no new interpretation supplied by its parser delegate. It replays the complete raw observations, including rejected fragments. The raw fixture also preserves parsing contexts for App parser integration tests. Its expected helmet totals are 576/264/2038/604; historical V2 on the same observations remains 576/264/2034/600. The independent references for the four recordings are 576/324/2050/604.

The new version recovers an independently image-verified fourth-recording wave from eight to nine groups of four helmets. This does not establish perfect recognition in every recording or exact arrival timestamps for repeated equal rows. The tests keep single-reading, unconfirmed extra-slot, name-glitch and fading-reappearance guards as separate requirements.
