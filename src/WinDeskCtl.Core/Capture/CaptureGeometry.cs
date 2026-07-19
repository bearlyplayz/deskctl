using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Capture;

/// <param name="X">Left edge, in the source frame's units.</param>
/// <param name="Y">Top edge, in the source frame's units.</param>
public readonly record struct CropBox(int X, int Y, int W, int H)
{
    /// <summary>
    /// Reads the <c>x,y,w,h</c> wire form both surfaces accept. Parsing lives with the type so
    /// the CLI and the MCP tool cannot drift into disagreeing about what a region is.
    /// </summary>
    public static CropBox Parse(string s)
    {
        string[] parts = s.Split(',');
        if (parts.Length != 4)
        {
            throw new FormatException($"A region must be 'x,y,w,h'; got '{s}'.");
        }

        int[] v = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i].Trim(), out v[i]))
            {
                throw new FormatException($"A region must be 'x,y,w,h' of integers; got '{s}'.");
            }
        }

        return new CropBox(v[0], v[1], v[2], v[3]);
    }
}

/// <summary>
/// The arithmetic behind cropping and downscaling a capture. Pure: this is where the
/// "max_width silently rescaled the image" bug is prevented, and it is prevented by
/// making Scale a mandatory output rather than an afterthought.
/// </summary>
public static class CaptureGeometry
{
    /// <summary>
    /// Narrows a frame to a sub-rectangle. The frame identity is unchanged — cropping a monitor
    /// still leaves you in that monitor's space — but the origin shifts, so points read off the
    /// cropped image still translate correctly.
    /// </summary>
    public static FrameRect Crop(FrameRect source, CropBox box)
    {
        if (box.W <= 0 || box.H <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(box),
                $"Crop must have positive area; got {box.W}x{box.H}.");
        }
        if (box.X < 0 || box.Y < 0 || box.X + box.W > source.W || box.Y + box.H > source.H)
        {
            throw new ArgumentOutOfRangeException(nameof(box),
                $"Crop {box.X},{box.Y} {box.W}x{box.H} does not fit inside {source.W}x{source.H}.");
        }

        // The origin is in screen pixels while the box is in frame units, so the offset must be
        // un-scaled before it is added.
        return source with
        {
            OriginX = source.OriginX + (int)Math.Round(box.X / source.Scale, MidpointRounding.AwayFromZero),
            OriginY = source.OriginY + (int)Math.Round(box.Y / source.Scale, MidpointRounding.AwayFromZero),
            W = box.W,
            H = box.H,
        };
    }

    /// <summary>
    /// The largest size within the given caps that preserves aspect ratio. Never upscales:
    /// enlarging costs tokens and invents no detail.
    /// </summary>
    /// <remarks>
    /// One exception to both promises, in the degenerate case where a cap would round a dimension
    /// to zero: that axis is clamped to a single pixel, so the result is fractionally taller or
    /// wider than the ratio asks and the returned scale describes the unclamped axis rather than
    /// the clamped one. A single scale cannot describe two different axis ratios, and the
    /// alternative — reporting the clamped axis — would misplace coordinates along the axis that
    /// still has pixels to address. The clamped axis admits only index 0, which maps within its
    /// source band under either value, so the error stays bounded to that one row or column.
    /// </remarks>
    public static (int W, int H, double Scale) FitTo(int w, int h, int? maxWidth, int? maxHeight)
    {
        double scale = 1.0;
        if (maxWidth is int mw && w > mw) scale = Math.Min(scale, (double)mw / w);
        if (maxHeight is int mh && h > mh) scale = Math.Min(scale, (double)mh / h);

        if (scale >= 1.0) return (w, h, 1.0);

        // Clamp to at least one pixel: an extreme cap on a thin source would otherwise round a
        // dimension to 0, which no encoder accepts.
        return (Math.Max(1, (int)Math.Round(w * scale, MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(h * scale, MidpointRounding.AwayFromZero)),
                scale);
    }

    /// <summary>
    /// Applies <see cref="FitTo"/> to a frame, recording the factor in <see cref="FrameRect.Scale"/>.
    /// The origin is deliberately untouched: it is expressed in absolute screen pixels, which do
    /// not scale.
    /// </summary>
    public static FrameRect Downscale(FrameRect source, int? maxWidth, int? maxHeight)
    {
        (int w, int h, double scale) = FitTo(source.W, source.H, maxWidth, maxHeight);
        return source with { W = w, H = h, Scale = source.Scale * scale };
    }
}
