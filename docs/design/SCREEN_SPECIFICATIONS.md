# Screen specifications

## SETTING
Camera form: enumerate/select/connect/disconnect; exposure and gain drafts; explicit Start/Stop acquisition.
Live camera: read-only ImageViewer inside ImageHud, in the left acquisition column; wheel zoom, drag pan and bottom-right vector navigation toolbar. The final `[1]` button restores the initial Fit viewport, not 100% pixel-to-DIP.
Model selector: one horizontal row only — ComboBox, Add, Edit and Delete icon buttons. Add/Edit opens an isolated draft dialog with visible labels for model code and model name; required and duplicate values are rejected inline, and Cancel must not mutate the active recipe draft. Save Recipe is the icon in the Model Library header. It publishes only after both ends have valid images, ROI, expected text and Apply state; Edit keeps the model identity and increments the revision.
Two end editors: independent captured image, one ROI, required text direction, ordered expected lines, Test OCR, Apply. Thuận requires detected 0°, Nghịch requires detected 180°; Auto is an explicit opt-out that accepts either detected direction.
Both editors use a floating left HUD rail for geometry/history and bottom-right navigation, with Expand on the canvas and no editor caption. Business inputs stay below. No external geometry text toolbar.
Model library: virtualized rows, two reference thumbnails with each end's expected text directly underneath, code/name/revision. Selecting a ComboBox item or library row immediately loads both reference images, ROIs, orientations and expected text into SETTING.
Empty selection: both end editors, their business buttons and Save Recipe are disabled. Add Model remains available and activates a new editable draft; selecting a saved model activates its loaded setup. Edit/Delete remain disabled until a saved model row is selected.
Save state: clean recipe disables/dims Save. Any draft change enables and highlights Save and shows one red `● CẦN LƯU` notification beside it; successful Save returns to the clean state.
Color and Template: disabled extension positions only.

Expected text format: one detected text region per newline, preserving all other characters.
The original two small OCR boxes are examples of automatic detector output, not a fixed number of manually authored ROIs.

## RUN
Press RUN to validate and freeze the saved recipe, automatically prepare camera acquisition, connect PLC when that recipe uses it, and enter WaitingEnd1. SETTING stops production acquisition/PLC but retains the camera connection for teaching.
Capture may be manual, camera-line Shared, PLC Shared or PLC PerEnd. Shared assigns one signal sequentially; PerEnd uses two distinct addressed signals.
After processing end 1, wait for end 2; only then publish a product result.
Both ends must pass ordinal exact text comparison and their configured 0°/180° direction for OK. OCR evaluates both directions independently of expected text. No OCR region, text mismatch or fixed-direction mismatch → NG. Dimension mismatch/native failure/disk failure → Error.
Stop invalidates late processing results. After a completed product is persisted and its output finishes, the next cycle starts automatically. The last completed images and total verdict remain available through KẾT QUẢ TRƯỚC.
Per end: full image and overlay, detected crops, expected/actual text, reason and first mismatched character index.
The image uses read-only ImageHud with caption and verdict floating above it. There is no ROI drawing rail in RUN.
RUN state styling: waiting is a prominent warning color; Stop has an error-red outline while active. Total and per-end OK/NG are 40 DIP. Recognized text and detail are green for OK and red for NG/ERROR, including text mismatch and rotation mismatch.
