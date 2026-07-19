using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Windows;

public enum WindowState { Normal, Minimized, Maximized }

public enum WindowAction { Focus, Move, Resize, Minimize, Maximize, Restore }

/// <param name="Rect">The window as drawn, from DWMWA_EXTENDED_FRAME_BOUNDS — not GetWindowRect,
/// which overstates it by the invisible resize border.</param>
public sealed record WindowInfo(
    long Hwnd,
    string Title,
    string ProcessName,
    int ProcessId,
    FrameRect Rect,
    WindowState State,
    bool IsForeground);

/// <param name="TitleContains">Case-insensitive substring filter.</param>
public sealed record WindowListInput(
    string? TitleContains = null,
    string? ProcessName = null,
    bool IncludeMinimized = true);

public sealed record WindowListResult(IReadOnlyList<WindowInfo> Windows);

/// <param name="X">For Move: the desired left edge of the VISIBLE window, not the raw rect.</param>
public sealed record WindowActionInput(
    long Hwnd,
    WindowAction Action,
    int? X = null,
    int? Y = null,
    int? W = null,
    int? H = null);

/// <summary>The window's state after the action, re-read rather than assumed — Windows may
/// clamp, snap, or refuse a request.</summary>
public sealed record WindowActionResult(WindowInfo Window);
