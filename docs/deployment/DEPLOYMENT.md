# Build and deployment
scripts/build.ps1 discovers Visual Studio/CMake, configures native x64, compiles Release native DLLs, copies OpenCV/ONNX runtime, then builds .NET.
-Publish produces a self-contained Windows x64 application.
-RequireOcrAssets blocks builds missing the required OCR files.
scripts/package.ps1 compiles the Inno Setup definition into dist without installing it.

The development installer can contain the editor without OCR/camera assets; RUN will explicitly remain unavailable.
Release readiness additionally requires model licensing/provenance, OCR dataset acceptance, vendor DLL/driver validation, x64 VC++ redistributable, target-PC smoke and throughput testing.
Data is under per-user LocalApplicationData, separate from installed binaries. Back up recipes and results before upgrades. No automatic retention deletion is enabled.
Hardware trigger semantics, camera disconnection behavior during production, and installer execution on a clean target PC are pending acceptance.
