using Deskctl.Core.Frames;

namespace Deskctl.Core.Capture;

/// <summary>
/// A fixed frame-rate/duration pairing for a burst capture. Presets rather than free fps and
/// duration because the two only make sense together: a fast animation wants a short high-rate
/// window, a slow one a long low-rate window, and every pairing here bounds the frame count
/// (max 30) so a burst can never fill a disk or a context window. Each rate stays well under any
/// monitor refresh, so every frame is a distinct repaint rather than a duplicate.
/// </summary>
public enum RecordPreset
{
    /// <summary>3 fps over 10s (30 frames). Gradual changes: progress bars, loading, downloads.</summary>
    Slow,

    /// <summary>6 fps over 5s (30 frames). General motion.</summary>
    Medium,

    /// <summary>9 fps over 1s (9 frames). Quick motion: spinners, short transitions.</summary>
    Fast,

    /// <summary>12 fps over 0.5s (6 frames). Very fast motion.</summary>
    Instant,
}

/// <summary>The timing each <see cref="RecordPreset"/> stands for. Pure grammar shared by both surfaces.</summary>
public static class RecordPresets
{
    /// <returns>The capture rate in frames per second and the window length in seconds.</returns>
    public static (int Fps, double Seconds) Timing(this RecordPreset preset) => preset switch
    {
        RecordPreset.Slow => (3, 10.0),
        RecordPreset.Medium => (6, 5.0),
        RecordPreset.Fast => (9, 1.0),
        RecordPreset.Instant => (12, 0.5),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown record preset."),
    };

    /// <summary>How many frames the preset produces: rate times duration, rounded.</summary>
    public static int FrameCount(this RecordPreset preset)
    {
        (int fps, double seconds) = preset.Timing();
        return (int)Math.Round(fps * seconds, MidpointRounding.AwayFromZero);
    }
}

/// <param name="Target">What to capture: a monitor or a window. Fixed for the whole burst.</param>
/// <param name="OutputDir">Directory to write the frames into. Created if it does not exist.</param>
/// <param name="Preset">The rate/duration pairing.</param>
/// <param name="Region">An optional sub-rectangle in the target's units, applied to every frame.
/// Cropping to just the animated area keeps each frame legible.</param>
/// <param name="MaxWidth">Per-frame downscale cap.</param>
/// <param name="Quality">JPEG quality, 1-100. Ignored for PNG.</param>
public sealed record RecordInput(
    Frame Target,
    string OutputDir,
    RecordPreset Preset = RecordPreset.Fast,
    CropBox? Region = null,
    int? MaxWidth = null,
    int? MaxHeight = null,
    ImageFormat Format = ImageFormat.Png,
    int Quality = 90)
{
    public string OutputDir { get; } = string.IsNullOrWhiteSpace(OutputDir)
        ? throw new ArgumentException("An output directory is required.", nameof(OutputDir))
        : OutputDir;
}

/// <summary>
/// The frames a burst wrote, and the one coordinate frame they all live in. A single rect covers
/// every frame because the target and region are fixed for the whole burst — the same reason a
/// caller can click into any frame using it. Paths are in capture order, matching the zero-padded
/// <c>frame_NNN</c> names on disk.
/// </summary>
public sealed record RecordResult(FrameRect Rect, ImageFormat Format, IReadOnlyList<string> Files);
