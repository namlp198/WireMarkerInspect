# Current project context — 2026-08-29

Session update — build/deploy launchers 2026-08-31:
- Added Windows batch and Git Bash/WSL shell launchers for explicit Debug, Release and Inno Setup deployment.
- Debug and Release now build matching native configurations and stage the corresponding OpenCV runtime instead of always consuming native Release DLLs.
- Deploy is a guarded pipeline: required OCR assets, self-contained win-x64 Release publish, managed/native tests, published WPF smoke and Inno Setup 6 compilation. It creates an installer but never installs it.
- Acceptance: `build-debug.sh` PASS with the Debug OpenCV runtime, `build-release.bat` PASS with 0 warnings/errors, and `deploy-inno.bat 0.1.0` PASS (managed 83/83, native 1/1, published WPF smoke, Inno compile). The resulting installer is `dist/WireMarkerInspection-Setup-0.1.0.exe` (83,992,815 bytes); it was not installed.

Session update — 2026-08-31:
- Recipe schema v2 owns camera-local inspection I/O: trigger kind/mapping/addresses/polling and separate OK/NG output actions. Schema v1 remains readable and is migrated from legacy machine settings when next saved.
- Shared means one signal is assigned sequentially to end 1 then end 2. PerEnd reads distinct end-1/end-2 bits. The UI uses operator-facing labels and hides the end-2 input for Shared.
- Physical PLC transport remains machine-level in `settings.json`; Ethernet IP and COM are unchanged. Logical trigger/output no longer publishes into machine settings.
- OUTPUT supports M/Y bit pulses with a 10–10000 ms configured hold and unconditional reset in a cleanup timeout, or signed word writes to D registers. Invalid/non-writable areas and ambiguous duplicate actions are rejected before RUN.
- RUN owns production resources: it auto-connects/applies/starts the camera, auto-connects PLC only if the frozen recipe uses it, and rolls back partial startup. Stop or SETTING stops acquisition and disconnects PLC; the physical camera connection remains open for teaching.
- Completing end 2 persists the product and sends its verdict before automatically opening a new cycle at end 1. The just-completed images and 40-DIP total verdict remain in a previous-result snapshot the operator can toggle without blocking the new cycle.
- Verification: Release build PASS with 0 warnings/errors; managed 83/83; native 1/1; WPF smoke PASS at `artifacts/smoke-20260831-112136`. No real PLC write was part of automated verification.

