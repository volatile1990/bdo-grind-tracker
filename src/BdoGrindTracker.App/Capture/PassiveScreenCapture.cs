using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.Capture;

/// <summary>
/// Passive DXGI desktop duplication matching BDO Companion 0.7.4's capture path.
/// It reads a selected output without addressing, focusing, or controlling a game.
/// </summary>
internal sealed unsafe class PassiveScreenCapture : IDisposable
{
    private readonly object _sync = new();
    private CompanionDesktopDuplication? _duplication;
    private bool _disposed;

    public CapturedDesktopBitmap Capture(
        Rectangle desktopRegion,
        CancellationToken cancellationToken)
    {
        ValidateRegion(desktopRegion);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (;;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_duplication is null || _duplication.DesktopRegion != desktopRegion)
                {
                    ResetDuplication();
                    _duplication = CompanionDesktopDuplication.Create(desktopRegion);
                }

                try
                {
                    return _duplication.Capture(cancellationToken);
                }
                catch (DesktopDuplicationAccessLostException)
                {
                    // Companion rebuilds the duplication object after display-mode,
                    // adapter, or secure-desktop transitions.
                    ResetDuplication();
                }
            }
        }
    }

    internal static bool IsHdrColorSpace(int colorSpace) => colorSpace is
        12 or // RGB_FULL_G2084_NONE_P2020
        13 or // YCBCR_STUDIO_G2084_LEFT_P2020
        14 or // RGB_STUDIO_G2084_NONE_P2020
        16 or // YCBCR_STUDIO_G2084_TOPLEFT_P2020
        18 or // YCBCR_STUDIO_GHLG_TOPLEFT_P2020
        19;   // YCBCR_FULL_GHLG_TOPLEFT_P2020

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            ResetDuplication();
            _disposed = true;
        }
    }

    private static void ValidateRegion(Rectangle desktopRegion)
    {
        if (desktopRegion.Width <= 0 || desktopRegion.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desktopRegion),
                desktopRegion,
                "Der Aufnahmebereich muss eine positive Breite und Höhe haben.");
        }
    }

    private void ResetDuplication()
    {
        _duplication?.Dispose();
        _duplication = null;
    }

    private sealed class CompanionDesktopDuplication : IDisposable
    {
        private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
        private const int DxgiErrorAccessLost = unchecked((int)0x887A0026);
        private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);
        private const uint D3d11CreateDeviceBgraSupport = 0x20;
        private const uint D3d11SdkVersion = 7;
        private const uint D3d11CpuAccessRead = 0x20000;
        private const uint D3d11UsageStaging = 3;
        private const uint D3d11MapRead = 1;

        private static readonly Guid IdxgiFactory1 =
            new("770aae78-f26f-4dba-a829-253c83d1b387");
        private static readonly Guid IdxgiOutput1 =
            new("00cddea8-939b-4b83-a340-a685226666cc");
        private static readonly Guid IdxgiOutput6 =
            new("068346e8-aaec-4b84-add7-137f513f77a1");
        private static readonly Guid Id3d11Texture2D =
            new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        private nint _device;
        private nint _context;
        private nint _outputDuplication;
        private bool _disposed;

        private CompanionDesktopDuplication(
            Rectangle desktopRegion,
            Rectangle outputBounds,
            bool isHdr,
            nint device,
            nint context,
            nint outputDuplication)
        {
            DesktopRegion = desktopRegion;
            OutputBounds = outputBounds;
            IsHdr = isHdr;
            _device = device;
            _context = context;
            _outputDuplication = outputDuplication;
        }

        public Rectangle DesktopRegion { get; }

        private Rectangle OutputBounds { get; }

        private bool IsHdr { get; }

        public static CompanionDesktopDuplication Create(Rectangle desktopRegion)
        {
            nint factory = 0;
            nint adapter = 0;
            nint output = 0;
            nint output1 = 0;
            nint device = 0;
            nint context = 0;
            nint duplication = 0;
            try
            {
                var factoryId = IdxgiFactory1;
                ThrowIfFailed(CreateDXGIFactory1(&factoryId, &factory));
                (adapter, output, var outputBounds, var isHdr) =
                    FindOutput(factory, desktopRegion);

                ThrowIfFailed(D3D11CreateDevice(
                    adapter,
                    driverType: 0,
                    software: 0,
                    flags: D3d11CreateDeviceBgraSupport,
                    featureLevels: null,
                    featureLevelCount: 0,
                    sdkVersion: D3d11SdkVersion,
                    device: &device,
                    selectedFeatureLevel: null,
                    immediateContext: &context));

                ThrowIfFailed(QueryInterface(output, IdxgiOutput1, out output1));
                ThrowIfFailed(DuplicateOutput(output1, device, out duplication));

                var result = new CompanionDesktopDuplication(
                    desktopRegion,
                    outputBounds,
                    isHdr,
                    device,
                    context,
                    duplication);
                device = 0;
                context = 0;
                duplication = 0;
                return result;
            }
            finally
            {
                Release(ref duplication);
                Release(ref context);
                Release(ref device);
                Release(ref output1);
                Release(ref output);
                Release(ref adapter);
                Release(ref factory);
            }
        }

        public CapturedDesktopBitmap Capture(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (;;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DxgiOutduplFrameInfo frameInfo = default;
                nint resource = 0;
                var frameAcquired = false;
                try
                {
                    var acquireResult = AcquireNextFrame(
                        _outputDuplication,
                        timeoutMilliseconds: 100,
                        &frameInfo,
                        &resource);
                    if (acquireResult == DxgiErrorWaitTimeout)
                    {
                        WaitOrCancel(cancellationToken, TimeSpan.FromMilliseconds(333));
                        continue;
                    }

                    if (acquireResult == DxgiErrorAccessLost)
                    {
                        throw new DesktopDuplicationAccessLostException();
                    }

                    ThrowIfFailed(acquireResult);
                    frameAcquired = true;

                    if (frameInfo.AccumulatedFrames == 0 || resource == 0)
                    {
                        WaitOrCancel(cancellationToken, TimeSpan.FromMilliseconds(25));
                        continue;
                    }

                    return CopyFrame(resource);
                }
                finally
                {
                    Release(ref resource);
                    if (frameAcquired)
                    {
                        var releaseResult = ReleaseFrame(_outputDuplication);
                        if (releaseResult == DxgiErrorAccessLost)
                        {
                            throw new DesktopDuplicationAccessLostException();
                        }

                        ThrowIfFailed(releaseResult);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Release(ref _outputDuplication);
            Release(ref _context);
            Release(ref _device);
            _disposed = true;
        }

        private CapturedDesktopBitmap CopyFrame(nint resource)
        {
            nint texture = 0;
            nint stagingTexture = 0;
            var isMapped = false;
            try
            {
                ThrowIfFailed(QueryInterface(resource, Id3d11Texture2D, out texture));
                D3d11Texture2DDesc description = default;
                GetTextureDescription(texture, &description);

                var sourceBounds = Rectangle.Intersect(DesktopRegion, OutputBounds);
                if (sourceBounds != DesktopRegion)
                {
                    throw new InvalidOperationException(
                        $"Der DXGI-Ausgang {OutputBounds} enthält den Aufnahmebereich " +
                        $"{DesktopRegion} nicht vollständig.");
                }

                var sourceLeft = checked((uint)(DesktopRegion.Left - OutputBounds.Left));
                var sourceTop = checked((uint)(DesktopRegion.Top - OutputBounds.Top));
                var cropRequired = sourceLeft != 0 ||
                    sourceTop != 0 ||
                    DesktopRegion.Width != description.Width ||
                    DesktopRegion.Height != description.Height;

                description.Width = checked((uint)DesktopRegion.Width);
                description.Height = checked((uint)DesktopRegion.Height);
                description.Usage = D3d11UsageStaging;
                description.BindFlags = 0;
                description.CpuAccessFlags = D3d11CpuAccessRead;
                description.MiscFlags = 0;
                ThrowIfFailed(CreateTexture2D(_device, &description, out stagingTexture));

                if (cropRequired)
                {
                    var sourceBox = new D3d11Box
                    {
                        Left = sourceLeft,
                        Top = sourceTop,
                        Front = 0,
                        Right = checked(sourceLeft + (uint)DesktopRegion.Width),
                        Bottom = checked(sourceTop + (uint)DesktopRegion.Height),
                        Back = 1,
                    };
                    CopySubresourceRegion(
                        _context,
                        stagingTexture,
                        destinationSubresource: 0,
                        destinationX: 0,
                        destinationY: 0,
                        destinationZ: 0,
                        texture,
                        sourceSubresource: 0,
                        &sourceBox);
                }
                else
                {
                    CopyResource(_context, stagingTexture, texture);
                }

                D3d11MappedSubresource mapped = default;
                ThrowIfFailed(Map(
                    _context,
                    stagingTexture,
                    subresource: 0,
                    D3d11MapRead,
                    mapFlags: 0,
                    &mapped));
                isMapped = true;

                var bitmap = CopyMappedBitmap(
                    mapped.Data,
                    mapped.RowPitch,
                    DesktopRegion.Width,
                    DesktopRegion.Height);
                return new CapturedDesktopBitmap(bitmap, IsHdr);
            }
            finally
            {
                if (isMapped)
                {
                    Unmap(_context, stagingTexture, subresource: 0);
                }

                Release(ref stagingTexture);
                Release(ref texture);
            }
        }

        private static Bitmap CopyMappedBitmap(
            nint source,
            uint sourceRowPitch,
            int width,
            int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            BitmapData? bitmapData = null;
            try
            {
                bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppRgb);
                var rowBytes = checked(width * 4);
                if (sourceRowPitch < rowBytes)
                {
                    throw new InvalidOperationException(
                        "Die DXGI-Zeilenbreite ist kleiner als die Bildzeile.");
                }

                for (var y = 0; y < height; y++)
                {
                    var sourceRow = (byte*)source + checked((nuint)y * sourceRowPitch);
                    var destinationRow = (byte*)bitmapData.Scan0 +
                        checked((nint)y * bitmapData.Stride);
                    Buffer.MemoryCopy(
                        sourceRow,
                        destinationRow,
                        Math.Abs(bitmapData.Stride),
                        rowBytes);
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            finally
            {
                if (bitmapData is not null)
                {
                    bitmap.UnlockBits(bitmapData);
                }
            }
        }

        private static (nint Adapter, nint Output, Rectangle Bounds, bool IsHdr) FindOutput(
            nint factory,
            Rectangle desktopRegion)
        {
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                nint adapter = 0;
                var adapterResult = EnumAdapters1(factory, adapterIndex, out adapter);
                if (adapterResult == DxgiErrorNotFound)
                {
                    break;
                }

                ThrowIfFailed(adapterResult);
                try
                {
                    if (IsSoftwareAdapter(adapter))
                    {
                        continue;
                    }

                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        nint output = 0;
                        var outputResult = EnumOutputs(adapter, outputIndex, out output);
                        if (outputResult == DxgiErrorNotFound)
                        {
                            break;
                        }

                        ThrowIfFailed(outputResult);
                        try
                        {
                            DxgiOutputDesc description = default;
                            ThrowIfFailed(GetOutputDescription(output, &description));
                            var bounds = description.DesktopCoordinates.ToRectangle();
                            if (description.AttachedToDesktop == 0 ||
                                !bounds.Contains(desktopRegion))
                            {
                                continue;
                            }

                            var retainedAdapter = AddRef(adapter);
                            var retainedOutput = AddRef(output);
                            return (
                                retainedAdapter,
                                retainedOutput,
                                bounds,
                                ReadHdrState(output));
                        }
                        finally
                        {
                            Release(ref output);
                        }
                    }
                }
                finally
                {
                    Release(ref adapter);
                }
            }

            throw new InvalidOperationException(
                $"Für den Desktopbereich {desktopRegion} wurde kein aktiver " +
                "Hardware-DXGI-Ausgang gefunden.");
        }

        private static bool IsSoftwareAdapter(nint adapter)
        {
            var description = stackalloc byte[312];
            new Span<byte>(description, 312).Clear();
            ThrowIfFailed(GetAdapterDescription1(adapter, description));
            return (*(uint*)(description + 304) & 2) != 0;
        }

        private static bool ReadHdrState(nint output)
        {
            var queryResult = QueryInterface(output, IdxgiOutput6, out var output6);
            if (queryResult < 0 || output6 == 0)
            {
                Release(ref output6);
                return false;
            }

            try
            {
                DxgiOutputDesc1 description = default;
                if (GetOutputDescription1(output6, &description) < 0 ||
                    description.AttachedToDesktop == 0)
                {
                    return false;
                }

                return IsHdrColorSpace(description.ColorSpace);
            }
            finally
            {
                Release(ref output6);
            }
        }

        private static void WaitOrCancel(
            CancellationToken cancellationToken,
            TimeSpan duration)
        {
            if (cancellationToken.WaitHandle.WaitOne(duration))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private static void ThrowIfFailed(int hresult)
        {
            if (hresult < 0)
            {
                Marshal.ThrowExceptionForHR(hresult);
            }
        }

        private static nint AddRef(nint value)
        {
            var method = (delegate* unmanaged[Stdcall]<nint, uint>)GetMethod(value, 1);
            _ = method(value);
            return value;
        }

        private static void Release(ref nint value)
        {
            var current = value;
            value = 0;
            if (current == 0)
            {
                return;
            }

            var method = (delegate* unmanaged[Stdcall]<nint, uint>)GetMethod(current, 2);
            _ = method(current);
        }

        private static int QueryInterface(nint value, Guid interfaceId, out nint result)
        {
            result = 0;
            fixed (nint* resultPointer = &result)
            {
                var method = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)
                    GetMethod(value, 0);
                return method(value, &interfaceId, resultPointer);
            }
        }

        private static int EnumAdapters1(nint factory, uint index, out nint adapter)
        {
            adapter = 0;
            fixed (nint* adapterPointer = &adapter)
            {
                var method = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)
                    GetMethod(factory, 12);
                return method(factory, index, adapterPointer);
            }
        }

        private static int GetAdapterDescription1(nint adapter, byte* description)
        {
            var method = (delegate* unmanaged[Stdcall]<nint, byte*, int>)
                GetMethod(adapter, 10);
            return method(adapter, description);
        }

        private static int EnumOutputs(nint adapter, uint index, out nint output)
        {
            output = 0;
            fixed (nint* outputPointer = &output)
            {
                var method = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)
                    GetMethod(adapter, 7);
                return method(adapter, index, outputPointer);
            }
        }

        private static int GetOutputDescription(
            nint output,
            DxgiOutputDesc* description)
        {
            var method = (delegate* unmanaged[Stdcall]<nint, DxgiOutputDesc*, int>)
                GetMethod(output, 7);
            return method(output, description);
        }

        private static int GetOutputDescription1(
            nint output,
            DxgiOutputDesc1* description)
        {
            var method = (delegate* unmanaged[Stdcall]<nint, DxgiOutputDesc1*, int>)
                GetMethod(output, 27);
            return method(output, description);
        }

        private static int DuplicateOutput(nint output, nint device, out nint duplication)
        {
            duplication = 0;
            fixed (nint* duplicationPointer = &duplication)
            {
                var method = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)
                    GetMethod(output, 22);
                return method(output, device, duplicationPointer);
            }
        }

        private static int AcquireNextFrame(
            nint duplication,
            uint timeoutMilliseconds,
            DxgiOutduplFrameInfo* frameInfo,
            nint* resource)
        {
            var method = (delegate* unmanaged[Stdcall]<
                nint,
                uint,
                DxgiOutduplFrameInfo*,
                nint*,
                int>)GetMethod(duplication, 8);
            return method(duplication, timeoutMilliseconds, frameInfo, resource);
        }

        private static int ReleaseFrame(nint duplication)
        {
            var method = (delegate* unmanaged[Stdcall]<nint, int>)
                GetMethod(duplication, 14);
            return method(duplication);
        }

        private static void GetTextureDescription(
            nint texture,
            D3d11Texture2DDesc* description)
        {
            var method = (delegate* unmanaged[Stdcall]<nint, D3d11Texture2DDesc*, void>)
                GetMethod(texture, 10);
            method(texture, description);
        }

        private static int CreateTexture2D(
            nint device,
            D3d11Texture2DDesc* description,
            out nint texture)
        {
            texture = 0;
            fixed (nint* texturePointer = &texture)
            {
                var method = (delegate* unmanaged[Stdcall]<
                    nint,
                    D3d11Texture2DDesc*,
                    nint,
                    nint*,
                    int>)GetMethod(device, 5);
                return method(device, description, 0, texturePointer);
            }
        }

        private static void CopyResource(nint context, nint destination, nint source)
        {
            var method = (delegate* unmanaged[Stdcall]<nint, nint, nint, void>)
                GetMethod(context, 47);
            method(context, destination, source);
        }

        private static void CopySubresourceRegion(
            nint context,
            nint destination,
            uint destinationSubresource,
            uint destinationX,
            uint destinationY,
            uint destinationZ,
            nint source,
            uint sourceSubresource,
            D3d11Box* sourceBox)
        {
            var method = (delegate* unmanaged[Stdcall]<
                nint,
                nint,
                uint,
                uint,
                uint,
                uint,
                nint,
                uint,
                D3d11Box*,
                void>)GetMethod(context, 46);
            method(
                context,
                destination,
                destinationSubresource,
                destinationX,
                destinationY,
                destinationZ,
                source,
                sourceSubresource,
                sourceBox);
        }

        private static int Map(
            nint context,
            nint resource,
            uint subresource,
            uint mapType,
            uint mapFlags,
            D3d11MappedSubresource* mapped)
        {
            var method = (delegate* unmanaged[Stdcall]<
                nint,
                nint,
                uint,
                uint,
                uint,
                D3d11MappedSubresource*,
                int>)GetMethod(context, 14);
            return method(context, resource, subresource, mapType, mapFlags, mapped);
        }

        private static void Unmap(nint context, nint resource, uint subresource)
        {
            var method = (delegate* unmanaged[Stdcall]<nint, nint, uint, void>)
                GetMethod(context, 15);
            method(context, resource, subresource);
        }

        private static nint GetMethod(nint value, int slot)
        {
            if (value == 0)
            {
                throw new InvalidOperationException("Ein erforderliches COM-Objekt fehlt.");
            }

            return (*(nint**)value)[slot];
        }

        [DllImport("dxgi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int CreateDXGIFactory1(Guid* interfaceId, nint* factory);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int D3D11CreateDevice(
            nint adapter,
            uint driverType,
            nint software,
            uint flags,
            uint* featureLevels,
            uint featureLevelCount,
            uint sdkVersion,
            nint* device,
            uint* selectedFeatureLevel,
            nint* immediateContext);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(
                Left,
                Top,
                Right,
                Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct DxgiOutputDesc
        {
            public fixed char DeviceName[32];
            public NativeRect DesktopCoordinates;
            public int AttachedToDesktop;
            public int Rotation;
            public nint Monitor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct DxgiOutputDesc1
        {
            public fixed char DeviceName[32];
            public NativeRect DesktopCoordinates;
            public int AttachedToDesktop;
            public int Rotation;
            public nint Monitor;
            public uint BitsPerColor;
            public int ColorSpace;
            public fixed float RedPrimary[2];
            public fixed float GreenPrimary[2];
            public fixed float BluePrimary[2];
            public fixed float WhitePoint[2];
            public float MinLuminance;
            public float MaxLuminance;
            public float MaxFullFrameLuminance;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DxgiOutduplFrameInfo
        {
            public long LastPresentTime;
            public long LastMouseUpdateTime;
            public uint AccumulatedFrames;
            public int RectsCoalesced;
            public int ProtectedContentMaskedOut;
            public int PointerPositionX;
            public int PointerPositionY;
            public int PointerPositionVisible;
            public uint TotalMetadataBufferSize;
            public uint PointerShapeBufferSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3d11Texture2DDesc
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public uint Format;
            public uint SampleCount;
            public uint SampleQuality;
            public uint Usage;
            public uint BindFlags;
            public uint CpuAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3d11MappedSubresource
        {
            public nint Data;
            public uint RowPitch;
            public uint DepthPitch;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3d11Box
        {
            public uint Left;
            public uint Top;
            public uint Front;
            public uint Right;
            public uint Bottom;
            public uint Back;
        }
    }

    private sealed class DesktopDuplicationAccessLostException : Exception;
}

internal readonly record struct CapturedDesktopBitmap(Bitmap Bitmap, bool IsHdr);
