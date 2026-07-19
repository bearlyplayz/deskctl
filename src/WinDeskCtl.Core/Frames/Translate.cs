namespace WinDeskCtl.Core.Frames;

/// <summary>
/// Conversion between coordinate spaces. Pure and total: no Win32, no I/O, no clock.
/// Every coordinate failure this project exists to fix reduces to this arithmetic,
/// which is precisely why it lives here and not inside a Win32 adapter.
/// </summary>
public static class Translate
{
    /// <summary>Converts <paramref name="p"/> from <paramref name="from"/> into <paramref name="to"/>'s space.</summary>
    /// <remarks>
    /// Virtual-desktop coordinates are the pivot: every frame declares its origin there, so an
    /// N-frame system needs N origins rather than N² conversions.
    /// </remarks>
    public static Point To(Point p, FrameRect from, FrameRect to)
    {
        if (p.Frame != from.Frame)
        {
            throw new ArgumentException(
                $"Point is in frame '{p.Frame}' but was given the rect for '{from.Frame}'.", nameof(from));
        }

        // Frame units -> physical pixels -> virtual desktop.
        double virtX = from.OriginX + (p.X / from.Scale);
        double virtY = from.OriginY + (p.Y / from.Scale);

        // Virtual desktop -> target's physical pixels -> target's frame units.
        double outX = (virtX - to.OriginX) * to.Scale;
        double outY = (virtY - to.OriginY) * to.Scale;

        // Round half away from zero. Banker's rounding would bias .5 cases toward even
        // pixels, which is invisible in a single conversion and drifts across a round trip.
        return new Point(to.Frame, RoundToPixel(outX), RoundToPixel(outY));
    }

    private static int RoundToPixel(double v) =>
        (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
