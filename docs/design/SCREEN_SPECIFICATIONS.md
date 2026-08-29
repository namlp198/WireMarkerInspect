# Screen specifications

## SETTING
Camera form: enumerate/select/connect/disconnect; exposure and gain drafts; explicit Start/Stop acquisition.
Live camera: read-only ImageViewer inside ImageHud, in the left acquisition column; wheel zoom, drag pan and bottom-right vector navigation toolbar.
Model selector: one horizontal row only — ComboBox, Add, Edit and Delete icon buttons. Add/Edit opens an isolated code/name draft dialog; required and duplicate values are rejected inline, and Cancel must not mutate the active recipe draft. Save Recipe is the icon in the Model Library header. It publishes only after both ends have valid images, ROI, expected text and Apply state; Edit keeps the model identity and increments the revision.
Two end editors: independent captured image, one ROI, orientation, ordered expected lines, Test OCR, Apply.
Both editors use a floating left HUD rail for geometry/history and bottom-right navigation, with Expand on the canvas and no editor caption. Business inputs stay below. No external geometry text toolbar.
Model library: virtualized rows, two reference thumbnails, code/name/revision.
Color and Template: disabled extension positions only.

Expected text format: one detected text region per newline, preserving all other characters.
The original two small OCR boxes are examples of automatic detector output, not a fixed number of manually authored ROIs.

## RUN
Press RUN to validate the saved recipe and OCR files, freeze the recipe revision, and enter WaitingEnd1.
Load offline image or accept a fresh continuous camera frame manually. This first version does not wire external PLC/electrical triggers.
After processing end 1, wait for end 2; only then publish a product result.
Both ends must pass ordinal exact comparison for OK. No OCR region → NG. Dimension mismatch/native failure/disk failure → Error.
Stop invalidates late processing results. Next product clears both displays and creates a fresh cycle ID.
Per end: full image and overlay, detected crops, expected/actual text, reason and first mismatched character index.
The image uses read-only ImageHud with caption and verdict floating above it. There is no ROI drawing rail in RUN.
