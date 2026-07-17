using Deskctl.Core.Frames;

namespace Deskctl.Core.Input;

/// <summary>
/// Pointer paths. Real movement rather than teleport, because Windows starts no drag until the
/// pointer passes SM_CXDRAGWIDTH/SM_CYDRAGHEIGHT while a button is down: a down at A and an up
/// at B with no intervening WM_MOUSEMOVE registers as a click, not a drag.
/// </summary>
public static class Interpolate
{
    /// <param name="hz">
    /// Sample rate. 120Hz is above a 60Hz display's refresh, so the motion is smooth to any app
    /// sampling it, while staying far below a per-pixel path that would flood the input queue on
    /// a long move across a wide desktop.
    /// </param>
    /// <returns>Points with the delay to wait BEFORE each. The final point is always exactly
    /// <paramref name="to"/>.</returns>
    public static IReadOnlyList<(Point At, TimeSpan Delay)> Path(
        Point from, Point to, TimeSpan over, Ease ease, int hz = 120)
    {
        if (from.Frame != to.Frame)
        {
            throw new ArgumentException(
                $"Cannot interpolate from '{from.Frame}' to '{to.Frame}'. Translate to a common frame first.",
                nameof(to));
        }

        if (over <= TimeSpan.Zero || (from.X == to.X && from.Y == to.Y))
        {
            return [(to, TimeSpan.Zero)];
        }

        int steps = Math.Max(1, (int)Math.Round(over.TotalSeconds * hz));
        TimeSpan tick = over / steps;

        List<(Point, TimeSpan)> path = new(steps);

        // Starts at i=1: i=0 would re-emit the origin, a redundant event that some apps read as
        // a spurious move.
        for (int i = 1; i <= steps; i++)
        {
            double t = Apply(ease, (double)i / steps);
            path.Add((
                new Point(to.Frame,
                    Lerp(from.X, to.X, t),
                    Lerp(from.Y, to.Y, t)),
                tick));
        }

        // Force the endpoint. Easing plus rounding can leave the last sample a pixel short, and
        // a drop one pixel off the target can land on the wrong control.
        path[^1] = (to, path[^1].Item2);

        return path;
    }

    private static int Lerp(int a, int b, double t) =>
        (int)Math.Round(a + ((b - a) * t), MidpointRounding.AwayFromZero);

    private static double Apply(Ease ease, double t) => ease switch
    {
        Ease.EaseIn => t * t,
        Ease.EaseOut => 1 - ((1 - t) * (1 - t)),
        Ease.EaseInOut => t < 0.5 ? 2 * t * t : 1 - (Math.Pow((-2 * t) + 2, 2) / 2),
        _ => t,
    };
}
