using WinDeskCtl.Core.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WinDeskCtl.Platform.Capture;

/// <summary>
/// Encodes captured pixels using the OS imaging stack (WIC via Windows.Graphics.Imaging), which
/// costs no third-party dependency and no AOT risk.
/// </summary>
public static class ImageEncoder
{
    /// <param name="outW">Target width. Equal to <paramref name="image"/>'s width when not downscaling.</param>
    public static async Task<byte[]> EncodeAsync(Bgra image, ImageFormat format, int quality, int outW, int outH)
    {
        Guid encoderId = format switch
        {
            ImageFormat.Jpeg => BitmapEncoder.JpegEncoderId,
            _ => BitmapEncoder.PngEncoderId,
        };

        using InMemoryRandomAccessStream stream = new();

        BitmapPropertySet properties = [];
        if (format == ImageFormat.Jpeg)
        {
            // Qualified from the global root: WinDeskCtl.Platform.Windows would otherwise shadow the
            // WinRT Windows namespace when resolved from a sibling namespace.
            properties.Add("ImageQuality",
                new BitmapTypedValue(quality / 100.0f, global::Windows.Foundation.PropertyType.Single));
        }

        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(encoderId, stream, properties);

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            // WGC surfaces are opaque; declaring Premultiplied here would have the encoder
            // reinterpret the alpha channel and darken the result.
            BitmapAlphaMode.Ignore,
            (uint)image.Width,
            (uint)image.Height,
            dpiX: 96,
            dpiY: 96,
            image.Pixels);

        if (outW != image.Width || outH != image.Height)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)outW;
            encoder.BitmapTransform.ScaledHeight = (uint)outH;
            // Fant is the highest-quality downscale filter WIC offers. Downscaling is the only
            // token lever available, so it is worth doing well rather than cheaply.
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        await encoder.FlushAsync();

        byte[] bytes = new byte[stream.Size];
        using DataReader reader = new(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
