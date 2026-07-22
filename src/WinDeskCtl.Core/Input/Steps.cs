using WinDeskCtl.Core.Capture;
using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Input;

public enum MouseButton { Left, Right, Middle, X1, X2 }

public enum Ease { Linear, EaseIn, EaseOut, EaseInOut }

/// <summary>What a down/up/press acts on. A closed pair, not a string: the device must be known
/// from the shape rather than guessed from the value.</summary>
public abstract record InputTarget
{
    private protected InputTarget() { }
}

public sealed record KeyRef(string Name) : InputTarget;

public sealed record ButtonRef(MouseButton Button) : InputTarget;

/// <summary>
/// One instruction in a batch. Steps are a closed union: SendInput takes an array of
/// type-tagged structs, so this mirrors the syscall's own shape rather than wrapping it
///.
/// </summary>
public abstract record Step
{
    private protected Step() { }

    /// <summary>Press and hold. Added to the held-set; released automatically if never lifted.</summary>
    public sealed record Down(InputTarget Target) : Step;

    /// <summary>Release. Removes from the held-set at any position, not just the end.</summary>
    public sealed record Up(InputTarget Target) : Step;

    /// <param name="To">Optional frame-qualified point to move to first, making this a click.</param>
    public sealed record Press(InputTarget Target, string? To = null) : Step;

    /// <param name="Over">Movement duration. Omitted means teleport — which is correct for
    /// positioning but wrong for dragging, since Windows starts no drag until the pointer
    /// passes SM_CXDRAGWIDTH while a button is down.</param>
    public sealed record Move(string To, TimeSpan? Over = null, Ease Ease = Ease.Linear) : Step;

    public sealed record Scroll(int Dy = 0, int Dx = 0, string? At = null) : Step;

    /// <summary>Types a literal string via KEYEVENTF_UNICODE — any character, layout-independent.</summary>
    public sealed record Text(string Value) : Step;

    public sealed record Invoke(string Target) : Step;

    public sealed record Fill(string Target, string Value) : Step;

    /// <summary>Polls for an element. Replaces sleeping a guessed interval.</summary>
    public sealed record WaitFor(string Target, TimeSpan Timeout) : Step;

    public sealed record Delay(TimeSpan Duration) : Step;

    /// <summary>
    /// Screenshots mid-batch, written to <paramref name="Path"/> — never returned inline. Blocks
    /// only for the screenshot itself. The batch result reports the file with its img: frame, so
    /// a later step in the same batch can click into it.
    /// </summary>
    /// <param name="Ocr">Recognize text in the capture; the batch result carries it with the file.</param>
    public sealed record Capture(
        Frame Target,
        string Path,
        CropBox? Region = null,
        int? MaxWidth = null,
        int? MaxHeight = null,
        ImageFormat Format = ImageFormat.Png,
        int Quality = 90,
        bool Ocr = false) : Step;

    /// <summary>
    /// A burst of frames written to <paramref name="OutputDir"/> mid-batch.
    /// </summary>
    /// <param name="Background">False runs the full burst before the next step — for watching
    /// the app react to the step before it. True starts the burst and lets the batch continue,
    /// so the frames capture what the following steps do — a drag, an animation being driven.
    /// The batch joins every outstanding burst before returning, and on failure, so the frames
    /// of a batch that threw are still on disk.</param>
    public sealed record Record(
        Frame Target,
        string OutputDir,
        RecordPreset Preset = RecordPreset.Fast,
        bool Background = false,
        CropBox? Region = null,
        int? MaxWidth = null,
        int? MaxHeight = null,
        ImageFormat Format = ImageFormat.Png,
        int Quality = 90) : Step;
}

/// <summary>A file a capture step wrote, with everything needed to click back into it: the img:
/// frame, its rect (origin, size in image pixels, scale), and OCR text when requested.</summary>
public sealed record CapturedImage(
    string Path, string? Image, FrameRect Rect, IReadOnlyList<OcrLine>? Text);

/// <param name="Focus">Whether naming a window or element target brings that window to the
/// foreground before injecting at it. On by default, because keyboard events go to whatever holds
/// the foreground and a batch that names a window means to reach it. Turn it off for input aimed
/// at a background window on purpose — hovering for a tooltip or scrolling a list you are watching
/// while something else stays focused — or to reach a window that refuses activation, since a
/// refused activation fails the whole batch.</param>
public sealed record InputRequest(IReadOnlyList<Step> Steps, bool Focus = true);

/// <param name="Released">Keys and buttons the held-set unwound automatically. Reported rather
/// than cleaned up silently: the unwind is not inert — releasing a dangling 'win' opens the
/// Start menu — so this is how a caller discovers why focus moved.</param>
/// <param name="ReResolved">Elements whose cached reference had died, found again by selector.
/// Reported because re-resolution is a heuristic: it finds an element matching the selector,
/// which is not provably the one that was snapshotted. Re-run snapshot if it matters.</param>
/// <param name="Focused">Windows this batch had to pull to the foreground, in the order it took
/// them. Windows already focused are absent, and repeats are deliberately not collapsed: the same
/// handle twice means something took the foreground back mid-batch, which is the difference
/// between a step that misbehaved and a step whose events went somewhere else entirely. That
/// distinction is invisible in a screenshot of the app you meant to drive.</param>
/// <param name="Captured">Files the batch's capture steps wrote, in step order. Empty when the
/// batch had none.</param>
/// <param name="Recorded">Bursts the batch's record steps wrote, in step order — including
/// background bursts, which are joined before the result is built. Empty when the batch had
/// none.</param>
public sealed record InputResult(
    int EventsSent,
    int Flushes,
    IReadOnlyList<string> Released,
    IReadOnlyList<string> ReResolved,
    IReadOnlyList<long> Focused,
    IReadOnlyList<CapturedImage> Captured,
    IReadOnlyList<RecordResult> Recorded);
