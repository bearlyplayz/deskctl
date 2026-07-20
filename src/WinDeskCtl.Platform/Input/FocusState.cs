using WinDeskCtl.Platform.Windows;

namespace WinDeskCtl.Platform.Input;

/// <summary>
/// One batch's foreground bookkeeping: which window its steps are aimed at, and which windows it
/// actually had to activate.
/// </summary>
/// <remarks>
/// A batch's steps do not all name a frame. <c>text</c> and bare key presses name nothing at all,
/// so they land in whatever holds the foreground when they are flushed — and between two flushes
/// the desktop has had real elapsed time to change that. <see cref="Reassert"/> closes the window
/// by re-taking the last target before each send; <see cref="Take"/> records the target as steps
/// name it.
///
/// Not thread-safe, and does not need to be: one instance belongs to one <c>RunAsync</c> call,
/// which is sequential.
/// </remarks>
internal sealed class FocusState(bool enabled)
{
    private readonly List<long> _taken = [];
    private nint _target;

    /// <summary>Windows pulled to the foreground, in order, repeats intact.</summary>
    internal IReadOnlyList<long> Taken => _taken;

    /// <summary>
    /// Focuses <paramref name="hwnd"/> and remembers it as the batch's current target. A zero
    /// handle means the frame named no window — a monitor or the virtual desktop, or an element
    /// whose owner could not be determined — and leaves the previous target in place rather than
    /// clearing it, since a batch that has established a window should keep asserting it.
    /// </summary>
    internal void Take(nint hwnd)
    {
        if (!enabled || hwnd == 0) return;

        _target = hwnd;
        if (WindowFocus.Ensure(hwnd)) _taken.Add(hwnd);
    }

    /// <summary>
    /// Re-takes the current target if something else has stolen the foreground since. A no-op
    /// before any step has named a window, which is what leaves a batch of pure keystrokes aimed
    /// at whatever the user had focused.
    /// </summary>
    internal void Reassert()
    {
        if (!enabled || _target == 0) return;

        if (WindowFocus.Ensure(_target)) _taken.Add(_target);
    }
}
