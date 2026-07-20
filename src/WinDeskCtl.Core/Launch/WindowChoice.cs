using WinDeskCtl.Core.Windows;

namespace WinDeskCtl.Core.Launch;

/// <param name="HasCaption">Whether the window has a title bar. Splash screens, toasts, and
/// tooltips are usually captionless popups, so this separates a program's real window from the
/// scaffolding it puts on screen while starting.</param>
/// <param name="Seen">Which poll first observed the window. Higher is newer — the only ordering
/// available, since window enumeration reports z-order rather than creation order.</param>
public sealed record LaunchCandidate(WindowInfo Window, bool HasCaption, int Seen);

/// <summary>Which window a launch reports, and the ones it passed over.</summary>
public sealed record WindowPick(WindowInfo? Best, IReadOnlyList<WindowInfo> Others);

/// <summary>
/// Ranks the windows a launch turned up.
/// </summary>
/// <remarks>
/// No rule distinguishes a program's main window from its splash screen in the general case —
/// both are visible, titled, top-level windows from the same process. So the hints rank rather
/// than filter: a caller whose <c>titleContains</c> guess is slightly off (a localized title, a
/// version suffix, a document name prefixed to the app name) still gets a window back instead
/// of nothing, and every window that lost is returned alongside for it to pick from.
///
/// This is deliberately the whole of the cleverness. When ranking picks wrong the answer is a
/// <c>titleContains</c> hint or a window listing, not another rule here.
/// </remarks>
public static class WindowChoice
{
    public static WindowPick Pick(
        IReadOnlyList<LaunchCandidate> candidates,
        string? titleContains,
        string? processName)
    {
        if (candidates.Count == 0) return new WindowPick(null, []);

        List<LaunchCandidate> ranked = [.. candidates
            .OrderByDescending(c => Score(c, titleContains, processName))
            .ThenByDescending(c => c.Seen)];

        return new WindowPick(ranked[0].Window, [.. ranked.Skip(1).Select(c => c.Window)]);
    }

    /// <summary>
    /// An explicit hint outranks a weaker one, and any hint outranks the caption heuristic — a
    /// caller that names the window it wants has better information than any guess made here.
    /// </summary>
    private static int Score(LaunchCandidate candidate, string? titleContains, string? processName)
    {
        int score = 0;

        if (titleContains is { Length: > 0 } title
            && candidate.Window.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (processName is { Length: > 0 } process
            && candidate.Window.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (candidate.HasCaption) score += 1;

        return score;
    }
}
