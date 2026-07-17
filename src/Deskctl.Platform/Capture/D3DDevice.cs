using System.Runtime.InteropServices;
using Deskctl.Platform.Interop;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Deskctl.Platform.Capture;

/// <summary>
/// The D3D11 device backing WGC's frame pool, plus the handles the read-back path needs.
/// </summary>
/// <remarks>
/// One device is shared across captures: creation costs tens of milliseconds and the device is
/// free-threaded, so making one per capture would dominate the cost of a capture.
/// </remarks>
public sealed class D3DDevice : IDisposable
{
    private static readonly Guid IID_IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    private readonly nint _device;
    private readonly nint _context;

    public IDirect3DDevice Winrt { get; }

    internal nint RawDevice => _device;

    internal nint RawContext => _context;

    private D3DDevice(nint device, nint context, IDirect3DDevice winrt)
    {
        _device = device;
        _context = context;
        Winrt = winrt;
    }

    public static D3DDevice Create()
    {
        // BGRA_SUPPORT is required: WGC frames arrive as B8G8R8A8, and the device must be able
        // to bind that format or CreateTexture2D fails at read-back rather than here.
        int hr = D3D11.D3D11CreateDevice(
            0, D3D11.D3D_DRIVER_TYPE_HARDWARE, 0, D3D11.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            0, 0, D3D11.D3D11_SDK_VERSION, out nint device, out _, out nint context);

        if (hr < 0)
        {
            // WARP is the software rasterizer. It keeps deskctl working over RDP and on boxes
            // with no usable GPU driver, at a speed cost that beats not working.
            hr = D3D11.D3D11CreateDevice(
                0, D3D11.D3D_DRIVER_TYPE_WARP, 0, D3D11.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                0, 0, D3D11.D3D11_SDK_VERSION, out device, out _, out context);
            Marshal.ThrowExceptionForHR(hr);
        }

        if (Marshal.QueryInterface(device, IID_IDXGIDevice, out nint dxgi) < 0)
        {
            throw new InvalidOperationException("ID3D11Device does not expose IDXGIDevice.");
        }

        try
        {
            Marshal.ThrowExceptionForHR(
                D3D11.CreateDirect3D11DeviceFromDXGIDevice(dxgi, out nint graphicsDevice));
            try
            {
                IDirect3DDevice winrt = MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
                return new D3DDevice(device, context, winrt);
            }
            finally
            {
                Marshal.Release(graphicsDevice);
            }
        }
        finally
        {
            Marshal.Release(dxgi);
        }
    }

    public void Dispose()
    {
        (Winrt as IDisposable)?.Dispose();
        if (_context != 0) Marshal.Release(_context);
        if (_device != 0) Marshal.Release(_device);
    }
}
