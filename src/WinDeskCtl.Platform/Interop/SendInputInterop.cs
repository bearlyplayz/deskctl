using System.Runtime.InteropServices;

namespace WinDeskCtl.Platform.Interop;

internal static partial class SendInputInterop
{
    internal const uint INPUT_MOUSE = 0;
    internal const uint INPUT_KEYBOARD = 1;

    internal const uint MOUSEEVENTF_MOVE = 0x0001;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    internal const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    internal const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    internal const uint MOUSEEVENTF_XDOWN = 0x0080;
    internal const uint MOUSEEVENTF_XUP = 0x0100;
    internal const uint MOUSEEVENTF_WHEEL = 0x0800;
    internal const uint MOUSEEVENTF_HWHEEL = 0x1000;
    internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    /// <summary>
    /// Normalizes against SM_XVIRTUALSCREEN/SM_CXVIRTUALSCREEN instead of the primary display.
    /// Without it, absolute coordinates are relative to primary only, so a monitor at a negative
    /// virtual origin normalizes negative and clamps to 0 — arithmetically unreachable, not
    /// merely mis-clicked. This flag is the fix for the project's founding bug.
    /// </summary>
    internal const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_UNICODE = 0x0004;

    internal const uint XBUTTON1 = 0x0001;
    internal const uint XBUTTON2 = 0x0002;

    internal const int WHEEL_DELTA = 120;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        internal int dx, dy;
        internal uint mouseData, dwFlags, time;
        internal nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        internal ushort wVk, wScan;
        internal uint dwFlags, time;
        internal nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] internal MOUSEINPUT mi;
        [FieldOffset(0)] internal KEYBDINPUT ki;
    }

    /// <summary>The syscall's own shape: a type tag plus a union. The step grammar mirrors it
    /// rather than wrapping it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        internal uint type;
        internal INPUTUNION u;
    }

    /// <summary>
    /// MSDN guarantees these events "are not interspersed with other keyboard or mouse input
    /// events inserted either by the user ... or by other calls to SendInput" — the OS-level
    /// atomicity that makes a batch a transaction rather than a convenience.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint count, [In] INPUT[] inputs, int size);
}
