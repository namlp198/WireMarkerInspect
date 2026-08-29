# Handoff — 2026-08-29

## Delivered
A runnable development implementation in this repository:
WPF/.NET 8 x64 shell, mandatory RoboStation HUD design system, independent read-only ImageViewer and editable ImageEditor, SETTING/RUN workflows, two-end recipe management, exact comparison, native C++ DB/CTC pipeline, NAcquire C API adapter and release scripts.

The implementation preserves the user's corrected interpretation: each end has ONE large search ROI; OCR automatically finds its individual text regions. `/` remains part of the full decoded region text and never creates a synthetic region boundary. Text samples remain operator-confirmed and are never repaired from the other end or expected text.

## HUD correction delivered
- Shared ImageHud in Controls, using RoboStation Overlay.ToolRail, ToolRail.Button/ToggleButton, alpha brushes and stroke icons.
- Floating left ROI tools/history, contextual polygon actions and compact bottom-right Zoom out/Zoom in/100% toolbar; selection/focus/disabled states stay synchronized with the editor.
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

The prior installer remains a development artifact and has not been rebuilt after the OCR acceptance changes. The local app build contains validated development OCR assets but no validated Hikrobot package.

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
NAcquire at the supplied location is a scaffold: backends/hikrobot/.gitkeep; CMake only declares an INTERFACE Hikrobot target. Its .NET code is a wrapper skeleton, not the described tested camera app. The camera adapter has not been run against a vendor binary or camera.
Actual mouse/touch/DPI acceptance remains for the workstation review. Geometry and native masks were tested programmatically; window layouts were visually inspected.
External triggers/PLC pairing, automatic retention and Color/Template are deferred.
Do not ship without checking vendor/model redistribution terms and target-machine native runtimes.

## Repository changes
Only the WireMarkerInspection repository was changed. Source references were read-only.
The initial implementation is committed as `3b47c80`; current OCR acceptance changes are uncommitted on `develop`.
