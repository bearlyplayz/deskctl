using System.Runtime.InteropServices;
using WinDeskCtl.Core.Commands;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Windows;
using WinDeskCtl.Platform.Displays;
using WinDeskCtl.Platform.Interop;
using WinDeskCtl.Platform.Windows;

namespace WinDeskCtl.Platform.Commands;

public sealed class WindowListCommand : ICommand<WindowListInput, WindowListResult>
{
    public Task<WindowListResult> RunAsync(WindowListInput input, CancellationToken ct)
    {
        IEnumerable<WindowInfo> windows = WindowEnumerator.List(input.IncludeMinimized);

        if (input.TitleContains is { Length: > 0 } title)
        {
            windows = windows.Where(w => w.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
        }
        if (input.ProcessName is { Length: > 0 } process)
        {
            windows = windows.Where(w => w.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(new WindowListResult([.. windows]));
    }
}

/// <summary>
/// Focus, move, resize, and state changes. Every action re-reads the window afterwards rather
/// than reporting what was asked for: Windows clamps, snaps, and sometimes refuses outright, so
/// the request is not the outcome.
/// </summary>
public sealed class WindowActionCommand : ICommand<WindowActionInput, WindowActionResult>
{
    public Task<WindowActionResult> RunAsync(WindowActionInput input, CancellationToken ct)
    {
        DisplayEnumerator.EnsurePerMonitorV2();

        nint hwnd = (nint)input.Hwnd;
        if (!User32.IsWindow(hwnd))
        {
            throw new ArgumentException(
                $"No window with handle {input.Hwnd}. List windows to get current handles.", nameof(input));
        }

        switch (input.Action)
        {
            case WindowAction.Focus:
                Focus(hwnd);
                break;

            case WindowAction.Move:
                MoveOrResize(hwnd, input, resize: false);
                break;

            case WindowAction.Resize:
                MoveOrResize(hwnd, input, resize: true);
                break;

            case WindowAction.Minimize:
                User32.ShowWindow(hwnd, User32.SW_MINIMIZE);
                break;

            case WindowAction.Maximize:
                User32.ShowWindow(hwnd, User32.SW_MAXIMIZE);
                break;

            case WindowAction.Restore:
                User32.ShowWindow(hwnd, User32.SW_RESTORE);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(input), $"Unhandled action '{input.Action}'.");
        }

        return Task.FromResult(new WindowActionResult(WindowEnumerator.Describe(hwnd)));
    }

    /// <summary>
    /// Brings a window to the foreground.
    /// </summary>
    /// <remarks>
    /// Three escalating attempts, because Windows actively resists a background process taking
    /// the foreground and each lever fails under different conditions. Success is confirmed by
    /// reading the foreground back after every attempt, never by trusting a return value:
    /// SetForegroundWindow returns TRUE under the foreground lock while doing nothing but flashing
    /// the taskbar button.
    ///
    /// 1. SetForegroundWindow alone. Succeeds when windeskctl already owns the foreground.
    /// 2. The same call with THIS thread attached to the foreground thread's input queue, which is
    ///    one of the documented ways a process earns the right to set the foreground. It must be
    ///    the calling thread that attaches — it is windeskctl's privilege that is lacking, so
    ///    attaching two other threads to each other changes nothing.
    /// 3. A minimize/restore round trip. When the foreground lock timeout has been raised — any
    ///    running app may raise it process-wide via SPI_SETFOREGROUNDLOCKTIMEOUT, and games and
    ///    streaming tools routinely do — steps 1 and 2 both fail outright. Restoring a window from
    ///    minimized makes the SYSTEM activate it, which is not subject to that lock. SW_RESTORE
    ///    returns a window to its previous size and position, so a maximized window stays
    ///    maximized. This is a visible flicker, hence a last resort rather than the first move.
    ///
    /// Rewriting the user's foreground-lock setting is deliberately not among these: it is a
    /// machine-wide anti-focus-stealing policy, and a tool that silently disables it to get its
    /// way leaves the desktop worse than it found it.
    /// </remarks>
    private static void Focus(nint hwnd)
    {
        // A minimized window cannot take focus. The restore also activates it, which is often
        // enough on its own.
        if (User32.IsIconic(hwnd)) User32.ShowWindow(hwnd, User32.SW_RESTORE);

        User32.SetForegroundWindow(hwnd);
        if (BecameForeground(hwnd)) return;

        nint foreground = User32.GetForegroundWindow();
        if (foreground == 0)
        {
            throw new InvalidOperationException($"Could not focus window {hwnd}: no foreground window to attach to.");
        }

        uint thisThread = Kernel32.GetCurrentThreadId();
        uint foregroundThread = User32.GetWindowThreadProcessId(foreground, out _);

        if (thisThread != foregroundThread)
        {
            User32.AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                User32.SetForegroundWindow(hwnd);
            }
            finally
            {
                User32.AttachThreadInput(thisThread, foregroundThread, false);
            }

            if (BecameForeground(hwnd)) return;
        }

        User32.ShowWindow(hwnd, User32.SW_MINIMIZE);
        User32.ShowWindow(hwnd, User32.SW_RESTORE);

        if (!BecameForeground(hwnd))
        {
            throw new InvalidOperationException(
                $"Could not focus window {hwnd}. It may be running elevated, which UIPI blocks " +
                "windeskctl from reaching, or the desktop may be showing a full-screen exclusive app.");
        }
    }

    /// <summary>How long to wait for an activation request to be honoured before calling it failed.</summary>
    /// <remarks>
    /// Activation is asynchronous across processes: SetForegroundWindow and ShowWindow post to the
    /// target's message queue and return before the foreground has changed, so reading the
    /// foreground back immediately reports the OLD window and mis-diagnoses a success as a failure.
    /// The budget is a ceiling, not a delay — the poll exits as soon as the switch lands.
    /// </remarks>
    private static bool BecameForeground(nint hwnd)
    {
        const int budgetMs = 600;
        const int pollMs = 20;

        for (int waited = 0; waited < budgetMs; waited += pollMs)
        {
            if (User32.GetForegroundWindow() == hwnd) return true;
            Thread.Sleep(pollMs);
        }

        return User32.GetForegroundWindow() == hwnd;
    }

    /// <summary>
    /// Places the window's VISIBLE edges where asked.
    /// </summary>
    /// <remarks>
    /// The conversion is the point. Callers think in the rect they can see, but SetWindowPos
    /// consumes GetWindowRect's space, which is larger by the invisible border. Passing a visible
    /// rect straight through misplaces the window by ~7px every time.
    ///
    /// A maximized window is restored first: SetWindowPos on a maximized window either does
    /// nothing or leaves it sized normally but still flagged maximized. The geometry is read
    /// after that restore, because a maximized window's rect is not the one being adjusted.
    /// </remarks>
    private static void MoveOrResize(nint hwnd, WindowActionInput input, bool resize)
    {
        if (User32.IsIconic(hwnd) || User32.IsZoomed(hwnd))
        {
            User32.ShowWindow(hwnd, User32.SW_RESTORE);
        }

        FrameRect visible = WindowGeometry.GetRect(hwnd);
        BorderDelta delta = WindowGeometry.GetBorderDelta(hwnd);

        // The rect is already absolute screen coordinates, the space SetWindowPos speaks, so the
        // only conversion needed is out of visible space into raw space.
        FrameRect wanted = visible with
        {
            OriginX = input.X ?? visible.OriginX,
            OriginY = input.Y ?? visible.OriginY,
            W = input.W ?? visible.W,
            H = input.H ?? visible.H,
        };

        if (wanted.W <= 0 || wanted.H <= 0)
        {
            throw new ArgumentException($"A window must have positive size; got {wanted.W}x{wanted.H}.", nameof(input));
        }

        FrameRect raw = WindowFrame.VisibleToRaw(wanted, delta);

        uint flags = User32.SWP_NOZORDER | User32.SWP_NOACTIVATE;
        if (!resize && input.W is null && input.H is null) flags |= User32.SWP_NOSIZE;
        if (resize && input.X is null && input.Y is null) flags |= User32.SWP_NOMOVE;

        if (!User32.SetWindowPos(hwnd, 0, raw.OriginX, raw.OriginY, raw.W, raw.H, flags))
        {
            int err = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"SetWindowPos failed for window {hwnd} (error {err}). " +
                "It may be running elevated, which UIPI blocks windeskctl from reaching.");
        }
    }
}
