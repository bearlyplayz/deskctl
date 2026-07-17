using Deskctl.Core.Input;

namespace Deskctl.Core.Tests;

public class BatchPlannerTests
{
    private static Step Down(string key) => new Step.Down(new KeyRef(key));
    private static Step Up(string key) => new Step.Up(new KeyRef(key));
    private static Step Press(string key) => new Step.Press(new KeyRef(key));
    private static Step DownBtn(MouseButton b) => new Step.Down(new ButtonRef(b));
    private static Step UpBtn(MouseButton b) => new Step.Up(new ButtonRef(b));

    private static string[] Released(Plan p) =>
        [.. p.AutoReleased.Select(t => t switch
        {
            KeyRef k => k.Name,
            ButtonRef b => b.Button.ToString().ToLowerInvariant(),
            _ => throw new InvalidOperationException(),
        })];

    [Fact]
    public void ZeroDelaySteps_CoalesceIntoOneSend()
    {
        // SendInput takes an array and guarantees the events are not interspersed with other
        // input. One send is one atomic gesture.
        Plan p = BatchPlanner.Plan([Down("ctrl"), Press("c"), Up("ctrl")]);

        PlannedOp.Send send = Assert.IsType<PlannedOp.Send>(Assert.Single(p.Ops));
        Assert.Equal(3, send.Steps.Count);
        Assert.False(send.IsUnwind);
        Assert.Empty(p.AutoReleased);
    }

    [Fact]
    public void ADelayForcesAFlush()
    {
        // The explicit trade of atomicity for real elapsed time.
        Plan p = BatchPlanner.Plan([
            Down("ctrl"),
            new Step.Delay(TimeSpan.FromMilliseconds(50)),
            Up("ctrl"),
        ]);

        Assert.Collection(p.Ops,
            op => Assert.IsType<PlannedOp.Send>(op),
            op => Assert.Equal(TimeSpan.FromMilliseconds(50), Assert.IsType<PlannedOp.Wait>(op).Duration),
            op => Assert.IsType<PlannedOp.Send>(op));
    }

    [Fact]
    public void ATimedMoveForcesAFlush()
    {
        Plan p = BatchPlanner.Plan([
            DownBtn(MouseButton.Left),
            new Step.Move("monitor:1@400,200", TimeSpan.FromMilliseconds(250)),
            UpBtn(MouseButton.Left),
        ]);

        Assert.True(p.Ops.Count > 1, "A timed move cannot join a zero-delay array.");
        Assert.Empty(p.AutoReleased);
    }

    [Fact]
    public void AnInstantMoveDoesNotFlush()
    {
        Plan p = BatchPlanner.Plan([new Step.Move("monitor:1@10,10"), Press("a")]);

        PlannedOp.Send send = Assert.IsType<PlannedOp.Send>(Assert.Single(p.Ops));
        Assert.Equal(2, send.Steps.Count);
    }

    [Fact]
    public void SemanticStepsAreTheirOwnFlushBoundary()
    {
        // invoke/fill/waitFor are COM calls and cannot join a SendInput array, but they mix
        // freely with synthetic steps.
        Plan p = BatchPlanner.Plan([
            Down("ctrl"),
            new Step.Invoke("elem:btn-save"),
            Up("ctrl"),
        ]);

        Assert.Collection(p.Ops,
            op => Assert.IsType<PlannedOp.Send>(op),
            op => Assert.IsType<Step.Invoke>(Assert.IsType<PlannedOp.Semantic>(op).Step),
            op => Assert.IsType<PlannedOp.Send>(op));
    }

    [Fact]
    public void AForgottenDown_IsUnwoundAtTheEnd()
    {
        Plan p = BatchPlanner.Plan([Down("ctrl"), Press("c")]);   // no up ctrl

        Assert.Equal(["ctrl"], Released(p));

        PlannedOp.Send send = Assert.IsType<PlannedOp.Send>(Assert.Single(p.Ops));
        Assert.Equal(3, send.Steps.Count);                        // down, press, and the appended up
        Assert.Equal(new Step.Up(new KeyRef("ctrl")), send.Steps[^1]);
    }

    [Fact]
    public void TheUnwindJoinsTheFinalSend_WhenNoTimedFlushIntervenes()
    {
        // A well-formed batch and an auto-unwound one emit IDENTICAL syscalls.
        Plan forgotten = BatchPlanner.Plan([Down("ctrl"), Press("c")]);
        Plan wellFormed = BatchPlanner.Plan([Down("ctrl"), Press("c"), Up("ctrl")]);

        PlannedOp.Send a = Assert.IsType<PlannedOp.Send>(Assert.Single(forgotten.Ops));
        PlannedOp.Send b = Assert.IsType<PlannedOp.Send>(Assert.Single(wellFormed.Ops));

        Assert.Equal(b.Steps, a.Steps);
    }

    [Fact]
    public void TheUnwindIsLifo()
    {
        Plan p = BatchPlanner.Plan([Down("ctrl"), Down("shift"), DownBtn(MouseButton.Left)]);
        Assert.Equal(["left", "shift", "ctrl"], Released(p));
    }

    [Fact]
    public void TheUnwindAfterATimedFlush_IsItsOwnSend()
    {
        Plan p = BatchPlanner.Plan([
            Down("ctrl"),
            new Step.Delay(TimeSpan.FromMilliseconds(50)),
        ]);

        Assert.Equal(["ctrl"], Released(p));

        PlannedOp.Send last = Assert.IsType<PlannedOp.Send>(p.Ops[^1]);
        Assert.True(last.IsUnwind);
        Assert.Equal(new Step.Up(new KeyRef("ctrl")), Assert.Single(last.Steps));
    }

    [Fact]
    public void CanonicalSequence_PlansAsOneSend_WithTheUnwindAppended()
    {
        // The canonical fixture. All steps are zero-delay, so the whole sequence
        // INCLUDING the unwind coalesces into one atomic SendInput array.
        Plan p = BatchPlanner.Plan([
            Down("ctrl"), Down("c"), Up("c"), Up("ctrl"),
            Down("ctrl"), Down("shift"), Press("v"),
            Down("alt"), Down("win"), Up("alt"),
        ]);

        Assert.Equal(["win", "shift", "ctrl"], Released(p));

        PlannedOp.Send send = Assert.IsType<PlannedOp.Send>(Assert.Single(p.Ops));
        Assert.Equal(13, send.Steps.Count);   // 10 given + 3 unwind

        Assert.Equal(new Step.Up(new KeyRef("win")), send.Steps[^3]);
        Assert.Equal(new Step.Up(new KeyRef("shift")), send.Steps[^2]);
        Assert.Equal(new Step.Up(new KeyRef("ctrl")), send.Steps[^1]);
    }

    [Fact]
    public void AnUpWithNoDown_IsPassedThrough_ButNotCountedAsReleased()
    {
        // Honoured as explicit caller intent (clearing a stray), but it was never ours, so it
        // must not appear in the report.
        Plan p = BatchPlanner.Plan([Up("alt")]);

        Assert.Empty(p.AutoReleased);
        Assert.Single(Assert.IsType<PlannedOp.Send>(Assert.Single(p.Ops)).Steps);
    }

    [Fact]
    public void AnEmptyBatch_PlansNothing()
    {
        Plan p = BatchPlanner.Plan([]);
        Assert.Empty(p.Ops);
        Assert.Empty(p.AutoReleased);
    }
}
