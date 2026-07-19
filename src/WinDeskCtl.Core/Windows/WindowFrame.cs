using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Windows;

/// <summary>How far the raw window rect overhangs the visible one, per edge. Non-negative.</summary>
public readonly record struct BorderDelta(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Converts between the two window coordinate spaces Win32 uses and never reconciles.
/// </summary>
/// <remarks>
/// GetWindowRect includes an invisible resize border DWM draws outside the window's painted
/// edge — typically ~7px on the left, right, and bottom, and 0 at the top. So the rect Windows
/// reports is not the rect you see.
///
/// The trap is that the two spaces are not interchangeable in one direction only: reads should
/// use DWMWA_EXTENDED_FRAME_BOUNDS, but SetWindowPos consumes raw-rect space. A tool that reads
/// visible and writes it back unconverted moves every window by the border width, every time.
/// The delta is measured per window rather than assumed, because it varies with window style —
/// a borderless window's delta is zero.
///
/// Both rects are in absolute screen coordinates, which is what a FrameRect origin holds, so the
/// arithmetic never crosses into virtual-frame space and needs no ScreenCoords conversion.
/// </remarks>
public static class WindowFrame
{
    public static BorderDelta Measure(FrameRect raw, FrameRect visible)
    {
        if (raw.Frame != visible.Frame)
        {
            throw new ArgumentException(
                $"Cannot measure a border between '{raw.Frame}' and '{visible.Frame}'.", nameof(visible));
        }

        return new BorderDelta(
            Left: visible.OriginX - raw.OriginX,
            Top: visible.OriginY - raw.OriginY,
            Right: (raw.OriginX + raw.W) - (visible.OriginX + visible.W),
            Bottom: (raw.OriginY + raw.H) - (visible.OriginY + visible.H));
    }

    /// <summary>Expands a visible rect into the raw space SetWindowPos expects.</summary>
    public static FrameRect VisibleToRaw(FrameRect visible, BorderDelta d) => visible with
    {
        OriginX = visible.OriginX - d.Left,
        OriginY = visible.OriginY - d.Top,
        W = visible.W + d.Left + d.Right,
        H = visible.H + d.Top + d.Bottom,
    };

    /// <summary>Shrinks a raw rect to what is actually painted.</summary>
    public static FrameRect RawToVisible(FrameRect raw, BorderDelta d) => raw with
    {
        OriginX = raw.OriginX + d.Left,
        OriginY = raw.OriginY + d.Top,
        W = raw.W - d.Left - d.Right,
        H = raw.H - d.Top - d.Bottom,
    };
}
