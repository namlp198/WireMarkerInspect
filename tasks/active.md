# Active task — Wire Marker Inspection foundation

## Completed implementation
- [x] Inspect supplied UI sketches and reference repositories.
- [x] Create independent solution/repository and design documentation.
- [x] Adapt theme tokens/shared styles.
- [x] ImageViewer, ImageEditor, SETTING and RUN views.
- [x] Model/recipe store with two-end snapshot and exact text rules.
- [x] C++ OCR core and managed C ABI adapter.
- [x] NAcquire adapter matching the supplied C header, with simulation labeling.
- [x] Direct Hikrobot MVS backend for GigE/USB enumeration, open/close, parameters, continuous acquisition and BGR24 frame ownership.
- [x] Real MV-CE120-10GM hardware probe: enumerate, open, ExposureTime/Gain, three consecutive 4024×3036 frames, stop/close.
- [x] Software tests and published WPF render verification.
- [x] Build/publish/installer scripts and first development installer.

## HUD redesign requested by user
- [x] Compare actual RoboStation Geo Measure toolbars and supplied HUD screenshots.
- [x] Mandate HUD style in DESIGN_SYSTEM and keep reference PNGs.
- [x] Shared ImageHud, left ROI rail, contextual polygon strip and bottom-right navigation.
- [x] Replace external geometry/zoom text toolbars in SETTING, Live and RUN.
- [x] Synchronize active/disabled states and history.
- [x] Build and 25 managed tests, native suite, published WPF HUD bounds/render checks.
- [x] Refresh development publish and installer; no installation performed.
- [x] Add ChevronButton TailMode dependency property; Straight for first item, Notched default for subsequent items.
- [x] Redesign SETTING/RUN as 208x64 metallic chevrons with parallel tip/notch edges and 14-DIP separation.
- [x] Set both side columns to 400 DIP and simplify selector to ComboBox + Add/Edit/Delete.
- [x] Add model details dialog and move Save Recipe to icon-only Model Library header.
- [x] Remove editor caption/helper text, reduce HUD to 32/16 DIP and 53% alpha, remove crosshair/visibility actions.
- [x] Replace practical camera/OCR/recipe/runtime text actions with icons and tooltips.

## Grab Image from live camera — completed 2026-08-30
- [x] Give GrabReference a real CanExecute so the HUD button is disabled when no fresh live frame exists.
- [x] Keep the two-second freshness rule but report the blocking reason instead of failing silently.
- [x] Copy the live buffer per end with a fresh frame id so ends never share a captured buffer.
- [x] Marshal live frames through a dispatcher captured at construction and refresh Grab availability on each frame and on stop.
- [x] Cover the whole start-acquisition → live frame → grab path with managed tests on a pumped dispatcher.
- [x] Assert the disabled/enabled/disabled button states and a real grab in the WPF smoke.
- [ ] Acceptance on the real MV-CE120-10GM: grab a reference image from the actual camera at production optics/exposure.

## Unsaved-draft selection crash — fixed 2026-08-30
- [x] Reproduce: new unsaved model draft, click another library row, answer No to the discard question.
- [x] Stop writing the previous selection back inside the originating selection change.
- [x] Defer the restore to the dispatcher and ignore it when a newer selection already superseded the rejected one.
- [x] Report the declined change to the operator instead of silently keeping the old status text.
- [x] Managed regression test that fails against the synchronous restore, plus a real-DataGrid decline/accept check in the WPF smoke.

## Phase A — per-model camera parameters — completed 2026-08-30
- [x] Store exposure, gain, gamma, black level, sensor ROI and strobe with the recipe; keep older recipes loadable.
- [x] Replace the untyped camera parameter API with typed read/describe/apply.
- [x] Show the device's real limits and disable parameters the camera does not expose.
- [x] Restore and apply a model's taught setup when it is opened.
- [x] Show camera name and serial only, without the GigE address.
- [x] Managed tests, WPF smoke assertions and a hardware probe on the real camera.
- [ ] Tune exposure/gain/gamma against production optics and lighting (needs the real fixture).

## Phase B — timing, soak and reconnect — not started
## Phase C — hardware trigger over the camera I/O line — not started
## Phase D — PLC trigger and result write-back — not started

## Awaiting external inputs / acceptance
- [x] Use the installed official Hikrobot MVS SDK and its C# samples/runtime.
- [ ] Obtain PaddleOCR detector, recognizer and exact dictionary.
- [ ] Obtain paired product images, model IDs and exact expected text.
- [ ] Evaluate and tune actual text detection/recognition and ordering.
- [x] Validate live free-run camera, ExposureTime/Gain and frame format on the development workstation.
- [ ] Validate external trigger sequencing, long-run disconnect/reconnect and production throughput.
- [ ] Human review of ImageEditor mouse interactions and target DPI/resolution.
- [ ] Clean target-PC install and production acceptance.

Color/template/print-quality and external PLC trigger integrations are outside the current implemented slice.

## Model setup usability — completed 2026-08-30
- [x] Add visible model-code and model-name labels to the Add/Edit dialog.
- [x] Show each end's expected text directly below its Model Library thumbnail.
- [x] Auto-load saved images and recipe fields when the ComboBox/library selection changes.
- [x] Lock both end editors, their actions and Save Recipe until a model is selected or a new draft is created.
- [x] Disable Edit/Delete without a saved selection and preserve Add Model as the entry point.
- [x] Add managed lifecycle coverage and WPF smoke assertions for enabled/disabled states.

## Text-direction verdict — completed 2026-08-30
- [x] Treat each fixed per-end orientation as an acceptance requirement, not a forced OCR assumption.
- [x] Evaluate OCR at both 0° and 180° without expected-text-conditioned selection.
- [x] Require exact text plus matching configured direction for OK; wrong direction is NG.
- [x] Show expected/actual direction mismatch in RUN details.
- [x] Preserve Auto as an explicit “do not reject by direction” option.
- [x] Cover 0°/180°/Auto, invalid rotation and same-text/different-direction product cases.
- [x] Pass the 26-image native and managed Load Image orientation regression.

## Operator status emphasis — completed 2026-08-30
- [x] Dim/disable Save when clean; highlight it only while dirty.
- [x] Show one red Save-required notification and clear it after successful save.
- [x] Render RUN waiting status prominently and outline Stop in red.
- [x] Double total/per-end OK/NG from 20 to 40 DIP.
- [x] Color recognized text and result detail green for OK, red for NG/error.
- [x] Add WPF smoke assertions and screenshots for dirty, clean, waiting and mixed OK/NG states.

## HUD viewport reset — completed 2026-08-30
- [x] Change the final `[1]` navigation button from 100%/1:1 to initial Fit.
- [x] Restore both aspect-fit zoom and centered offset after operator zoom/pan.
- [x] Update tooltip/accessibility text to describe the Fit reset.
- [x] Add managed and WPF smoke coverage that distinguishes Fit zoom from 1.0.

## Acquisition state hardening — completed 2026-08-30
- [x] Auto-search Hikrobot MVS once after the production window loads, with a five-second timeout and visible Finding indicator.
- [x] Lock camera controls by explicit NotFound, Found, Connected and Acquiring states; after timeout only Search remains enabled.
- [x] Disable Connect after open; enable Disconnect, parameters and Start Acquisition only after a successful connection.
- [x] Make Acquisition a two-state action; the Stop state is a rounded square with a red border.
- [x] Add continuously bound Live Camera Expand using the shared ImageHud.
- [x] Highlight Finding, Found/Connected, Acquiring and error/timeout messages.
- [x] Add discovery timeout/retry managed tests and WPF control-state/render assertions.
