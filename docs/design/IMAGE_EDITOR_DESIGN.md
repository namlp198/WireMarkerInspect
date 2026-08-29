# ImageViewer / ImageEditor contract

## Ownership

Both controls live in WireMarkerInspection.Controls and depend only on pure geometry data from Domain. They do not know camera APIs, model IDs, recipe stores or OCR services.
The application-level EndEditorView adds module selection, expected text, source selection, Test OCR and Apply.

ImageViewer never mutates Roi. Runtime uses ImageViewer exclusively.
ImageEditor inherits the viewport and adds editing.

## Required RoboStation HUD composition

Every application viewport is wrapped in `ImageHud`, in the Controls library. The image remains a full-area sibling of the floating HUD; toolbar layout never changes the image transform. No external text toolbar is permitted for geometry or zoom controls.

ImageEditor receives the vertical `Overlay.ToolRail` with vector `ToolRail.ToggleButton` tools on the left, a contextual polygon strip and a compact bottom-right navigation HUD. Live/Runtime ImageViewer receives only the navigation HUD. Expand is a floating overlay inside the control; recipe ImageEditor hides its caption. Text samples and icon-only Load/Grab/Test/Apply remain outside this generic control.

The HUD observes viewport and edit-state events, including keyboard/history changes. Undo/Redo/Delete and polygon Finish are enabled from actual editor state. HUD uses shared RoboStation alpha brushes, icon geometries and interaction styles; see DESIGN_SYSTEM.md for mandatory dimensions and colors.

## Data model

Each end owns exactly one SearchRoi: Rectangle (two opposite corners), Circle (center and radius point), or Polygon (ordered vertices).
Coordinates are original image pixels. The ROI is not an OCR result. OCR can return zero, one, two or more text regions inside it.
ExpectedLines is ordered to match those automatic regions, not manually assigned to sub-ROIs.

## Transform

ViewPoint = ImagePoint * Zoom + Offset
ImagePoint = (ViewPoint - Offset) / Zoom

Fit retains aspect ratio with letterboxing. Wheel zoom preserves the pixel under the cursor. Pan is viewport-local. Source dimensions changing force a new fit. Live frames of the same size preserve the viewport.
Stroke/handle dimensions remain screen-space so zooming does not enlarge controls.

## Interaction

- Rectangle/Circle: press, drag, release; validate before commit.
- Polygon: click vertices, preview next segment; Enter/Finish commits, Backspace removes the last point, Escape cancels.
- Select: drag handles to resize/edit vertices; drag within ROI bounds to move.
- Space+drag or middle drag: temporary pan.
- Delete removes ROI; Ctrl+Z/Ctrl+Y undo/redo.
- Invalid geometry is reported and does not replace the prior committed ROI.
- Loading a new reference image clears its ROI and editor history; dimensions are never silently rescaled.
- Navigation does not alter saved recipe data. Apply marks a validated draft; Save persists both ends.

## Native geometry

Crop uses the ROI's bounding rectangle and a white exclusion mask for circles/polygons. Automatic text boxes are rectified in C++. Their coordinates are transformed back to original source pixels, including the selected 180-degree orientation.
There is no fallback search outside the ROI.

## Acceptance

Automated: pixel transform roundtrip, viewer/editor independence, delete/undo/redo, malformed/out-of-bounds geometry, native circle/polygon masks; HUD tool/history synchronization, source replacement and read-only rail absence.
UI smoke: both editors and read-only RUN render in an actual WPF window, Full HD and smaller layout, plus a dedicated expanded HUD screenshot. Bounds checks reject clipped navigation and rail/caption/navigation overlap.
Still needed: mouse/touch interaction on target workstation at 100/150/200% scaling and real high-resolution camera images.
