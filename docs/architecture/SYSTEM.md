# Architecture

Desktop -> Application -> Domain.
Infrastructure implements storage/acquisition interfaces; Vision implements IOcrEngine with a serialized C ABI adapter.
Vision also implements ITemplateMatcher through a separate matching ABI v1. InspectionSession combines exact OCR/direction with per-end terminal matching on the same owned frame. See TERMINAL_MATCHING.md for algorithms, thresholds, masks, schema v3, teaching workflow and acceptance limits.
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
WaitingEnd1 -> ProcessingEnd1 -> WaitingEnd2 -> ProcessingEnd2 -> Completed. After persistence and verdict output, Desktop snapshots the completed presentation and begins a new WaitingEnd1 cycle automatically; the previous snapshot is review-only and never feeds the active session.
Stop increments generation and cancels pending work; a late result cannot populate another cycle.
Duplicate frame identity, wrong image dimensions, bad recipe, missing model and persistence failures cannot produce OK.
Manual capture uses a new live frame after entering the next waiting state. Camera-line and PLC sources enter through `TriggerRouter`; PLC may use a shared rising edge or distinct per-end inputs.

## Persistence
Versioned JSON recipes with immutable generation-named reference/template images. JSON rename is the publication point. Schema v2 adds recipe-owned `CameraInspectionIo`; schema v3 adds per-end terminal templates/profiles. Schema v1/v2 remain readable and migrate on the next save without silently enabling matching.
Results include exact recipe, ordered OCR evidence, verdicts and full source PPM images.
Deleted recipes are moved out of the catalog but remain recoverable.
Concurrent multi-process recipe editing is not supported; one application instance per data directory is intended.

## Camera acquisition
The Desktop composes `HikrobotMvsCamera` through `ICamera`. It uses the official Hikrobot MVS .NET wrapper and native runtime for GigE/USB enumeration, device lifecycle, continuous acquisition, parameter writes and owned-buffer release. The app copies every frame to packed BGR24 before releasing the SDK buffer. Mono8 and RGB8/BGR8 have direct conversions; Bayer and packed formats use the MVS pixel converter. Frame dimensions, lengths and repeated SDK frame numbers are rejected.

The SDK is initialized lazily on Scan; startup and offline UI smoke never connect to hardware. GigE open applies the SDK-recommended packet size. SETTING may use free-run for teaching. Entering RUN ensures the camera is connected, applies the frozen recipe and starts acquisition; leaving RUN stops production acquisition but retains the physical camera connection. Trigger-mode changes occur only while grabbing is stopped.

The production MainWindow invokes camera discovery once from its Loaded event with a five-second UI timeout. The ViewModel exposes explicit Idle/Finding/NotFound/Found/Connected/Acquiring/Error states and derived enable rules; it does not reuse the broad model-editing flag for camera controls. A timed-out native enumeration remains tracked so retry can consume its eventual result without starting concurrent SDK calls. Offline smoke disables auto-discovery explicitly.

Hardware validation on 2026-08-30 passed enumerate/open/ExposureTime/Gain/start/three-frame grab/stop/close against MV-CE120-10GM serial `00G29911748`, IP `169.254.172.4`, producing 4024×3036 BGR24 frames.

`NAcquireCamera` remains a legacy implementation of the same interface. Its inspected checkout has a synthetic OpenCV provider and a placeholder Hikrobot target, so it is not composed into the production Desktop.

## PLC connection
`IPlcLink` isolates the application from NModbus. `ModbusPlcLink` supports Ethernet IP and serial COM; COM selects Modbus ASCII or RTU plus baud/data/parity/stop/timeout. The Delta DVP default is the proven prior configuration COM11/9600/ASCII/7E1/unit 1. `DeltaDvpAddressMap` maps X as a read-only discrete input (function 02), Y/M as coils, D as holding registers, and interprets X/Y suffixes as octal.

Physical PLC transport is machine-owned in `settings.json`; logical trigger/output belongs to each recipe so future cameras can use independent recipes and addresses. Shared polling emits an unlabelled rising edge that the session assigns end 1 then end 2. PerEnd polls two addresses and labels the requested end explicitly.

RUN owns the production PLC connection. It probes/connects only when the frozen recipe uses PLC trigger or output, then disconnects on Stop/SETTING. Manual Connect remains a setup diagnostic. `PlcVerdictOutputWriter` either writes a signed D word or pulses a writable M/Y coil; a pulse reset runs in `finally` under an independent cleanup timeout. Target type/writability and conflicting OK/NG destinations are validated before RUN. Automated tests use fakes; real writes require an explicitly approved safe hardware address.
