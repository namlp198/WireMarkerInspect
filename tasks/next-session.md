# Next session

1. Ask the user to review the new RoboStation HUD in artifacts/release-smoke/hud-editor.png, setting.png and run.png (synthetic UI fixtures only). Keep DESIGN_SYSTEM.md mandatory; do not restore form-style text toolbars. Validate actual mouse/polygon editing and workstation DPI with the user.
2. Resolve NAcquire checkout discrepancy before claiming Hikrobot integration. Do not silently replace it with the synthetic backend.
3. Stage validated x64 camera DLLs in vendor/camera; verify C ABI size/version against the actual binary and test acquisition separately.
4. Stage detector.onnx, recognizer.onnx and dictionary.txt in assets/ocr.
5. Build an offline labeled dataset from product image pairs. Include exact-match, wrong-model, punctuation/case/single-character errors, absent text, rotated and low-quality images.
6. Verify automatic region counts/order and return raw decoded text unchanged. Calibrate detection/recognition only against held-out images and record false accepts/rejects.
7. Confirm cycle trigger semantics with the hardware owner before adding automatic PLC/line operation.
8. Run scripts/verify-release.ps1 and inspect screenshots after changes; keep handoff/current context updated.
