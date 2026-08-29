# Component rules
Reuse WPF styles before introducing primitive wrappers.
ChevronButton owns the `TailMode` dependency property. Use Straight for the first taskbar item and the default Notched for subsequent items; both shapes share one ModeButton template and all interaction states.
ImageViewer is the reusable read-only image surface. ImageEditor extends it, not a duplicate rendering engine.
ImageHud is the shared presentation layer for both; use the same ToolRail styles and alpha HUD tokens in SETTING, RUN, Live and expanded editors. No per-view geometry/zoom toolbar duplication. Button active state must follow the actual editor, including keyboard actions.
EndEditorView is a recipe-specific composition, and EndResultView is a result-specific composition.
Controls bind immutable ROI/result values and expose visual methods; they never call acquisition/OCR/storage services.
Per-end views must not share zoom, geometry, undo history or captured frame buffers.
Expected text and camera numeric inputs remain drafts until the relevant Apply action. Never normalize text silently.
Apply is a prominent text button (`Button.Apply`) for camera parameters and each end editor; do not render it as an icon-only check mark. Action-required status lines use `StatusMessage.Attention`, while a successfully applied end uses the success color.
Resource dictionaries self-import dependencies. App smoke is mandatory after changing styles.
