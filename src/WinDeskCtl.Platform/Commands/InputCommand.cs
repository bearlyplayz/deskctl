using System.Runtime.InteropServices;
using WinDeskCtl.Core.Commands;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Input;
using WinDeskCtl.Core.Uia;
using WinDeskCtl.Platform.Displays;
using WinDeskCtl.Platform.Input;
using WinDeskCtl.Platform.Interop;
using WinDeskCtl.Platform.Uia;
using WinDeskCtl.Platform.Windows;

namespace WinDeskCtl.Platform.Commands;

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
        FocusState focus = new(request.Focus);

        try
        {
            foreach (PlannedOp op in plan.Ops)
            {
                ct.ThrowIfCancellationRequested();

                switch (op)
                {
                    case PlannedOp.Send send:
                        // Steps that name no frame — text, and bare key presses — resolve nothing,
                        // so the foreground they land in is whatever the previous op left. Between
                        // two ops the desktop has had real elapsed time to change it: a delay, a
                        // timed drag, or a waitFor is exactly long enough for a notification to
                        // take the foreground, and the batch would then type into it. Re-asserting
                        // the last window this batch focused costs one GetForegroundWindow when
                        // nothing moved.
                        focus.Reassert();

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

                        sent += InputSender.Send(send.Steps, bounds, s => Resolve(s, bounds, reResolved, focus));

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
                        sent += await MoveOverTimeAsync(move, bounds, reResolved, focus, ct);
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
        return new InputResult(
            sent, flushes, [.. plan.AutoReleased.Select(Describe)], [.. reResolved.Distinct()], focus.Taken);
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
        Step.Move move, FrameRect bounds, List<string> reResolved, FocusState focus, CancellationToken ct)
    {
        Point target = Resolve(move.To, bounds, reResolved, focus);

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
    private static Point Resolve(string reference, FrameRect bounds, List<string> reResolved, FocusState focus)
    {
        (Frame frame, int x, int y) = PointRef.Parse(reference);
        FrameRect rect = RectFor(frame, bounds, reResolved, focus);

        if (PointRef.IsCentre((frame, x, y)))
        {
            x = rect.W / 2;
            y = rect.H / 2;
        }

        return Translate.To(new Point(frame, x, y), rect, bounds);
    }

    /// <summary>
    /// The frame's live bounds, focusing its owning window on the way when the frame names one.
    /// </summary>
    /// <remarks>
    /// Focusing here rather than in a separate pass is what makes it total: this is the one place
    /// a frame-qualified reference becomes a rect, so every injecting step — move, press with a
    /// "to", scroll with an "at", and every sample of a timed move — routes through it and cannot
    /// be forgotten. Naming a window is taken as intent to interact with it; keyboard events go to
    /// whatever holds the foreground, so without this a batch that names a background window types
    /// into whatever the user was last using.
    ///
    /// ponytail: within one coalesced SendInput array the focus of every step happens before any
    /// event is dispatched, so a batch naming two windows leaves the last one focused. Mouse
    /// events still route by cursor position and land correctly; keyboard events do not. Splitting
    /// the array on a window change would fix it — put a delay between the two windows' steps
    /// until then.
    /// </remarks>
    private static FrameRect RectFor(Frame frame, FrameRect bounds, List<string> reResolved, FocusState focus)
    {
        switch (frame)
        {
            case Frame.Virtual:
                return bounds;

            case Frame.Monitor m:
                return DisplayEnumerator.GetMonitors().FirstOrDefault(x => x.Id == m.Id)?.Bounds
                    ?? throw new ArgumentException($"No monitor with id '{m.Id}'.");

            case Frame.Window w:
                focus.Take((nint)w.Hwnd);
                return WindowGeometry.GetRect((nint)w.Hwnd);

            case Frame.Element e:
                return ElementRectOf(e, reResolved, focus);

            default:
                throw new ArgumentOutOfRangeException(nameof(frame));
        }
    }

    /// <summary>
    /// The live bounds of a snapshotted element, so a coordinate step can aim at one:
    /// "press left at elem:btn-save" targets its centre.
    /// </summary>
    /// <remarks>
    /// Read fresh rather than taken from the snapshot — the element may have moved or scrolled
    /// since, and a stale rect is how a click lands on whatever took its place. The owning window
    /// is focused before the rect is read, not after: activation can move or restore the window,
    /// and a rect read beforehand would describe where the element used to be.
    /// </remarks>
    private static FrameRect ElementRectOf(Frame.Element e, List<string> reResolved, FocusState focus)
    {
        (nint abi, Resolution resolution) = HandleRegistry.Resolve(e.Handle);
        if (resolution == Resolution.ReResolved) reResolved.Add(e.ToString());
        try
        {
            focus.Take(ElementWindow.OwnerOf(e.Handle, abi));

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
