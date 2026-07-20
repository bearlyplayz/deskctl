using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Windows;

/// <summary>
/// Brings a window to the foreground. Shared by the explicit <c>windows focus</c> action and by
/// the input path, which focuses a target window before injecting at it.
/// </summary>
public static class WindowFocus
{
    /// <summary>
    /// Focuses <paramref name="hwnd"/> unless it already holds the foreground.
    /// </summary>
    /// <returns>True when the foreground had to be taken, false when the window already held it.
    /// Callers report the true cases: a window that has to be re-taken during one batch is being
    /// fought over by something else on the desktop.</returns>
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
    public static bool Ensure(nint hwnd)
    {
        // A minimized window cannot take focus. The restore also activates it, which is often
        // enough on its own.
        //
        // Tested before the already-foreground shortcut, not after: GetForegroundWindow keeps
        // naming a window that has just been minimized, so a shortcut that trusted it alone would
        // return "already focused" for a window sitting off-screen at -32000,-32000. Callers that
        // read geometry afterwards then get the minimized rect, and the point they aim at is
        // outside the virtual desktop entirely.
        bool restored = User32.IsIconic(hwnd);
        if (restored) User32.ShowWindow(hwnd, User32.SW_RESTORE);

        if (!restored && User32.GetForegroundWindow() == hwnd) return false;

        User32.SetForegroundWindow(hwnd);
        if (BecameForeground(hwnd)) return true;

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

            if (BecameForeground(hwnd)) return true;
        }

        User32.ShowWindow(hwnd, User32.SW_MINIMIZE);
        User32.ShowWindow(hwnd, User32.SW_RESTORE);

        if (!BecameForeground(hwnd))
        {
            throw new InvalidOperationException(
                $"Could not focus window {hwnd}. It may be running elevated, which UIPI blocks " +
                "windeskctl from reaching, or the desktop may be showing a full-screen exclusive app.");
        }

        return true;
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
}
