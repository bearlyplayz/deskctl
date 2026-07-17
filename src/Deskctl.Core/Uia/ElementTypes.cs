using Deskctl.Core.Frames;

namespace Deskctl.Core.Uia;

/// <summary>
/// How to find an element again after its cached reference dies.
/// </summary>
/// <param name="Ancestry">
/// The chain from the snapshot root down to the element's parent, each entry
/// "controlType:name". Included because controlType+name alone is not unique in a real UI —
/// several panes may each hold a "Save" button — and matching the wrong one is worse than
/// failing.
/// </param>
public sealed record ElementSelector(
    string ControlType,
    string? Name,
    string? AutomationId,
    IReadOnlyList<string> Ancestry);

/// <summary>Whether an element came from its cached reference or had to be found again.</summary>
public enum Resolution
{
    Cached,

    /// <summary>
    /// The cached reference died and the selector found the element again. Reported to the caller
    /// because re-resolution is a heuristic: it finds an element matching the selector, which is
    /// not provably the same element that was snapshotted.
    /// </summary>
    ReResolved,
}

/// <param name="Handle">Opaque, session-scoped, minted by Snapshot. Never a RuntimeId — those
/// churn when the tree rebuilds. Callers must not construct these.</param>
/// <param name="Rect">The element's own frame. Its origin is absolute screen coordinates, which
/// is what UIA reports and what a FrameRect origin already means — no conversion applies.</param>
/// <param name="Patterns">What can be done with it: "invoke", "value", "toggle", ...</param>
public sealed record ElementNode(
    string Handle,
    string ControlType,
    string Name,
    string? AutomationId,
    FrameRect Rect,
    bool IsEnabled,
    bool IsOffscreen,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<ElementNode> Children);

/// <param name="Target">"win:&lt;hwnd&gt;" or "elem:&lt;handle&gt;" to scope to a subtree.</param>
/// <param name="InteractiveOnly">Keep only elements that can be acted on, plus the containers
/// needed to reach them. A raw tree is mostly layout scaffolding.</param>
public sealed record SnapshotInput(
    string Target,
    int MaxDepth = 12,
    bool Vision = false,
    bool InteractiveOnly = true);

/// <param name="Truncated">True when MaxDepth or the element cap cut the walk short. Reported so
/// a caller does not read a partial tree as a complete one.</param>
public sealed record SnapshotResult(
    FrameRect Rect,
    ElementNode? Root,
    int ElementCount,
    bool Truncated);
