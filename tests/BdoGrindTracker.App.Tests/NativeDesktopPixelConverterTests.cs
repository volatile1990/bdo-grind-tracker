using System.Runtime.InteropServices;
using BdoGrindTracker.App.Capture;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeDesktopPixelConverterTests
{
    private const byte Sentinel = 0xa7;
    private const int GuardBytes = 32;

    [Fact]
    public void EveryHalfBitPatternMatchesManagedAndExpectedBgraIncludingNonfiniteValues()
    {
        RequireNative();
        const int width = 256;
        const int height = 256;
        const int sourceStride = width * 8 + 24;
        const int destinationStride = width * 4 + 20;
        var lookup = DesktopPixelConverter.CreateHalfLookup();
        var source = FilledBuffer(sourceStride * height);
        var expected = FilledBuffer(destinationStride * height);

        for (var bits = 0; bits <= ushort.MaxValue; bits++)
        {
            var y = bits / width;
            var x = bits % width;
            var red = (ushort)bits;
            var green = (ushort)((bits + 17389) & ushort.MaxValue);
            var blue = (ushort)(bits ^ 0xaaaa);
            var offset = GuardBytes + y * sourceStride + x * 8;
            WriteHalf(source, offset, red);
            WriteHalf(source, offset + 2, green);
            WriteHalf(source, offset + 4, blue);
            WriteHalf(source, offset + 6, (ushort)bits);
            var outputOffset = GuardBytes + y * destinationStride + x * 4;
            expected[outputOffset] = lookup[blue];
            expected[outputOffset + 1] = lookup[green];
            expected[outputOffset + 2] = lookup[red];
            expected[outputOffset + 3] = 255;
        }

        AssertParity(source, sourceStride, destinationStride, width, height, lookup, expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(257)]
    public void TailWidthsRespectPaddingGuardsAndBothDestinationDirections(int width)
    {
        RequireNative();
        const int height = 7;
        var sourceStride = width * 8 + 19;
        var destinationStride = width * 4 + 13;
        var source = FilledBuffer(sourceStride * height);
        var lookup = DesktopPixelConverter.CreateHalfLookup();
        var random = new Random(width);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        for (var channel = 0; channel < 4; channel++)
            WriteHalf(source, GuardBytes + y * sourceStride + x * 8 + channel * 2,
                (ushort)random.Next(ushort.MaxValue + 1));

        AssertParity(source, sourceStride, destinationStride, width, height, lookup);
        AssertParity(source, sourceStride, -destinationStride, width, height, lookup);
    }

    [Fact]
    public void ConcurrentConversionsShareInputAndLookupWithoutChangingThem()
    {
        RequireNative();
        const int width = 127;
        const int height = 19;
        const int sourceStride = width * 8 + 16;
        const int destinationStride = width * 4 + 12;
        var lookup = DesktopPixelConverter.CreateHalfLookup();
        var lookupBefore = lookup.ToArray();
        var source = FilledBuffer(sourceStride * height);
        new Random(731).NextBytes(source.AsSpan(GuardBytes, sourceStride * height));
        var sourceBefore = source.ToArray();
        using var pinnedSource = new PinnedBuffer(source);
        var managed = FilledBuffer(destinationStride * height);
        using (var pinnedManaged = new PinnedBuffer(managed))
            DesktopPixelConverter.CopyHdrPixelsManaged(pinnedSource.Pointer + GuardBytes, sourceStride,
                pinnedManaged.Pointer + GuardBytes, destinationStride, width, height, lookup);

        Parallel.For(0, 32, _ =>
        {
            var output = FilledBuffer(destinationStride * height);
            using var pinnedOutput = new PinnedBuffer(output);
            Assert.True(NativeDesktopPixelConverter.TryConvert(pinnedSource.Pointer + GuardBytes, sourceStride,
                pinnedOutput.Pointer + GuardBytes, destinationStride, width, height, lookup));
            Assert.Equal(managed, output);
        });
        Assert.Equal(sourceBefore, source);
        Assert.Equal(lookupBefore, lookup);
    }

    [Fact]
    public void DispatcherCanUseManagedFallbackWithIdenticalPixels()
    {
        RequireNative();
        const int width = 33;
        const int height = 5;
        const int sourceStride = width * 8 + 8;
        const int destinationStride = width * 4 + 8;
        var lookup = DesktopPixelConverter.CreateHalfLookup();
        var source = new byte[sourceStride * height];
        new Random(928).NextBytes(source);
        var native = FilledBuffer(destinationStride * height);
        var fallback = FilledBuffer(destinationStride * height);
        using var pinnedSource = new PinnedBuffer(source);
        using var pinnedNative = new PinnedBuffer(native);
        using var pinnedFallback = new PinnedBuffer(fallback);
        Assert.True(DesktopPixelConverter.CopyHdrPixels(pinnedSource.Pointer, sourceStride,
            pinnedNative.Pointer + GuardBytes, destinationStride, width, height, lookup));
        Assert.False(DesktopPixelConverter.CopyHdrPixels(pinnedSource.Pointer, sourceStride,
            pinnedFallback.Pointer + GuardBytes, destinationStride, width, height, lookup, useNative: false));
        Assert.Equal(native, fallback);
    }

    [Fact]
    public void InvalidNativeLayoutsAreRejectedWithoutWritingOutput()
    {
        RequireNative();
        var lookup = DesktopPixelConverter.CreateHalfLookup();
        var source = new byte[32];
        var output = FilledBuffer(32);
        var original = output.ToArray();
        using var pinnedSource = new PinnedBuffer(source);
        using var pinnedOutput = new PinnedBuffer(output);
        var sourcePointer = pinnedSource.Pointer;
        var outputPointer = pinnedOutput.Pointer + GuardBytes;
        Assert.False(NativeDesktopPixelConverter.TryConvert(0, 8, outputPointer, 4, 1, 1, lookup));
        Assert.False(NativeDesktopPixelConverter.TryConvert(sourcePointer, 8, 0, 4, 1, 1, lookup));
        Assert.False(NativeDesktopPixelConverter.TryConvert(sourcePointer, 8, outputPointer, 4, 0, 1, lookup));
        Assert.False(NativeDesktopPixelConverter.TryConvert(sourcePointer, 8, outputPointer, 4, 1, -1, lookup));
        Assert.False(NativeDesktopPixelConverter.TryConvert(sourcePointer, 7, outputPointer, 4, 1, 1, lookup));
        Assert.False(NativeDesktopPixelConverter.TryConvert(sourcePointer, 8, outputPointer, 3, 1, 1, lookup));
        Assert.False(NativeDesktopPixelConverter.TryConvert(sourcePointer, 8, outputPointer, -3, 1, 1, lookup));
        Assert.False(NativeDesktopPixelConverter.TryConvert(sourcePointer, 8, outputPointer, 4, 1, 1, []));
        Assert.False(NativeDesktopPixelConverter.TryConvert(sourcePointer, 8, outputPointer, 4, 1, 1, null!));
        Assert.Equal(original, output);
    }

    [Fact]
    public void NativeAbiRejectsInvalidAndOverflowingSpansBeforeReadingOrWriting()
    {
        RequireNative();
        var source = new byte[32];
        var output = FilledBuffer(32);
        var original = output.ToArray();
        var lookup = DesktopPixelConverter.CreateHalfLookup();
        using var pinnedSource = new PinnedBuffer(source);
        using var pinnedOutput = new PinnedBuffer(output);
        using var pinnedLookup = new PinnedBuffer(lookup);
        // Call the export directly: the production wrapper rejects basic invalid
        // layouts first, which would otherwise leave the C ABI checks untested.
        var library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Grindcrest.Native.dll"));
        try
        {
            var convert = Marshal.GetDelegateForFunctionPointer<ConvertPixels>(NativeLibrary.GetExport(library,
                "grindcrest_convert_rgba16f_to_bgra8"));
            var sourcePointer = pinnedSource.Pointer;
            var outputPointer = pinnedOutput.Pointer + GuardBytes;
            var lookupPointer = pinnedLookup.Pointer;
            Assert.Equal(-1, convert(0, 8, outputPointer, 4, 1, 1, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 8, 0, 4, 1, 1, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 8, outputPointer, 4, 1, 1, 0));
            Assert.Equal(-1, convert(sourcePointer, 8, outputPointer, 4, 0, 1, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 8, outputPointer, 4, 1, 0, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 7, outputPointer, 4, 1, 1, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 8, outputPointer, 3, 1, 1, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 8, outputPointer, -3, 1, 1, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 8, outputPointer, nint.MinValue, 1, 1, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, nuint.MaxValue, outputPointer, 4, 1, 2, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 8, outputPointer, nint.MaxValue, 1, 2, lookupPointer));
            Assert.Equal(-1, convert(sourcePointer, 8, outputPointer, 4, int.MaxValue, 1, lookupPointer));
            Assert.Equal(original, output);
        }
        finally { NativeLibrary.Free(library); }
    }

    private static void RequireNative() => Assert.True(NativeDesktopPixelConverter.IsAvailable,
        "The native HDR conversion DLL must be built and copied to the Windows x64 test output; native parity must not be skipped.");

    private static byte[] FilledBuffer(int payloadBytes)
    {
        var bytes = new byte[payloadBytes + GuardBytes * 2];
        Array.Fill(bytes, Sentinel);
        return bytes;
    }

    private static void WriteHalf(byte[] bytes, int offset, ushort bits)
    {
        bytes[offset] = (byte)bits;
        bytes[offset + 1] = (byte)(bits >> 8);
    }

    private static void AssertParity(byte[] source, int sourceStride, int destinationStride,
        int width, int height, byte[] lookup, byte[]? expected = null)
    {
        var sourceBefore = source.ToArray();
        var lookupBefore = lookup.ToArray();
        var outputBytes = checked(Math.Abs(destinationStride) * height);
        var native = FilledBuffer(outputBytes);
        var managed = FilledBuffer(outputBytes);
        var firstRowOffset = GuardBytes + (destinationStride < 0 ? (height - 1) * -destinationStride : 0);
        using var pinnedSource = new PinnedBuffer(source);
        using var pinnedNative = new PinnedBuffer(native);
        using var pinnedManaged = new PinnedBuffer(managed);
        DesktopPixelConverter.CopyHdrPixelsManaged(pinnedSource.Pointer + GuardBytes, (uint)sourceStride,
            pinnedManaged.Pointer + firstRowOffset, destinationStride, width, height, lookup);
        Assert.True(NativeDesktopPixelConverter.TryConvert(pinnedSource.Pointer + GuardBytes, (uint)sourceStride,
            pinnedNative.Pointer + firstRowOffset, destinationStride, width, height, lookup));
        Assert.Equal(managed, native);
        if (expected is not null) Assert.Equal(expected, native);
        Assert.Equal(sourceBefore, source);
        Assert.Equal(lookupBefore, lookup);
        Assert.All(native.Take(GuardBytes), value => Assert.Equal(Sentinel, value));
        Assert.All(native.TakeLast(GuardBytes), value => Assert.Equal(Sentinel, value));
        for (var y = 0; y < height; y++)
        {
            var rowStart = GuardBytes + y * Math.Abs(destinationStride);
            Assert.All(native.Skip(rowStart + width * 4).Take(Math.Abs(destinationStride) - width * 4),
                value => Assert.Equal(Sentinel, value));
        }
    }

    private sealed class PinnedBuffer(byte[] bytes) : IDisposable
    {
        private GCHandle _handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        internal nint Pointer => _handle.AddrOfPinnedObject();
        public void Dispose() => _handle.Free();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConvertPixels(nint source, nuint sourceStride, nint destination,
        nint destinationStride, int width, int height, nint lookup);
}
