using Deskctl.Core.Commands;
using Deskctl.Core.Frames;
using Deskctl.Platform.Interop;

namespace Deskctl.Platform.Displays;

public static class DisplayEnumerator
{
    private static bool _dpiSet;

    /// <summary>
    /// Declares per-monitor-v2 DPI awareness. Must run before any geometry is read: without it
    /// Windows virtualizes coordinates for the process and every rect below is a lie on a scaled
    /// display. Idempotent — the second call fails with ERROR_ACCESS_DENIED, which is expected
    /// and ignored, because awareness can only be set once per process.
    /// </summary>
    public static void EnsurePerMonitorV2()
    {
        if (_dpiSet) return;
        User32.SetProcessDpiAwarenessContext(User32.DpiAwarenessContextPerMonitorAwareV2);
        _dpiSet = true;
    }

    /// <summary>
    /// The virtual desktop's bounds. The origin is negative whenever a monitor is positioned
    /// above or left of primary — this is the coordinate space that input must normalize
    /// against, not the primary display's.
    /// </summary>
    public static FrameRect GetVirtualBounds()
    {
        EnsurePerMonitorV2();
        return new FrameRect(
            new Frame.Virtual(),
            OriginX: User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN),
            OriginY: User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN),
            W: User32.GetSystemMetrics(User32.SM_CXVIRTUALSCREEN),
            H: User32.GetSystemMetrics(User32.SM_CYVIRTUALSCREEN));
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        EnsurePerMonitorV2();

        List<MonitorInfo> monitors = [];

        // The callback is invoked synchronously before EnumDisplayMonitors returns, so the
        // delegate cannot be collected mid-enumeration and needs no GC handle.
        User32.MonitorEnumProc callback = (nint hMonitor, nint _, ref User32.RECT _, nint _) =>
        {
            User32.MONITORINFOEXW info = default;
            info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<User32.MONITORINFOEXW>();
            if (!User32.GetMonitorInfo(hMonitor, ref info)) return 1;   // skip, keep enumerating

            uint dpi = 96;
            if (Shcore.GetDpiForMonitor(hMonitor, Shcore.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
            {
                dpi = dpiX;
            }

            string id = DeviceNameToId(info.DeviceName, monitors.Count);

            monitors.Add(new MonitorInfo(
                Id: id,
                Bounds: new FrameRect(
                    new Frame.Monitor(id),
                    OriginX: info.rcMonitor.Left,
                    OriginY: info.rcMonitor.Top,
                    W: info.rcMonitor.Right - info.rcMonitor.Left,
                    H: info.rcMonitor.Bottom - info.rcMonitor.Top),
                IsPrimary: (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0,
                Dpi: (int)dpi));

            return 1;
        };

        if (!User32.EnumDisplayMonitors(0, 0, callback, 0))
        {
            throw new InvalidOperationException("EnumDisplayMonitors failed.");
        }

        return monitors;
    }

    /// <summary>
    /// Reduces a device name such as "\\.\DISPLAY1" to a short stable id ("1"). Falls back to
    /// the enumeration index when the name does not match that shape, so an id is always minted.
    /// </summary>
    private static string DeviceNameToId(string deviceName, int index)
    {
        const string prefix = @"\\.\DISPLAY";
        return deviceName.StartsWith(prefix, StringComparison.Ordinal)
            ? deviceName[prefix.Length..]
            : index.ToString();
    }
}
