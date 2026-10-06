#pragma once

#if defined(_MSC_VER)
// MSVC's compiler headers define these types without requiring SDK/UCRT headers.
#include <vcruntime.h>
#else
#include <stddef.h>
#endif

#if defined(_WIN32)
#define GRINDCREST_NATIVE_EXPORT extern "C" __declspec(dllexport)
#define GRINDCREST_NATIVE_CALL __cdecl
#else
#define GRINDCREST_NATIVE_EXPORT extern "C"
#define GRINDCREST_NATIVE_CALL
#endif

GRINDCREST_NATIVE_EXPORT int GRINDCREST_NATIVE_CALL grindcrest_native_abi_version();

// The caller owns every buffer. Source, destination and the 65,536-byte lookup
// must not overlap, and each row must contain width * 8 / width * 4 bytes.
// destination points to the logical first row, including for a negative stride.
// Returns -1 for invalid arguments before writing any pixels, or 0 on success.
// No memory allocation, worker threads, floating-point math or retained pointers.
GRINDCREST_NATIVE_EXPORT int GRINDCREST_NATIVE_CALL grindcrest_convert_rgba16f_to_bgra8(
    const void* source,
    size_t source_stride,
    void* destination,
    ptrdiff_t destination_stride,
    int width,
    int height,
    const unsigned char* lookup);
