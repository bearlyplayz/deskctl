namespace Deskctl.Core.Frames;

/// <summary>
/// The single crossing point between Core's frame coordinates and Win32's absolute
/// virtual-screen coordinates.
/// </summary>
/// <remarks>
/// The two spaces are not the same and the difference is invisible until a monitor sits above or
/// left of primary:
/// <list type="bullet">
/// <item>A point in the <c>virtual</c> frame is measured from the virtual desktop's top-left
/// corner, so it is never negative (see <see cref="Translate"/>).</item>
/// <item>Win32 cursor and window APIs measure from the <em>primary</em> monitor's top-left, so a
/// monitor stacked above primary has genuinely negative coordinates.</item>
/// </list>
/// They coincide exactly when the virtual origin is 0,0 — which is every single-monitor box and
/// every side-by-side arrangement. Passing a frame point straight to Win32 therefore looks
/// correct right up until a monitor sits above or left of primary. Every P/Invoke taking or
/// returning screen coordinates must route through here.
/// </remarks>
public static class ScreenCoords
{
    /// <summary>Converts a point in the <c>virtual</c> frame to absolute screen coordinates.</summary>
    public static (int X, int Y) ToScreen(Point virtualPoint, FrameRect virtualBounds)
    {
        if (virtualPoint.Frame != virtualBounds.Frame)
        {
            throw new ArgumentException(
                $"Expected a point in '{virtualBounds.Frame}' but got '{virtualPoint.Frame}'.",
                nameof(virtualPoint));
        }

        return (virtualPoint.X + virtualBounds.OriginX, virtualPoint.Y + virtualBounds.OriginY);
    }

    /// <summary>Converts absolute screen coordinates into a point in the <c>virtual</c> frame.</summary>
    public static Point FromScreen(int x, int y, FrameRect virtualBounds) =>
        new(virtualBounds.Frame, x - virtualBounds.OriginX, y - virtualBounds.OriginY);
}
