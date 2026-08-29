# Wire Marker Inspection

Windows x64 inspection application: WPF/.NET 8 UI, reusable image controls, C++/OpenCV/ONNX Runtime core.

## Current status — 2026-08-29

Implemented: SETTING/RUN screens, independent ImageViewer and ImageEditor controls, recipe persistence, exact comparison, two-capture session, native OCR interface/pipeline, NAcquire C API adapter, tests and deployment scripts.

UI style is **RoboStation / Geo Measure HUD**: a metallic chevron SETTING/RUN taskbar, shared low-alpha floating toolbars, compact vector icons, a left ROI tool rail and bottom-right navigation. The mandatory specification is docs/design/DESIGN_SYSTEM.md. Live Camera now sits in the acquisition column to leave more room for the two editors.

**Not production accepted:** the supplied NAcquire checkout has only a synthetic backend and placeholder Hikrobot target; the expected PaddleOCR detector/recognizer/dictionary and product test images are absent. Missing dependencies are visible errors. No OCR results are fabricated. Camera hardware, accuracy, throughput and vendor deployment require separate validation.

## Build and run

Prerequisites: Windows, .NET SDK with .NET 8 Windows Desktop targeting pack, Visual Studio C++ tools, CMake, OpenCV SDK and ONNX Runtime SDK.

    powershell -ExecutionPolicy Bypass -File scripts/build.ps1
    dotnet run --project src/WireMarkerInspection.Desktop -c Release

The build script accepts -OpenCvRoot and -OrtRoot; defaults match this development PC. All native runtime DLLs are copied beside the executable, so the running app does not use those source paths.

## Operator workflow

1. SETTING → + Model. Enter unique model code and name.
2. Load Image for each end, or connect the supplied validated camera and start acquisition, then Grab Image.
3. Draw **one search ROI per end**. The OCR core detects multiple text regions inside it.
4. Enter expected text with **one detected region per line**, ordered top-to-bottom, then left-to-right in a row. This is a data format, not two manually drawn OCR ROIs.
5. Select fixed 0°, fixed 180°, or Auto reading orientation. Fixed is preferred when fixture orientation is known.
6. Apply each end; Save Recipe publishes both ends as one revision.
7. RUN uses a frozen saved recipe. Load the first offline image or capture a fresh camera frame, then the second end of the same product.
8. Both ends must exactly match. Stop cancels the current cycle. Sản phẩm tiếp begins a fresh cycle.

Comparison preserves case, punctuation, whitespace and order. No similarity threshold, cross-end repair, O/0 substitution, or target-conditioned OCR selection is used.

ImageViewer supports wheel zoom, drag pan, Fit and 1:1. ImageEditor adds rectangle/circle/polygon, handle editing, move, delete, undo/redo. Polygon: Enter/Finish, Backspace/remove point, Escape/cancel. Space+drag temporarily pans. Expand opens the same recipe editor larger.

## Runtime assets

See assets/ocr/README.md and vendor/camera/README.md.

- assets/ocr/detector.onnx
- assets/ocr/recognizer.onnx
- assets/ocr/dictionary.txt
- vendor/camera/NAcquireCAPI.dll and the validated camera dependencies

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

Smoke uses a separate data folder under artifacts, renders both views, and checks image transforms and editor undo/redo. Its images are explicitly labeled synthetic UI fixtures, not OCR validation images. It does not contact hardware.

## Deployment

    powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Publish
    powershell -ExecutionPolicy Bypass -File scripts/package.ps1 -Version 0.1.0

Production asset gate: add -RequireOcrAssets to the build command. Development packages can omit models, but RUN remains blocked. Installation is per-user and does not overwrite recipe data. Install the vendor camera drivers and the matching x64 VC++ runtime on the target PC.

See docs/design/DESIGN_SYSTEM.md, docs/design/IMAGE_EDITOR_DESIGN.md, docs/architecture/SYSTEM.md, and handoff/latest.md.
