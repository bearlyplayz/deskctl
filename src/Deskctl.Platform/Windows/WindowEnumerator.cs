using System.Diagnostics;
using System.Runtime.InteropServices;
using Deskctl.Core.Windows;
using Deskctl.Platform.Displays;
using Deskctl.Platform.Interop;

namespace Deskctl.Platform.Windows;

public static partial class WindowEnumerator
{
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    public static IReadOnlyList<WindowInfo> List(bool includeMinimized)
    {
        DisplayEnumerator.EnsurePerMonitorV2();

        List<nint> handles = [];

        // The callback runs synchronously before EnumWindows returns, so the delegate cannot be
        // collected mid-enumeration and needs no GC handle.
        User32.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (IsInteresting(hwnd, includeMinimized)) handles.Add(hwnd);
            return 1;
        };

        if (!User32.EnumWindows(callback, 0))
        {
            throw new InvalidOperationException("EnumWindows failed.");
        }

        List<WindowInfo> windows = [];
        foreach (nint hwnd in handles)
        {
            try
            {
                windows.Add(Describe(hwnd));
            }
            catch (InvalidOperationException)
            {
                // A window can close between enumeration and description, leaving its geometry
                // unreadable. Dropping it is correct; failing the whole listing is not.
            }
        }

        return windows;
    }

    /// <summary>
    /// Whether a window is one a user would point at. EnumWindows returns every top-level HWND,
    /// most of which are invisible message-only windows, tool windows, and shell plumbing.
    /// </summary>
    private static bool IsInteresting(nint hwnd, bool includeMinimized)
    {
        bool minimized = User32.IsIconic(hwnd);

        // A minimized window is still a real window someone may want to restore, so it survives
        // the visibility test that would otherwise exclude it.
        if (!User32.IsWindowVisible(hwnd) && !minimized) return false;
        if (minimized && !includeMinimized) return false;

        if (User32.GetWindowTextLength(hwnd) == 0) return false;

        nint exStyle = User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);
        if (((uint)exStyle & User32.WS_EX_TOOLWINDOW) != 0) return false;

        // Cloaked windows are the ones that make a naive window list confusing: UWP apps park
        // suspended windows and shell hosts as cloaked, so they are invisible to the user but
        // fully "visible" to Win32.
        if (DwmGetWindowAttribute(hwnd, User32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) >= 0 && cloaked != 0)
        {
            return false;
        }

        return true;
    }

    public static WindowInfo Describe(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);

        return new WindowInfo(
            Hwnd: hwnd,
            Title: GetTitle(hwnd),
            ProcessName: GetProcessName((int)pid),
            ProcessId: (int)pid,
            Rect: WindowGeometry.GetRect(hwnd),
            State: GetState(hwnd),
            IsForeground: User32.GetForegroundWindow() == hwnd);
    }

    private static unsafe string GetTitle(nint hwnd)
    {
        int length = User32.GetWindowTextLength(hwnd);
        if (length == 0) return "";

        // +1 for the terminating null GetWindowTextW writes but does not count.
        char[] buffer = new char[length + 1];
        fixed (char* p = buffer)
        {
            int written = User32.GetWindowText(hwnd, p, buffer.Length);
            return new string(buffer, 0, written);
        }
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The process may be gone, or be a protected process this one cannot open. The window
            // is still worth listing without a name.
            return "";
        }
    }

    private static WindowState GetState(nint hwnd)
    {
        User32.WINDOWPLACEMENT placement = default;
        placement.length = (uint)Marshal.SizeOf<User32.WINDOWPLACEMENT>();

        if (!User32.GetWindowPlacement(hwnd, ref placement))
        {
            return User32.IsIconic(hwnd) ? WindowState.Minimized : WindowState.Normal;
        }

        return placement.showCmd switch
        {
            User32.SW_SHOWMINIMIZED => WindowState.Minimized,
            User32.SW_SHOWMAXIMIZED => WindowState.Maximized,
            _ => WindowState.Normal,
        };
    }
}
