# Camera runtime staging
Place the validated x64 NAcquireCAPI.dll, NAcquireCore.dll and vendor dependencies in this directory.
DLLs are ignored by Git and copied beside the app during build.
The adapter matches NVision/NAcquire C API 0.1. Its actual checked-out Hikrobot backend is a placeholder.
Do not ship the synthetic OpenCV backend as a real Hikrobot driver.
Supported buffer formats: Mono8, RGB8, BGR8. Bayer and packed 12-bit are explicitly rejected pending vendor validation.
