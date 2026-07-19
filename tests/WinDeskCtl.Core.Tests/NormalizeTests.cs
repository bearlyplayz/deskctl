using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Input;

namespace WinDeskCtl.Core.Tests;

public class NormalizeTests
{
    private static readonly Frame V = Frame.Parse("virtual");
    private static readonly Frame Mon2 = Frame.Parse("monitor:2");

    // A virtual desktop with a monitor stacked above primary, giving a negative origin.
    // Neutral illustrative figures — not this machine's topology.
    private static readonly FrameRect Bounds = new(V, OriginX: 0, OriginY: -1200, W: 3840, H: 2280);

    // The monitor that produces that negative origin.
    private static readonly FrameRect Monitor2Rect = new(Mon2, OriginX: 0, OriginY: -1200, W: 1920, H: 1200);

    /// <summary>
    /// Windows' own inverse: an absolute event lands on <c>floor(n * size / 65536)</c>. Landing
    /// on the pixel the caller named is the whole contract, so it is asserted against the
    /// conversion the OS actually performs rather than against a hand-computed constant.
    /// </summary>
    private static int Invert(int n, int size) => (int)Math.Floor(n * size / 65536.0);

    private static void AssertRoundTrips(Point p, FrameRect bounds)
    {
        (int nx, int ny) = Normalize.ToAbsolute(p, bounds);

        Assert.InRange(nx, 0, 65535);
        Assert.InRange(ny, 0, 65535);
        Assert.Equal(p.X, Invert(nx, bounds.W));
        Assert.Equal(p.Y, Invert(ny, bounds.H));
    }

    [Fact]
    public void EveryColumnAndRow_RoundTripsExactly()
    {
        // The invariant that matters: a normalized point comes back as the pixel it named. Aiming
        // at the leading edge of a pixel's band instead of its centre is off by half a pixel and
        // lands one short wherever rounding crosses the boundary — sporadically, which reads as
        // flakiness rather than as arithmetic. Exhaustive because 'mostly right' is the failure.
        for (int x = 0; x < Bounds.W; x++) AssertRoundTrips(new Point(V, x, 0), Bounds);
        for (int y = 0; y < Bounds.H; y++) AssertRoundTrips(new Point(V, 0, y), Bounds);
    }

    [Fact]
    public void TopLeftAndBottomRightOfTheVirtualDesktop_RoundTrip()
    {
        // A virtual point is measured from the virtual desktop's own top-left, so its top-left is
        // 0,0 no matter where that corner sits on screen.
        AssertRoundTrips(new Point(V, 0, 0), Bounds);
        AssertRoundTrips(new Point(V, Bounds.W - 1, Bounds.H - 1), Bounds);
    }

    [Fact]
    public void TheRectsOriginIsReadForSizeOnly_NotSubtractedAgain()
    {
        // Translate already expressed the point relative to the virtual origin. Subtracting it a
        // second time here displaces every point by the origin and throws outright for a negative
        // one — the topology this project exists to serve.
        FrameRect samePlacedAtZero = new(V, OriginX: 0, OriginY: 0, W: Bounds.W, H: Bounds.H);

        Assert.Equal(
            Normalize.ToAbsolute(new Point(V, 640, 480), samePlacedAtZero),
            Normalize.ToAbsolute(new Point(V, 640, 480), Bounds));
    }

    [Fact]
    public void APointOnTheMonitorAbovePrimary_NormalizesPositive_AndIsNotClamped()
    {
        // THE founding bug, end to end. The point sits at screen y=-1100, above the primary.
        // Normalizing that against the PRIMARY's height gives -1100 * 65535 / 1080 = -66,760,
        // which clamps to 0 and lands at the top of primary — the monitor is arithmetically
        // unreachable. Translated into the virtual frame first and normalized against the VIRTUAL
        // desktop, it is an ordinary positive value.
        Point onMonitor2 = new(Mon2, 100, 100);
        Point inVirtual = Translate.To(onMonitor2, Monitor2Rect, Bounds);

        Assert.Equal(new Point(V, 100, 100), inVirtual);          // 100 above the desktop's top edge
        Assert.Equal((-1100), ScreenCoords.ToScreen(inVirtual, Bounds).Y);   // ...which is negative on screen

        (int _, int ny) = Normalize.ToAbsolute(inVirtual, Bounds);

        Assert.True(ny > 0, "A point above the primary must not normalize to 0.");
        AssertRoundTrips(inVirtual, Bounds);
    }

    [Fact]
    public void APointOnAMonitorLeftOfPrimary_NormalizesToZero_NotNegative()
    {
        FrameRect leftward = new(V, OriginX: -1920, OriginY: 0, W: 3840, H: 1080);

        // The leftmost addressable column sits at screen x=-1920.
        Point inVirtual = ScreenCoords.FromScreen(-1920, 0, leftward);

        Assert.Equal(new Point(V, 0, 0), inVirtual);

        (int nx, int _) = Normalize.ToAbsolute(inVirtual, leftward);
        Assert.True(nx >= 0, "A point left of the primary must not normalize negative.");
        AssertRoundTrips(inVirtual, leftward);
    }

    [Fact]
    public void NormalizationIsRelativeToTheVirtualDesktop_NotThePrimary()
    {
        // Same point, two topologies. If the result were primary-relative these would match.
        FrameRect single = new(V, 0, 0, 1920, 1080);
        FrameRect extended = new(V, 0, -1200, 3840, 2280);

        Assert.NotEqual(
            Normalize.ToAbsolute(new Point(V, 960, 540), single),
            Normalize.ToAbsolute(new Point(V, 960, 540), extended));
    }

    [Fact]
    public void OutsideTheVirtualDesktop_Throws()
    {
        // Clamping would silently click somewhere the caller did not ask for. Refuse instead.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Normalize.ToAbsolute(new Point(V, 99999, 0), Bounds));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Normalize.ToAbsolute(new Point(V, 0, Bounds.H), Bounds));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Normalize.ToAbsolute(new Point(V, 0, -1), Bounds));
    }

    [Fact]
    public void ANonVirtualFrame_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Normalize.ToAbsolute(new Point(Frame.Parse("monitor:1"), 0, 0), Bounds));
    }
}
