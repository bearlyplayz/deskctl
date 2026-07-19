using System.Runtime.InteropServices.Marshalling;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Uia;

/// <summary>Reads a live element's bounds as a frame.</summary>
internal static class ElementRect
{
    /// <summary>
    /// The element's placement, read now rather than taken from a snapshot.
    /// </summary>
    /// <remarks>
    /// UIA's BoundingRectangle is already in absolute screen pixels with absolute far edges,
    /// which is exactly what a FrameRect origin means — so the origin is copied and the size is
    /// a subtraction. Nothing is translated here: routing this through ScreenCoords would
    /// subtract the virtual origin from a value that never had it added, which on a desktop with
    /// a monitor above primary silently shifts every element by the origin.
    /// </remarks>
    internal static unsafe FrameRect Of(nint abi, Frame frame)
    {
        IUIAutomationElement element = ComInterfaceMarshaller<IUIAutomationElement>
            .ConvertToManaged((void*)abi)!;

        element.get_CurrentBoundingRectangle(out User32.RECT r);

        return new FrameRect(frame, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
