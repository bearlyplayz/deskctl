using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Deskctl.Core.Frames;
using Deskctl.Core.Uia;
using Deskctl.Platform.Interop;

namespace Deskctl.Platform.Uia;

/// <param name="Abi">The live COM reference, cached for the fast path. Owned by the registry,
/// which releases it.</param>
public sealed record WalkedElement(ElementNode Node, ElementSelector Selector, nint Abi);

/// <summary>
/// Walks a UIA subtree into a serializable shape.
/// </summary>
/// <remarks>
/// This is what an HWND-only tool cannot do. EnumChildWindows reports window class names, and a
/// modern XAML/Chromium/Electron UI is one opaque host HWND — so it yields nothing usable no
/// matter how well it is written. The same window returns a full tree of named, invokable
/// elements here.
/// </remarks>
public static class TreeWalker
{
    /// <summary>
    /// Caps the walk regardless of depth. A Chromium document can hold tens of thousands of
    /// elements; serializing them all would produce a payload far larger than the screenshot the
    /// tree exists to replace.
    /// </summary>
    private const int MaxElements = 1500;

    /// <summary>
    /// Types worth keeping for their own sake. Disjoint from <see cref="ContainerTypes"/> by
    /// construction: a type is either something to act on or something to look inside.
    /// </summary>
    private static readonly HashSet<string> InteractiveTypes =
    [
        "button", "checkbox", "combobox", "edit", "hyperlink", "listitem", "menuitem",
        "radiobutton", "slider", "splitbutton", "tabitem", "treeitem", "spinner", "dataitem",
    ];

    public static (ElementNode? Root, List<WalkedElement> Flat, bool Truncated) Walk(
        nint rootAbi, int maxDepth, bool interactiveOnly)
    {
        HandleMinter minter = new();
        List<WalkedElement> flat = [];
        bool truncated = false;

        ElementNode? root = Visit(rootAbi, depth: 0, ancestry: [], own: false);

        return (root, flat, truncated);

        ElementNode? Visit(nint abi, int depth, List<string> ancestry, bool own)
        {
            if (flat.Count >= MaxElements)
            {
                truncated = true;
                if (own) Marshal.Release(abi);
                return null;
            }

            IUIAutomationElement element = Wrap(abi);

            string controlType, name, automationId;
            bool enabled, offscreen;
            User32.RECT rect;

            try
            {
                element.get_CurrentControlType(out int controlTypeId);
                controlType = UiaIds.ControlTypeName(controlTypeId);
                name = element.get_CurrentName() ?? "";
                automationId = element.get_CurrentAutomationId() ?? "";
                element.get_CurrentIsEnabled(out int isEnabled);
                element.get_CurrentIsOffscreen(out int isOffscreen);
                element.get_CurrentBoundingRectangle(out rect);
                enabled = isEnabled != 0;
                offscreen = isOffscreen != 0;
            }
            catch (COMException)
            {
                // The element died mid-walk — normal in a live UI. Drop it rather than failing
                // the whole snapshot.
                if (own) Marshal.Release(abi);
                return null;
            }

            List<string> patterns = SupportedPatterns(element);

            List<ElementNode> children = [];
            if (depth < maxDepth)
            {
                List<string> childAncestry = [.. ancestry, $"{controlType}:{name}"];
                foreach (nint childAbi in Children(element))
                {
                    ElementNode? child = Visit(childAbi, depth + 1, childAncestry, own: true);
                    if (child is not null) children.Add(child);
                }
            }
            else if (HasChildren(element))
            {
                truncated = true;
            }

            // An element with nothing to act on and nothing beneath it is layout scaffolding:
            // dropping it is the difference between a readable tree and a wall of nested panes.
            //
            // The walk root is exempt. It is the tree's identity and frame, and the filter
            // cascades: a window whose interactive content sits below maxDepth loses every
            // descendant and then itself, reporting "no elements" for a window that is merely
            // deeper than the walk went. That reads as "what you wanted does not exist" — the
            // precise misreading Truncated exists to prevent.
            bool keep = depth == 0
                || !interactiveOnly
                || InteractiveTypes.Contains(controlType)
                || patterns.Count > 0
                || children.Count > 0;

            if (!keep)
            {
                if (own) Marshal.Release(abi);
                return null;
            }

            string handle = minter.Mint(controlType, name, automationId);

            ElementNode node = new(
                Handle: handle,
                ControlType: controlType,
                Name: name,
                AutomationId: automationId.Length == 0 ? null : automationId,
                // UIA reports absolute screen pixels with absolute far edges, which is already
                // what a FrameRect origin means — so the origin is copied, never converted, and
                // the size is a subtraction. Converting here would double-count the origin.
                Rect: new FrameRect(
                    new Frame.Element(handle),
                    OriginX: rect.Left,
                    OriginY: rect.Top,
                    W: rect.Right - rect.Left,
                    H: rect.Bottom - rect.Top),
                IsEnabled: enabled,
                IsOffscreen: offscreen,
                Patterns: patterns,
                Children: children);

            flat.Add(new WalkedElement(
                node,
                new ElementSelector(
                    controlType,
                    name.Length == 0 ? null : name,
                    automationId.Length == 0 ? null : automationId,
                    ancestry),
                abi));

            return node;
        }
    }