Session update — 2026-08-30:
- PLC connection is now split into two operator-facing physical types: Ethernet IP and COM. The independent PLC CONNECTION panel owns configuration, COM scan, Connect/Disconnect and colored connection state; settings lock while connected.
- The prior Delta DVP code used COM11, 9600 baud, Modbus ASCII, 7E1, unit 1. These are the defaults and legacy-settings upgrade path; COM also permits RTU, while Ethernet uses host/port.
- Connect must receive a PLC response from the configured read bit before it reports success. PLC RUN requires that connection and leaves it open when RUN stops; explicit Disconnect/app shutdown owns teardown.
- Delta X inputs use Modbus function 02 (`DiscreteInput`) at the existing octal 0x0400 base, matching the prior working implementation.
- Verification: Release build PASS; managed 77/77; native contract 1/1; WPF smoke PASS at `artifacts/smoke-20260830-224311`. Real PLC was not contacted. The OS listed COM3–COM6, not the old COM11, so actual-port selection and real link/trigger/write acceptance remain open.
- Phase D of the approved plan: RUN can be triggered from a PLC bit, and the verdict can be written back.
- `IPlcLink` and `IPlcAddressMap` keep every library type out of the application. `ModbusPlcLink` (NModbus, MIT) speaks Modbus over Ethernet IP or COM (ASCII/RTU); supporting another brand means adding an address map, not changing the app.
- `DeltaDvpAddressMap` covers S, X, Y, T, M, C and D. X and Y are numbered in OCTAL on a DVP, so X10 is the ninth input: a decimal reading would address the wrong contact and still appear to work. X is a physical input and is refused for writes.
- `PlcTriggerSource` polls the configured bits and fires only on a rising edge, so a held button or latched bit cannot capture repeatedly. A read failure is surfaced in the status line, not swallowed into a dead trigger.
- A PLC trigger drives the camera through its software trigger and waits for a genuinely new frame, so the image stays tied to the signal that requested it.
- `PlcReporter` writes stage (waiting end 1/2, busy), verdict (OK/NG/ERROR) and a heartbeat. Writing is opt-in and off by default, every address is declared explicitly in `settings.json`, and a failed write is reported without aborting an inspection that already produced a verdict.
- Machine-level configuration moved to `settings.json` via `FileSettingsStore`. A corrupt file falls back to defaults and says so instead of stopping the station.
- `scripts/plc-probe.ps1 -ReadAddress X0 [-WriteAddress Y0]` is the hardware acceptance gate. Reading is safe; the write pulse is opt-in and warns, because a PLC write can move machinery.
- Not verified: any real PLC. The Delta address table and the whole link are covered only by fakes; the mapping must be checked against the connected PLC before acceptance.
- Managed suite is 74/74.
- Phase C of the approved plan: RUN can be driven by a hardware trigger on the camera's I/O line instead of only a button.
- `ITriggerSource` covers manual and camera-line sources today and leaves the PLC source for phase D. In triggered acquisition the arriving frame is the trigger event, so no second grab path exists.
- `TriggerRouter` decides which end a signal belongs to: `Shared` follows the session state, `PerEnd` takes the end from the signal and refuses one that does not match what is expected. It blocks repeats inside a configurable window and ignores signals while an image is being processed. Every ignored signal is logged with its reason, never dropped silently.
- One camera exposes a single TriggerSource node, so `CameraLine` + `PerEnd` is rejected in validation rather than pretending two lines can drive the two ends. Per-end signals are a PLC feature.
- Free-run and triggered acquisition are treated as different lifecycles: acquisition stops and restarts around a trigger-mode change, and a quiet triggered camera is no longer mistaken for a lost link.
- `RetakeLastEnd` lets the operator drop a bad first image and shoot it again without losing the product.
- Hardware verified on MV-CE120-10GM `00G29911748` with `scripts/camera-probe.ps1 -SoftwareTrigger`: the camera stayed silent for 700 ms with the trigger armed, delivered a full 4024x3036 frame exactly on the software trigger, and returned to free-run. Evidence: `artifacts/camera-probe-phase-c.json`.
- Not verified: a physical pulse on the 6-pin I/O cable. TriggerSource=Line, TriggerActivation and LineDebouncerTime still need real wiring.
- Managed suite is 65/65, repeated three times.
- Phase B of the approved plan: every cycle is now measured, acquisition survives a lost camera, and a soak tool produces evidence.
- All durations come from `MonotonicClock` (Stopwatch ticks). `DateTimeOffset.UtcNow` is used only to stamp evidence, never to compute an interval, so a time-service correction cannot produce a negative or jumped measurement.
- `EndResult` carries frame-age, OCR, compare and end-total stages; `ProductResult` carries the cycle total. The cycle total is computed before the result is written, so `result.json` actually contains it. Persist time is reported separately through `InspectionSession.LastPersistMilliseconds`.
- RUN shows the last cycle with average, p95 and max over a rolling window of 50, next to acquisition counters.
- Acquisition reconnects with a bounded backoff (1s, 2s, 5s, 10s, four consecutive attempts) and reapplies the taught camera settings. `CameraUiState.Reconnecting` is a visible warning state.
- Safety rule enforced: losing the camera mid-cycle faults the product through `InspectionSession.Fault`, clears both ends and restarts at end 1. A frame from after the outage can never be filed as end 2 of the interrupted product.
- `FileDiagnosticsLog` appends JSON Lines to `%LOCALAPPDATA%\WireMarkerInspection\diagnostics\`; diagnostics failures never interrupt a cycle.
- `scripts/camera-soak.ps1 -Minutes N` measures frame rate, frame-interval spread, timeouts and temperature drift against real hardware.
- One-minute soak on MV-CE120-10GM `00G29911748`: 449 frames, 0 timeouts, 0 errors, 7.467 fps, frame interval min 36.9 / average 133.5 / p95 160.7 / max 511.2 ms. The 511 ms outlier is real jitter worth watching; this model reports no temperature node. Evidence: `artifacts/camera-soak-phase-b.json`.
- Test classes that drive a real acquisition loop now share one serialized xUnit collection. Running them in parallel starved the thread pool and made pumped waits time out.
- Managed suite is 56/56, repeated three times.
- Phase A of the approved trigger/PLC plan: acquisition settings are now taught per model. `Recipe` carries an optional `CameraSettings` (exposure, gain, gamma, black level, sensor ROI, strobe); recipes written before this field still load and simply keep the current machine setup.
- `ICamera` replaced the untyped `SetParameter(name,value)` with `ReadInfo`, `DescribeParameters`, `ReadSettings` and `ApplySettings`. The UI shows the device's real GenICam limits instead of hard-coded numbers, and only offers parameters the connected camera actually exposes.
- Selecting a model restores its taught setup and pushes it to a connected camera; editing any camera value marks the recipe dirty because it is part of the recipe.
- The camera selector shows name and serial only. The GigE address is no longer in the operator list.
- Hardware check on MV-CE120-10GM `00G29911748`: name/serial/pixel format/sensor size read back correctly, real limits are ExposureTime 34–1999733 us, Gain 0–19.996 dB, Gamma 0–4, and full-resolution ResultingFrameRate is 7.65 fps. The camera exposes no BlackLevel node, so that row is disabled rather than failing on Apply. Evidence: `artifacts/camera-probe-phase-a.json`.
- Fixed a pre-existing flaky test: camera discovery timeout raced a 120 ms sleep against a 20 ms timeout. Discovery now blocks on a gate the test releases, so the timeout path is deterministic.
- Managed suite is 51/51.
- Fixed a crash: answering No to `Bỏ thay đổi chưa lưu và mở model đã chọn?` threw an unhandled `ArgumentNullException` from `DataGridItemAutomationPeer`. `OnSelectedModelChanged` wrote the previous selection back while the DataGrid/ComboBox was still inside its own selection change, and that re-entrant write made WPF build an item automation peer for a null item.
- `RestoreSelection` now defers the write-back to the dispatcher at Background priority and skips it when a newer selection has already superseded the rejected one. The accepted path still loads synchronously.
- Declining now also reports `Giữ lại thay đổi chưa lưu...` instead of leaving the previous status text.
- Regression coverage: a managed test asserts the selection is NOT restored synchronously and the draft survives the deferred restore, and the WPF smoke drives the real Model Library DataGrid through decline and accept. Managed suite is 46/46.
- Grab Image in both SETTING end editors is now a real, state-driven action. `GrabReferenceCommand` has a `CanGrabReference` guard, so the HUD button is disabled unless a model draft/selection is active and acquisition is delivering frames no older than two seconds.
- The acquisition loop marshals frames through a dispatcher captured when the view model is constructed instead of `Application.Current`, and it refreshes Grab availability whenever a frame arrives or acquisition stops.
- A grabbed frame is copied per end with a fresh frame id, so the two ends never share a captured buffer, and the grab clears that end's ROI/Applied state like Load Image does.
- Blocked grabs report why (`Chưa có ảnh live...` / `Frame live đã quá cũ...`) instead of failing silently.
- New managed tests drive the real acquisition loop with a fake camera on a pumped STA dispatcher; the WPF smoke drives it with a synthetic camera and asserts the button disabled/enabled/disabled around an actual grab. Managed suite is 44/44.
- Grab Image has not been exercised against the real MV-CE120-10GM yet; that remains a hardware acceptance step.
- ACQUISITION now has an explicit Idle/Finding/NotFound/Found/Connected/Acquiring/Error state machine and auto-searches on production MainWindow Loaded with a five-second timeout.
- Finding shows a warning progress indicator; timeout/no device disables all acquisition inputs/actions except Search. Found enables selector/Connect; Connected enables Disconnect/parameters/Start and disables Connect; Acquiring changes the same action to a red outlined rounded-square Stop.
- Live Camera now uses ImageHud Expand and stays bound to current live frames in the large viewer. Important camera states use warning/success/error/brand colors.
- Managed discovery/timeout/retry and WPF control-state coverage bring the suite to 42/42. Latest camera-state smoke renders `camera-finding.png` and `camera-acquiring.png`.
- Final verification: Release build 0 warnings/errors; managed 42/42; native 1/1; WPF smoke PASS at `artifacts/smoke-20260830-145501`; post-change hardware probe PASS at `artifacts/camera-probe-acquisition-state.json`.
- Added a direct official Hikrobot MVS `ICamera` backend; Desktop no longer depends on the placeholder NAcquire Hikrobot target for live acquisition.
- Real hardware passed enumerate/open/ExposureTime=10000/Gain=0/start/three fresh grabs/stop/close on MV-CE120-10GM serial `00G29911748`, GigE `169.254.172.4`; captured BGR24 frames are 4024×3036 with stride 12072.
- `scripts/camera-probe.ps1` provides repeatable enumerate-only and `-Grab` diagnostics with JSON/PNG evidence.
- MVS Mono8/RGB8 conversion regression tests are retained in the managed suite.
- The final HUD navigation button (icon `[1]`) now restores the initial centered Fit view after zoom/pan; it no longer switches to 100%/1:1.
- Managed and WPF smoke tests verify a 100×50 image in a 300×200 viewport returns to Fit zoom 3.0 rather than zoom 1.0.
- Latest verification: Release build zero warnings/errors; managed 37/37; native 1/1; WPF smoke PASS at `artifacts/smoke-20260830-134015`.
- Save is dim/disabled when clean; dirty state highlights the icon and shows one red `● CẦN LƯU` notification beside it.
- RUN waiting state is prominent warning yellow and Stop has a red outline.
- Total/per-end OK/NG are 40 DIP; actual text and result detail are green on OK, red on NG/error.
- WPF smoke asserts the exact enabled/visibility/brush/font-size states and renders dirty, clean, waiting and mixed OK/NG screenshots.
- Verdict now requires both ordinal exact text and the configured per-end direction: Thuận=0°, Nghịch=180°; Auto explicitly accepts either.
- Native OCR is always invoked in 0°/180° evaluation mode so reported Rotation is observed rather than forced by the recipe. Expected text is never used to choose orientation.
- Wrong direction produces NG even when every recognized character matches; the RUN detail reports expected versus actual rotation.
- Orientation coverage includes the same-text/different-direction product scenario and the 26-image native + managed Load Image regression.
- Add/Edit Model now shows visible labels for the model-code and model-name inputs.
- Model Library rows render expected text below each end thumbnail.
- Selecting a model from the ComboBox or library row immediately loads both saved references and recipe fields into SETTING.
- With no selection/draft, both end editors and their actions plus Save Recipe are disabled. Add Model opens a new active draft; Edit/Delete require a saved selection.
- Release build completed with zero warnings/errors; 28 managed tests and native 1/1 passed; offline WPF smoke passed at 1920×1080 and 1366×900.

The user approved implementation after confirming:
- one camera captures two ends sequentially;
- one search ROI per end, with automatic OCR text-region detection inside;
- exact text including punctuation/case/order plus required 0°/180° direction, independent per end;
- C++ core called natively by .NET/C#;
- separate read-only zoom/pan ImageViewer and editable ImageEditor.

Repository initialized with `main` and `develop`; current work is on `develop`. Reference repositories were not modified.

Implemented software:
- 6 production .NET projects, 1 C++ native project and 1 managed test project;
- mandatory RoboStation Geo Measure HUD: shared ImageHud, floating ROI rail, navigation HUD, vector icons and alpha tokens; SETTING/RUN and per-end editors/viewers;
- model catalog, immutable recipe image generations, atomic recipe publication;
- exact comparer and generation-protected two-capture session;
- DB/CTC OCR implementation with ROI masks, source-space boxes and C ABI;
- NAcquire ABI adapter (not hardware-validated);
- self-contained publish, Inno Setup installer and repeatable release verification scripts.

Latest verification: scripts/verify-release.ps1 completed successfully.
- Release build: zero warnings/errors.
- Managed tests: 27 passed, zero failed/skipped, including complete model Add/Save/Reload/Edit/Delete coverage.
- Native CTest suite: 1/1 passed.
- Published WPF smoke passed; screenshots at artifacts/release-smoke.
- Installer compiled: dist/WireMarkerInspection-Setup-0.1.0.exe. Not installed.

Latest compact UI revision requested by the user:
- ChevronButton exposes TailMode=Straight|Notched (default). SETTING explicitly uses Straight; RUN uses Notched. SETTING/RUN taskbar uses 208x64 metallic chevrons; identical tip/notch vectors are separated by 14 DIP for parallel edges.
- Camera and Model Library columns are 400 DIP; center editors are intentionally narrower.
- Model selection is one row: ComboBox + icon-only Add/Edit/Delete. Add/Edit uses a model details dialog; Save Recipe is the Model Library header icon.
- Recipe ImageEditor hides caption and removed helper labels. Expand remains inside the image HUD.
- HUD button/icon sizes are 32/16 DIP, alpha reduced to 53%; navigation contains Zoom out, Zoom in and reset-to-initial-Fit only.
- Camera, OCR asset, recipe and RUN actions use icons with tooltips/accessibility names where practical.
HUD revision requested by the user:
- DESIGN_SYSTEM.md explicitly mandates image-centric HUD and forbids external text-button geometry/zoom toolbars.
- Live Camera moved into the acquisition column to give the two editor canvases more height.
- ROI rail, polygon strip and keyboard/history state are shared in Controls/ImageHud; business actions stay in EndEditorView.
- HUD render bounds/non-overlap checked at 1920x1080 and 1366x900; dedicated expanded control image at artifacts/release-smoke/hud-editor.png.
- Repeated smoke runs use unique isolated fixture data and assert successful recipe save/reload.

External blockers:
1. External trigger/PLC semantics, long-run reconnect, production optics/exposure, throughput and clean target-PC acceptance remain open.
2. Model and Hikrobot runtime redistribution/license approval plus broader production image coverage remain open.

Offline OCR validation now uses locally converted fixed-shape Paddle models. The native CLI and managed WPF Load Image path both pass 26/26 supplied BMPs with Auto orientation and normalized ROI `[0.08, 0.28, 0.92, 0.68]`. Checksums/provenance are in `assets/ocr/README.md`; checked ground truth is in `tests/real-images.expected.json`.

Do not extrapolate the 26/26 regression result to production accuracy. Basic continuous camera connectivity is now validated on one development workstation; throughput, long-run resilience, DPI/mouse acceptance and clean-PC installation are not.
