# Handoff — 2026-08-29

## Delivered
A runnable development implementation in this repository:
WPF/.NET 8 x64 shell, mandatory RoboStation HUD design system, independent read-only ImageViewer and editable ImageEditor, SETTING/RUN workflows, two-end recipe management, exact comparison, native C++ DB/CTC pipeline, NAcquire C API adapter and release scripts.

The implementation preserves the user's corrected interpretation: each end has ONE large search ROI; OCR automatically finds its individual text regions. Text samples remain operator-confirmed and are never repaired from the other end or expected text.

## HUD correction delivered
- Shared ImageHud in Controls, using RoboStation Overlay.ToolRail, ToolRail.Button/ToggleButton, alpha brushes and stroke icons.
- Floating left ROI tools/history, contextual polygon actions and compact bottom-right Zoom out/Zoom in/100% toolbar; selection/focus/disabled states stay synchronized with the editor.
- Read-only Live/RUN have no drawing rail. Hiding overlays preserves ROI/results.
- External geometry/zoom text toolbars removed. Recipe editor caption/helper text removed; Expand remains inside HUD. HUD buttons/icons are 32/16 DIP and overlay alpha is 53%.
- ChevronButton now owns a TailMode dependency property: Straight for the first step, Notched by default for following steps. SETTING uses Straight; RUN uses Notched. SETTING/RUN chevrons are 208x64 DIP. Identical 42x31 tip/notch vectors render 14 DIP apart, so their diagonal edges remain parallel. Camera/Model Library are 400 DIP; center is narrower.
- Model selector has exactly ComboBox + Add/Edit/Delete icon buttons. Add/Edit opens the code/name dialog; Save is the Model Library header icon. Camera/runtime actions are iconized.
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

The installer is a development artifact, without OCR model files or a validated Hikrobot package. RUN is explicitly blocked when required OCR assets are missing.

## Verification actually performed
scripts/verify-release.ps1 completed with exit 0 after the final source changes.
Release build and self-contained publish succeeded (0 warnings, 0 errors).
25 .NET tests passed; native contract suite 1/1 passed.
Tests cover strict punctuation/case/whitespace/order comparison, missing/extra regions, mixed results, duplicate frame IDs, cancellation/late results, recipe snapshots, persistence errors, 160-model storage, ROI validation, native mask pixels and viewport/undo behavior. Added HUD tool/history synchronization, source replacement and read-only rail checks.
WPF smoke executed from the published folder, using isolated synthetic fixtures, and rendered SETTING/RUN at Full HD plus a smaller scrolling layout. HUD bounds/non-overlap checks pass at 1920x1080 and 1366x900, with an expanded control render. Each smoke run uses a fresh data directory and verifies recipe save/reload; no previous fixture collision is tolerated.
Installer compiled successfully. It was NOT installed or exercised on a clean workstation.

## Remaining requirements
No product images or actual OCR weights/dictionary were available. No recognition accuracy or throughput claim is supported.
NAcquire at the supplied location is a scaffold: backends/hikrobot/.gitkeep; CMake only declares an INTERFACE Hikrobot target. Its .NET code is a wrapper skeleton, not the described tested camera app. The camera adapter has not been run against a vendor binary or camera.
Actual mouse/touch/DPI acceptance remains for the workstation review. Geometry and native masks were tested programmatically; window layouts were visually inspected.
External triggers/PLC pairing, automatic retention and Color/Template are deferred.
Do not ship without checking vendor/model redistribution terms and target-machine native runtimes.

## Repository changes
Only the new WireMarkerInspection repository was changed. Source references were read-only.
No commit or remote push was made. Branch: codex/initial-implementation.
