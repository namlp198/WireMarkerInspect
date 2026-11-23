# Camera runtime staging
The production UI uses the official Hikrobot MVS backend in `HikrobotMvsCamera`.

- Preferred development setup: install Hikrobot MVS. MSBuild discovers `Development/DotNet/AnyCpu/MvCameraControl.Net.dll`; the native runtime/driver remains supplied by MVS.
- Staged build setup: place the matching `MvCameraControl.Net.dll` here. DLLs are ignored by Git and copied beside the app.
- The adapter supports GigE Vision and USB3 Vision, continuous acquisition, ExposureTime/Gain, Mono8/RGB8/BGR8, and SDK conversion of Bayer/packed formats to Mono8 or BGR8.
- Run `scripts/camera-probe.ps1 -Grab` after driver/runtime installation. Close or Stop acquisition in MVS if it holds exclusive access.

`NAcquireCamera` remains as a legacy adapter for NAcquire C ABI 0.1. If used separately, stage validated x64 `NAcquireCAPI.dll`, `NAcquireCore.dll` and dependencies here. The inspected NAcquire Hikrobot backend is still a placeholder; never present its synthetic OpenCV provider as a real camera.
