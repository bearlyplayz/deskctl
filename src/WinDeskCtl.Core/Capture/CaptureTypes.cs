using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Capture;

public enum ImageFormat
{
    Png,
    Jpeg,
}

/// <param name="Target">What to capture: a monitor, a window, an element, or the virtual desktop.</param>
/// <param name="Region">An optional sub-rectangle in the target's own units.</param>
/// <param name="MaxWidth">Downscale cap. Downscaling is the only lever that reduces image token
/// cost, since cost tracks pixel dimensions rather than bytes.</param>
/// <param name="Quality">JPEG quality, 1-100. Ignored for PNG.</param>
public sealed record CaptureInput(
    Frame Target,
    CropBox? Region = null,
    int? MaxWidth = null,
    int? MaxHeight = null,
    ImageFormat Format = ImageFormat.Png,
    int Quality = 90)
{
    /// <summary>
    /// Validated here rather than at each surface: this record is what both the CLI and MCP
    /// build, so it is the one place a bad value cannot route around. Out-of-range quality
    /// reaches WIC as a raw ratio, where 0 or a negative is not an error it reports — it is an
    /// image it silently ruins.
    /// </summary>
    public int Quality { get; } = Quality is >= 1 and <= 100
        ? Quality
        : throw new ArgumentOutOfRangeException(
            nameof(Quality), Quality, "JPEG quality must be between 1 and 100.");

    public int? MaxWidth { get; } = MaxWidth is null or > 0
        ? MaxWidth
        : throw new ArgumentOutOfRangeException(nameof(MaxWidth), MaxWidth, "Max width must be positive.");

    public int? MaxHeight { get; } = MaxHeight is null or > 0
        ? MaxHeight
        : throw new ArgumentOutOfRangeException(nameof(MaxHeight), MaxHeight, "Max height must be positive.");
}

/// <summary>
/// An image and the coordinate space it lives in. The two are one value because separating them
/// is the root of every coordinate failure this project exists to fix.
/// </summary>
public sealed record CaptureResult(FrameRect Rect, ImageFormat Format, byte[] Bytes)
{
    public string MimeType => Format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        _ => "image/png",
    };
}
