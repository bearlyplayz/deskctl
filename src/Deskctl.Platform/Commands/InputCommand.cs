using System.Runtime.InteropServices;
using Deskctl.Core.Commands;
using Deskctl.Core.Frames;
using Deskctl.Core.Input;
using Deskctl.Core.Uia;
using Deskctl.Platform.Displays;
using Deskctl.Platform.Input;
using Deskctl.Platform.Interop;
using Deskctl.Platform.Uia;
using Deskctl.Platform.Windows;

namespace Deskctl.Platform.Commands;

/// <summary>
/// The unified input primitive. One tool subsuming the ten a conventional surface exposes
/// separately, because SendInput's native shape is an array and its atomicity guarantee is what
/// makes cross-device combos and real drags expressible at all.
/// </summary>
public sealed class InputCommand : ICommand<InputRequest, InputResult>
{
    public async Task<InputResult> RunAsync(InputRequest request, CancellationToken ct)
    {
        DisplayEnumerator.EnsurePerMonitorV2();
        ExitGuard.Arm();

        FrameRect bounds = DisplayEnumerator.GetVirtualBounds();
        Plan plan = BatchPlanner.Plan(request.Steps);

        int sent = 0, flushes = 0;
        List<string> reResolved = [];

        try
        {
            foreach (PlannedOp op in plan.Ops)
            {
                ct.ThrowIfCancellationRequested();

                switch (op)
                {
                    case PlannedOp.Send send:
                        // Mirror into the exit guard BEFORE sending: a crash between the send and the
                        // bookkeeping would otherwise leave a key held with nothing tracking it. The
                        // superset of everything the array downs is tracked, then narrowed afterwards
                        // to what the array actually leaves held — computing the net effect in step
                        // order, because an array may release and re-hold the same target
                        // ([up ctrl, down ctrl] ends holding ctrl) and a track-all-then-untrack-all
                        // pass would untrack it while it is still physically down.
                        List<InputTarget> touched = [.. send.Steps
                            .Select(s => s switch
                            {
                                Step.Down d => d.Target,
                                Step.Press p => p.Target,
                                _ => null,
                            })
                            .OfType<InputTarget>()];

                        foreach (InputTarget t in touched) ExitGuard.Track(t);

                        sent += InputSender.Send(send.Steps, bounds, s => Resolve(s, bounds, reResolved));

                        IReadOnlyList<InputTarget> stillHeld = HeldSet.NetHeld(send.Steps);
                        foreach (InputTarget t in touched)
                        {
                            if (!stillHeld.Contains(t)) ExitGuard.Untrack(t);
                        }

                        flushes++;
                        break;

                    case PlannedOp.Wait wait:
                        await Task.Delay(wait.Duration, ct);
                        break;

                    case PlannedOp.Semantic { Step: Step.Move { Over: not null } move }:
                        sent += await MoveOverTimeAsync(move, bounds, reResolved, ct);
                        flushes++;
                        break;

                    case PlannedOp.Semantic { Step: Step.Invoke invoke }:
                        if (await SemanticActions.InvokeAsync(HandleOf(invoke.Target), ct) == Resolution.ReResolved)
                        {
                            reResolved.Add(invoke.Target);
                        }
                        flushes++;
                        break;

                    case PlannedOp.Semantic { Step: Step.Fill fill }:
                        if (await SemanticActions.FillAsync(HandleOf(fill.Target), fill.Value, ct) == Resolution.ReResolved)
                        {
                            reResolved.Add(fill.Target);
                        }
                        flushes++;
                        break;

                    case PlannedOp.Semantic { Step: Step.WaitFor waitFor }:
                        if (await SemanticActions.WaitForAsync(HandleOf(waitFor.Target), waitFor.Timeout, ct)
                            == Resolution.ReResolved)
                        {
                            reResolved.Add(waitFor.Target);
                        }
                        flushes++;
                        break;

                    default:
                        throw new InvalidOperationException($"Unhandled op {op.GetType().Name}.");
                }
            }
        }
        catch (Exception ex)
        {
            // A batch is a transaction: it unwinds on failure as well as success.
            // The planner's unwind is the batch's final op, so a throw part-way through the plan
            // would otherwise skip it and strand a modifier or — far worse — a mouse button down
            // for the rest of the process, which under stdio is the whole client session.
            //
            // This is necessarily a separate SendInput call: already-flushed events cannot be
            // recalled. The guard is the authority on what is genuinely still
            // held, since the plan describes what should have happened, not what did.
            IReadOnlyList<InputTarget> released = ExitGuard.ReleaseAll();
            if (released.Count == 0) throw;

            // Rule 4: report the unwind. There is no InputResult on this path, so the exception
            // is the only channel left to tell the caller what was released and why.
            throw new InvalidOperationException(
                $"{ex.Message} Auto-released after the failure, newest first: " +
                $"{string.Join(", ", released.Select(Describe))}.", ex);
        }

        // Distinct: a batch may touch one element several times, and the caller's question is
        // which elements were found again rather than how often.
        return new InputResult(sent, flushes, [.. plan.AutoReleased.Select(Describe)], [.. reResolved.Distinct()]);
    }

