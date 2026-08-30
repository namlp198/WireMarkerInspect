# Reference provenance

Read-only references:
- D:\src\ai-workspace\06_Repositories\sol-drcp\robo-station
- D:\src\learn_opencv_all\WireMarkerInspection
- D:\src\ai-workspace\06_Repositories\NVision\NAcquire
- D:\src\learn_opencv_all\Libraries
- D:\opencv
- C:\Program Files (x86)\MVS\Development\Samples and MVS SDK headers/XML

Copied and adapted: RoboStation Themes/Tokens and selected Themes/Components (Text, Buttons, Inputs, DataGrid, ScrollBars, Overlay). Pack URIs now reference this app. Inspection styles, disabled states and ComboBox selected-item template behavior are adapted locally.
Geo Measure contributed drawing/viewport interaction guidance, not geographic calculations or map dependencies.
The native OCR implementation follows the reference DB detector/CTC pipeline with new ROI-only search, per-instance sessions, exact-output preservation and C ABI. TYPE, OCV demo and paired-text repair were not imported.
The NAcquire PInvoke declarations are matched to its C header; managed pixel ownership and lifecycle handling are implemented here. The active Hikrobot adapter follows the official installed MVS C# GrabImage, ConvertPixelType and BasicDemoWPF samples; no vendor sample source was copied.
The three user-supplied UI PNGs are stored under design/references.
The user's later HUD screenshots are preserved as design/references/hud-editor.png and hud-viewer.png. ImageHud follows the actual GeoMeasure ToolRail and Overlay styles; the new Select/Pan/Redo/Expand/ActualSize/Check/Close geometries use the same stroke grid. No map or robot behavior was imported.
The latest taskbar and cleanup annotations are preserved as taskbar-chevron.png, hud-cleanup-annotation.png and setting-layout-annotation.png. They govern the metallic chevron taskbar, 400-DIP side columns, compact 32/16-DIP HUD and removed editor labels/actions.
The final parallel-edge reference is preserved as taskbar-chevron-parallel.png; its geometry is implemented with matching 42x31 edge vectors and 14-DIP separation.
OpenCV/ONNX notices are copied by the build script. Local detector/recognizer conversion sources, runtime contracts and SHA-256 values are recorded in `assets/ocr/README.md`. Model and vendor runtime redistribution terms still need confirmation before distribution.
The first-item straight-tail annotation is preserved as design/references/taskbar-first-straight.png.
The operator-status and text-Apply annotation is preserved as design/references/status-apply-annotation.png; it defines the highlighted action messages and explicit Apply buttons.
