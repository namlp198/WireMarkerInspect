#pragma once
#include <cstdint>
#ifdef WMI_IMPORT
#define WMI __declspec(dllimport)
#else
#define WMI __declspec(dllexport)
#endif
extern "C" {
WMI int __cdecl wmi_abi_version();
WMI void* __cdecl wmi_create(const wchar_t* detector, const wchar_t* recognizer, const wchar_t* dictionary, char** error);
WMI void __cdecl wmi_destroy(void* context);
WMI void __cdecl wmi_free(char* output);
// Returned strings are owned UTF-8: release with wmi_free. Input pixels are borrowed.
// Shape: 0 rectangle, 1 circle, 2 polygon. Orientation: 0 fixed, 1 180 degrees, 2 auto.
WMI char* __cdecl wmi_inspect(void* context, const uint8_t* bgr, int width, int height,
    int stride, int shape, const double* xy, int points, int orientation);
WMI char* __cdecl wmi_crop(const uint8_t* bgr, int width, int height,
    int stride, int shape, const double* xy, int points);
}
