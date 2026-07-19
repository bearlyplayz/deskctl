using WinDeskCtl.Core.Input;

namespace WinDeskCtl.Core.Tests;

public class HeldSetTests
{
    private static KeyRef K(string n) => new(n);
    private static ButtonRef B(MouseButton b) => new(b);

    private static string[] Names(IReadOnlyList<InputTarget> targets) =>
        [.. targets.Select(t => t switch
        {
            KeyRef k => k.Name,
            ButtonRef b => b.Button.ToString().ToLowerInvariant(),
            _ => throw new InvalidOperationException(),
        })];

    [Fact]
    public void Down_AddsToTheSet()
    {
        HeldSet h = new();
        h.Down(K("ctrl"));
        Assert.Equal(["ctrl"], Names(h.UnwindOrder()));
    }

    [Fact]
    public void Down_Twice_IsANoOp_AndKeepsTheOriginalPosition()
    {
        // Windows auto-repeat legitimately re-sends downs; a duplicate must not reorder the set
        // or the unwind order silently changes.
        HeldSet h = new();
        h.Down(K("ctrl"));
        h.Down(K("shift"));
        h.Down(K("ctrl"));

        Assert.Equal(2, h.Count);
        Assert.Equal(["shift", "ctrl"], Names(h.UnwindOrder()));   // ctrl is still the OLDEST
    }

    [Fact]
    public void Up_RemovesFromTheMiddle_NotJustTheEnd()
    {
        HeldSet h = new();
        h.Down(K("ctrl"));
        h.Down(K("shift"));
        h.Up(K("ctrl"));

        Assert.Equal(["shift"], Names(h.UnwindOrder()));
    }

    [Fact]
    public void Up_WithNoMatchingDown_IsHonouredButDoesNotAffectTheSet()
    {
        // An explicit up clears a stray from a previous session. It was never ours to track,
        // so it must not appear in the unwind.
        HeldSet h = new();
        h.Up(K("alt"));
        Assert.True(h.IsEmpty);
    }

    [Fact]
    public void Press_NeverLingers()
    {
        HeldSet h = new();
        h.Down(K("ctrl"));
        h.Press(K("v"));
        Assert.Equal(["ctrl"], Names(h.UnwindOrder()));
    }

    [Fact]
    public void UnwindOrder_IsLifo()
    {
        HeldSet h = new();
        h.Down(K("ctrl"));
        h.Down(K("shift"));
        Assert.Equal(["shift", "ctrl"], Names(h.UnwindOrder()));
    }

    [Fact]
    public void UnwindOrder_LifoKeepsCtrlHeldUntilTheButtonIsReleased()
    {
        // The case that costs data. Explorer reads ctrl+drag as COPY and a bare drag as MOVE.
        // An oldest-first unwind drops ctrl while the button is still down, so the app sees a
        // coherent gesture with the WRONG meaning and files get relocated instead of duplicated
        //.
        HeldSet h = new();
        h.Down(K("ctrl"));
        h.Down(K("shift"));
        h.Down(B(MouseButton.Left));

        string[] order = Names(h.UnwindOrder());

        Assert.Equal(["left", "shift", "ctrl"], order);
        Assert.True(
            Array.IndexOf(order, "left") < Array.IndexOf(order, "ctrl"),
            "The mouse button must be released BEFORE ctrl, or a copy silently becomes a move.");
    }

    [Fact]
    public void MouseAndKeyboardShareOneSet()
    {
        // The set is ordered by insertion across both devices — a cross-device gesture unwinds
        // in the order a human would release it.
        HeldSet h = new();
        h.Down(K("shift"));
        h.Down(B(MouseButton.Left));
        h.Down(K("alt"));

        Assert.Equal(["alt", "left", "shift"], Names(h.UnwindOrder()));
    }

    [Fact]
    public void SameNamedKeyAndButton_AreDistinct()
    {
        // 'left' the arrow key and 'left' the mouse button are different things held at once.
        HeldSet h = new();
        h.Down(K("left"));
        h.Down(B(MouseButton.Left));

        Assert.Equal(2, h.Count);
        h.Up(K("left"));
        Assert.Equal(["left"], Names(h.UnwindOrder()));
        Assert.IsType<ButtonRef>(h.UnwindOrder()[0]);
    }

    /// <summary>
    /// The canonical fixture. It exercises every unwind rule in one sequence: mid-sequence
    /// removal, full drain to empty, re-add after drain, press not lingering, and removal from
    /// the middle of a four-element set.
    /// </summary>
    [Fact]
    public void CanonicalSequence_UnwindsToWinShiftCtrl()
    {
        HeldSet h = new();

        h.Down(K("ctrl"));                                        // [ctrl]
        h.Down(K("c"));                                           // [ctrl, c]
        Assert.Equal(["c", "ctrl"], Names(h.UnwindOrder()));

        h.Up(K("c"));                                             // [ctrl]
        Assert.Equal(["ctrl"], Names(h.UnwindOrder()));

        h.Up(K("ctrl"));                                          // []
        Assert.True(h.IsEmpty);

        h.Down(K("ctrl"));                                        // [ctrl]
        h.Down(K("shift"));                                       // [ctrl, shift]
        h.Press(K("v"));                                          // [ctrl, shift]  — press never lingers
        Assert.Equal(["shift", "ctrl"], Names(h.UnwindOrder()));

        h.Down(K("alt"));                                         // [ctrl, shift, alt]
        h.Down(K("win"));                                         // [ctrl, shift, alt, win]
        h.Up(K("alt"));                                           // [ctrl, shift, win]  — mid-list removal

        Assert.Equal(["win", "shift", "ctrl"], Names(h.UnwindOrder()));
    }

    /// <summary>
    /// The case that defeats partitioning a sequence into its downs and its ups: the same target
    /// is released and then re-held, so only an in-order evaluation reports it still held. A
    /// caller that untracked every up would stop tracking a key that is physically down.
    /// </summary>
    [Fact]
    public void NetHeld_ReHoldingAfterARelease_IsStillHeld()
    {
        Assert.Equal(["ctrl"], Names(HeldSet.NetHeld([new Step.Up(K("ctrl")), new Step.Down(K("ctrl"))])));
    }

    [Fact]
    public void NetHeld_ReleasingAfterAHold_IsNotHeld()
    {
        Assert.Empty(HeldSet.NetHeld([new Step.Down(K("ctrl")), new Step.Up(K("ctrl"))]));
    }

    [Fact]
    public void NetHeld_ReportsNewestFirst()
    {
        Assert.Equal(
            ["left", "shift"],
            Names(HeldSet.NetHeld([new Step.Down(K("shift")), new Step.Down(B(MouseButton.Left))])));
    }

    [Fact]
    public void NetHeld_APressHoldsNothing()
    {
        Assert.Empty(HeldSet.NetHeld([new Step.Press(K("v"))]));
    }

    [Fact]
    public void NetHeld_IgnoresStepsThatHoldNothing()
    {
        Assert.Empty(HeldSet.NetHeld([new Step.Text("hello"), new Step.Scroll(0, -3, null)]));
    }
}
