using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using WinDeskCtl.Core.Uia;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Uia;

/// <summary>
/// Maps element handles to live UIA elements, with a re-resolution path for when they die.
/// </summary>
/// <remarks>
/// UIA RuntimeIds are not stable: they churn when the tree rebuilds on navigation or
/// virtualization, and elements are destroyed outright. A raw RuntimeId in a handle would go
/// stale between snapshot and use and reintroduce exactly the race this design removes
///.
///
/// So each handle carries both a cached COM reference (the fast path) and a selector (the
/// recovery path). Handles live for the process lifetime, which under stdio is the client
/// session.
/// </remarks>
public static class HandleRegistry
{
    /// <param name="ScopeAbi">The snapshot root. Re-resolution searches within it rather than the
    /// whole desktop, so a selector cannot match an identically-named element in another app.</param>
    /// <param name="MaxDepth">The depth the snapshot walked. Re-resolution re-walks with the same
    /// depth because a selector's ancestry is relative to the walk that produced it — a re-walk
    /// of a different depth would compare ancestries that mean different things.</param>
    private sealed record Entry(ElementSelector Selector, nint Abi, nint ScopeAbi, int MaxDepth);

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Entry> Entries = [];

    /// <summary>
    /// Takes its own reference on everything it keeps. The caller retains ownership of the
    /// references it passed in and must release them — without this the snapshot root would be
    /// released twice, once by the registry and once by the command that created it.
    /// </summary>
    public static void Register(IEnumerable<WalkedElement> elements, nint scopeAbi, int maxDepth)
    {
        lock (Gate)
        {
            foreach (WalkedElement e in elements)
            {
                Marshal.AddRef(e.Abi);
                Marshal.AddRef(scopeAbi);

                // A re-snapshot re-mints the same handle for the same element. Release the old
                // entry's references or every snapshot leaks the entire previous tree. The new
                // references are taken first: the old entry may hold the very same pointers, and
                // releasing those to zero before re-taking them would free a live element.
                if (Entries.TryGetValue(e.Node.Handle, out Entry? existing))
                {
                    Marshal.Release(existing.Abi);
                    Marshal.Release(existing.ScopeAbi);
                }

                Entries[e.Node.Handle] = new Entry(e.Selector, e.Abi, scopeAbi, maxDepth);
            }
        }
    }

    /// <summary>
    /// Resolves a handle to a live element, trying the cache first.
    /// </summary>
    /// <returns>The element, which the caller owns and must release, and how it was found. A
    /// caller that gets <see cref="Resolution.ReResolved"/> is being told the original element
    /// died and this one merely matches its selector.</returns>
    public static (nint Abi, Resolution How) Resolve(string handle)
    {
        Entry entry;
        lock (Gate)
        {
            if (!Entries.TryGetValue(handle, out Entry? found))
            {
                throw new ArgumentException(
                    $"No element '{handle}' in this session. Take a snapshot first; handles are minted by " +
                    "snapshot and must not be constructed by hand.", nameof(handle));
            }
            entry = found;
        }

        if (IsAlive(entry.Abi))
        {
            Marshal.AddRef(entry.Abi);
            return (entry.Abi, Resolution.Cached);
        }

        // Arrives with one reference, owned by this call.
        nint revived = ReResolve(entry);

        lock (Gate)
        {
            // Only replace the entry this call actually read. A concurrent resolve of the same
            // handle may have revived it already, and releasing the old pointer a second time
            // would underflow its refcount.
            if (Entries.TryGetValue(handle, out Entry? current) && current.Abi == entry.Abi)
            {
                Marshal.Release(entry.Abi);
                Entries[handle] = entry with { Abi = revived };

                // The registry has taken the reference ReResolve returned, so the caller needs
                // one of its own. Where the entry was not replaced, the caller simply keeps that
                // original reference and nothing further is owed.
                Marshal.AddRef(revived);
            }
        }

        return (revived, Resolution.ReResolved);
    }

    /// <summary>
    /// The snapshot root a handle was minted under, without taking a reference on it.
    /// </summary>
    /// <remarks>
    /// For reading properties of the scope itself — its native window handle, say. The pointer is
    /// valid only while the entry lives, which is the process lifetime, so it is safe to read but
    /// must not be stored or released. Returns 0 for an unknown handle rather than throwing: a
    /// caller asking which window owns an element is doing so to improve on a best effort, not to
    /// act on the answer.
    /// </remarks>
    public static nint ScopeOf(string handle)
    {
        lock (Gate)
        {
            return Entries.TryGetValue(handle, out Entry? entry) ? entry.ScopeAbi : 0;
        }
    }

