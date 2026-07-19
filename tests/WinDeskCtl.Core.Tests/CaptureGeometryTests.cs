using WinDeskCtl.Core.Capture;
using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Tests;

public class CaptureGeometryTests
{
    private static readonly Frame Mon = Frame.Parse("monitor:1");

    // A monitor at a negative origin — the case that must survive crop and scale intact.
    private static readonly FrameRect Source = new(Mon, OriginX: -1920, OriginY: -1200, W: 1920, H: 1200);

    [Fact]
    public void Crop_ShiftsOriginByTheCropOffset()
    {
        FrameRect r = CaptureGeometry.Crop(Source, new CropBox(100, 50, 400, 300));

        Assert.Equal(-1820, r.OriginX);   // -1920 + 100
        Assert.Equal(-1150, r.OriginY);   // -1200 + 50
        Assert.Equal(400, r.W);
        Assert.Equal(300, r.H);
        Assert.Equal(1.0, r.Scale);
        Assert.Equal(Mon, r.Frame);       // a crop does not change which frame you are in
    }

    [Fact]
    public void Crop_ResultTranslatesBackToTheSameVirtualPoint()
    {
        // The property that matters: a point identified in the cropped image must land on the
        // same pixel as the equivalent point in the uncropped one.
        FrameRect virt = new(Frame.Parse("virtual"), -1920, -1200, 3840, 2280);
        FrameRect cropped = CaptureGeometry.Crop(Source, new CropBox(100, 50, 400, 300));

        Point inCrop = new(Mon, 10, 10);
        Point inSource = new(Mon, 110, 60);

        Assert.Equal(
            Translate.To(inSource, Source, virt),
            Translate.To(inCrop, cropped, virt));
    }

    [Fact]
    public void Crop_OutOfBounds_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CaptureGeometry.Crop(Source, new CropBox(1800, 0, 400, 300)));   // runs off the right edge
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CaptureGeometry.Crop(Source, new CropBox(-1, 0, 10, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CaptureGeometry.Crop(Source, new CropBox(0, 0, 0, 10)));         // zero-area
    }

    [Fact]
    public void CropBox_Parse_ReadsTheWireForm()
    {
        Assert.Equal(new CropBox(10, 20, 300, 400), CropBox.Parse("10,20,300,400"));
        // Negative offsets parse; Crop is what rejects them, so the error names the real problem.
        Assert.Equal(new CropBox(-1, 0, 5, 5), CropBox.Parse("-1, 0, 5, 5"));
    }

    [Fact]
    public void CropBox_Parse_RejectsMalformedInput()
    {
        Assert.Throws<FormatException>(() => CropBox.Parse("1,2,3"));
        Assert.Throws<FormatException>(() => CropBox.Parse("1,2,3,4,5"));
        Assert.Throws<FormatException>(() => CropBox.Parse("1,2,3,x"));
        Assert.Throws<FormatException>(() => CropBox.Parse(""));
    }

    [Fact]
    public void FitTo_NoConstraint_IsUnscaled()
    {
        Assert.Equal((1920, 1200, 1.0), CaptureGeometry.FitTo(1920, 1200, null, null));
    }

    [Fact]
    public void FitTo_AlreadySmaller_DoesNotUpscale()
    {
        // Upscaling costs tokens and invents no detail.
        Assert.Equal((800, 600, 1.0), CaptureGeometry.FitTo(800, 600, 1920, null));
    }

    [Fact]
    public void FitTo_MaxWidth_PreservesAspectRatio()
    {
        Assert.Equal((960, 600, 0.5), CaptureGeometry.FitTo(1920, 1200, 960, null));
    }

    [Fact]
    public void FitTo_BothConstraints_TakesTheTighter()
    {
        // 1920x1200 into 960x300: width wants 0.5, height wants 0.25. Height wins or the
        // result overflows the height cap.
        Assert.Equal((480, 300, 0.25), CaptureGeometry.FitTo(1920, 1200, 960, 300));
    }

    [Fact]
    public void FitTo_NeverYieldsAZeroDimension()
    {
        // A pathological cap must not produce a 0-pixel image, which cannot be encoded.
        (int w, int h, _) = CaptureGeometry.FitTo(1920, 4, 8, null);
        Assert.Equal(8, w);
        Assert.True(h >= 1);
    }

    [Fact]
    public void Downscale_SetsScaleAndKeepsOrigin()
    {
        FrameRect r = CaptureGeometry.Downscale(Source, maxWidth: 960, maxHeight: null);

        Assert.Equal(0.5, r.Scale);
        Assert.Equal(960, r.W);
        Assert.Equal(600, r.H);
        Assert.Equal(-1920, r.OriginX);   // the origin is in screen pixels and does not scale
        Assert.Equal(-1200, r.OriginY);
    }

    [Fact]
    public void Downscale_PointInScaledImageResolvesToTheRightVirtualPixel()
    {
        // The whole point of Scale. A click derived from a downscaled image must land where
        // the user saw it.
        FrameRect virt = new(Frame.Parse("virtual"), -1920, -1200, 3840, 2280);
        FrameRect scaled = CaptureGeometry.Downscale(Source, maxWidth: 960, maxHeight: null);

        Point p = Translate.To(new Point(Mon, 480, 300), scaled, virt);

        Assert.Equal(new Point(Frame.Parse("virtual"), 960, 600), p);   // 480/0.5=960 abs -960 -> 960
    }

    [Fact]
    public void CropThenDownscale_Composes()
    {
        FrameRect cropped = CaptureGeometry.Crop(Source, new CropBox(100, 50, 800, 600));
        FrameRect scaled = CaptureGeometry.Downscale(cropped, maxWidth: 400, maxHeight: null);

        Assert.Equal(-1820, scaled.OriginX);
        Assert.Equal(-1150, scaled.OriginY);
        Assert.Equal(400, scaled.W);
        Assert.Equal(300, scaled.H);
        Assert.Equal(0.5, scaled.Scale);
    }
}
