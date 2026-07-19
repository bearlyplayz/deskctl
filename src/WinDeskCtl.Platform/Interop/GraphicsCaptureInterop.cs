using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WinDeskCtl.Platform.Interop;

/// <summary>
/// The activation factory path for creating a GraphicsCaptureItem from an HWND or HMONITOR.
/// There is no projected WinRT API for this — it exists only as interop.
/// </summary>
[GeneratedComInterface]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
internal partial interface IGraphicsCaptureItemInterop
{
    void CreateForWindow(nint window, in Guid iid, out nint result);

    void CreateForMonitor(nint monitor, in Guid iid, out nint result);
}

internal static class GraphicsCaptureInterop
{
    private static readonly Guid IID_ItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    /// <summary>
    /// The GraphicsCaptureItem activation factory, as the interop interface.
    /// </summary>
    /// <remarks>
    /// The factory pointer comes from CsWinRT rather than a raw RoGetActivationFactory so that
    /// WinRT and COM initialisation stay CsWinRT's responsibility; the pointer is then re-wrapped
    /// as a source-generated COM interface, which is the AOT-clean path. CsWinRT caches the
    /// factory per class, so the returned reference is not disposed here.
    /// </remarks>
    internal static IGraphicsCaptureItemInterop GetFactory() =>
        ComInterop.QueryInterface<IGraphicsCaptureItemInterop>(
            WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem").ThisPtr,
            IID_ItemInterop);
}
