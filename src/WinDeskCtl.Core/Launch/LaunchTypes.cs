using WinDeskCtl.Core.Windows;

namespace WinDeskCtl.Core.Launch;

/// <param name="Path">The executable. A rooted path is used as-is; a bare name is resolved
/// against PATH.</param>
/// <param name="Arguments">Passed to the program verbatim — each element becomes one argv entry,
/// quoted on the way into the single command line Win32 actually takes.</param>
/// <param name="Environment">Additional <c>KEY=VALUE</c> assignments, layered over the current
/// environment rather than replacing it. A replacement block would strip PATH and SystemRoot,
/// which many programs will not start without.</param>
/// <param name="WorkingDirectory">Defaults to the executable's own directory when
/// <paramref name="Path"/> is rooted, because programs that read config relative to the
/// working directory otherwise fail depending on where windeskctl happened to be run from.</param>
/// <param name="WaitForWindowMs">How long to keep looking for the program's window. Reaching
/// this is not a failure — the result simply carries no window.</param>
/// <param name="SettleMs">How long to keep watching after the first candidate window appears,
/// so a splash screen that hands off to the real window is not reported as the answer.</param>
/// <param name="TitleContains">A hint, not a filter. Ranks a matching window first; a window is
/// still returned when nothing matches, because a near-miss guess should not cost the caller
/// the window entirely.</param>
/// <param name="ProcessName">A second hint on the same terms. Earns its keep when the launched
/// process hands off to a pre-existing one and there is no lineage left to follow.</param>
public sealed record LaunchInput(
    string Path,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyList<string>? Environment = null,
    string? WorkingDirectory = null,
    string? LogPath = null,
    bool AppendLog = false,
    int WaitForWindowMs = 60_000,
    int SettleMs = 1_000,
    string? TitleContains = null,
    string? ProcessName = null);

/// <summary>
/// The outcome of a launch. Exact about the process, best-effort about the window.
/// </summary>
/// <remarks>
/// <paramref name="ProcessId"/> and <paramref name="LogPath"/> are always right.
/// <paramref name="Window"/> is a convenience that saves a window listing in the common case and
/// is null whenever the heuristics found nothing to report — a program that never opened a
/// window, one that outlived the wait, or one whose window could not be tied back to it.
/// A null window is not an error: the log path is how a launch that went wrong gets diagnosed,
/// so it is returned rather than thrown away.
/// </remarks>
/// <param name="ExitCode">Null while the process is still running. A non-null zero usually means
/// the program handed off to another process rather than that it did nothing.</param>
/// <param name="OtherWindows">Every other window attributed to the launch, newest last. The
/// escape hatch for when the ranking picked wrong: the alternatives are already here, so the
/// caller does not have to go looking for them.</param>
public sealed record LaunchResult(
    int ProcessId,
    string LogPath,
    int? ExitCode,
    WindowInfo? Window,
    IReadOnlyList<WindowInfo> OtherWindows);
