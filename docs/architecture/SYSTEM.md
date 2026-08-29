# Architecture

Desktop -> Application -> Domain.
Infrastructure implements storage/acquisition interfaces; Vision implements IOcrEngine with a serialized C ABI adapter.
Controls depends on Domain geometry only. Desktop's constructor composition wires implementations.
No external reference repository is required at runtime.

## Core
C++17 with OpenCV and ONNX Runtime CPU. One Engine owns its sessions and mutex; no mutable global detector/recognizer.
C ABI version 1: wmi_create, wmi_destroy, wmi_inspect, wmi_crop, wmi_free.
Image input is borrowed BGR24; responses are owned UTF-8 JSON allocated/freed in the same native module.
Crop images are PNG byte arrays serialized as base64. Exceptions are converted into explicit error payloads.
Managed gate prevents disposal during active OCR. Cancellation cannot interrupt a native inference already executing; its eventual result is discarded using the session generation.

Pipeline: mask/crop search ROI -> detect DB regions -> deterministic geometric row ordering -> rectify -> CTC decode -> optional whole-ROI 180-degree evaluation -> source-space boxes.
No text correction, cross-end reconciliation, TYPE classification, target-conditioned decoding or central-image fallback.
The initial DB/CTC preprocessing contract is documented in assets/ocr/README.md. Thresholds and rectification need calibration against the actual product dataset before production claims.

## Session
One cycle snapshots a full recipe revision and owns copied frame bytes.
WaitingEnd1 -> ProcessingEnd1 -> WaitingEnd2 -> ProcessingEnd2 -> Completed.
Stop increments generation and cancels pending work; a late result cannot populate another cycle.
Duplicate frame identity, wrong image dimensions, bad recipe, missing model and persistence failures cannot produce OK.
Manual capture uses a new live frame after entering the next waiting state. External trigger pairing is not implemented yet.

## Persistence
Versioned JSON recipes with immutable generation-named reference images. JSON rename is the publication point.
Results include exact recipe, ordered OCR evidence, verdicts and full source PPM images.
Deleted recipes are moved out of the catalog but remain recoverable.
Concurrent multi-process recipe editing is not supported; one application instance per data directory is intended.

## NAcquire integration
Inspected checkout: NVision/NAcquire at the path supplied by the user, 2026-08-29.
Its current implementation has a synthetic OpenCV provider, and Hikrobot is only an INTERFACE target. No Hikrobot DLL or actual C# test sample is present beyond wrapper skeletons.
Adapter matches the C API header, validates version/stride/format, copies pixels before frame release and exposes simulation state.
Real Hikrobot testing requires the validated native package and its dependencies. No camera is opened during app startup or UI smoke.
