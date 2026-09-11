using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace BdoGrindTracker.App.Capture;

/// <summary>Windows.Graphics.Capture + D3D11 readback. No display or game input is changed.</summary>
internal sealed class GraphicsWindowFrameSource : IWindowFrameSource
{
    private readonly GraphicsCaptureItem _item;
    private readonly WindowCaptureDevice _device;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly bool _isHdr;
    private bool _disposed;
    private volatile bool _closed;
    internal bool IsCaptureBorderSuppressed { get; }

    internal GraphicsWindowFrameSource(WindowCaptureTarget target, WindowCaptureGeometry geometry)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows unterstützt die Spielfensteraufnahme auf diesem System nicht.");
        _isHdr = geometry.IsHdr;
        _item = WindowCaptureDevice.CreateItem(target.Handle);
        _device = new WindowCaptureDevice();
        try
        {
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device.ProjectedDevice,
                _isHdr ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2, _item.Size);
            try
            {
                _session = _pool.CreateCaptureSession(_item);
                _session.IsCursorCaptureEnabled = false;
                IsCaptureBorderSuppressed = WindowCaptureDevice.TryDisableCaptureBorder(_session);
                _item.Closed += OnClosed;
                _session.StartCapture();
            }
            catch
            {
                _item.Closed -= OnClosed;
                _session?.Dispose();
                _pool.Dispose();
                throw;
            }
        }
        catch { _device.Dispose(); throw; }
    }

    public CapturedDesktopBitmap Capture(Func<WindowCaptureGeometry> readGeometry, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = Stopwatch.GetTimestamp();
        // Frame timestamps and Stopwatch use the same QPC epoch. A paused or full
        // frame pool must not replay a buffered image as a fresh 200 ms observation.
        var requestedAt = TimeSpan.FromSeconds((double)started / Stopwatch.Frequency);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_closed) throw new InvalidOperationException("Das aufgenommene Black-Desert-Spielfenster wurde geschlossen.");
            var geometry = readGeometry();
            if (geometry.IsHdr != _isHdr)
                throw new InvalidOperationException("Der HDR-Modus des Spielfensters hat sich geändert. Tracking bitte erneut starten.");
            using var frame = _pool.TryGetNextFrame();
            if (frame is not null && frame.SystemRelativeTime >= requestedAt)
            {
                var crop = geometry.ClientCrop(new Size(frame.ContentSize.Width, frame.ContentSize.Height));
                var bitmap = _device.CopyClient(frame.Surface, crop);
                try
                {
                    // A geometry or visibility transition during GPU readback cannot
                    // turn old pixels into a newly confirmed HUD observation.
                    var after = readGeometry();
                    if (after.IsHdr != _isHdr || after.ClientCrop(new Size(frame.ContentSize.Width, frame.ContentSize.Height)) != crop)
                        throw new InvalidOperationException("Die Geometrie des Spielfensters hat sich während der Aufnahme geändert. Tracking bitte erneut starten.");
                    return new CapturedDesktopBitmap(bitmap, _isHdr, IsToneMapped: _isHdr)
                    {
                        AcquiredAtTimestamp = checked((long)Math.Round(frame.SystemRelativeTime.TotalSeconds * Stopwatch.Frequency))
                    };
                }
                catch { bitmap.Dispose(); throw; }
            }
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(3))
                throw new InvalidOperationException("Black Desert liefert keine neuen Fensterbilder. Spiel sichtbar öffnen und Tracking erneut starten.");
            if (cancellationToken.WaitHandle.WaitOne(15)) cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private void OnClosed(GraphicsCaptureItem sender, object args) => _closed = true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _item.Closed -= OnClosed;
        _session.Dispose();
        _pool.Dispose();
        _device.Dispose();
    }
}

internal sealed unsafe class WindowCaptureDevice : IDisposable
{
    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid TextureId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid SurfaceAccessId = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private nint _device;
    private nint _context;
    private nint _staging;
    private Size _stagingSize;
    private uint _stagingFormat;
    internal IDirect3DDevice ProjectedDevice { get; }

