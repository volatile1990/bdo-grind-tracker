# PP-OCRv6 Small recognition model

- Official source: https://huggingface.co/PaddlePaddle/PP-OCRv6_small_rec_onnx
- Pinned revision: `b8f84f0b80c529de40b4fbb3544b84fa7233a513`.
- Retrieved 2026-09-08; Apache License 2.0, see `licenses/PaddleOCR-Apache-2.0.txt`.
- `sources.json` records upstream URLs, SHA-256 hashes and file sizes.
- `inference.onnx` is unchanged (21,159,378 bytes).
- `characters.json` is a UTF-8 JSON array derived from the pinned `inference.yml`:
  `["blank"] + PostProcess.character_dict + [" "]` (18,710 classes).

Runtime: Microsoft.ML.OnnxRuntime 1.29.0, CPU, two intra-op threads per worker,
one inter-op thread, sequential execution. No model downloads occur in the app.
Input: BGR, height 48, aspect-preserving width in 320–3200, zero-padded normalized
float32 NCHW tensor; pixels are normalized with `pixel / 127.5 - 1`.
Output: greedy CTC, blank class 0, adjacent repeated classes collapsed.

The shipped model is multilingual; enabling a new game language additionally
requires its catalog aliases and support in the primary Windows OCR path.
