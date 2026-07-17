namespace Deskctl.Core.Input;

/// <summary>
/// Tracks what this process is holding down, so a batch cannot leak global input state.
/// </summary>
/// <remarks>
/// A batch is a transaction. A stuck ctrl makes the desktop misbehave; a stuck left mouse button
/// makes it unusable, since every subsequent motion becomes a drag and only a physical click
/// recovers it. An LLM composing scripts will forget an up, so this cannot rely on caller
/// discipline.
///
/// Only what this process injected is tracked. Never blanket-clear via GetAsyncKeyState: it
/// cannot distinguish injected from physical input, so clearing indiscriminately fights the
/// user's real keyboard.
///
/// A List rather than a HashSet: insertion order IS the contract, and the set is never larger
/// than a handful of entries, so linear removal is faster than the hashing it would replace.
/// </remarks>
public sealed class HeldSet
{
    private readonly List<InputTarget> _held = [];

    public int Count => _held.Count;

    public bool IsEmpty => _held.Count == 0;

    /// <summary>Records a hold. A repeat is a no-op that keeps the original position — Windows
    /// auto-repeat legitimately re-sends downs, and reordering on each would corrupt the unwind
    /// order.</summary>
    public void Down(InputTarget target)
    {
        if (!_held.Contains(target)) _held.Add(target);
    }

    /// <summary>Records a release, from any position. An up with no matching down is honoured by
    /// the caller as explicit intent but changes nothing here — it was never ours.</summary>
    public void Up(InputTarget target) => _held.Remove(target);

    public void Press(InputTarget target)
    {
        Down(target);
        Up(target);
    }

    /// <summary>
    /// Applies a step's effect on what is held. Steps that hold nothing (moves, scrolls, text,
    /// semantic actions) leave the set untouched.
    /// </summary>
    public void Apply(Step step)
    {
        switch (step)
        {
            case Step.Down d: Down(d.Target); break;
            case Step.Up u: Up(u.Target); break;
            case Step.Press p: Press(p.Target); break;
        }
    }

    /// <summary>
    /// What a step sequence leaves held, newest first — its net effect, evaluated in order.
    /// Order matters: <c>[up ctrl, down ctrl]</c> ends holding ctrl, so a caller cannot
    /// determine the outcome by partitioning the sequence into downs and ups.
    /// </summary>
    public static IReadOnlyList<InputTarget> NetHeld(IEnumerable<Step> steps)
    {
        HeldSet net = new();
        foreach (Step s in steps) net.Apply(s);
        return net.UnwindOrder();
    }

    /// <summary>
    /// What remains, newest first. LIFO is a correctness requirement: given
    /// [down ctrl, down shift, down left], releasing oldest-first drops ctrl while the button is
    /// still down, and Explorer reads the result as a move rather than a copy — the app sees a
    /// coherent gesture with the wrong meaning and files are relocated instead of duplicated.
    /// LIFO mirrors human release order and preserves the gesture's semantics through teardown.
    /// </summary>
    public IReadOnlyList<InputTarget> UnwindOrder()
    {
        List<InputTarget> reversed = [.. _held];
        reversed.Reverse();
        return reversed;
    }
}
