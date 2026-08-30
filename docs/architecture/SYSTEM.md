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

Pipeline: mask/crop search ROI -> detect DB regions -> deterministic geometric row ordering -> rectify -> CTC decode -> marker-alphabet cleanup -> optional whole-ROI 180-degree evaluation -> source-space boxes.
Marker cleanup is independent of recipe targets: preserve case/alphanumerics and `.`/`/`, remove layout whitespace, map `:`/`,` glyph confusions to the supported dot. `/` remains in the text and never splits a detector region. There is no cross-end reconciliation, TYPE classification, similarity matching, target-conditioned decoding or central-image fallback.
The DB/CTC preprocessing contract and model hashes are documented in assets/ocr/README.md. The supplied 26 BMPs pass the checked native and managed Load Image regression; broader production calibration is still required.

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

## Camera acquisition
The Desktop composes `HikrobotMvsCamera` through `ICamera`. It uses the official Hikrobot MVS .NET wrapper and native runtime for GigE/USB enumeration, device lifecycle, continuous acquisition, parameter writes and owned-buffer release. The app copies every frame to packed BGR24 before releasing the SDK buffer. Mono8 and RGB8/BGR8 have direct conversions; Bayer and packed formats use the MVS pixel converter. Frame dimensions, lengths and repeated SDK frame numbers are rejected.

The SDK is initialized lazily on Scan; startup and offline UI smoke never connect to hardware. GigE open applies the SDK-recommended packet size. The current UI deliberately selects continuous mode with trigger off; PLC/external trigger pairing is a later slice.

The production MainWindow invokes camera discovery once from its Loaded event with a five-second UI timeout. The ViewModel exposes explicit Idle/Finding/NotFound/Found/Connected/Acquiring/Error states and derived enable rules; it does not reuse the broad model-editing flag for camera controls. A timed-out native enumeration remains tracked so retry can consume its eventual result without starting concurrent SDK calls. Offline smoke disables auto-discovery explicitly.

Hardware validation on 2026-08-30 passed enumerate/open/ExposureTime/Gain/start/three-frame grab/stop/close against MV-CE120-10GM serial `00G29911748`, IP `169.254.172.4`, producing 4024×3036 BGR24 frames.

`NAcquireCamera` remains a legacy implementation of the same interface. Its inspected checkout has a synthetic OpenCV provider and a placeholder Hikrobot target, so it is not composed into the production Desktop.
