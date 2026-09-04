# Wire Marker Inspection

Windows x64 inspection application: WPF/.NET 8 UI, reusable image controls, C++/OpenCV/ONNX Runtime core.

## Current status — 2026-09-04

Matching diagnostics no longer turn an early feature-gate rejection into a misleading row of zero scores. Counts survive KNN/distance/mutual/RANSAC filtering; a valid diagnostic alignment can report appearance even below a feature gate, but that gate still forces NG. Unmeasured metrics display **N/A**, and results retain their threshold snapshot. Critical teaching parameters have amber `*` labels and impact tooltips. RUN/teaching color each check independently; recognized text is 18 DIP and check details 16 DIP, with scrollable small-window results. The exact reported model4/v8 cycle was replayed read-only: SIFT end 1 had 24/38 inliers (63.16% < 65%), not zero matches; NCC 0.989 / SSIM 0.912 / edge 0.694. End 2 had 4/12 inliers and invalid geometry. No saved thresholds, OCR rules, camera or PLC behavior were changed. See [diagnostic evidence and replay](docs/architecture/TERMINAL_MATCHING.md#diagnostic-evidence--2026-09-04).

Debug/native deployment fix: the reported `wmi_matching_abi_version` entry-point failure was caused by an old Debug DLL (Release already contained matching). Desktop builds/F5 now run the matching native configuration through incremental CMake before collecting runtime DLLs; Visual Studio's C#-only fast-up-to-date shortcut is disabled. The matcher checks required exports before calling them and reports rebuild/restart guidance for an outdated DLL. Debug and Release both pass clean builds, 117/117 managed tests, native 1/1 and WPF matching smoke (`artifacts/smoke-20260904-163344` Debug; `artifacts/smoke-20260904-163449` Release).

Per-end terminal/cos template matching is implemented with **Normal, AKAZE, SIFT, ORB and ORB Max Stable**. Each end has an independent template image, learn mask, runtime search ROI, algorithm and basic/advanced thresholds. With matching enabled, OK requires **exact text AND required text direction AND terminal match**; both ends must pass. Simulator uses exactly this pipeline. Admin teaches/tests the template from the new button in each end editor; Operator remains read-only. See [terminal matching workflow and acceptance limits](docs/architecture/TERMINAL_MATCHING.md).

New models enable template inspection on both ends by default. Legacy recipes remain explicitly labeled OCR-only; they are not silently changed. Saving publishes schema v3 and immutable template PNGs alongside the reference images. RUN freezes template bytes/profiles and shows the learned/aligned images, source-space outline, score/NCC/SSIM/edge/pose/inlier evidence. Native errors cannot produce OK; Stop discards late matching results.

Add Model confirmation now visibly identifies the new unsaved draft in the center callout and selector, rather than continuing to show “no model selected”. Both editors unlock immediately; the model joins the saved library only after configuring and applying both ends and saving the Recipe. SETTING OCR now fills the expected-text draft with recognized lines in order. It preserves required direction, requires explicit Apply/Save for changed text, and keeps the existing sample if the read is empty/incomplete or fails.

Implemented: SETTING/RUN screens, independent ImageViewer and ImageEditor controls, recipe persistence, exact comparison, two-capture session, native OCR interface/pipeline, offline Load Image validation, NAcquire C API adapter, tests and deployment scripts.

The acquisition source selector is now vendor-neutral **CAMERA** and defaults to **Simulator**. Simulator runs the same frozen saved Recipe, OCR, direction check, two-end session and OK/NG evaluation as production, but accepts images from disk and deliberately bypasses physical camera acquisition, PLC trigger and PLC output. Operator may select a saved model, enter RUN and load the two offline images; all recipe and device settings remain locked. Selecting a physical camera restores the normal automatic camera/PLC RUN lifecycle.

The application starts in **Operator** mode. A separate USER ACCESS area provides Admin login (`admin/admin`). Operator can use ACQUISITION and select a saved model from the prominent selector in the center workspace; Add/Edit/Delete, both recipe editors, Save and the right Model Library remain locked. Camera parameters and PLC/trigger/output configuration in the left panel stay hidden until Admin login. Logout safely returns to Operator restrictions.

The UI can switch live between Vietnamese, English and Korean and remembers the selected language under `%LOCALAPPDATA%\WireMarkerInspection\language.txt`. The header uses a single circular vector flag aligned with the product title. The header no longer shows implementation-stack text: its red `OFFLINE` / green `ONLINE` indicator now reflects the physical camera connection. The selected model and frozen RUN model are shown in prominent bordered callouts so the operator can confirm the active recipe at a glance.

Each end retains independent ordinal exact text and configured text direction checks, plus terminal-template matching when enabled. Thuận requires detected 0°, Nghịch requires detected 180°; Auto explicitly accepts either direction. OCR always evaluates both directions without using expected text to choose a result. Terminal pose is relative to the taught template and never substitutes for text direction.

UI style is **RoboStation / Geo Measure HUD**: a metallic chevron SETTING/RUN taskbar, shared low-alpha floating toolbars, compact vector icons, a left ROI tool rail and bottom-right navigation. The mandatory specification is docs/design/DESIGN_SYSTEM.md. Live Camera now sits in the acquisition column to leave more room for the two editors.

Model setup is locked until an existing model is selected or Add Model creates a new draft. Selecting either the ComboBox item or a Model Library row immediately loads both reference images and recipe fields. Library rows show the expected text below each end thumbnail; the Add/Edit dialog labels its model-code and model-name inputs.

Save Recipe is dimmed and disabled when the active recipe is clean. Any draft change highlights Save and shows a red “CẦN LƯU” notification. RUN renders waiting states prominently, keeps Stop outlined in red, and uses 40-DIP total/per-end OK/NG. Text, direction and template metrics now use their own green/red verdicts; unavailable measurements remain neutral.

The supplied 26 BMP dataset now passes 26/26 in both the native CLI and the managed WPF Load Image path using a representative ROI and Auto 0°/180°. This is a regression baseline for the supplied images, not a production accuracy/throughput claim.

Live acquisition now uses the official Hikrobot MVS .NET SDK directly. On 2026-08-30 the adapter successfully enumerated, opened, configured and acquired three consecutive 4024×3036 frames from a real MV-CE120-10GM (serial `00G29911748`, GigE `169.254.172.4`). Production optics/exposure, long-run reconnect behavior, external trigger sequencing, throughput, target-PC deployment and vendor redistribution terms still require acceptance.

When the main window loads it automatically searches MVS for up to five seconds. During Finding all camera inputs/actions are locked and a prominent progress indicator is shown. NotFound/timeout leaves only Search enabled; Found enables device selection and Connect; Connected disables Connect and enables Disconnect, parameters and Start Acquisition. While Acquiring, the same action becomes a compact rounded-square Stop button with a red border. Live Camera can be expanded into a continuously bound large viewer.

PLC configuration is split at the correct ownership boundary. **PLC CONNECTION** contains only the machine-level physical link, with explicit **Ethernet IP** and **COM** choices. COM exposes port, baud rate, Modbus ASCII/RTU, data bits, parity and stop bits; Ethernet exposes host and port. The Delta DVP defaults remain `COM11`, 9600 baud, Modbus ASCII, 7E1, unit 1. Recipe schema v2 owns each model's trigger mapping and OK/NG outputs.

Shared trigger means one button is routed sequentially to end 1 and then end 2. PerEnd means two independent button bits; the Shared form therefore has no editable end-2 field. Each OK/NG output can pulse an M/Y bit and reset it after the configured delay, or write a configured signed value to a D register.

RUN now owns device lifecycle. Entering RUN automatically finds/connects the selected camera when needed, applies the frozen recipe settings, starts acquisition and connects PLC only when that recipe uses PLC trigger/output. Stop or returning to SETTING stops RUN acquisition and disconnects PLC while keeping the camera connection available for setup. After end 2, the verdict is persisted and sent to PLC, then a fresh cycle starts automatically at `CHỜ ĐẦU 1`; the completed images/verdict remain available through **KẾT QUẢ TRƯỚC**.

## Build and run

Prerequisites: Windows, .NET SDK with .NET 8 Windows Desktop targeting pack, Visual Studio C++ tools, CMake, OpenCV SDK and ONNX Runtime SDK. Live camera builds also require Hikrobot MVS; the project discovers its AnyCpu `MvCameraControl.Net.dll` from the installed MVS SDK or `vendor/camera`.

    scripts\build-debug.bat
    scripts\build-release.bat
    bash scripts/build-debug.sh
    bash scripts/build-release.sh
    dotnet run --project src/WireMarkerInspection.Desktop -c Release

The `.bat` and `.sh` launchers call the same `build.ps1`; additional PowerShell arguments such as `-OpenCvRoot` and `-OrtRoot` can be appended. Debug now builds and stages native Debug DLLs, while Release uses native Release DLLs. The `.sh` files are Windows Git Bash/WSL launchers and still require Windows PowerShell, Visual Studio C++ tools and the Windows SDK.
Direct Desktop `dotnet build` and Visual Studio Build/F5 also build native code for the selected configuration. `build.ps1 -NativeOnly` is the non-recursive native build entry point used by MSBuild. The full build script uses `SkipNativeBuild=true` only after completing that native build itself. Restart the app after replacing a DLL already loaded by a debugging session. Test and smoke scripts accept `-Configuration Debug` or `-Configuration Release` (default Release).

## Operator workflow

1. The app opens as Operator: select an existing saved model and RUN. Log in as Admin only when setup is required, then use SETTING → + Model or edit an existing model. The Add/Edit dialog labels model code and model name, validates required/duplicate values, and Cancel leaves the current draft unchanged. Selecting an existing row loads its images and recipe automatically.
2. CAMERA defaults to **Simulator** while the app searches for physical cameras. For offline verification, keep Simulator selected, enter RUN and load the end-1/end-2 images from disk; PLC is intentionally bypassed. For production, select a discovered physical camera; SETTING may connect/start acquisition for teaching and preview, while RUN starts its own acquisition lifecycle automatically.
3. In **PLC CONNECTION**, choose Ethernet IP or COM and save the physical machine parameters. Connect PLC remains available as a setup/diagnostic action, but RUN connects automatically when the selected recipe needs PLC.
4. Under **TRIGGER**, choose Shared (one button used in end order) or PerEnd (separate end-1/end-2 bits). Under **OUTPUT**, configure separate OK and NG rows as an M/Y pulse with automatic reset or a D-register value. These logical settings are saved with the model recipe.
5. Draw **one search ROI per end**. The OCR core detects multiple text regions inside it.
6. Enter expected text with **one detected region per line**, ordered top-to-bottom, then left-to-right in a row. This is a data format, not two manually drawn OCR ROIs.
7. Select the required direction per end: Thuận (must detect 0°), Nghịch (must detect 180°), or Auto (do not reject by direction).
8. Open **Terminal template** for each end, use/load its template image, draw the learn and search regions, choose the algorithm and thresholds, then Test on OK/NG samples. Apply the template draft, Apply each end and Save the two ends, camera setup, trigger and outputs as one revision. Only Admin may explicitly opt out of template inspection.
9. RUN freezes that saved recipe and automatically prepares its camera/PLC runtime. After both ends complete, the next cycle is armed automatically; use **KẾT QUẢ TRƯỚC** to review the last completed product without blocking production.
10. A camera-line trigger supports Shared only. PerEnd is a PLC feature because it needs two independently identified signals. Repeats inside the configured window and signals received while processing are ignored and logged. Chụp lại đầu này drops a bad first image without losing the product.

The current wire-marker OCR alphabet is alphanumeric plus `.` and `/`; layout whitespace is removed and the recognizer's `:`/`,` dot confusions are mapped to `.` before comparison. Case and field order are preserved. The domain comparison remains ordinal and exact, then independently checks detected 0°/180° against the per-end recipe. No similarity threshold, cross-end repair, O/0 substitution, or target-conditioned orientation selection is used.

ImageViewer supports wheel zoom, drag pan and a HUD reset that restores the initial aspect-preserving Fit view and centered offset. ImageEditor adds rectangle/circle/polygon, handle editing, move, delete, undo/redo. Polygon: Enter/Finish, Backspace/remove point, Escape/cancel. Space+drag temporarily pans. Expand opens the same recipe editor larger.

## Runtime assets

See assets/ocr/README.md and vendor/camera/README.md.

- assets/ocr/detector.onnx
- assets/ocr/recognizer.onnx
- assets/ocr/dictionary.txt
- Hikrobot MVS runtime/driver installed on the workstation
- vendor/camera/MvCameraControl.Net.dll (optional staging alternative to the installed SDK)
- vendor/camera/NAcquireCAPI.dll and dependencies only for the retained legacy NAcquire adapter

The currently inspected NAcquire C# example returns frame metadata only; this application's adapter also copies the owned pixel buffer before releasing the native frame. Camera acquisition is opt-in; startup never connects to hardware.

## Data and recovery

Per-user data: %LOCALAPPDATA%\WireMarkerInspection
- recipes/<model-id>/recipe.json and immutable reference PNGs
- results/<date>/<cycle-id>/result.json and end1/end2.ppm

Recipe saves publish JSON only after both images exist. Previous immutable images remain for recovery; delete removes the model from the catalog by renaming its recipe JSON, not destroying the assets.
Schema-v1/v2 recipes remain readable. Saving publishes schema v3; v1 legacy machine-level trigger/verdict bits still migrate into recipe-owned I/O settings, while physical PLC transport remains in `settings.json`. Legacy recipes stay OCR-only unless Admin explicitly teaches/enables templates. Missing required template assets reject the recipe with a visible load error.
Back up this entire directory. Inspection image retention/automatic cleanup is not yet enabled: provision and monitor disk capacity.

## Verification

Latest software-only verification (2026-09-04): Debug and Release builds 0 warnings/errors, managed **124/124** and native **1/1** in both configurations. WPF smoke PASS: Release `artifacts/smoke-20260904-174719`, Debug `artifacts/smoke-20260904-174731`. Coverage includes all five algorithms, different terminals, reflection/wrong rotation, ambiguous duplicates, masks, persistence, cancellation and stable multilingual profiles, plus retained early-gate evidence, N/A versus measured zero and independently colored text/direction/template metrics. Real teaching-window Test/Cancel and RUN evidence render at Full HD/1366×900 are covered alongside model/Simulator/role/OCR tests. Read-only replay of the supplied cycle establishes its diagnostic cause, not production accuracy; calibrate with independent labeled OK/NG images. The historical 26-image OCR baseline was not revalidated: the external dataset still lacks TYPE1-I1-00/TYPE1-I2-00. No real hardware command or PLC write was issued.

    powershell -ExecutionPolicy Bypass -File scripts/test.ps1
    powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
    powershell -ExecutionPolicy Bypass -File scripts/test-real-images.ps1
    powershell -ExecutionPolicy Bypass -File scripts/camera-probe.ps1
    powershell -ExecutionPolicy Bypass -File scripts/camera-probe.ps1 -Grab
    powershell -ExecutionPolicy Bypass -File scripts/camera-probe.ps1 -SoftwareTrigger
    powershell -ExecutionPolicy Bypass -File scripts/camera-soak.ps1 -Minutes 30
    powershell -ExecutionPolicy Bypass -File scripts/plc-probe.ps1 -ReadAddress X0

The real-image script checks the native batch and then launches the WPF executable in a hidden acceptance mode that uses the same BMP decoder, `ImageFrame`, ROI, `NativeOcrEngine`, and `ExactTextComparer` as Load Image. Its ground truth is `tests/real-images.expected.json`; the BMP files remain outside Git. UI smoke uses synthetic fixtures. `camera-probe.ps1`, `camera-soak.ps1` and `plc-probe.ps1` contact real hardware; the PLC probe reads only unless a write address is supplied explicitly. The camera soak reports frame rate, frame-interval spread, timeouts and temperature drift over the requested period. `camera-probe.ps1` `-Grab` opens the first discovered camera, applies ExposureTime 10000/Gain 0, acquires three fresh frames, saves the last PNG and then closes the device.

## Deployment

    scripts\deploy-inno.bat 0.1.0
    bash scripts/deploy-inno.sh 0.1.0

Deploy performs a Release self-contained publish with the production OCR-asset gate, runs managed/native tests and the published WPF smoke, then invokes Inno Setup 6 to create `dist\WireMarkerInspection-Setup-<version>.exe`. It compiles the installer but does not install it. Installation is per-user and does not overwrite recipe data. Install the vendor camera drivers and the matching x64 VC++ runtime on the target PC.

See docs/design/DESIGN_SYSTEM.md, docs/design/IMAGE_EDITOR_DESIGN.md, docs/architecture/SYSTEM.md, and handoff/latest.md.
