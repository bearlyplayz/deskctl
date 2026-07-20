using WinDeskCtl.Core.Commands;
using WinDeskCtl.Core.Launch;
using WinDeskCtl.Core.Windows;
using WinDeskCtl.Platform.Displays;
using WinDeskCtl.Platform.Interop;
using WinDeskCtl.Platform.Processes;
using WinDeskCtl.Platform.Windows;

namespace WinDeskCtl.Platform.Commands;

/// <summary>
/// Starts a program with its output captured, and waits for the window it opens.
/// </summary>
/// <remarks>
/// A window is found by taking a census of top-level handles before the launch and watching for
/// handles that were not in it, then keeping only those belonging to the launched process or one
/// of its descendants. Newness alone is not enough — two launches overlapping, or an unrelated
/// program starting during a long wait, both produce new windows — and lineage alone is not
/// enough either, because a program's existing windows are also in its process tree.
///
/// The one case lineage cannot cover is hand-off: Chrome, Explorer, and other single-instance
/// programs pass the request to a process that is already running and exit, leaving a window
/// with no relationship to the launched process at all. That path falls back to matching the
/// executable name, and only after the launched process has exited cleanly without producing a
/// window of its own.
/// </remarks>
public sealed class LaunchCommand : ICommand<LaunchInput, LaunchResult>
{
    private const int PollMs = 250;

    public async Task<LaunchResult> RunAsync(LaunchInput input, CancellationToken ct)
    {
        DisplayEnumerator.EnsurePerMonitorV2();

        if (string.IsNullOrWhiteSpace(input.Path))
        {
            throw new ArgumentException("A program to launch is required.", nameof(input));
        }
        if (input.WaitForWindowMs < 0 || input.SettleMs < 0)
        {
            throw new ArgumentException("Wait and settle durations cannot be negative.", nameof(input));
        }
        if (Path.IsPathRooted(input.Path) && !File.Exists(input.Path))
        {
            throw new ArgumentException($"No such program: '{input.Path}'.", nameof(input));
        }

        string logPath = input.LogPath is { Length: > 0 } given
            ? Path.GetFullPath(given)
            : ProcessLauncher.DefaultLogPath(input.Path);

        // Taken before the launch, so the window this is looking for cannot already be in it.
        HashSet<nint> baseline = WindowEnumerator.AllTopLevelHandles();

        using LaunchedProcess process = ProcessLauncher.Start(input, logPath);

        Dictionary<nint, LaunchCandidate> candidates = [];
        string exeName = Path.GetFileNameWithoutExtension(input.Path);

        int? exitCode = null;
        int poll = 0;
        long deadline = Environment.TickCount64 + input.WaitForWindowMs;
        long? settleUntil = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            exitCode ??= process.TryGetExitCode();
            Collect(candidates, baseline, process.ProcessId, exeName, input.ProcessName, exitCode, poll);

            // The settle window opens when the first window appears, not when the wait started:
            // a splash screen that is replaced a moment later would otherwise be the answer.
            if (candidates.Count > 0) settleUntil ??= Environment.TickCount64 + input.SettleMs;

            long now = Environment.TickCount64;
            if (settleUntil is not null && now >= settleUntil) break;

            // A program that failed to start has nothing to wait for, and its log already says
            // why. Waiting out the full timeout would just delay reporting that.
            if (exitCode is not null and not 0 && candidates.Count == 0) break;

            if (now >= deadline) break;

            await Task.Delay(PollMs, ct).ConfigureAwait(false);
            poll++;
        }

        WindowPick pick = WindowChoice.Pick(
            [.. candidates.Values.Where(c => User32.IsWindow((nint)c.Window.Hwnd)).OrderBy(c => c.Seen)],
            input.TitleContains,
            input.ProcessName);

        return new LaunchResult(
            ProcessId: process.ProcessId,
            LogPath: logPath,
            ExitCode: exitCode ?? process.TryGetExitCode(),
            Window: Refresh(pick.Best),
            OtherWindows: [.. pick.Others.Select(Refresh).OfType<WindowInfo>()]);
    }

    /// <summary>
    /// Adds any window that has appeared since the baseline and can be attributed to this launch.
    /// Windows already recorded keep their original poll number, which is what makes the ordering
    /// mean "when it first appeared" rather than "where it currently sits in the z-order".
    /// </summary>
    private static void Collect(
        Dictionary<nint, LaunchCandidate> candidates,
        HashSet<nint> baseline,
        int processId,
        string exeName,
        string? processNameHint,
        int? exitCode,
        int poll)
    {
        // Only consulted once a clean exit has produced no window of its own, so a program that
        // is still running can never have an unrelated same-named window attributed to it.
        // ponytail: in that fallback the executable name is the only evidence left, so a second
        // copy of the same program started elsewhere at the same instant could be picked up. It
        // lands in otherWindows for the caller to reject; a per-machine launch mutex would close
        // it if that ever matters.
        bool handOff = exitCode == 0 && candidates.Count == 0;

        HashSet<int> tree = ProcessTree.DescendantsOf(processId);

        foreach (WindowInfo window in WindowEnumerator.List(includeMinimized: true))
        {
            nint hwnd = (nint)window.Hwnd;
            if (baseline.Contains(hwnd) || candidates.ContainsKey(hwnd)) continue;

            bool ours = tree.Contains(window.ProcessId)
                || (handOff && MatchesName(window.ProcessName, processNameHint, exeName));

            if (!ours) continue;

            candidates[hwnd] = new LaunchCandidate(window, WindowEnumerator.HasCaption(hwnd), poll);
        }
    }

    private static bool MatchesName(string processName, string? hint, string exeName)
    {
        string wanted = hint is { Length: > 0 } ? hint : exeName;
        return wanted.Length > 0 && processName.Equals(wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Re-reads a window's geometry at the moment of reporting. A window found early in the wait
    /// is usually still being positioned and sized, so the rect recorded when it first appeared
    /// is not the one a caller is about to click into.
    /// </summary>
    private static WindowInfo? Refresh(WindowInfo? window)
    {
        if (window is null) return null;

        try
        {
            return WindowEnumerator.Describe((nint)window.Hwnd);
        }
        catch (InvalidOperationException)
        {
            // Closed between the pick and the read. Reporting it as it last was would be worse
            // than not reporting it.
            return null;
        }
    }
}
