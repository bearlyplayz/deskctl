using System.Runtime.InteropServices;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Capture;

/// <summary>
/// The fallback for windows WGC refuses. PW_RENDERFULLCONTENT asks the window to
/// redraw itself into a DC, so unlike BitBlt it works while occluded — but it asks the app
/// nicely, and an app that ignores the request returns black. WGC is tried first for that reason.
/// </summary>
public static class PrintWindowCapture
{
    public static Bgra Capture(nint hwnd, int width, int height)
    {
        nint windowDc = User32Print.GetWindowDC(hwnd);
        if (windowDc == 0) throw new InvalidOperationException("GetWindowDC failed.");

        nint memDc = 0, bitmap = 0, previous = 0;
        try
        {
            memDc = Gdi32.CreateCompatibleDC(windowDc);
            bitmap = Gdi32.CreateCompatibleBitmap(windowDc, width, height);
            if (memDc == 0 || bitmap == 0) throw new InvalidOperationException("Failed to create a device context.");

            previous = Gdi32.SelectObject(memDc, bitmap);

            if (!User32Print.PrintWindow(hwnd, memDc, Gdi32.PW_RENDERFULLCONTENT))
            {
                throw new InvalidOperationException("PrintWindow failed.");
            }

            Gdi32.BITMAPINFOHEADER info = new()
            {
                biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>(),
                biWidth = width,
                // Negative height requests a top-down DIB. A positive value gives the GDI default
                // of bottom-up, which arrives vertically mirrored.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Gdi32.BI_RGB,
            };

            byte[] pixels = new byte[width * height * 4];
            if (Gdi32.GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref info, Gdi32.DIB_RGB_COLORS) == 0)
            {
                throw new InvalidOperationException("GetDIBits failed.");
            }

            return new Bgra(width, height, pixels);
        }
        finally
        {
            if (previous != 0) Gdi32.SelectObject(memDc, previous);
            if (bitmap != 0) Gdi32.DeleteObject(bitmap);
            if (memDc != 0) Gdi32.DeleteDC(memDc);
            User32Print.ReleaseDC(hwnd, windowDc);
        }
    }
}
