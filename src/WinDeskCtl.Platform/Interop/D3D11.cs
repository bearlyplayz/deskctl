using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WinDeskCtl.Platform.Interop;

internal static partial class D3D11
{
    internal const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    internal const uint D3D11_SDK_VERSION = 7;
    internal const int D3D_DRIVER_TYPE_HARDWARE = 1;
    internal const int D3D_DRIVER_TYPE_WARP = 5;

    internal const uint D3D11_CPU_ACCESS_READ = 0x20000;
    internal const uint D3D11_USAGE_STAGING = 3;
    internal const uint D3D11_MAP_READ = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11_TEXTURE2D_DESC
    {
        internal uint Width, Height, MipLevels, ArraySize;
        internal uint Format;
        internal uint SampleCount, SampleQuality;
        internal uint Usage;
        internal uint BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11_MAPPED_SUBRESOURCE
    {
        internal nint pData;
        internal uint RowPitch;
        internal uint DepthPitch;
    }

    [LibraryImport("d3d11.dll")]
    internal static partial int D3D11CreateDevice(
        nint adapter, int driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelCount, uint sdkVersion,
        out nint device, out int featureLevel, out nint context);

    /// <summary>Wraps an ID3D11Device as the WinRT IDirect3DDevice that WGC's frame pool requires.</summary>
    [LibraryImport("d3d11.dll")]
    internal static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}

/// <summary>
/// The bridge from a projected WinRT IDirect3DSurface back to the underlying D3D11 texture.
/// CsWinRT projects the surface but not this interface, so it is declared by hand.
/// </summary>
[GeneratedComInterface]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
internal partial interface IDirect3DDxgiInterfaceAccess
{
    void GetInterface(in Guid iid, out nint pv);
}
