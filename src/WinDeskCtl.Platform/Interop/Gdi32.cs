using System.Runtime.InteropServices;

namespace WinDeskCtl.Platform.Interop;

internal static partial class Gdi32
{
    internal const uint BI_RGB = 0;
    internal const uint DIB_RGB_COLORS = 0;
    internal const uint PW_RENDERFULLCONTENT = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        internal uint biSize;
        internal int biWidth, biHeight;
        internal ushort biPlanes, biBitCount;
        internal uint biCompression, biSizeImage;
        internal int biXPelsPerMeter, biYPelsPerMeter;
        internal uint biClrUsed, biClrImportant;
    }

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleBitmap(nint hdc, int w, int h);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial int GetDIBits(
        nint hdc, nint bitmap, uint start, uint lines, [Out] byte[]? bits,
        ref BITMAPINFOHEADER info, uint usage);
}

internal static partial class User32Print
{
    [LibraryImport("user32.dll")]
    internal static partial nint GetWindowDC(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintWindow(nint hwnd, nint hdcBlt, uint flags);
}
