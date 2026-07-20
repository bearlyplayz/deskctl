using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Launch;
using WinDeskCtl.Core.Windows;

namespace WinDeskCtl.Core.Tests;

public class WindowChoiceTests
{
    private static LaunchCandidate Candidate(long hwnd, string title, string process, bool caption, int seen) =>
        new(
            new WindowInfo(
                hwnd, title, process, 1,
                new FrameRect(Frame.Parse($"win:{hwnd}"), 0, 0, 800, 600),
                WindowState.Normal, IsForeground: false),
            caption,
            seen);

    [Fact]
    public void NoCandidates_PicksNothing()
    {
        WindowPick pick = WindowChoice.Pick([], null, null);

        Assert.Null(pick.Best);
        Assert.Empty(pick.Others);
    }

    [Fact]
    public void CaptionedWindowBeatsACaptionlessSplash()
    {
        WindowPick pick = WindowChoice.Pick(
            [
                Candidate(1, "Loading", "app", caption: false, seen: 0),
                Candidate(2, "App", "app", caption: true, seen: 0),
            ],
            null,
            null);

        Assert.Equal(2, pick.Best!.Hwnd);
    }

    [Fact]
    public void TitleHintOutranksTheCaptionHeuristic()
    {
        // A caller that names the window it wants knows more than any guess made here — even when
        // the window it names is the one the heuristic would have discarded.
        WindowPick pick = WindowChoice.Pick(
            [
                Candidate(1, "Document - App", "app", caption: false, seen: 0),
                Candidate(2, "App", "app", caption: true, seen: 0),
            ],
            "Document",
            null);

        Assert.Equal(1, pick.Best!.Hwnd);
    }

    [Fact]
    public void TitleHintOutranksTheProcessHint()
    {
        WindowPick pick = WindowChoice.Pick(
            [
                Candidate(1, "Wanted", "other", caption: false, seen: 0),
                Candidate(2, "Something else", "app", caption: false, seen: 0),
            ],
            "Wanted",
            "app");

        Assert.Equal(1, pick.Best!.Hwnd);
    }

    [Fact]
    public void UnmatchedTitleHintStillReturnsAWindow()
    {
        // The hint ranks rather than filters, so a guess that is merely close — a localized
        // title, a version suffix — costs nothing.
        WindowPick pick = WindowChoice.Pick(
            [Candidate(1, "Notepad", "notepad", caption: true, seen: 0)],
            "Editor",
            null);

        Assert.Equal(1, pick.Best!.Hwnd);
    }

    [Fact]
    public void NewerWindowWinsATie()
    {
        WindowPick pick = WindowChoice.Pick(
            [
                Candidate(1, "App", "app", caption: true, seen: 0),
                Candidate(2, "App", "app", caption: true, seen: 3),
            ],
            null,
            null);

        Assert.Equal(2, pick.Best!.Hwnd);
    }

    [Fact]
    public void EveryLoserIsReportedAsAnAlternative()
    {
        // The escape hatch for a wrong pick: the alternatives come back with the answer, so the
        // caller never has to go looking for them.
        WindowPick pick = WindowChoice.Pick(
            [
                Candidate(1, "Splash", "app", caption: false, seen: 0),
                Candidate(2, "App", "app", caption: true, seen: 1),
                Candidate(3, "Tip", "app", caption: false, seen: 1),
            ],
            null,
            null);

        Assert.Equal(2, pick.Best!.Hwnd);
        Assert.Equal([3L, 1L], [.. pick.Others.Select(w => w.Hwnd)]);
    }

    [Fact]
    public void TitleMatchIsCaseInsensitive()
    {
        WindowPick pick = WindowChoice.Pick(
            [
                Candidate(1, "Untitled - NOTEPAD", "notepad", caption: true, seen: 0),
                Candidate(2, "Other", "notepad", caption: true, seen: 1),
            ],
            "notepad",
            null);

        Assert.Equal(1, pick.Best!.Hwnd);
    }
}
