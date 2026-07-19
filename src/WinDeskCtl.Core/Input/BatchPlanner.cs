namespace WinDeskCtl.Core.Input;

/// <summary>One unit of execution. The adapter walks these and does exactly what each says.</summary>
public abstract record PlannedOp
{
    private protected PlannedOp() { }

    /// <param name="IsUnwind">True when this send exists only to release held state after a
    /// timed flush already committed the events it is cleaning up after.</param>
    public sealed record Send(IReadOnlyList<Step> Steps, bool IsUnwind = false) : PlannedOp;

    public sealed record Wait(TimeSpan Duration) : PlannedOp;

    /// <summary>A step the sender cannot express as one entry in a SendInput array: a UIA call,
    /// or a move whose delays between samples are the point.</summary>
    public sealed record Semantic(Step Step) : PlannedOp;
}

public sealed record Plan(IReadOnlyList<PlannedOp> Ops, IReadOnlyList<InputTarget> AutoReleased);

/// <summary>
/// Groups steps into atomic sends and appends the held-set unwind.
/// </summary>
/// <remarks>
/// The unwind is planned here rather than executed in a finally block in the adapter. Where no
/// timed flush intervenes, the unwind joins the batch's final array, so a well-formed batch and
/// an auto-unwound one emit identical syscalls. A finally block could only ever emit a separate
/// call, which would break that equivalence.
/// </remarks>
public static class BatchPlanner
{
    public static Plan Plan(IReadOnlyList<Step> steps)
    {
        List<PlannedOp> ops = [];
        List<Step> pending = [];
        HeldSet held = new();

        void Flush(bool isUnwind = false)
        {
            if (pending.Count == 0) return;
            ops.Add(new PlannedOp.Send([.. pending], isUnwind));
            pending.Clear();
        }

        foreach (Step step in steps)
        {
            switch (step)
            {
                case Step.Down:
                case Step.Up:
                case Step.Press:
                    held.Apply(step);
                    pending.Add(step);
                    break;

                // A timed move trades atomicity for real elapsed time, so it cannot share an
                // array with what surrounds it.
                case Step.Move { Over: not null }:
                case Step.Invoke:
                case Step.Fill:
                case Step.WaitFor:
                    Flush();
                    ops.Add(new PlannedOp.Semantic(step));
                    break;

                case Step.Delay delay:
                    Flush();
                    ops.Add(new PlannedOp.Wait(delay.Duration));
                    break;

                default:
                    pending.Add(step);
                    break;
            }
        }

        IReadOnlyList<InputTarget> released = held.UnwindOrder();

        foreach (InputTarget target in released)
        {
            pending.Add(new Step.Up(target));
        }

        // isUnwind is true only when the unwind could not join a preceding send — i.e. when
        // everything before it was already flushed. That distinction is what the report needs
        // to explain why events arrived in two calls rather than one.
        Flush(isUnwind: released.Count > 0 && pending.Count == released.Count);

        return new Plan(ops, released);
    }
}