    private static unsafe IUIAutomationElement Wrap(nint abi) =>
        ComInterfaceMarshaller<IUIAutomationElement>.ConvertToManaged((void*)abi)!;

    private static List<string> SupportedPatterns(IUIAutomationElement element)
    {
        List<string> patterns = [];

        Add(UiaIds.UIA_InvokePatternId, "invoke");
        Add(UiaIds.UIA_ValuePatternId, "value");
        Add(UiaIds.UIA_TogglePatternId, "toggle");
        Add(UiaIds.UIA_ExpandCollapsePatternId, "expand");
        Add(UiaIds.UIA_SelectionItemPatternId, "select");

        return patterns;

        void Add(int id, string label)
        {
            try
            {
                element.GetCurrentPattern(id, out nint pattern);
                if (pattern == 0) return;
                Marshal.Release(pattern);
                patterns.Add(label);
            }
            catch (COMException)
            {
                // An unsupported pattern is a normal answer, not an error.
            }
        }
    }

    /// <summary>
    /// The element's control-view children, as raw references the caller owns.
    /// </summary>
    /// <remarks>
    /// Materialized rather than yielded. FindAll has already built the whole array, so laziness
    /// buys nothing, and an iterator holding COM references across a yield makes their lifetime
    /// depend on whether the consumer finishes enumerating.
    /// </remarks>
    private static unsafe List<nint> Children(IUIAutomationElement element)
    {
        nint arrayAbi;
        try
        {
            element.FindAll(UiaIds.TreeScope_Children, UiaSession.Current.ControlViewCondition, out arrayAbi);
        }
        catch (COMException)
        {
            return [];
        }

        if (arrayAbi == 0) return [];

        List<nint> children = [];
        try
        {
            IUIAutomationElementArray array = ComInterfaceMarshaller<IUIAutomationElementArray>
                .ConvertToManaged((void*)arrayAbi)!;

            array.get_Length(out int length);
            for (int i = 0; i < length; i++)
            {
                array.GetElement(i, out nint child);
                if (child != 0) children.Add(child);
            }
        }
        catch (COMException)
        {
            // The subtree collapsed mid-enumeration. The children already collected are still
            // valid; the caller owns and releases them.
        }
        finally
        {
            Marshal.Release(arrayAbi);
        }

        return children;
    }

    /// <summary>Whether the walk stopped short of real content, as opposed to reaching a leaf.
    /// Only the difference makes a truncation report honest.</summary>
    private static bool HasChildren(IUIAutomationElement element)
    {
        try
        {
            element.FindFirst(UiaIds.TreeScope_Children, UiaSession.Current.ControlViewCondition, out nint first);
            if (first == 0) return false;
            Marshal.Release(first);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
