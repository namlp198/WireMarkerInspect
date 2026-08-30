# Wire Marker Inspection

Windows x64 inspection application: WPF/.NET 8 UI, reusable image controls, C++/OpenCV/ONNX Runtime core.

## Current status — 2026-08-30

Implemented: SETTING/RUN screens, independent ImageViewer and ImageEditor controls, recipe persistence, exact comparison, two-capture session, native OCR interface/pipeline, offline Load Image validation, NAcquire C API adapter, tests and deployment scripts.

Each end now evaluates two independent conditions: ordinal exact text and configured text direction. Thuận requires detected 0°, Nghịch requires detected 180°; Auto explicitly accepts either direction. OCR always evaluates both directions without using expected text to choose a result.

UI style is **RoboStation / Geo Measure HUD**: a metallic chevron SETTING/RUN taskbar, shared low-alpha floating toolbars, compact vector icons, a left ROI tool rail and bottom-right navigation. The mandatory specification is docs/design/DESIGN_SYSTEM.md. Live Camera now sits in the acquisition column to leave more room for the two editors.

Model setup is locked until an existing model is selected or Add Model creates a new draft. Selecting either the ComboBox item or a Model Library row immediately loads both reference images and recipe fields. Library rows show the expected text below each end thumbnail; the Add/Edit dialog labels its model-code and model-name inputs.

Save Recipe is dimmed and disabled when the active recipe is clean. Any draft change highlights Save and shows a red “CẦN LƯU” notification. RUN renders waiting states prominently, keeps Stop outlined in red, doubles total/per-end OK/NG text to 40 DIP, and colors recognized text plus result detail green for OK or red for NG/error.

The supplied 26 BMP dataset now passes 26/26 in both the native CLI and the managed WPF Load Image path using a representative ROI and Auto 0°/180°. This is a regression baseline for the supplied images, not a production accuracy/throughput claim.

Live acquisition now uses the official Hikrobot MVS .NET SDK directly. On 2026-08-30 the adapter successfully enumerated, opened, configured and acquired three consecutive 4024×3036 frames from a real MV-CE120-10GM (serial `00G29911748`, GigE `169.254.172.4`). Production optics/exposure, long-run reconnect behavior, external trigger sequencing, throughput, target-PC deployment and vendor redistribution terms still require acceptance.

When the main window loads it automatically searches MVS for up to five seconds. During Finding all camera inputs/actions are locked and a prominent progress indicator is shown. NotFound/timeout leaves only Search enabled; Found enables device selection and Connect; Connected disables Connect and enables Disconnect, parameters and Start Acquisition. While Acquiring, the same action becomes a compact rounded-square Stop button with a red border. Live Camera can be expanded into a continuously bound large viewer.

## Build and run

Prerequisites: Windows, .NET SDK with .NET 8 Windows Desktop targeting pack, Visual Studio C++ tools, CMake, OpenCV SDK and ONNX Runtime SDK. Live camera builds also require Hikrobot MVS; the project discovers its AnyCpu `MvCameraControl.Net.dll` from the installed MVS SDK or `vendor/camera`.

    powershell -ExecutionPolicy Bypass -File scripts/build.ps1
    dotnet run --project src/WireMarkerInspection.Desktop -c Release

The build script accepts -OpenCvRoot and -OrtRoot; defaults match this development PC. All native runtime DLLs are copied beside the executable, so the running app does not use those source paths.

## Operator workflow

1. SETTING → + Model, or select an existing model from the selector/library. The Add/Edit dialog labels model code and model name, validates required/duplicate values, and Cancel leaves the current draft unchanged. Selecting an existing row loads its images and recipe automatically.
2. The app automatically searches for Hikrobot cameras after loading. When Found, select/connect the camera, apply parameters if needed and start acquisition; otherwise use Search to retry. Load Image remains available for offline setup.
3. Draw **one search ROI per end**. The OCR core detects multiple text regions inside it.
4. Enter expected text with **one detected region per line**, ordered top-to-bottom, then left-to-right in a row. This is a data format, not two manually drawn OCR ROIs.
5. Select the required direction per end: Thuận (must detect 0°), Nghịch (must detect 180°), or Auto (do not reject by direction).
6. Apply each end; the Save icon in the Model Library header publishes both ends as one revision. The saved row is selected and reloaded immediately. Edit Model updates the same identity and increments its revision.
7. RUN uses a frozen saved recipe. Load the first offline image or capture a fresh camera frame, then the second end of the same product.
8. Both ends must match exact text and their configured direction. Stop cancels the current cycle. Sản phẩm tiếp begins a fresh cycle.

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
Back up this entire directory. Inspection image retention/automatic cleanup is not yet enabled: provision and monitor disk capacity.

## Verification

    powershell -ExecutionPolicy Bypass -File scripts/test.ps1
    powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
    powershell -ExecutionPolicy Bypass -File scripts/test-real-images.ps1
    powershell -ExecutionPolicy Bypass -File scripts/camera-probe.ps1
    powershell -ExecutionPolicy Bypass -File scripts/camera-probe.ps1 -Grab

The real-image script checks the native batch and then launches the WPF executable in a hidden acceptance mode that uses the same BMP decoder, `ImageFrame`, ROI, `NativeOcrEngine`, and `ExactTextComparer` as Load Image. Its ground truth is `tests/real-images.expected.json`; the BMP files remain outside Git. UI smoke uses synthetic fixtures. Only `camera-probe.ps1` contacts hardware; `-Grab` opens the first discovered camera, applies ExposureTime 10000/Gain 0, acquires three fresh frames, saves the last PNG and then closes the device.

## Deployment

    powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Publish
    powershell -ExecutionPolicy Bypass -File scripts/package.ps1 -Version 0.1.0

Production asset gate: add -RequireOcrAssets to the build command. Development packages can omit models, but RUN remains blocked. Installation is per-user and does not overwrite recipe data. Install the vendor camera drivers and the matching x64 VC++ runtime on the target PC.

See docs/design/DESIGN_SYSTEM.md, docs/design/IMAGE_EDITOR_DESIGN.md, docs/architecture/SYSTEM.md, and handoff/latest.md.
