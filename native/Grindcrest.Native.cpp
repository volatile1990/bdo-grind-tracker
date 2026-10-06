#include "Grindcrest.Native.h"
#include <stdint.h>

static_assert(sizeof(void*) == 8, "Grindcrest.Native requires an x64 build.");

extern "C" void* __cdecl memcpy(void* destination, const void* source, size_t count);
#pragma intrinsic(memcpy)

namespace
{
    // Fixed-size intrinsic copies are safe for unaligned buffers and compile
    // to one 16-bit load, without a CRT call or byte-by-byte reconstruction.
    __forceinline unsigned short read_half_bits(const unsigned char* source)
    {
        unsigned short bits;
        memcpy(&bits, source, sizeof(bits));
        return bits;
    }

    __forceinline void convert_pixel(const unsigned char* source, unsigned char* destination,
        const unsigned char* lookup)
    {
        // Windows x64 is little endian. One unaligned 32-bit store replaces
        // four byte stores, including the constant opaque alpha channel.
        const uint32_t bgra = 0xff000000u
            | static_cast<uint32_t>(lookup[read_half_bits(source + 4)])
            | (static_cast<uint32_t>(lookup[read_half_bits(source + 2)]) << 8)
            | (static_cast<uint32_t>(lookup[read_half_bits(source)]) << 16);
        memcpy(destination, &bgra, sizeof(bgra));
    }

    bool valid_span(size_t stride, size_t row_bytes, int height)
    {
        const size_t maximum = static_cast<size_t>(PTRDIFF_MAX);
        return row_bytes <= maximum && stride >= row_bytes
            && (height == 1 || stride <= (maximum - row_bytes) / static_cast<size_t>(height - 1));
    }
}

int GRINDCREST_NATIVE_CALL grindcrest_native_abi_version()
{
    return 1;
}

int GRINDCREST_NATIVE_CALL grindcrest_convert_rgba16f_to_bgra8(
    const void* source, size_t source_stride, void* destination, ptrdiff_t destination_stride,
    int width, int height, const unsigned char* lookup)
{
    if (source == nullptr || destination == nullptr || lookup == nullptr || width <= 0 || height <= 0
        || destination_stride == PTRDIFF_MIN
        || static_cast<size_t>(width) > static_cast<size_t>(PTRDIFF_MAX) / 8)
        return -1;

    const size_t source_row_bytes = static_cast<size_t>(width) * 8;
    const size_t destination_row_bytes = static_cast<size_t>(width) * 4;
    const size_t destination_absolute_stride = static_cast<size_t>(
        destination_stride < 0 ? -destination_stride : destination_stride);
    if (!valid_span(source_stride, source_row_bytes, height)
        || !valid_span(destination_absolute_stride, destination_row_bytes, height))
        return -1;

    const auto* source_bytes = static_cast<const unsigned char*>(source);
    auto* destination_bytes = static_cast<unsigned char*>(destination);
    for (int y = 0; y < height; ++y)
    {
        const auto* input = source_bytes + static_cast<size_t>(y) * source_stride;
        auto* output = destination_bytes + static_cast<ptrdiff_t>(y) * destination_stride;
        int x = 0;
        for (; x <= width - 4; x += 4)
        {
            convert_pixel(input, output, lookup);
            convert_pixel(input + 8, output + 4, lookup);
            convert_pixel(input + 16, output + 8, lookup);
            convert_pixel(input + 24, output + 12, lookup);
            input += 32;
            output += 16;
        }
        for (; x < width; ++x)
        {
            convert_pixel(input, output, lookup);
            input += 8;
            output += 4;
        }
    }
    return 0;
}
