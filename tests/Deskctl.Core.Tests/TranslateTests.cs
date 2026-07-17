using Deskctl.Core.Frames;

namespace Deskctl.Core.Tests;

public class TranslateTests
{
    private static readonly Frame Virt = Frame.Parse("virtual");
    private static readonly Frame Mon1 = Frame.Parse("monitor:1");
    private static readonly Frame Mon2 = Frame.Parse("monitor:2");
    private static readonly Frame Win = Frame.Parse("win:100");

    // A virtual desktop whose origin is negative because monitor:2 sits ABOVE primary.
    // This is the exact shape that makes primary-relative maths unreachable.
    private static readonly FrameRect VirtualRect = new(Virt, OriginX: 0, OriginY: -1200, W: 3840, H: 2280);
    private static readonly FrameRect Monitor1Rect = new(Mon1, OriginX: 0, OriginY: 0, W: 1920, H: 1080);
    private static readonly FrameRect Monitor2Rect = new(Mon2, OriginX: 0, OriginY: -1200, W: 1920, H: 1200);

    [Fact]
    public void ToSameFrame_IsIdentity()
    {
        Point p = new(Mon1, 10, 20);
        Assert.Equal(p, Translate.To(p, Monitor1Rect, Monitor1Rect));
    }

    [Fact]
    public void MonitorToVirtual_AddsOrigin()
    {
        Point p = Translate.To(new Point(Mon1, 10, 20), Monitor1Rect, VirtualRect);
        Assert.Equal(new Point(Virt, 10, 1220), p);   // y: 0 + 20 - (-1200)
    }

    [Fact]
    public void VirtualToMonitor_SubtractsOrigin()
    {
        Point p = Translate.To(new Point(Virt, 10, 1220), VirtualRect, Monitor1Rect);
        Assert.Equal(new Point(Mon1, 10, 20), p);
    }

    [Fact]
    public void NegativeOrigin_IsFirstClass_NotClamped()
    {
        // The origin case. A point at the top-left of a monitor stacked above primary
        // is y=-1200 in virtual space. Nothing here may clamp it to 0.
        Point p = Translate.To(new Point(Mon2, 0, 0), Monitor2Rect, VirtualRect);
        Assert.Equal(new Point(Virt, 0, 0), p);       // virtual-relative: 0 - (-1200) - 1200 = 0

        Point q = Translate.To(new Point(Mon2, 0, 0), Monitor2Rect, Monitor1Rect);
        Assert.Equal(new Point(Mon1, 0, -1200), q);   // monitor:1-relative it is genuinely negative
    }

    [Fact]
    public void MonitorToMonitor_GoesThroughVirtual()
    {
        Point p = Translate.To(new Point(Mon1, 5, 5), Monitor1Rect, Monitor2Rect);
        Assert.Equal(new Point(Mon2, 5, 1205), p);    // 5 - 0 + 0 - (-1200) = 1205
    }

    [Fact]
    public void Downscale_ImagePointMapsBackToFullResolution()
    {
        // A 1920x1080 monitor captured and downscaled to 960x540: Scale = 0.5.
        // W/H are IMAGE dimensions; a point read off the image is in image units.
        FrameRect scaled = new(Mon1, OriginX: 0, OriginY: 0, W: 960, H: 540, Scale: 0.5);

        Point p = Translate.To(new Point(Mon1, 480, 270), scaled, VirtualRect);
        Assert.Equal(new Point(Virt, 960, 1740), p);  // 480/0.5 = 960; 270/0.5 = 540 - (-1200) = 1740
    }

    [Fact]
    public void Downscale_RoundTripsBack()
    {
        FrameRect scaled = new(Mon1, OriginX: 0, OriginY: 0, W: 960, H: 540, Scale: 0.5);
        Point original = new(Mon1, 480, 270);

        Point virt = Translate.To(original, scaled, VirtualRect);
        Point back = Translate.To(virt, VirtualRect, scaled);

        Assert.Equal(original, back);
    }

    [Fact]
    public void WindowRelativePoint_SurvivesWindowMoving()
    {
        // The point of window-relative frames: the same window-relative coordinate resolves to
        // a different virtual point after the window moves, with no arithmetic by the caller.
        FrameRect before = new(Win, OriginX: 100, OriginY: 100, W: 800, H: 600);
        FrameRect after = new(Win, OriginX: 500, OriginY: 300, W: 800, H: 600);
        Point local = new(Win, 40, 20);

        Assert.Equal(new Point(Virt, 140, 1320), Translate.To(local, before, VirtualRect));
        Assert.Equal(new Point(Virt, 540, 1520), Translate.To(local, after, VirtualRect));
    }

    [Fact]
    public void To_MismatchedSourceFrame_Throws()
    {
        // Passing a point from one frame with another frame's rect is a caller bug
        // and silently producing a plausible number is how these bugs survive.
        Point p = new(Mon1, 10, 10);
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => Translate.To(p, Monitor2Rect, VirtualRect));
        Assert.Contains("monitor:1", ex.Message);
        Assert.Contains("monitor:2", ex.Message);
    }

    [Fact]
    public void Contains_IsExclusiveOfFarEdge()
    {
        FrameRect r = new(Mon1, OriginX: 0, OriginY: 0, W: 1920, H: 1080);
        Assert.True(r.Contains(new Point(Mon1, 0, 0)));
        Assert.True(r.Contains(new Point(Mon1, 1919, 1079)));
        Assert.False(r.Contains(new Point(Mon1, 1920, 1080)));   // far edge is the first pixel outside
        Assert.False(r.Contains(new Point(Mon1, -1, 0)));
    }

    /// <summary>
    /// Scale is a divisor when mapping a frame's units back to physical pixels. Zero yields
    /// infinity and a garbage coordinate rather than an error, so the type refuses to hold one —
    /// which is what keeps Translate total.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FrameRect_RefusesAScaleTranslateCannotDivideBy(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameRect(new Frame.Virtual(), 0, 0, 1920, 1080, scale));
    }

    /// <summary>`with` is how every downscale builds its rect, so the guard has to survive it.</summary>
    [Fact]
    public void FrameRect_RefusesAnInvalidScaleThroughWith()
    {
        FrameRect rect = new(new Frame.Virtual(), 0, 0, 1920, 1080);
        Assert.Throws<ArgumentOutOfRangeException>(() => rect with { Scale = 0.0 });
    }
}
