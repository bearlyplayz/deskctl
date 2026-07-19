using System.Runtime.InteropServices;
using WinDeskCtl.Core.Commands;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Windows;
using WinDeskCtl.Platform.Displays;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Windows;

public static partial class WindowGeometry
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, int attr, out User32.RECT value, int size);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out User32.RECT rect);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(Cursor.POINT pt, uint flags);

    /// <summary>
    /// The window's rect as drawn. GetWindowRect includes the invisible resize border DWM adds,
    /// overstating the window by roughly 7px per side — enough to miss a click near an edge.
    /// </summary>
    public static FrameRect GetRect(nint hwnd)
    {
        DisplayEnumerator.EnsurePerMonitorV2();

        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out User32.RECT r,
                Marshal.SizeOf<User32.RECT>()) < 0)
        {
            // DWM composition is effectively always on since Windows 8, but a window can be
            // destroyed between enumeration and this call. Falling back keeps the border error
            // rather than failing outright.
            if (!GetWindowRect(hwnd, out r))
            {
                throw new InvalidOperationException($"Window {hwnd} has no readable geometry.");
            }
        }

        // DWM reports absolute screen coordinates, which is exactly what a FrameRect origin is.
        return new FrameRect(
            new Frame.Window(hwnd),
            OriginX: r.Left,
            OriginY: r.Top,
            W: r.Right - r.Left,
            H: r.Bottom - r.Top);
    }

    /// <summary>
    /// Measures this window's invisible border. Per window, not a constant: it varies with window
    /// style, and a borderless window's delta is zero.
    /// </summary>
    public static BorderDelta GetBorderDelta(nint hwnd)
    {
        FrameRect visible = GetRect(hwnd);

        if (!GetWindowRect(hwnd, out User32.RECT r))
        {
            throw new InvalidOperationException($"GetWindowRect failed for {hwnd}.");
        }

        // Both rects are absolute screen coordinates, the same space a FrameRect origin holds.
        FrameRect raw = new(new Frame.Window(hwnd), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return WindowFrame.Measure(raw, visible);
    }

    /// <summary>Resolves a monitor id minted by DisplayEnumerator back to an HMONITOR.</summary>
    public static nint MonitorFromId(string id)
    {
        FrameRect virtualBounds = DisplayEnumerator.GetVirtualBounds();
        MonitorInfo? m = DisplayEnumerator.GetMonitors().FirstOrDefault(x => x.Id == id);
        if (m is null) throw new ArgumentException($"No monitor with id '{id}'.", nameof(id));

        Point centre = new(m.Bounds.Frame, m.Bounds.W / 2, m.Bounds.H / 2);
        (int x, int y) = ScreenCoords.ToScreen(Translate.To(centre, m.Bounds, virtualBounds), virtualBounds);

        // MONITOR_DEFAULTTONULL: a point inside the monitor's bounds resolves to that monitor,
        // and null rather than a wrong guess if the topology changed since enumeration.
        nint hmon = MonitorFromPoint(new Cursor.POINT { X = x, Y = y }, 0);
        if (hmon == 0) throw new InvalidOperationException($"Monitor '{id}' disappeared between enumeration and use.");
        return hmon;
    }
}
