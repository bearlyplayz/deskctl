namespace WinDeskCtl.Core.Frames;

/// <summary>
/// A frame's placement and size.
/// </summary>
/// <param name="OriginX">
/// The frame's top-left in absolute screen coordinates — measured from the primary monitor's
/// top-left, so a monitor above or left of primary is genuinely negative. This is NOT the same
/// space as a <see cref="Point"/> in this frame, which is measured from this origin and is never
/// negative. The two coincide only when the virtual desktop's origin is 0,0, which is why
/// conflating them survives review and fails in the field.
/// </param>
/// <param name="OriginY">The frame's top-left in absolute screen coordinates. May be negative.</param>
/// <param name="W">Width in the frame's own units — i.e. image pixels when <paramref name="Scale"/> is not 1.</param>
/// <param name="H">Height in the frame's own units.</param>
/// <param name="Scale">
/// Frame units per physical pixel. 0.5 means the frame was downscaled by half.
/// A capture that downscales MUST set this; silently rescaling is the bug this design exists
/// to prevent.
/// </param>
public sealed record FrameRect(Frame Frame, int OriginX, int OriginY, int W, int H, double Scale = 1.0)
{
    private readonly double _scale = Validated(Scale);

    /// <summary>
    /// Guarded so Translate stays total. Scale is a divisor when converting a frame's
    /// units back to physical pixels: zero yields infinity and a NaN coordinate, and a negative
    /// one mirrors the frame. Neither throws on its own — both produce a plausible number that is
    /// wrong, which is the failure mode this type exists to make impossible. The init accessor
    /// carries the guard through `with`, which is how every downscale builds its rect.
    /// </summary>
    public double Scale
    {
        get => _scale;
        init => _scale = Validated(value);
    }

    private static double Validated(double scale) => double.IsFinite(scale) && scale > 0
        ? scale
        : throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must be finite and greater than zero.");

    /// <summary>Whether <paramref name="p"/> lies inside this rect. The far edge is exclusive:
    /// a 1920-wide frame's last addressable column is 1919.</summary>
    public bool Contains(Point p) => p.X >= 0 && p.Y >= 0 && p.X < W && p.Y < H;
}