    /// <summary>Extracts the handle from an "elem:&lt;handle&gt;" reference. Semantic steps act on
    /// elements only — a coordinate has nothing to invoke.</summary>
    private static string HandleOf(string reference)
    {
        Frame frame = Frame.Parse(reference);
        return frame is Frame.Element e
            ? e.Handle
            : throw new ArgumentException(
                $"'{reference}' is not an element. invoke/fill/waitFor act on 'elem:<handle>' from a snapshot.",
                nameof(reference));
    }

    /// <summary>
    /// Walks the pointer along an interpolated path. Each sample is its own SendInput call
    /// because the delays between them are the point — a drag needs the app to observe motion,
    /// and a single array would deliver every sample at once, which is a teleport with extra
    /// steps.
    /// </summary>
    private static async Task<int> MoveOverTimeAsync(
        Step.Move move, FrameRect bounds, List<string> reResolved, CancellationToken ct)
    {
        Point target = Resolve(move.To, bounds, reResolved);

        if (!Cursor.GetCursorPos(out Cursor.POINT current))
        {
            throw new InvalidOperationException("GetCursorPos failed; cannot interpolate from an unknown position.");
        }

        // GetCursorPos reports absolute screen coordinates, which are not virtual-frame
        // coordinates on any desktop whose origin is negative. ScreenCoords is the only crossing.
        Point from = ScreenCoords.FromScreen(current.X, current.Y, bounds);

        int sent = 0;
        foreach ((Point at, TimeSpan delay) in Interpolate.Path(from, target, move.Over!.Value, move.Ease))
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);

            // The sample is already a virtual-frame point, so the resolver hands it straight
            // back rather than round-tripping it through the reference syntax.
            sent += InputSender.Send([new Step.Move(at.ToString())], bounds, _ => at);
        }

        return sent;
    }

    /// <summary>Resolves a frame-qualified reference to a virtual-desktop point.</summary>
    /// <param name="reResolved">
    /// Collects references whose element had to be found again rather than remembered. Aiming a
    /// coordinate step at an element re-resolves exactly as a semantic step does, and carries the
    /// same twin-survivor hazard — so it is reported on the same channel.
    /// </param>
    private static Point Resolve(string reference, FrameRect bounds, List<string> reResolved)
    {
        (Frame frame, int x, int y) = PointRef.Parse(reference);
        FrameRect rect = RectFor(frame, bounds, reResolved);

        if (PointRef.IsCentre((frame, x, y)))
        {
            x = rect.W / 2;
            y = rect.H / 2;
        }

        return Translate.To(new Point(frame, x, y), rect, bounds);
    }

    private static FrameRect RectFor(Frame frame, FrameRect bounds, List<string> reResolved) => frame switch
    {
        Frame.Virtual => bounds,
        Frame.Monitor m => DisplayEnumerator.GetMonitors().FirstOrDefault(x => x.Id == m.Id)?.Bounds
            ?? throw new ArgumentException($"No monitor with id '{m.Id}'."),
        Frame.Window w => WindowGeometry.GetRect((nint)w.Hwnd),
        Frame.Element e => ElementRectOf(e, reResolved),
        _ => throw new ArgumentOutOfRangeException(nameof(frame)),
    };

    /// <summary>
    /// The live bounds of a snapshotted element, so a coordinate step can aim at one:
    /// "press left at elem:btn-save" targets its centre.
    /// </summary>
    /// <remarks>
    /// Read fresh rather than taken from the snapshot — the element may have moved or scrolled
    /// since, and a stale rect is how a click lands on whatever took its place.
    /// </remarks>
    private static FrameRect ElementRectOf(Frame.Element e, List<string> reResolved)
    {
        (nint abi, Resolution resolution) = HandleRegistry.Resolve(e.Handle);
        if (resolution == Resolution.ReResolved) reResolved.Add(e.ToString());
        try
        {
            return ElementRect.Of(abi, e);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    private static string Describe(InputTarget t) => t switch
    {
        KeyRef k => k.Name,
        ButtonRef b => b.Button.ToString().ToLowerInvariant(),
        _ => t.ToString() ?? "?",
    };
}
