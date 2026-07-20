using System.Runtime.InteropServices.Marshalling;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Uia;

/// <summary>Finds the top-level window that owns a UIA element.</summary>
internal static class ElementWindow
{
    /// <summary>
    /// The element's owning top-level window, or 0 when it cannot be determined.
    /// </summary>
    /// <remarks>
    /// NativeWindowHandle is non-zero only on elements that are themselves an HWND — a window, or
    /// a windowed control. Most elements in a modern UI framework are drawn by their parent and
    /// report 0, so the snapshot root is the fallback: an element handle is only ever minted by a
    /// snapshot, and a snapshot of a window has that window as its root. Both are run through
    /// GA_ROOT, since a windowed control's own HWND is not the window a caller means.
    ///
    /// ponytail: a snapshot rooted at a monitor or the virtual desktop has no owning HWND, so this
    /// returns 0 and the caller skips focusing. Walking up the UIA tree to the nearest ancestor
    /// with a native handle would cover it, at the cost of an IUIAutomationTreeWalker binding.
    /// </remarks>
    internal static nint OwnerOf(string handle, nint elementAbi)
    {
        nint hwnd = Root(NativeHandleOf(elementAbi));
        if (hwnd != 0) return hwnd;

        nint scope = HandleRegistry.ScopeOf(handle);
        return scope == 0 ? 0 : Root(NativeHandleOf(scope));
    }

    private static nint Root(nint hwnd) =>
        hwnd != 0 && User32.IsWindow(hwnd) ? User32.GetAncestor(hwnd, User32.GA_ROOT) : 0;

    /// <summary>
    /// Reads NativeWindowHandle, treating any COM failure as "no handle". A dead or unreachable
    /// element is not worth propagating from here — the caller is deciding whether to focus, and
    /// the step that follows will fail on its own terms with a better message.
    /// </summary>
    private static unsafe nint NativeHandleOf(nint abi)
    {
        try
        {
            IUIAutomationElement element = ComInterfaceMarshaller<IUIAutomationElement>
                .ConvertToManaged((void*)abi)!;

            element.get_CurrentNativeWindowHandle(out nint hwnd);
            return hwnd;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return 0;
        }
    }
}
