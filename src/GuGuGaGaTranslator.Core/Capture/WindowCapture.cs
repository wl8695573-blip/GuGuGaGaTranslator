using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using GuGuGaGaTranslator.Core.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>从目标窗口的合成画面读取内容，不包含盖在上面的其他窗口。</summary>
public sealed class WindowCapture : IDisposable
{
    private readonly IDirect3DDevice _device;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly GraphicsCaptureItem _item;
    private Frame? _lastFrame;
    private bool _disposed;
    private int _poolWidth;
    private int _poolHeight;
    private readonly object _frameLock = new();
    private Direct3D11CaptureFrame? _nextFrame;
    private Exception? _captureError;

    public nint Handle { get; }

    public WindowCapture(nint handle)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("当前系统不支持窗口捕获，请在识别设置中选择屏幕捕获。");
        Handle = handle;
        _item = CreateItem(handle);
        _poolWidth = _item.Size.Width;
        _poolHeight = _item.Size.Height;
        _device = CreateDevice();
        try
        {
            _item.Closed += OnTargetClosed;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _item.Size);
            _pool.FrameArrived += OnFrameArrived;
            try
            {
                _session = _pool.CreateCaptureSession(_item);
                _session.IsCursorCaptureEnabled = false;
                _session.StartCapture();
            }
            catch
            {
                _session?.Dispose();
                _pool.FrameArrived -= OnFrameArrived;
                _pool.Dispose();
                throw;
            }
        }
        catch { _item.Closed -= OnTargetClosed; _device.Dispose(); throw; }
    }

    public async Task<Frame> CaptureAsync(WindowInfo window, Int32Rect region, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var deadline = Environment.TickCount64 + 1500;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Direct3D11CaptureFrame? next;
            lock (_frameLock)
            {
                if (_captureError is { } error)
                    throw new InvalidOperationException("窗口捕获已中断，请重新开始翻译或切换捕获方式。", error);
                next = _nextFrame;
                _nextFrame = null;
            }
            using var captured = next;
            if (captured is not null)
            {
                var size = captured.ContentSize;
                if (size.Width != _poolWidth || size.Height != _poolHeight)
                {
                    _lastFrame = null;
                    _poolWidth = size.Width;
                    _poolHeight = size.Height;
                    captured.Dispose();
                    lock (_frameLock)
                    {
                        _nextFrame?.Dispose();
                        _nextFrame = null;
                        _pool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, size);
                    }
                    continue;
                }
                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(captured.Surface)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyToBuffer(bytes.AsBuffer());
                _lastFrame = new Frame
                {
                    Bgra = bytes, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight,
                    SourceRegion = new Int32Rect(window.FrameRect.X, window.FrameRect.Y,
                        bitmap.PixelWidth, bitmap.PixelHeight),
                    CapturedAt = DateTimeOffset.Now,
                };
            }

            if (_lastFrame is { } frame)
            {
                // 静止画面可能不产生新帧；位置以当前窗口为准，尺寸变化则等待新帧。
                if (frame.Width == window.FrameRect.Width && frame.Height == window.FrameRect.Height)
                    return ImageOps.Crop(new Frame
                    {
                        Bgra = frame.Bgra, Width = frame.Width, Height = frame.Height,
                        CapturedAt = frame.CapturedAt, SourceRegion = window.FrameRect,
                    }, new Int32Rect(
                        region.X - window.FrameRect.X, region.Y - window.FrameRect.Y, region.Width, region.Height));
                _lastFrame = null;
            }
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("窗口捕获未收到有效画面。请检查窗口是否最小化，或在识别设置中切换屏幕捕获并保持选区无遮挡。");
            await Task.Delay(16, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IDirect3DDevice CreateDevice()
    {
        var result = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var device, out _, out var context);
        if (result < 0)
            result = D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out device, out _, out context);
        Marshal.ThrowExceptionForHR(result);
        nint dxgi = 0, inspectable = 0;
        try
        {
            var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgi != 0) Marshal.Release(dxgi);
            if (context != 0) Marshal.Release(context);
            if (device != 0) Marshal.Release(device);
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (_frameLock)
        {
            if (_disposed) return;
            Direct3D11CaptureFrame? latest = null;
            try
            {
                latest = sender.TryGetNextFrame();
                while (sender.TryGetNextFrame() is { } newer)
                {
                    latest?.Dispose();
                    latest = newer;
                }
                if (latest is null) return;
                _nextFrame?.Dispose();
                _nextFrame = latest;
                latest = null;
            }
            catch (Exception error) { _captureError = error; }
            finally { latest?.Dispose(); }
        }
    }

    private void OnTargetClosed(GraphicsCaptureItem sender, object args)
    {
        lock (_frameLock) _captureError = new InvalidOperationException("目标窗口已关闭。");
    }

    private static GraphicsCaptureItem CreateItem(nint handle)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        nint name = 0, factory = 0, item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out name));
            var iid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, in iid, out factory));
            var vtable = Marshal.ReadIntPtr(factory);
            var create = Marshal.GetDelegateForFunctionPointer<CreateForWindow>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
            var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            Marshal.ThrowExceptionForHR(create(factory, handle, in itemIid, out item));
            return GraphicsCaptureItem.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            if (name != 0) WindowsDeleteString(name);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindow(nint factory, nint window, in Guid iid, out nint item);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string value, int length, out nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint hstring, in Guid iid, out nint factory);

    public void Dispose()
    {
        lock (_frameLock)
        {
            if (_disposed) return;
            _disposed = true;
            _nextFrame?.Dispose();
            _nextFrame = null;
        }
        _pool.FrameArrived -= OnFrameArrived;
        _item.Closed -= OnTargetClosed;
        _session.Dispose();
        _pool.Dispose();
        _device.Dispose();
        _lastFrame = null;
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
