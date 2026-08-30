# Build and deployment
scripts/build.ps1 discovers Visual Studio/CMake, configures native x64, compiles Release native DLLs, copies OpenCV/ONNX runtime, then builds .NET.
-Publish produces a self-contained Windows x64 application.
-RequireOcrAssets blocks builds missing the required OCR files.
scripts/package.ps1 compiles the Inno Setup definition into dist without installing it.

The development installer can contain the editor without OCR/camera assets; RUN will explicitly remain unavailable.
Live camera deployment requires the Hikrobot MVS driver/native runtime on the workstation and a matching `MvCameraControl.Net.dll` discovered from the installed SDK or staged in `vendor/camera`. Run `scripts/camera-probe.ps1 -Grab` on the target machine before operator acceptance.
Release readiness additionally requires model licensing/provenance, OCR dataset acceptance, Hikrobot redistribution approval, x64 VC++ redistributable, target-PC smoke and throughput testing.
Data is under per-user LocalApplicationData, separate from installed binaries. Back up recipes and results before upgrades. No automatic retention deletion is enabled.
`scripts/camera-soak.ps1 -Minutes N` measures frame rate, frame-interval spread, timeouts and reconnects against real hardware; run it on the target PC before operator acceptance. Operational events are appended as JSON Lines under the per-user data directory.
Continuous free-run acquisition has been validated on the development camera. Hardware trigger semantics, long-run disconnect/reconnect behavior, optics/exposure tuning and installer execution on a clean target PC are pending acceptance.