    /// <summary>
    /// Probes the cached reference with a cheap property read. UIA signals a dead element by
    /// throwing from any call, so there is no "is alive" to ask. Any COM failure counts as dead:
    /// re-resolution will either find the element or fail loudly, which beats acting on a
    /// reference that just failed to answer.
    /// </summary>
    private static bool IsAlive(nint abi)
    {
        try
        {
            Wrap(abi).get_CurrentControlType(out _);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the element again by selector, within the original snapshot's scope.
    /// </summary>
    /// <remarks>
    /// Fails loudly on zero matches AND on more than one. Acting on the wrong element is worse
    /// than not acting: an invoke on the wrong button is not recoverable by the caller, whereas
    /// an error is.
    ///
    /// Re-walks with the tree walker rather than issuing a UIA FindAll, because a selector
    /// matches on ancestry and UIA cannot express ancestry as a condition at all. The walk
    /// already computes each element's ancestry, so matching here is a comparison rather than a
    /// second traversal strategy that would have to agree with the first.
    /// </remarks>
    private static nint ReResolve(Entry entry)
    {
        // interactiveOnly is off: the filter cannot change any element's ancestry, and walking
        // everything guarantees the target is in the result even if the original snapshot's
        // filter would now drop it.
        (_, List<WalkedElement> flat, _) = TreeWalker.Walk(entry.ScopeAbi, entry.MaxDepth, interactiveOnly: false);

        try
        {
            List<WalkedElement> matches = [.. flat.Where(w => Matches(w.Selector, entry.Selector))];

            if (matches.Count == 1)
            {
                nint match = matches[0].Abi;
                Marshal.AddRef(match);
                return match;
            }

            string describe = Describe(entry.Selector);

            throw new InvalidOperationException(matches.Count == 0
                ? $"Element {describe} no longer exists and could not be found again. Re-run snapshot."
                : $"Element {describe} matched {matches.Count} elements after its reference went stale. " +
                  "Refusing to guess — re-run snapshot to get unambiguous handles.");
        }
        finally
        {
            // The walk takes a reference per element it produces, except the root: it is handed
            // in, so the caller's reference is reused rather than duplicated. Releasing it here
            // would drop a reference this method never took — the registry owns the scope for the
            // entry's whole lifetime, and underflowing it frees an element still being used.
            foreach (WalkedElement w in flat)
            {
                if (w.Abi != entry.ScopeAbi) Marshal.Release(w.Abi);
            }
        }
    }

    /// <summary>
    /// Compares selectors field by field. Not record equality: ElementSelector's Ancestry is an
    /// IReadOnlyList, which a record compares by reference, so two structurally identical
    /// selectors are never equal.
    /// </summary>
    private static bool Matches(ElementSelector candidate, ElementSelector wanted)
    {
        if (candidate.ControlType != wanted.ControlType) return false;
        if (!candidate.Ancestry.SequenceEqual(wanted.Ancestry)) return false;

        // Every recorded field must agree: the selector describes the element that was
        // snapshotted, and a candidate differing in any of them is a different element. Matching
        // on automationId alone would accept a twin whose name has since changed, which is the
        // silent wrong-element action rule 4 exists to prevent.
        //
        // Absent fields are not constraints. UIA leaves name or automationId empty on plenty of
        // real elements, and treating empty as "must be empty" is a constraint the snapshot never
        // observed — but ControlType and Ancestry always carry, so a selector is never vacuous.
        if (wanted.AutomationId is { Length: > 0 } id && candidate.AutomationId != id) return false;
        if (wanted.Name is { Length: > 0 } name && candidate.Name != name) return false;

        return true;
    }

    /// <summary>
    /// Renders the whole selector. The error is the caller's only view of why a re-resolution
    /// failed, so it names every field that was matched on rather than the first that was set.
    /// </summary>
    private static string Describe(ElementSelector s)
    {
        List<string> fields = [s.ControlType];
        if (s.Name is { Length: > 0 } name) fields.Add($"name='{name}'");
        if (s.AutomationId is { Length: > 0 } id) fields.Add($"automationId='{id}'");

        string what = string.Join(" ", fields);
        return s.Ancestry.Count > 0 ? $"{what} under {string.Join(" > ", s.Ancestry)}" : what;
    }

    private static unsafe IUIAutomationElement Wrap(nint abi) =>
        ComInterfaceMarshaller<IUIAutomationElement>.ConvertToManaged((void*)abi)!;
}
