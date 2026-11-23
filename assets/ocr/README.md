# OCR runtime assets

The runtime files are intentionally ignored by Git. Copy the validated files to this folder before building a production package.

| File | Runtime contract | SHA-256 of validated local asset |
|---|---|---|
| `detector.onnx` | PaddleOCR DB, float `[1,3,960,960]`, probability `[1,1,H,W]` | `F2EB027BAB8C8EBA1DECAC8FFC429DA981F6AE256E87515B90853C81B25B87E5` |
| `recognizer.onnx` | English PP-OCRv3 CTC, float `[1,3,48,320]`, probability `[1,T,C]` | `5EAA8475427462B3B81C7CF873E2BBC62272B7B983220A042E107CA251406E05` |
| `dictionary.txt` | UTF-8, one token per line, excluding CTC blank | `5662DF9D2D03F0E8CA0D3B0649D6ACBAB904B6A14B3D3521463C71C37C668CE3` |

Local source models used for this development build:

- Detector: `D:\src\ocr_phone_case\models\det\ch_PP-OCRv4_det_infer`
- Recognizer: `D:\src\ocr_phone_case\models\rec\en_PP-OCRv3_rec_infer`
- Dictionary: `D:\src\ocr_phone_case\models\en_dict.txt`
- Conversion: fixed Paddle input shapes, then Paddle2ONNX 1.3.1.

The current marker alphabet is alphanumeric plus `.` and `/`. OCR removes layout whitespace and maps `:` or `,` glyph confusions to the printed dot before the domain layer performs its strict ordinal comparison. `/` remains part of the recognized text. OCR region count comes only from DB text detection; characters never create or split regions.

Run `scripts\test-real-images.ps1` after `scripts\build.ps1 -RequireOcrAssets`. The checked manifest is `tests\real-images.expected.json`; source BMP files remain outside the repository.

Missing or incompatible assets block OCR. No mock OCR is substituted. Replacing a model requires updating checksums, provenance, and the real-image acceptance report.
