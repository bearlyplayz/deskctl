using System.Runtime.InteropServices;
using Deskctl.Core.Input;
using Deskctl.Platform.Displays;

namespace Deskctl.Platform.Input;

/// <summary>
/// Releases anything still held when the process ends, however it ends.
/// </summary>
/// <remarks>
/// The planner's unwind covers a batch that completes. This covers the batch that does not: an
/// unhandled exception, a closed stdin, or a SIGTERM mid-drag would otherwise leave a mouse
/// button down, at which point every subsequent motion is a drag and only a physical click
/// recovers the desktop.
///
/// A hard kill is the one uncoverable case — no handler runs. doctor's stuck-modifiers check
/// exists to surface a session that died that way; it reports rather than releases, since
/// GetAsyncKeyState cannot tell a stuck key from one the user is holding.
/// </remarks>
public static class ExitGuard
{
    private static readonly Lock Gate = new();
    private static readonly HeldSet Held = new();
    private static bool _armed;

    /// <summary>Holds the SIGTERM registration for the process's lifetime. Without a live
    /// reference the registration is finalized and the handler silently stops firing.</summary>
    private static PosixSignalRegistration? _sigterm;

    public static void Arm()
    {
        lock (Gate)
        {
            if (_armed) return;
            _armed = true;

            AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseAll();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseAll();
            Console.CancelKeyPress += (_, e) =>
            {
                ReleaseAll();
                // Let the default handler terminate: swallowing Ctrl+C would leave the process
                // alive after the user asked it to stop.
                e.Cancel = false;
            };
            _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => ReleaseAll());
        }
    }

    public static void Track(InputTarget t)
    {
        lock (Gate) Held.Down(t);
    }

    public static void Untrack(InputTarget t)
    {
        lock (Gate) Held.Up(t);
    }

    /// <summary>
    /// Releases everything this process injected, newest first. Only what we injected: a blanket
    /// clear via GetAsyncKeyState cannot tell injected from physical and would fight the user's
    /// real keyboard.
    /// </summary>
    public static IReadOnlyList<InputTarget> ReleaseAll()
    {
        List<Step> ups;
        IReadOnlyList<InputTarget> released;
        lock (Gate)
        {
            if (Held.IsEmpty) return [];
            released = Held.UnwindOrder();
            ups = [.. released.Select(t => (Step)new Step.Up(t))];
            foreach (InputTarget t in released) Held.Up(t);
        }

        try
        {
            InputSender.Send(ups, DisplayEnumerator.GetVirtualBounds(), _ =>
                throw new InvalidOperationException("An exit release never moves the pointer."));
        }
        catch (Exception)
        {
            // Best effort. This runs during teardown, where throwing achieves nothing and can
            // mask the original fault that caused the exit.
        }

        return released;
    }
}
