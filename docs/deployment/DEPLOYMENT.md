# Build and deployment

Operator-facing launchers:

    scripts\build-debug.bat
    scripts\build-release.bat
    scripts\deploy-inno.bat 0.1.0
    bash scripts/build-debug.sh
    bash scripts/build-release.sh
    bash scripts/deploy-inno.sh 0.1.0

The `.bat` and `.sh` files are thin launchers over the PowerShell pipeline. The shell launchers target Git Bash/WSL on Windows and resolve their script path to a Windows path before invoking `powershell.exe`/`pwsh.exe`.

`scripts/build.ps1` discovers Visual Studio/CMake, configures native x64, builds the requested Debug or Release native DLLs, stages the matching OpenCV/ONNX runtime and then builds .NET with the same configuration. `-Publish` produces a self-contained Windows x64 Release application. `-RequireOcrAssets` blocks builds missing required OCR files.

`deploy-inno` calls `verify-release.ps1`: Release publish with required OCR assets, managed/native tests, published WPF smoke, and `package.ps1`. Inno Setup 6 compiles `installer/WireMarkerInspection.iss` into `dist/WireMarkerInspection-Setup-<version>.exe`; it never installs the package automatically. Versions must use a form such as `1.2.3` or `1.2.3-preview.1`.

Acceptance on 2026-08-31: `build-debug.sh` and `build-release.bat` passed, and `deploy-inno.bat 0.1.0` passed the complete pipeline (managed 83/83, native 1/1, published WPF smoke and Inno compilation). It produced `dist/WireMarkerInspection-Setup-0.1.0.exe` (83,992,815 bytes) and did not install it.

The development installer can contain the editor without OCR/camera assets; RUN will explicitly remain unavailable.
Live camera deployment requires the Hikrobot MVS driver/native runtime on the workstation and a matching `MvCameraControl.Net.dll` discovered from the installed SDK or staged in `vendor/camera`. Run `scripts/camera-probe.ps1 -Grab` on the target machine before operator acceptance.
Release readiness additionally requires model licensing/provenance, OCR dataset acceptance, Hikrobot redistribution approval, x64 VC++ redistributable, target-PC smoke and throughput testing.
Data is under per-user LocalApplicationData, separate from installed binaries. Back up recipes and results before upgrades. No automatic retention deletion is enabled.
`scripts/camera-soak.ps1 -Minutes N` measures frame rate, frame-interval spread, timeouts and reconnects against real hardware; run it on the target PC before operator acceptance. Operational events are appended as JSON Lines under the per-user data directory.
Continuous free-run acquisition has been validated on the development camera. Hardware trigger semantics, long-run disconnect/reconnect behavior, optics/exposure tuning and installer execution on a clean target PC are pending acceptance.
