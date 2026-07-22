using System.Collections.Concurrent;
using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Platform.Capture;

/// <summary>
/// Maps img: frame handles to the rect a capture was taken with, so a point read off the image
/// resolves through the ordinary translation path — scale included — instead of the caller doing
/// the arithmetic. The rect describes where the target was at capture time; a window that moves
/// afterwards makes the frame stale, exactly as a stale element handle would be.
/// </summary>
/// <remarks>
/// Handles live for the process lifetime, which under stdio is the client session. A CLI run is
/// one process per command, so a handle printed there cannot be clicked by a later run — the
/// rect printed alongside is the cross-process form of the same information.
/// </remarks>
public static class ImageFrames
{
    /// <param name="Hwnd">The captured window, or 0 for a monitor capture. Kept so input aimed
    /// at the image can focus the window it shows, matching how a win: target behaves.</param>
    private sealed record Entry(FrameRect Rect, nint Hwnd);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();
    private static int _next;

    /// <summary>
    /// Mints a handle for a capture's final rect and returns the full "img:&lt;handle&gt;" frame
    /// string. The registered rect's frame is the img: frame itself, which is what lets
    /// Translate treat a coordinate read off the image as native to it.
    /// </summary>
    public static string Mint(FrameRect final, nint hwnd)
    {
        string handle = Interlocked.Increment(ref _next).ToString();
        Frame.Image frame = new(handle);
        Entries[handle] = new Entry(final with { Frame = frame }, hwnd);
        return frame.ToString();
    }

    /// <returns>The capture's rect, in the img: frame's own space, and the captured window
    /// (0 for a monitor capture).</returns>
    public static (FrameRect Rect, nint Hwnd) Resolve(string handle)
    {
        if (!Entries.TryGetValue(handle, out Entry? entry))
        {
            throw new ArgumentException(
                $"No image '{handle}' in this session. img: handles are minted by capture and die " +
                "with the process — take a capture first, and do not construct them by hand.",
                nameof(handle));
        }

        return (entry.Rect, entry.Hwnd);
    }
}
