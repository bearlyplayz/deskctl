using System.Runtime.InteropServices;
using Deskctl.Platform.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Deskctl.Platform.Capture;

/// <summary>Tightly packed BGRA pixels. Row padding is removed during read-back.</summary>
public sealed record Bgra(int Width, int Height, byte[] Pixels);

/// <summary>
/// Windows.Graphics.Capture. The only API that captures occluded and hardware-accelerated
/// windows — GDI BitBlt returns black for DirectComposition surfaces and cannot see behind an
/// occluding window at all.
/// </summary>
public static class WgcCapture
{
    private static readonly Guid IID_GraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>
    /// How long to wait for the first frame before giving up on WGC for this target. A window
    /// that never repaints never produces one, so this bounds the wait rather than measuring
    /// anything: it is the point at which the PrintWindow fallback becomes worth trying.
    /// </summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public static Task<Bgra> CaptureWindowAsync(nint hwnd, D3DDevice device, CancellationToken ct)
    {
        nint abi;
        try
        {
            GraphicsCaptureInterop.GetFactory().CreateForWindow(hwnd, in IID_GraphicsCaptureItem, out abi);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"WGC refused to create a capture item for window {hwnd}.", ex);
        }

        return CaptureAsync(ItemFromAbi(abi, hwnd), device, ct);
    }

    public static Task<Bgra> CaptureMonitorAsync(nint hmonitor, D3DDevice device, CancellationToken ct)
    {
        nint abi;
        try
        {
            GraphicsCaptureInterop.GetFactory().CreateForMonitor(hmonitor, in IID_GraphicsCaptureItem, out abi);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"WGC refused to create a capture item for monitor {hmonitor}.", ex);
        }

        return CaptureAsync(ItemFromAbi(abi, hmonitor), device, ct);
    }

    private static GraphicsCaptureItem ItemFromAbi(nint abi, nint handle)
    {
        if (abi == 0) throw new InvalidOperationException($"WGC returned no capture item for handle {handle}.");

        try
        {
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(abi);
        }
        finally
        {
            // FromAbi adds its own reference; this releases the one the factory handed back.
            Marshal.Release(abi);
        }
    }

    private static async Task<Bgra> CaptureAsync(GraphicsCaptureItem item, D3DDevice device, CancellationToken ct)
    {
        using Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device.Winrt,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            item.Size);

        using GraphicsCaptureSession session = pool.CreateCaptureSession(item);

        // Windows draws a yellow border around anything being captured. For a one-shot capture
        // that is visual noise the user did not ask for. Both properties are version-gated:
        // they were added after the 1903 baseline, so their absence must not be fatal.
        TrySet(() => session.IsBorderRequired = false);
        TrySet(() => session.IsCursorCaptureEnabled = false);

        TaskCompletionSource<Direct3D11CaptureFrame> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrame(Direct3D11CaptureFramePool sender, object _)
        {
            Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
            if (frame is not null) tcs.TrySetResult(frame);
        }

        pool.FrameArrived += OnFrame;
        try
        {
            session.StartCapture();

            // A window that is minimized or never repaints may never produce a frame. Bound the
            // wait so a capture fails loudly instead of hanging the caller forever.
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(FrameTimeout);

            Direct3D11CaptureFrame frame;
            try
            {
                frame = await tcs.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The linked token cannot say which source fired, but the distinction is the
                // whole contract here: the caller cancelling means stop, while WGC accepting the
                // item and never delivering a frame means try PrintWindow instead. Reported as a
                // timeout so a cancellation and a silent window are not the same exception.
                throw new TimeoutException(
                    $"Windows.Graphics.Capture produced no frame for this target within " +
                    $"{FrameTimeout.TotalSeconds:0.#}s.");
            }

            using (frame)
            {
                return D3DReadback.ToBgra(frame.Surface, device);
            }
        }
        finally
        {
            pool.FrameArrived -= OnFrame;
        }
    }

    private static void TrySet(Action set)
    {
        try { set(); }
        catch (Exception) { /* Property unavailable on this Windows build; the capture is still valid. */ }
    }
}
