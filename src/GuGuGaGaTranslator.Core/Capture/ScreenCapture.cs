using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using GuGuGaGaTranslator.Core.Imaging;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>How a frame is obtained from the target window.</summary>
public enum CaptureBackend
{
    Screen,

    /// <summary>Ask the window to render itself, which also works while it is covered.</summary>
    PrintWindow,
}

/// <summary>GDI screen capture: the desktop is copied straight into a DIB section, so a frame costs one <c>BitBlt</c> and one array copy.</summary>
public static class ScreenCapture
{
    [SupportedOSPlatform("windows")]
    public static Frame CaptureScreenRegion(Int32Rect region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region), region, "capture region must have a positive size");

        return CaptureVia(region.Width, region.Height, region, memoryDc =>
        {
            var desktopDc = NativeMethods.GetDC(0);
            if (desktopDc == 0)
                throw new InvalidOperationException("GetDC(desktop) returned no DC");
            try
            {
                // CAPTUREBLT keeps layered windows (including the overlay) in the copy on old drivers.
                var ok = NativeMethods.BitBlt(
                    memoryDc, 0, 0, region.Width, region.Height,
                    desktopDc, region.X, region.Y,
                    NativeMethods.Srccopy | NativeMethods.Captureblt);
                if (!ok)
                    throw new InvalidOperationException($"BitBlt failed for region {region.X},{region.Y} {region.Width}×{region.Height}");
            }
            finally
            {
                NativeMethods.ReleaseDC(0, desktopDc);
            }
        });
    }

    [SupportedOSPlatform("windows")]
    public static Frame CaptureWindowClient(WindowInfo window, CaptureBackend backend = CaptureBackend.Screen)
    {
        if (window.IsMinimized)
            throw new InvalidOperationException($"window {window.Title} is minimized, so it cannot be captured");
        if (!window.HasClientArea)
            throw new InvalidOperationException($"window {window.Title} has an empty client area");

        if (backend == CaptureBackend.Screen)
            return CaptureScreenRegion(window.ClientRect);

        // PrintWindow renders the whole window rectangle, frame included, so the
        // client area is cropped out afterwards. GetWindowRect is the right size
        // here, unlike the DWM's visible frame bounds.
        if (!NativeMethods.GetWindowRect(window.Handle, out var raw))
            throw new InvalidOperationException($"GetWindowRect failed for 0x{window.Handle:X}");
        var width = raw.Right - raw.Left;
        var height = raw.Bottom - raw.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"window {window.Title} reported an empty window rectangle");

        var whole = CaptureVia(width, height, new Int32Rect(raw.Left, raw.Top, width, height), memoryDc =>
        {
            if (!NativeMethods.PrintWindow(window.Handle, memoryDc, NativeMethods.PwRenderFullContent))
                throw new InvalidOperationException($"PrintWindow failed for 0x{window.Handle:X}");
        });

        var crop = new Int32Rect(
            window.ClientRect.X - raw.Left,
            window.ClientRect.Y - raw.Top,
            window.ClientRect.Width,
            window.ClientRect.Height);
        return ImageOps.Crop(whole, crop);
    }

    private static Frame CaptureVia(int width, int height, Int32Rect sourceRegion, Action<nint> draw)
    {
        var desktopDc = NativeMethods.GetDC(0);
        if (desktopDc == 0)
            throw new InvalidOperationException("GetDC(desktop) returned no DC");

        nint memoryDc = 0;
        nint bitmap = 0;
        nint previousBitmap = 0;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(desktopDc);
            if (memoryDc == 0)
                throw new InvalidOperationException("CreateCompatibleDC failed");

            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = width,
                    // Negative height requests a top-down DIB, matching Frame's row order.
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                },
            };

            bitmap = NativeMethods.CreateDIBSection(memoryDc, ref info, NativeMethods.DibRgbColors, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0)
                throw new InvalidOperationException("CreateDIBSection failed");

            previousBitmap = NativeMethods.SelectObject(memoryDc, bitmap);
            draw(memoryDc);

            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            return new Frame
            {
                Bgra = pixels,
                Width = width,
                Height = height,
                SourceRegion = sourceRegion,
                CapturedAt = DateTimeOffset.Now,
            };
        }
        finally
        {
            if (previousBitmap != 0 && memoryDc != 0)
                NativeMethods.SelectObject(memoryDc, previousBitmap);
            if (bitmap != 0)
                NativeMethods.DeleteObject(bitmap);
            if (memoryDc != 0)
                NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(0, desktopDc);
        }
    }
}
