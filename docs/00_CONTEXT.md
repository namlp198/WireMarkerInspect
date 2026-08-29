# Current project context — 2026-08-29

The user approved implementation after confirming:
- one camera captures two ends sequentially;
- one search ROI per end, with automatic OCR text-region detection inside;
- exact text including punctuation/case/order, independent expected values per end;
- C++ core called natively by .NET/C#;
- separate read-only zoom/pan ImageViewer and editable ImageEditor.

Repository initialized on codex/initial-implementation. Reference repositories were not modified.

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
- Managed tests: 25 passed, zero failed/skipped.
- Native CTest suite: 1/1 passed.
- Published WPF smoke passed; screenshots at artifacts/release-smoke.
- Installer compiled: dist/WireMarkerInspection-Setup-0.1.0.exe. Not installed.

Latest compact UI revision requested by the user:
- ChevronButton exposes TailMode=Straight|Notched (default). SETTING explicitly uses Straight; RUN uses Notched. SETTING/RUN taskbar uses 208x64 metallic chevrons; identical tip/notch vectors are separated by 14 DIP for parallel edges.
- Camera and Model Library columns are 400 DIP; center editors are intentionally narrower.
- Model selection is one row: ComboBox + icon-only Add/Edit/Delete. Add/Edit uses a model details dialog; Save Recipe is the Model Library header icon.
- Recipe ImageEditor hides caption and removed helper labels. Expand remains inside the image HUD.
- HUD button/icon sizes are 32/16 DIP, alpha reduced to 53%; navigation contains Zoom out, Zoom in and 100% only.
- Camera, OCR asset, recipe and RUN actions use icons with tooltips/accessibility names where practical.
HUD revision requested by the user:
- DESIGN_SYSTEM.md explicitly mandates image-centric HUD and forbids external text-button geometry/zoom toolbars.
- Live Camera moved into the acquisition column to give the two editor canvases more height.
- ROI rail, polygon strip and keyboard/history state are shared in Controls/ImageHud; business actions stay in EndEditorView.
- HUD render bounds/non-overlap checked at 1920x1080 and 1366x900; dedicated expanded control image at artifacts/release-smoke/hud-editor.png.
- Repeated smoke runs use unique isolated fixture data and assert successful recipe save/reload.

External blockers:
1. Actual detector.onnx, recognizer.onnx and dictionary.txt are absent.
2. No product image pairs / expected text dataset supplied yet.
3. NAcquire/backends/hikrobot currently contains only .gitkeep; the CMake target is INTERFACE. The validated Hikrobot build / C# test mentioned by the user is not in this checkout.

Do not report OCR accuracy, camera connectivity, throughput, DPI/mouse acceptance or clean-PC installation as validated. The app can be reviewed and recipes edited offline; production RUN requires the missing assets.
