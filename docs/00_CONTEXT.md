# Current project context — 2026-08-29

Session update — 2026-08-30:
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
1. NAcquire/backends/hikrobot currently contains only .gitkeep; the CMake target is INTERFACE. The validated Hikrobot build / C# test mentioned by the user is not in this checkout.
2. Model redistribution/license approval, broader production image coverage, throughput and target-PC acceptance remain open.

Offline OCR validation now uses locally converted fixed-shape Paddle models. The native CLI and managed WPF Load Image path both pass 26/26 supplied BMPs with Auto orientation and normalized ROI `[0.08, 0.28, 0.92, 0.68]`. Checksums/provenance are in `assets/ocr/README.md`; checked ground truth is in `tests/real-images.expected.json`.

Do not extrapolate the 26/26 regression result to production accuracy. Camera connectivity, throughput, DPI/mouse acceptance and clean-PC installation are not validated.
