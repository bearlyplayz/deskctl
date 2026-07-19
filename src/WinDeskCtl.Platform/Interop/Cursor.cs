using System.Runtime.InteropServices;

namespace WinDeskCtl.Platform.Interop;

internal static partial class Cursor
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT p);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        internal int X, Y;
    }
}
