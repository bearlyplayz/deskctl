using Deskctl.Core.Frames;

namespace Deskctl.Core.Tests;

public class ScreenCoordsTests
{
    private static readonly Frame Virt = Frame.Parse("virtual");
    private static readonly Frame Mon2 = Frame.Parse("monitor:2");

    // A virtual desktop whose origin is negative because monitor:2 sits ABOVE primary.
    // Neutral illustrative figures — not this machine's topology.
    private static readonly FrameRect VirtualRect = new(Virt, OriginX: 0, OriginY: -1200, W: 3840, H: 2280);
    private static readonly FrameRect Monitor2Rect = new(Mon2, OriginX: 0, OriginY: -1200, W: 1920, H: 1200);

    [Fact]
    public void ToScreen_AddsTheVirtualOrigin()
    {
        Assert.Equal((10, -1180), ScreenCoords.ToScreen(new Point(Virt, 10, 20), VirtualRect));
    }

    [Fact]
    public void TopLeftOfTheVirtualDesktop_IsTheNegativeScreenOrigin()
    {
        // The frame's top-left is 0,0 by definition; on screen it is genuinely negative.
        // Conflating the two is what puts a click on the wrong monitor.
        Assert.Equal((0, -1200), ScreenCoords.ToScreen(new Point(Virt, 0, 0), VirtualRect));
    }

    [Fact]
    public void RoundTripsThroughFromScreen()
    {
        Point original = new(Virt, 123, 456);
        (int x, int y) = ScreenCoords.ToScreen(original, VirtualRect);
        Assert.Equal(original, ScreenCoords.FromScreen(x, y, VirtualRect));
    }

    [Fact]
    public void AMonitorAbovePrimary_ReachesNegativeScreenCoordinates()
    {
        // The founding bug, end to end: monitor:2's top-left must land at a negative screen y,
        // not be clamped to the top of primary.
        Point inVirtual = Translate.To(new Point(Mon2, 0, 0), Monitor2Rect, VirtualRect);
        Assert.Equal((0, -1200), ScreenCoords.ToScreen(inVirtual, VirtualRect));
    }

    [Fact]
    public void AVirtualPointIsNeverNegative_ButItsScreenPointMayBe()
    {
        // Guards the invariant that separates the two spaces: every addressable virtual-frame
        // point is non-negative, which is exactly why it cannot be handed to Win32 unconverted.
        Point centre = Translate.To(new Point(Mon2, 960, 600), Monitor2Rect, VirtualRect);
        Assert.True(centre.X >= 0 && centre.Y >= 0);
        Assert.True(VirtualRect.Contains(centre));

        (int _, int screenY) = ScreenCoords.ToScreen(centre, VirtualRect);
        Assert.True(screenY < 0, "monitor:2's centre is above primary and must stay negative on screen");
    }

    [Fact]
    public void ToScreen_MismatchedFrame_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ScreenCoords.ToScreen(new Point(Mon2, 0, 0), VirtualRect));
    }
}
