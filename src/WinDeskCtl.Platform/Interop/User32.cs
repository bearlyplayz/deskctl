using System.Runtime.InteropServices;

namespace WinDeskCtl.Platform.Interop;

internal static partial class User32
{
    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;
    internal const int SM_CXDRAGWIDTH = 68;
    internal const int SM_CYDRAGHEIGHT = 69;

    internal const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.</summary>
    internal static readonly nint DpiAwarenessContextPerMonitorAwareV2 = -4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// Blittable by construction: the device name is an inline fixed buffer rather than a
    /// marshalled string. <c>ByValTStr</c> would require a marshalling stub that the
    /// source-generated <c>LibraryImport</c> path — the AOT-clean one — cannot emit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MONITORINFOEXW
    {
        /// <summary>CCHDEVICENAME. The Win32 struct layout depends on this exact length.</summary>
        internal const int CchDeviceName = 32;

        internal uint cbSize;
        internal RECT rcMonitor;
        internal RECT rcWork;
        internal uint dwFlags;
        internal fixed char szDevice[CchDeviceName];

        /// <summary>The device name up to its null terminator, e.g. <c>\\.\DISPLAY1</c>.</summary>
        internal string DeviceName
        {
            get
            {
                fixed (char* p = szDevice)
                {
                    // The buffer is only null-terminated when the name is shorter than the
                    // field; a full-length name fills it exactly and has no terminator.
                    ReadOnlySpan<char> span = new(p, CchDeviceName);
                    int end = span.IndexOf('\0');
                    return new string(end < 0 ? span : span[..end]);
                }
            }
        }
    }

    internal delegate int MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEXW info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessDpiAwarenessContext(nint value);

    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>WS_BORDER | WS_DLGFRAME. Test for all of it, not any of it — either bit alone is
    /// a plain border rather than a title bar.</summary>
    internal const uint WS_CAPTION = 0x00C00000;

    internal const int SW_MINIMIZE = 6;
    internal const int SW_MAXIMIZE = 3;
    internal const int SW_RESTORE = 9;

    internal const int SWP_NOSIZE = 0x0001;
    internal const int SWP_NOMOVE = 0x0002;
    internal const int SWP_NOZORDER = 0x0004;
    internal const int SWP_NOACTIVATE = 0x0010;

    internal const int DWMWA_CLOAKED = 14;

    /// <summary>showCmd values GetWindowPlacement reports.</summary>
    internal const uint SW_SHOWMINIMIZED = 2;
    internal const uint SW_SHOWMAXIMIZED = 3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        internal uint length;
        internal uint flags;
        internal uint showCmd;
        internal Cursor.POINT ptMinPosition;
        internal Cursor.POINT ptMaxPosition;
        internal RECT rcNormalPosition;
    }

    internal delegate int EnumWindowsProc(nint hwnd, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc callback, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsZoomed(nint hwnd);

    /// <summary>
    /// Raw pointer buffer rather than a marshalled array: LibraryImport cannot size an
    /// <c>[Out] char[]</c> without a count marshaller, and the caller already owns the buffer.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static unsafe partial int GetWindowText(nint hwnd, char* text, int count);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLength(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    /// <summary>GA_ROOT — walk to the top-level window, past any child-HWND control.</summary>
    internal const uint GA_ROOT = 2;

    [LibraryImport("user32.dll")]
    internal static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(uint from, uint to, [MarshalAs(UnmanagedType.Bool)] bool attach);

    internal const uint SPI_GETMOUSE = 0x0003;
    internal const uint SPI_GETMOUSESPEED = 0x0070;

    /// <summary>
    /// Reads the pointer's two acceleration inputs. SPI_GETMOUSE fills three ints — the x and y
    /// thresholds and the enable flag — while SPI_GETMOUSESPEED yields a single 1-20 slider
    /// value, so the parameter is typed as a raw pointer rather than modelled per-action.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SystemParametersInfo(uint action, uint param, void* data, uint winIni);
}

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();
}

internal static partial class Shcore
{
    internal const int MDT_EFFECTIVE_DPI = 0;

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);
}
