using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Capture;

public enum ImageFormat
{
    Png,
    Jpeg,
}

/// <summary>
/// Defaults a capture applies when the caller sets no downscale cap of its own. A separate
/// helper rather than a <see cref="CaptureInput"/> default so a caller that genuinely wants the
/// source resolution — a record burst sampling motion — can simply not apply it.
/// </summary>
public static class CaptureDefaults
{
    /// <summary>Width cap applied when neither dimension is capped. Full-window captures are the
    /// dominant call, and an uncapped 1920-wide window costs image tokens that a downscaled one
    /// plus its recorded scale does not. Never silent: the result's rect reports the applied
    /// scale identically for a defaulted and an explicit cap.</summary>
    public const int MaxWidth = 1200;

    /// <summary>The effective width cap: the caller's, or <see cref="MaxWidth"/> when the caller
    /// capped neither axis. A height-only cap is respected as given — adding a width cap the
    /// caller did not ask for would change the geometry they specified.</summary>
    public static int? Apply(int? maxWidth, int? maxHeight) =>
        maxWidth ?? (maxHeight is null ? MaxWidth : null);
}

/// <summary>One recognized word and its bounding box, in the capture's own frame units.</summary>
public sealed record OcrWord(string Text, CropBox Rect);

/// <summary>
/// One recognized line: its full text, the union of its words' boxes, and the words themselves.
/// Word rects matter when a line spans several targets — a menu bar reads as one line, and only
/// the word's own rect gives a correct click point for one entry in it.
/// </summary>
public sealed record OcrLine(string Text, CropBox Rect, IReadOnlyList<OcrWord> Words);

/// <param name="Target">What to capture: a monitor, a window, an element, or the virtual desktop.</param>
/// <param name="Region">An optional sub-rectangle in the target's own units.</param>
/// <param name="MaxWidth">Downscale cap. Downscaling is the only lever that reduces image token
/// cost, since cost tracks pixel dimensions rather than bytes.</param>
/// <param name="Quality">JPEG quality, 1-100. Ignored for PNG.</param>
/// <param name="Ocr">Recognize text in the capture. Runs on the full-resolution pixels before
/// any downscale, with the reported rects converted into the output image's units.</param>
/// <param name="MintFrame">Whether to mint an img: frame for the result. On for captures a
/// caller will click back into; off for a record burst's individual frames, which share one
/// rect and get one frame for the whole burst.</param>
public sealed record CaptureInput(
    Frame Target,
    CropBox? Region = null,
    int? MaxWidth = null,
    int? MaxHeight = null,
    ImageFormat Format = ImageFormat.Png,
    int Quality = 90,
    bool Ocr = false,
    bool MintFrame = true)
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

/// <summary>The capture result without its pixels: what a surface reports alongside the image —
/// the coordinate frame to click back into, and OCR text when it was requested.</summary>
public sealed record CaptureInfo(string? Image, FrameRect Rect, IReadOnlyList<OcrLine>? Text);

/// <summary>
/// An image and the coordinate space it lives in. The two are one value because separating them
/// is the root of every coordinate failure this project exists to fix.
/// </summary>
/// <param name="Image">The minted img: frame for this capture, or null when minting was off.
/// A point read off the image is clickable as "img:&lt;handle&gt;@x,y" — the frame carries the
/// capture's origin and scale, so the caller never converts image pixels itself. Session-scoped:
/// it dies with the process that minted it.</param>
/// <param name="Text">Recognized text when OCR was requested, with rects in the image's own
/// units — directly usable as coordinates in <paramref name="Image"/>. Null when OCR was off.</param>
public sealed record CaptureResult(
    FrameRect Rect,
    ImageFormat Format,
    byte[] Bytes,
    string? Image = null,
    IReadOnlyList<OcrLine>? Text = null)
{
    public string MimeType => Format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        _ => "image/png",
    };
}
