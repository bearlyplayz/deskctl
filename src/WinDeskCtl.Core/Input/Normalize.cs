using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Input;

/// <summary>
/// Converts a virtual-desktop point into SendInput's 0-65535 absolute range.
/// </summary>
/// <remarks>
/// This is the arithmetic at the centre of the project. Paired with MOUSEEVENTF_VIRTUALDESK it
/// normalizes against SM_?VIRTUALSCREEN; the common stack normalizes against SM_CXSCREEN /
/// SM_CYSCREEN — the primary display only — which sends any point above or left of primary
/// negative, where it clamps to 0. Such a monitor is unreachable by construction, not
/// mis-clicked.
///
/// The input is a point in the <c>virtual</c> frame, which is already measured from the virtual
/// desktop's top-left: <see cref="Translate"/> subtracted the origin on the way in. The rect's
/// origin is therefore read for its size only. Subtracting it again would displace every point
/// by the origin and throw for any topology whose origin is negative — which is precisely the
/// topology this exists to serve.
/// </remarks>
public static class Normalize
{
    public static (int Nx, int Ny) ToAbsolute(Point p, FrameRect virtualBounds)
    {
        if (p.Frame is not Frame.Virtual)
        {
            throw new ArgumentException(
                $"Normalization takes a virtual-desktop point; got '{p.Frame}'. Translate first.", nameof(p));
        }

        if (!virtualBounds.Contains(p))
        {
            throw new ArgumentOutOfRangeException(nameof(p),
                $"{p} is outside the virtual desktop (origin {virtualBounds.OriginX},{virtualBounds.OriginY} " +
                $"size {virtualBounds.W}x{virtualBounds.H}). Clamping would click somewhere you did not ask for.");
        }

        return (Scale(p.X, virtualBounds.W), Scale(p.Y, virtualBounds.H));
    }

    /// <summary>
    /// Maps a pixel index onto the 0-65535 axis, aiming at the pixel's CENTRE.
    /// </summary>
    /// <remarks>
    /// Windows converts an absolute event back with <c>floor(n * size / 65536)</c>, so a pixel
    /// owns the half-open band <c>[i*65536/size, (i+1)*65536/size)</c> and the exact hit is that
    /// band's midpoint. The intuitive <c>i * 65535 / (size-1)</c> aims at the band's leading
    /// EDGE instead: it is off by half a pixel everywhere, and wherever that half-pixel plus
    /// rounding falls below the boundary the event lands one pixel short. Measured against this
    /// inverse it misses 110 of 7680 columns — sporadically, which is worse than always, because
    /// it reads as flakiness rather than as arithmetic. Aiming at the centre is exact for every
    /// column at every width.
    ///
    /// The far edge therefore does not reach 65535, and should not: 65535 is the last band's
    /// trailing edge, not the last pixel's centre. Landing on the requested pixel is the
    /// contract; saturating the range is not.
    /// </remarks>
    private static int Scale(int index, int size) =>
        (int)Math.Round((index + 0.5) * 65536.0 / size, MidpointRounding.AwayFromZero);
}
