using System.Runtime.InteropServices;
using WinDeskCtl.Platform.Interop;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace WinDeskCtl.Platform.Capture;

/// <summary>
/// Copies a WGC frame off the GPU. A captured frame lives in GPU memory with no CPU access, so
/// it must be copied into a staging texture before it can be mapped and read.
/// </summary>
internal static class D3DReadback
{
    private static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    internal static Bgra ToBgra(IDirect3DSurface surface, D3DDevice device)
    {
        nint texture = GetTexture(surface);
        try
        {
            D3D11.D3D11_TEXTURE2D_DESC desc = GetDesc(texture);

            D3D11.D3D11_TEXTURE2D_DESC staging = desc with
            {
                Usage = D3D11.D3D11_USAGE_STAGING,
                CPUAccessFlags = D3D11.D3D11_CPU_ACCESS_READ,
                BindFlags = 0,
                MiscFlags = 0,
            };

            nint stagingTexture = CreateTexture2D(device.RawDevice, in staging);
            try
            {
                CopyResource(device.RawContext, stagingTexture, texture);
                D3D11.D3D11_MAPPED_SUBRESOURCE map = Map(device.RawContext, stagingTexture);
                try
                {
                    return Pack(map, (int)desc.Width, (int)desc.Height);
                }
                finally
                {
                    Unmap(device.RawContext, stagingTexture);
                }
            }
            finally
            {
                Marshal.Release(stagingTexture);
            }
        }
        finally
        {
            Marshal.Release(texture);
        }
    }

    /// <summary>
    /// Unwraps the projected surface down to the raw ID3D11Texture2D behind it.
    /// </summary>
    /// <remarks>
    /// The projection has no path back to D3D, so the surface is taken to its ABI pointer and
    /// re-queried for the DXGI interface-access shim, which does.
    /// </remarks>
    private static nint GetTexture(IDirect3DSurface surface)
    {
        nint surfacePtr = MarshalInspectable<IDirect3DSurface>.FromManaged(surface);
        try
        {
            IDirect3DDxgiInterfaceAccess access =
                ComInterop.QueryInterface<IDirect3DDxgiInterfaceAccess>(surfacePtr, IID_IDirect3DDxgiInterfaceAccess);
            access.GetInterface(in IID_ID3D11Texture2D, out nint texture);
            return texture;
        }
        finally
        {
            Marshal.Release(surfacePtr);
        }
    }

    /// <summary>
    /// Copies row by row because RowPitch is the GPU's stride, not Width*4 — the driver pads
    /// rows for alignment. Treating the mapped buffer as tightly packed produces an image that
    /// shears progressively toward the bottom.
    /// </summary>
    private static Bgra Pack(D3D11.D3D11_MAPPED_SUBRESOURCE map, int width, int height)
    {
        int rowBytes = width * 4;
        byte[] pixels = new byte[rowBytes * height];

        for (int y = 0; y < height; y++)
        {
            nint src = map.pData + (y * (int)map.RowPitch);
            Marshal.Copy(src, pixels, y * rowBytes, rowBytes);
        }

        return new Bgra(width, height, pixels);
    }

    // ID3D11Device and ID3D11DeviceContext are called through their vtables rather than declared
    // as [GeneratedComInterface]: only four methods are needed out of ~40, and hand-declaring the
    // full interfaces in vtable order to reach them is more code and more ways to be wrong.
    private static unsafe D3D11.D3D11_TEXTURE2D_DESC GetDesc(nint texture)
    {
        const int GetDescSlot = 10;   // IUnknown(3) + ID3D11DeviceChild(4) + ID3D11Resource(3)
        var fn = (delegate* unmanaged<nint, D3D11.D3D11_TEXTURE2D_DESC*, void>)
            (*(void***)texture)[GetDescSlot];
        D3D11.D3D11_TEXTURE2D_DESC desc;
        fn(texture, &desc);
        return desc;
    }

    private static unsafe nint CreateTexture2D(nint device, in D3D11.D3D11_TEXTURE2D_DESC desc)
    {
        const int CreateTexture2DSlot = 5;   // IUnknown(3) + CreateBuffer + CreateTexture1D
        var fn = (delegate* unmanaged<nint, D3D11.D3D11_TEXTURE2D_DESC*, nint, nint*, int>)
            (*(void***)device)[CreateTexture2DSlot];

        nint result;
        fixed (D3D11.D3D11_TEXTURE2D_DESC* d = &desc)
        {
            Marshal.ThrowExceptionForHR(fn(device, d, 0, &result));
        }
        return result;
    }

    private static unsafe void CopyResource(nint context, nint dst, nint src)
    {
        const int CopyResourceSlot = 47;
        var fn = (delegate* unmanaged<nint, nint, nint, void>)(*(void***)context)[CopyResourceSlot];
        fn(context, dst, src);
    }

    private static unsafe D3D11.D3D11_MAPPED_SUBRESOURCE Map(nint context, nint resource)
    {
        const int MapSlot = 14;
        var fn = (delegate* unmanaged<nint, nint, uint, uint, uint, D3D11.D3D11_MAPPED_SUBRESOURCE*, int>)
            (*(void***)context)[MapSlot];

        D3D11.D3D11_MAPPED_SUBRESOURCE map;
        Marshal.ThrowExceptionForHR(fn(context, resource, 0, D3D11.D3D11_MAP_READ, 0, &map));
        return map;
    }

    private static unsafe void Unmap(nint context, nint resource)
    {
        const int UnmapSlot = 15;
        var fn = (delegate* unmanaged<nint, nint, uint, void>)(*(void***)context)[UnmapSlot];
        fn(context, resource, 0);
    }
}
