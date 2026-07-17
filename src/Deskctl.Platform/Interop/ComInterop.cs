using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Deskctl.Platform.Interop;

internal static class ComInterop
{
    /// <summary>
    /// Wraps a raw IUnknown as a source-generated COM interface.
    /// </summary>
    /// <remarks>
    /// CsWinRT's own <c>As&lt;T&gt;</c> cannot do this: it marshals through the WinRT projection,
    /// which knows nothing about <see cref="GeneratedComInterfaceAttribute"/> types. Going through
    /// <see cref="ComInterfaceMarshaller{T}"/> is the ComWrappers path the source generator emits,
    /// and it is the one that survives AOT.
    /// </remarks>
    internal static unsafe T QueryInterface<T>(nint unknown, Guid iid) where T : class
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, iid, out nint p));
        try
        {
            return ComInterfaceMarshaller<T>.ConvertToManaged((void*)p)
                ?? throw new InvalidOperationException($"QueryInterface for {iid} returned null.");
        }
        finally
        {
            // ConvertToManaged builds an RCW holding its own reference; this drops the QI's.
            Marshal.Release(p);
        }
    }
}
