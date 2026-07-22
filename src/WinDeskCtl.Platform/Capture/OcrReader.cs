using System.Runtime.InteropServices.WindowsRuntime;
using WinDeskCtl.Core.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
// Aliased: OcrLine/OcrWord exist in both this project's contract types and the WinRT engine's,
// and WinDeskCtl.Platform.Windows shadows the WinRT Windows root from sibling namespaces.
using WinOcr = Windows.Media.Ocr;
using WinRect = Windows.Foundation.Rect;

namespace WinDeskCtl.Platform.Capture;

/// <summary>
/// Recognizes text in captured pixels using the OS engine (Windows.Media.Ocr) — no dependency,
/// no network. Runs on the full-resolution pixels rather than the downscaled output, because
/// downscaling degrades recognition; the returned rects are converted into the output image's
/// units so they are directly usable as img: frame coordinates.
/// </summary>
public static class OcrReader
{
    /// <param name="image">The full-resolution pixels, after any region crop.</param>
    /// <param name="outW">The output image's width, whose units the returned rects are in.</param>
    public static async Task<IReadOnlyList<OcrLine>> ReadAsync(Bgra image, int outW)
    {
        WinOcr.OcrEngine engine = WinOcr.OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new NotSupportedException(
                "No OCR language is available. Install a language pack with OCR support " +
                "(Settings > Time & Language > Language) and retry.");

        // The engine rejects images beyond its dimension limit, which a 4K monitor capture
        // exceeds. Such an image is scaled to fit for recognition only; the factor is folded
        // into the coordinate conversion below.
        int max = (int)WinOcr.OcrEngine.MaxImageDimension;
        (SoftwareBitmap bitmap, double ocrScale) = image.Width > max || image.Height > max
            ? await ScaledToFitAsync(image, max)
            : (SoftwareBitmap.CreateCopyFromBuffer(
                image.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height), 1.0);

        using (bitmap)
        {
            WinOcr.OcrResult result = await engine.RecognizeAsync(bitmap);

            // Recognition pixels -> source pixels -> output units, folded into one factor.
            double f = (double)outW / image.Width / ocrScale;

            List<OcrLine> lines = [];
            foreach (WinOcr.OcrLine line in result.Lines)
            {
                List<OcrWord> words = [.. line.Words.Select(w => new OcrWord(w.Text, Scaled(w.BoundingRect, f)))];
                if (words.Count == 0) continue;

                // The engine exposes no line rect; the union of its words' boxes is computed in
                // recognition space so a single rounding pass applies.
                WinRect union = line.Words[0].BoundingRect;
                foreach (WinOcr.OcrWord w in line.Words.Skip(1)) union.Union(w.BoundingRect);

                lines.Add(new OcrLine(line.Text, Scaled(union, f), words));
            }

            return lines;
        }
    }

    private static CropBox Scaled(WinRect r, double f) => new(
        Round(r.X * f), Round(r.Y * f), Round(r.Width * f), Round(r.Height * f));

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Scales pixels down to the engine's limit by round-tripping through the encoder — the one
    /// scaler already in the project. Costs one PNG encode/decode, only on captures too large to
    /// recognize directly.
    /// </summary>
    private static async Task<(SoftwareBitmap, double)> ScaledToFitAsync(Bgra image, int max)
    {
        (int w, int h, double scale) = CaptureGeometry.FitTo(image.Width, image.Height, max, max);
        byte[] png = await ImageEncoder.EncodeAsync(image, ImageFormat.Png, quality: 100, w, h);

        using InMemoryRandomAccessStream stream = new();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        return (await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore), scale);
    }
}