    internal static bool TryDisableCaptureBorder(GraphicsCaptureSession session)
    {
        // UniversalApiContract v12 is newer than our Windows 10 SDK target.
        // Use its documented ABI without raising the minimum supported OS.
        if (!ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            return false;
        nint borderSession = 0;
        try
        {
            var access = RequestBorderlessAccess();
            using var reference = WinRT.MarshalInspectable<GraphicsCaptureSession>.CreateMarshaler(session);
            Marshal.ThrowExceptionForHR(QueryInterface(reference.ThisPtr,
                new Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be"), out borderSession));
            var setBorder = (delegate* unmanaged[Stdcall]<nint, byte, int>)Method(borderSession, 7);
            Marshal.ThrowExceptionForHR(setBorder(borderSession, 0));
            byte required = 1;
            var getBorder = (delegate* unmanaged[Stdcall]<nint, byte*, int>)Method(borderSession, 6);
            Marshal.ThrowExceptionForHR(getBorder(borderSession, &required));
            // Windows retains the border if access is denied, or another capture
            // application still requires it. Never change the global OS policy.
            return access == AppCapabilityAccessStatus.Allowed && required == 0;
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.TraceWarning("Windows could not disable this capture session's border: {0}", error.Message);
            return false;
        }
        finally { Release(ref borderSession); }
    }

    private static AppCapabilityAccessStatus RequestBorderlessAccess()
    {
        nint className = 0, factory = 0, operation = 0;
        const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureAccess";
        var accessId = new Guid("743ed370-06ec-5040-a58a-901f0f757095");
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, runtimeClass.Length, &className));
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, &accessId, &factory));
            var request = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Method(factory, 6);
            // GraphicsCaptureAccessKind.Borderless = 0. This runs on the capture worker.
            Marshal.ThrowExceptionForHR(request(factory, 0, &operation));
            var pending = WinRT.MarshalInterface<Windows.Foundation.IAsyncOperation<AppCapabilityAccessStatus>>.FromAbi(operation);
            try { return pending.AsTask().GetAwaiter().GetResult(); }
            finally { pending.Close(); }
        }
        finally
        {
            Release(ref operation);
            Release(ref factory);
            if (className != 0) WindowsDeleteString(className);
        }
    }

    internal WindowCaptureDevice()
    {
        nint device = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, &device, 0, &context));
            Marshal.ThrowExceptionForHR(QueryInterface(device, DxgiDeviceId, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, &inspectable));
            ProjectedDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            _device = device;
            _context = context;
            device = context = 0;
        }
        finally
        {
            Release(ref inspectable);
            Release(ref dxgi);
            Release(ref context);
            Release(ref device);
        }
    }

    internal static GraphicsCaptureItem CreateItem(nint window)
    {
        nint className = 0, factory = 0, item = 0;
        const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        var interopId = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
        var itemId = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, runtimeClass.Length, &className));
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, &interopId, &factory));
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Method(factory, 3);
            Marshal.ThrowExceptionForHR(create(factory, window, &itemId, &item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            Release(ref item);
            Release(ref factory);
            if (className != 0) WindowsDeleteString(className);
        }
    }

    internal Bitmap CopyClient(IDirect3DSurface surface, Rectangle crop)
    {
        nint access = 0, texture = 0;
        var mapped = false;
        using var surfaceReference = WinRT.MarshalInterface<IDirect3DSurface>.CreateMarshaler(surface);
        try
        {
            Marshal.ThrowExceptionForHR(QueryInterface(surfaceReference.ThisPtr, SurfaceAccessId, out access));
            var getInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Method(access, 3);
            var textureId = TextureId;
            Marshal.ThrowExceptionForHR(getInterface(access, &textureId, &texture));
            TextureDescription description = default;
            ((delegate* unmanaged[Stdcall]<nint, TextureDescription*, void>)Method(texture, 10))(texture, &description);
            if (!new Rectangle(0, 0, checked((int)description.Width), checked((int)description.Height)).Contains(crop))
                throw new InvalidOperationException("Die aktuelle Grafikoberfläche enthält den Spielbereich nicht vollständig.");
            if (_staging == 0 || _stagingSize != crop.Size || _stagingFormat != description.Format)
            {
                Release(ref _staging);
                description.Width = checked((uint)crop.Width);
                description.Height = checked((uint)crop.Height);
                description.Usage = 3;
                description.BindFlags = description.MiscFlags = 0;
                description.CpuAccessFlags = 0x20000;
                nint staging = 0;
                var create = (delegate* unmanaged[Stdcall]<nint, TextureDescription*, nint, nint*, int>)Method(_device, 5);
                Marshal.ThrowExceptionForHR(create(_device, &description, 0, &staging));
                _staging = staging;
                _stagingSize = crop.Size;
                _stagingFormat = description.Format;
            }
            var box = new TextureBox { Left = checked((uint)crop.Left), Top = checked((uint)crop.Top),
                Right = checked((uint)crop.Right), Bottom = checked((uint)crop.Bottom), Back = 1 };
            var copy = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, TextureBox*, void>)Method(_context, 46);
            copy(_context, _staging, 0, 0, 0, 0, texture, 0, &box);
            MappedTexture data = default;
            var map = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, MappedTexture*, int>)Method(_context, 14);
            Marshal.ThrowExceptionForHR(map(_context, _staging, 0, 1, 0, &data));
            mapped = true;
            return DesktopPixelConverter.CopyBitmap(data.Data, data.RowPitch, crop.Width, crop.Height, _stagingFormat);
        }
        finally
        {
            if (mapped) ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Method(_context, 15))(_context, _staging, 0);
            Release(ref texture);
            Release(ref access);
        }
    }

    public void Dispose()
    {
        Release(ref _staging);
        ProjectedDevice.Dispose();
        Release(ref _context);
        Release(ref _device);
    }

    private static nint Method(nint value, int slot) => (*(nint**)value)[slot];
    private static int QueryInterface(nint value, Guid id, out nint result)
    {
        result = 0;
        fixed (nint* pointer = &result)
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Method(value, 0))(value, &id, pointer);
    }
    private static void Release(ref nint value)
    {
        var current = value;
        value = 0;
        if (current != 0) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Method(current, 2))(current);
    }
    [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
    {
        internal uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MappedTexture { internal nint Data; internal uint RowPitch, DepthPitch; }
    [StructLayout(LayoutKind.Sequential)] private struct TextureBox { internal uint Left, Top, Front, Right, Bottom, Back; }

    [DllImport("d3d11.dll", ExactSpelling = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelCount, uint sdkVersion, nint* device, nint selectedFeatureLevel, nint* context);
    [DllImport("d3d11.dll", ExactSpelling = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, nint* graphicsDevice);
    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WindowsCreateString(string value, int length, nint* result);
    [DllImport("combase.dll", ExactSpelling = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll", ExactSpelling = true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RoGetActivationFactory(nint className, Guid* interfaceId, nint* factory);
}
