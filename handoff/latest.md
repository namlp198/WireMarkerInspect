# Handoff — 2026-08-30

## Latest session
- Phase B of the approved plan is done: cycles are measured, acquisition survives a lost camera, and a soak tool produces evidence.
- Durations come from `MonotonicClock` (Stopwatch ticks). `DateTimeOffset.UtcNow` now only stamps evidence; it is never used to compute an interval, so an NTP correction cannot produce a negative or jumped measurement. A test pins that a backwards interval clamps to zero.
- `EndResult` carries frame-age, OCR, compare and end-total stages, and `ProductResult` carries the cycle total. The cycle total is computed before the file is written so `result.json` contains it; persist time is reported through `InspectionSession.LastPersistMilliseconds`.
- RUN shows the last cycle plus average, p95 and max over a rolling window of 50, beside live acquisition counters. SETTING shows the same counters.
- Acquisition reconnects with a bounded backoff (1s, 2s, 5s, 10s; four consecutive attempts before giving up) and reapplies the taught camera settings on reopen. `CameraUiState.Reconnecting` is a visible warning state.
- Safety rule: losing the camera mid-cycle faults the product via the new `InspectionSession.Fault`, clears both ends and restarts at end 1. A post-outage frame can never be filed as end 2 of the interrupted product. Covered by a test.
- `FileDiagnosticsLog` appends JSON Lines under `%LOCALAPPDATA%\WireMarkerInspection\diagnostics\`. A diagnostics write failure never interrupts a cycle.
- `scripts/camera-soak.ps1 -Minutes N` reports frame rate, frame-interval spread, timeouts and temperature drift.
- One-minute soak on MV-CE120-10GM `00G29911748`: 449 frames, 0 timeouts, 0 errors, 7.467 fps, frame interval min 36.9 / average 133.5 / p95 160.7 / max 511.2 ms. That 511 ms outlier is genuine jitter and matters when trigger latency is specified in phase C. This model exposes no temperature node. Evidence: `artifacts/camera-soak-phase-b.json`.
- Test classes driving a real acquisition loop now share one serialized xUnit collection: in parallel they starved the thread pool and two of them timed out. This was a harness problem, not a product defect.
- Verification: Release build 0 warnings/errors; managed 56/56 (repeated three times); native 1/1; WPF smoke PASS at `artifacts/smoke-20260830-172932`; one-minute hardware soak PASS.
- Still open for phase B: a full-shift soak at production optics, and splitting native detect/recognize timing if the totals show OCR is the bottleneck.
- Phase A of the approved trigger/PLC plan is done: acquisition settings are taught per model instead of being development defaults shared by every model.
- `Recipe` gained an optional `CameraSettings` (exposure, gain, gamma, black level, sensor ROI, strobe). It is an additive optional field, so recipes saved by the previous build still load and simply keep the current machine setup; a regression test pins that.
- `ICamera` now exposes `ReadInfo`, `DescribeParameters`, `ReadSettings` and `ApplySettings` in place of `SetParameter(name,value)`. `NAcquireCamera` implements the same contract and fails loudly for settings that ABI cannot apply.
- The SETTING form shows the device's real GenICam limits beside each value, groups advanced values (sensor ROI, strobe) behind a toggle, and disables any parameter the connected camera does not expose.
- Opening a model restores its taught setup and applies it to a connected camera; editing a camera value marks the recipe dirty.
- The camera selector shows name and serial only; the GigE address is no longer in the operator list.
- Hardware verified on MV-CE120-10GM `00G29911748`: identity and limits read back correctly (ExposureTime 34–1999733 us, Gain 0–19.996 dB, Gamma 0–4), full-resolution ResultingFrameRate is 7.65 fps, three frames grabbed. This camera has no BlackLevel node, so that row is disabled instead of failing on Apply. Evidence: `artifacts/camera-probe-phase-a.json`.
- Fixed a pre-existing flaky test: the discovery-timeout case raced a 120 ms sleep against a 20 ms timeout and failed once under parallel load. Discovery now waits on a gate the test releases.
- Verification: Release build 0 warnings/errors; managed 51/51 (repeated three times); native 1/1; WPF smoke PASS at `artifacts/smoke-20260830-171225`; hardware probe PASS.
- Still open for phase A: exposure/gain/gamma tuning against production optics and lighting.
- Fixed the crash reported when declining the unsaved-draft question: creating a new model, leaving it unsaved, selecting another library row and answering No threw an unhandled `System.ArgumentNullException` from `DataGridItemAutomationPeer` and killed the application.
- Cause: `OnSelectedModelChanged` restored the previous selection synchronously, so `SelectedItem` was written back while the DataGrid/ComboBox was still processing the selection change. The nested change made WPF construct an item automation peer for a null item.
- Fix: `RestoreSelection` posts the write-back to the dispatcher at Background priority, after the originating change has completed, and skips it when a newer selection has already superseded the rejected one. The accepted path still loads the recipe synchronously.
- Declining now sets `Giữ lại thay đổi chưa lưu. Save Recipe hoặc chọn lại model khác.` so the operator sees why the selection snapped back.
- Regression coverage: `DecliningToDiscardADraftKeepsItAndRestoresTheSelectionAfterTheSelectionChange` asserts no synchronous write-back and an intact draft — it fails against the old code — plus `AcceptingTheDiscardLoadsTheSelectedModelWithoutWaitingForTheDispatcher`, and a WPF smoke check that drives the real Model Library DataGrid through decline and accept.
- Test helpers were consolidated into `DispatcherTestHost` (STA thread plus dispatcher pumping) and shared by the model, camera, grab and control test files.
- Verification: Release build 0 warnings/errors; managed 46/46; native 1/1; WPF smoke PASS at `artifacts/smoke-20260830-160520`.
- Grab Image in both SETTING end editors now works as a state-driven action instead of an always-enabled button that could only report an error into the main status line.
- `GrabReferenceCommand` gained a `CanGrabReference` guard (`CanConfigureModel` + acquisition running + a live frame at most two seconds old), so the HUD button is genuinely disabled when a grab is impossible and enables as soon as frames arrive. The tooltip now says the action needs Start Acquisition.
- The acquisition loop publishes frames through a dispatcher captured when the view model is constructed rather than `Application.Current.Dispatcher`, and refreshes Grab availability when a frame lands, when acquisition stops and when the model draft changes.
- A grab stores a per-end copy of the buffer with a fresh frame id, clears that end's ROI/Applied state exactly like Load Image, and reports the source and frame size.
- New `GrabReferenceTests` drive the real acquisition loop with a fake camera on a pumped STA dispatcher: availability transitions, a blocked grab that explains itself, independent per-end buffers and captured references surviving Stop.
- The offline WPF smoke now drives a synthetic camera through connect/start/grab/stop and asserts the Grab buttons are disabled, then enabled, then disabled again, with the grabbed frame stored in end 1.
- Verification: Release build 0 warnings/errors; managed 44/44; native 1/1; WPF smoke PASS at `artifacts/smoke-20260830-154507`.
- Not yet accepted: grabbing a reference image from the real MV-CE120-10GM. Only the synthetic/fake camera paths have been exercised for this feature.
- Hardened ACQUISITION with explicit Idle/Finding/NotFound/Found/Connected/Acquiring/Error states and a five-second auto-discovery timeout launched only from production MainWindow Loaded.
- Finding has a visible warning/progress indicator. On timeout/no device only Search is enabled; Found enables selector/Connect; successful connection disables Connect and enables Disconnect, parameters and Start. During acquisition only the same red outlined rounded-square Stop action remains available among lifecycle actions.
- Live Camera now exposes shared-HUD Expand; the expanded non-modal window remains bound to live frames. Camera status text uses warning/success/error/brand colors.
- Added fake-camera tests for discovery, connection, timeout and retry plus WPF assertions/renders for every enable state. Managed suite is 42/42.
- Final verification: Release build 0 warnings/errors; managed 42/42; native 1/1; WPF smoke PASS at `artifacts/smoke-20260830-145501`; actual MV-CE120-10GM three-frame probe PASS at `artifacts/camera-probe-acquisition-state.json`.
- Added `HikrobotMvsCamera`, the active Desktop camera backend built on the official installed MVS .NET SDK. It supports GigE/USB discovery, deterministic device IDs, lifecycle, optimal GigE packet size, continuous mode, trigger-off, ExposureTime/Gain, timeout/error diagnostics and safe SDK-buffer release.
- Frames are normalized to owned packed BGR24. Mono8/RGB8/BGR8 are handled directly; other mono/Bayer/packed formats use the MVS converter. New conversion tests pass.
- Actual hardware acceptance passed on MV-CE120-10GM serial `00G29911748`, IP `169.254.172.4`: enumerate, open, ExposureTime 10000, Gain 0, start, three consecutive 4024×3036/stride-12072 frames, stop and close.
- Repeatable evidence: `scripts/camera-probe.ps1 [-Grab]`, `artifacts/camera-probe-final.json`, and its captured PNG. Managed tests are now 40/40; the camera-specific Desktop build is 0 warnings/errors.
- Final verification after all source changes: Release solution build 0 warnings/errors; managed 40/40; native 1/1; offline WPF smoke PASS at `artifacts/smoke-20260830-142014`.
- The final HUD navigation button (icon `[1]`) now resets zoom/pan to the initial centered Fit view instead of 100%/1:1. Tooltip and accessibility name were corrected.
- Managed and WPF smoke coverage explicitly distinguishes Fit zoom 3.0 from zoom 1.0.
- Save Recipe is now disabled/dim when clean and highlighted when dirty, with one red `● CẦN LƯU` notification that clears after save.
- RUN waiting status uses prominent warning color; active Stop has a red outline.
- Total and per-end OK/NG increased from 20 to 40 DIP. Actual OCR text and detail are green for OK, red for NG/error.
- WPF smoke verifies enabled/visibility/brush/font-size values and renders `setting-dirty.png`, `setting.png`, `run-waiting.png` and mixed `run.png`.
- Verification: Release build 0 warnings/errors; managed 37/37; native 1/1; WPF smoke PASS. Latest folder: `artifacts/smoke-20260830-134015`.
- Verdict now requires exact text and per-end direction. Thuận requires detected 0°, Nghịch requires detected 180°; Auto explicitly accepts either.
- Native OCR always evaluates both 0°/180° and reports observed rotation; expected text is not used to choose an OCR candidate.
- Same recognized text with the wrong direction is NG, with expected/actual rotation included in the reason shown by RUN.
- Managed tests now cover all fixed/Auto combinations, invalid rotation and a two-end product with identical text but a wrong second-end direction.
- Verification after the direction change: Release build 0 warnings/errors; managed 37/37 including HUD Fit reset; native 1/1; WPF smoke PASS; native CLI and managed Load Image real-image regressions 26/26 each.
- The Add/Edit Model dialog now has explicit `Mã model` and `Tên model` labels.
- Each Model Library row shows expected text below both reference thumbnails.
- Selecting a model from either selector automatically loads both saved images, ROIs, orientations and expected text into SETTING.
- SETTING's two end editors, their actions and Save Recipe stay disabled with no selected model/new draft. Add Model activates an editable draft; Edit/Delete require a saved selection.
- Added selection/load/clear and draft-lock managed tests plus WPF smoke assertions against actual enabled states.
- Verification: Release build 0 warnings/errors; managed 28/28; native 1/1; offline WPF smoke PASS at 1920×1080 and 1366×900. Latest render folder: `artifacts/smoke-20260830-115117`.

## Delivered
A runnable development implementation in this repository:
WPF/.NET 8 x64 shell, mandatory RoboStation HUD design system, independent read-only ImageViewer and editable ImageEditor, SETTING/RUN workflows, two-end recipe management, exact comparison, native C++ DB/CTC pipeline, NAcquire C API adapter and release scripts.

The implementation preserves the user's corrected interpretation: each end has ONE large search ROI; OCR automatically finds its individual text regions. `/` remains part of the full decoded region text and never creates a synthetic region boundary. Text samples remain operator-confirmed and are never repaired from the other end or expected text.

## HUD correction delivered
- Shared ImageHud in Controls, using RoboStation Overlay.ToolRail, ToolRail.Button/ToggleButton, alpha brushes and stroke icons.
- Floating left ROI tools/history, contextual polygon actions and compact bottom-right Zoom out/Zoom in/reset-to-Fit toolbar; selection/focus/disabled states stay synchronized with the editor.
- Read-only Live/RUN have no drawing rail. Hiding overlays preserves ROI/results.
- External geometry/zoom text toolbars removed. Recipe editor caption/helper text removed; Expand remains inside HUD. HUD buttons/icons are 32/16 DIP and overlay alpha is 53%.
- ChevronButton now owns a TailMode dependency property: Straight for the first step, Notched by default for following steps. SETTING uses Straight; RUN uses Notched. SETTING/RUN chevrons are 208x64 DIP. Identical 42x31 tip/notch vectors render 14 DIP apart, so their diagonal edges remain parallel. Camera/Model Library are 400 DIP; center is narrower.
- Model selector has exactly ComboBox + Add/Edit/Delete icon buttons. Add/Edit uses an isolated validated draft dialog, so Cancel does not mutate the active model; Save is the Model Library header icon. Save reloads the stored row, while Edit preserves identity and increments revision. Camera/runtime actions are iconized.
- DESIGN_SYSTEM.md specifies mandatory style, exact tokens/placement, forbidden alternatives and control/business boundaries. Original HUD screenshots retained in design/references.

## Artifacts
- publish/WireMarkerInspection/WireMarkerInspection.Desktop.exe (keep with its DLLs)
- dist/WireMarkerInspection-Setup-0.1.0.exe
- artifacts/release-smoke/setting.png
- artifacts/release-smoke/setting-1366.png
- artifacts/release-smoke/run.png
- artifacts/release-smoke/hud-editor.png
- artifacts/release-smoke/model-dialog.png
- artifacts/release-build.log
- artifacts/release-tests.log
- artifacts/release-package.log
- artifacts/test-results/managed.trx

The prior installer remains a development artifact and has not been rebuilt after the OCR/camera changes. The local build resolves the installed MVS SDK; a clean target still needs MVS runtime/driver validation and redistribution review.

## Verification actually performed
scripts/verify-release.ps1 completed with exit 0 after the final source changes.
Release build and self-contained publish succeeded (0 warnings, 0 errors).
27 .NET tests passed; native contract suite 1/1 passed.
Tests cover strict punctuation/case/whitespace/order comparison, missing/extra regions, mixed results, duplicate frame IDs, cancellation/late results, recipe snapshots, persistence errors, 160-model storage, complete model Add/Save v1/Reload/Edit/Save v2/Delete, identity validation, ROI validation, native mask pixels and viewport/undo behavior. Added HUD tool/history synchronization, source replacement and read-only rail checks.
WPF smoke executed from the published folder, using isolated synthetic fixtures, and rendered SETTING/RUN at Full HD plus a smaller scrolling layout. HUD bounds/non-overlap checks pass at 1920x1080 and 1366x900, with an expanded control render. Each smoke run uses a fresh data directory and verifies recipe save/reload; no previous fixture collision is tolerated.
Installer compiled successfully. It was NOT installed or exercised on a clean workstation.

Offline OCR acceptance after the prior release verification:
- converted fixed-shape Paddle detector and English PP-OCRv3 recognizer are present locally; hashes/provenance are documented;
- `scripts/test-real-images.ps1` passes 26/26 exact cases through the native CLI;
- the same script passes 26/26 through the actual WPF Load Image decode, `NativeOcrEngine`, ROI and `ExactTextComparer` path;
- Auto orientation selects the checked 0°/180° direction for all 26 images;
- report files are under `artifacts/test-results` and are ignored by Git.

## Remaining requirements
The supplied 26 BMP images are now a checked regression set, but they are too small and homogeneous to support a production accuracy or throughput claim. Raw images and ONNX assets remain outside Git.
NAcquire at the supplied location remains a scaffold and is retained only as a legacy adapter. Live Desktop acquisition now uses the official MVS SDK directly and has been run successfully against the development camera. External triggering, disconnect/reconnect soak, production optics and throughput remain unvalidated.
Actual mouse/touch/DPI acceptance remains for the workstation review. Geometry and native masks were tested programmatically; window layouts were visually inspected.
External triggers/PLC pairing, automatic retention and Color/Template are deferred.
Do not ship without checking vendor/model redistribution terms and target-machine native runtimes.

## Repository changes
Only the WireMarkerInspection repository was changed. Source references were read-only.
All work described above is committed on `develop`; the working tree is clean and `develop` is in sync with `origin/develop`.

- `3b47c80` Initial Wire Marker Inspection implementation (also the current `main`/`origin/main` tip).
- `ec06803` Validate real-image OCR and model workflow.
- `644c12e` Improve model setup and inspection feedback.
- `abc9b6b` Add Hikrobot camera acquisition workflow — current `develop`/`origin/develop` tip.

`develop` is three commits ahead of `main`; the camera, OCR-acceptance, model-setup and HUD/status changes have not been merged to `main` yet.
