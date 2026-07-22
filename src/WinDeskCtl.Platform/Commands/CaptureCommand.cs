using WinDeskCtl.Core.Capture;
using WinDeskCtl.Core.Commands;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Platform.Capture;
using WinDeskCtl.Platform.Displays;
using WinDeskCtl.Platform.Windows;

namespace WinDeskCtl.Platform.Commands;

/// <summary>
/// Pixels, with the coordinate frame they live in. Capture is the fallback path for perceiving
/// the screen — Snapshot's element tree is the default, because a whole-desktop image on a wide
/// display is megabytes that a vision model downscales into uselessness.
/// </summary>
public sealed class CaptureCommand : ICommand<CaptureInput, CaptureResult>, IDisposable
{
    private readonly Lazy<D3DDevice> _device = new(D3DDevice.Create);

    public async Task<CaptureResult> RunAsync(CaptureInput input, CancellationToken ct)
    {
        DisplayEnumerator.EnsurePerMonitorV2();

        (Bgra pixels, FrameRect rect) = await AcquireAsync(input.Target, ct);

        // WGC rounds an item's size up to a multiple it likes, so a frame can come back larger
        // than the rect it describes. Trusting the rect over the frame would crop from the wrong
        // offsets; the rect is re-derived from the pixels actually delivered.
        rect = rect with { W = pixels.Width, H = pixels.Height };

        if (input.Region is CropBox box)
        {
            rect = CaptureGeometry.Crop(rect, box);
            pixels = CropPixels(pixels, box);
        }

        FrameRect final = CaptureGeometry.Downscale(rect, input.MaxWidth, input.MaxHeight);

        byte[] bytes = await ImageEncoder.EncodeAsync(pixels, input.Format, input.Quality, final.W, final.H);

        // OCR reads the full-resolution pixels, not the downscaled output — downscaling degrades
        // recognition, and the rects come back in output units either way.
        IReadOnlyList<OcrLine>? text = input.WantsOcr
            ? OcrFilters.Apply(await OcrReader.ReadAsync(pixels, final.W), input.OcrFilter)
            : null;

        string? image = input.MintFrame
            ? ImageFrames.Mint(final, input.Target is Frame.Window w ? (nint)w.Hwnd : 0)
            : null;

        return new CaptureResult(final, input.Format, bytes, image, text);
    }

    private async Task<(Bgra, FrameRect)> AcquireAsync(Frame target, CancellationToken ct)
    {
        switch (target)
        {
            case Frame.Monitor m:
                {
                    nint hmon = WindowGeometry.MonitorFromId(m.Id);
                    FrameRect rect = DisplayEnumerator.GetMonitors().First(x => x.Id == m.Id).Bounds;
                    return (await WgcCapture.CaptureMonitorAsync(hmon, _device.Value, ct), rect);
                }

            case Frame.Window w:
                {
                    nint hwnd = (nint)w.Hwnd;
                    FrameRect rect = WindowGeometry.GetRect(hwnd);
                    try
                    {
                        return (await WgcCapture.CaptureWindowAsync(hwnd, _device.Value, ct), rect);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // WGC refuses some windows outright. PrintWindow is worse — an app
                        // may ignore it and return black — so it is only reached once WGC has failed.
                        return (PrintWindowCapture.Capture(hwnd, rect.W, rect.H), rect);
                    }
                }

            case Frame.Virtual:
                // The virtual desktop has no single WGC item. Capturing the primary monitor and
                // calling it "virtual" would return an image whose frame is a lie, so this fails
                // instead. Callers wanting the whole desktop capture each monitor.
                throw new NotSupportedException(
                    "The virtual desktop is not directly capturable. Capture a specific monitor " +
                    "(monitor:<id>) or window (win:<hwnd>).");

            case Frame.Element:
                throw new NotSupportedException(
                    "Element capture requires the UIA tier. Capture the containing window and crop with --region.");

            case Frame.Image:
                throw new NotSupportedException(
                    "An img: frame is a past capture, not a live surface. Capture the window or " +
                    "monitor it shows.");

            default:
                throw new ArgumentOutOfRangeException(nameof(target), $"Unhandled frame '{target}'.");
        }
    }

    /// <summary>
    /// Crops in CPU memory rather than asking the GPU for a sub-region: WGC hands out whole
    /// items, and a memcpy per row of an already-resident frame costs less than a second capture.
    /// </summary>
    private static Bgra CropPixels(Bgra src, CropBox box)
    {
        int rowBytes = box.W * 4;
        byte[] pixels = new byte[rowBytes * box.H];

        for (int y = 0; y < box.H; y++)
        {
            int srcOffset = (((box.Y + y) * src.Width) + box.X) * 4;
            Buffer.BlockCopy(src.Pixels, srcOffset, pixels, y * rowBytes, rowBytes);
        }

        return new Bgra(box.W, box.H, pixels);
    }

    public void Dispose()
    {
        if (_device.IsValueCreated) _device.Value.Dispose();
    }
}
