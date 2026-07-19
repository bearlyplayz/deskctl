using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Input;

namespace WinDeskCtl.Core.Tests;

public class InterpolateTests
{
    private static readonly Frame V = Frame.Parse("virtual");

    [Fact]
    public void Path_StartsAfterTheOrigin_AndEndsExactlyOnTheTarget()
    {
        // Landing exactly on the target is non-negotiable: a drop one pixel off can hit the
        // wrong control. Interpolation error must never reach the endpoint.
        var path = Interpolate.Path(new Point(V, 0, 0), new Point(V, 100, 50),
            TimeSpan.FromMilliseconds(100), Ease.Linear);

        Assert.NotEqual(new Point(V, 0, 0), path[0].At);
        Assert.Equal(new Point(V, 100, 50), path[^1].At);
    }

    [Fact]
    public void Path_IsMonotonic_ForALinearMove()
    {
        var path = Interpolate.Path(new Point(V, 0, 0), new Point(V, 100, 0),
            TimeSpan.FromMilliseconds(100), Ease.Linear);

        for (int i = 1; i < path.Count; i++)
        {
            Assert.True(path[i].At.X >= path[i - 1].At.X, "A linear move must never backtrack.");
        }
    }

    [Fact]
    public void Path_CrossesTheDragThreshold_EarlyInTheMove()
    {
        // The point of interpolating at all: the app must see movement past ~4px while the
        // button is down, or no drag begins.
        var path = Interpolate.Path(new Point(V, 0, 0), new Point(V, 400, 0),
            TimeSpan.FromMilliseconds(250), Ease.Linear);

        Assert.Contains(path, p => p.At.X > 4 && p.At.X < 100);
    }

    [Fact]
    public void Path_HonoursTheTotalDuration()
    {
        var path = Interpolate.Path(new Point(V, 0, 0), new Point(V, 100, 0),
            TimeSpan.FromMilliseconds(200), Ease.Linear);

        double total = path.Sum(p => p.Delay.TotalMilliseconds);
        Assert.InRange(total, 190, 210);
    }

    [Fact]
    public void Path_RespectsTheSampleRate()
    {
        // 200ms at 120Hz is ~24 steps. Emitting a step per pixel would flood the queue on a long
        // move; emitting too few would skip the drag threshold entirely.
        var path = Interpolate.Path(new Point(V, 0, 0), new Point(V, 4000, 0),
            TimeSpan.FromMilliseconds(200), Ease.Linear, hz: 120);

        Assert.InRange(path.Count, 20, 28);
    }

    [Fact]
    public void Path_ZeroDuration_IsASingleTeleport()
    {
        var path = Interpolate.Path(new Point(V, 0, 0), new Point(V, 100, 50),
            TimeSpan.Zero, Ease.Linear);

        Assert.Single(path);
        Assert.Equal(new Point(V, 100, 50), path[0].At);
        Assert.Equal(TimeSpan.Zero, path[0].Delay);
    }

    [Fact]
    public void Path_SamePoint_StillEmitsOneStep()
    {
        var path = Interpolate.Path(new Point(V, 10, 10), new Point(V, 10, 10),
            TimeSpan.FromMilliseconds(100), Ease.Linear);

        Assert.Single(path);
        Assert.Equal(new Point(V, 10, 10), path[0].At);
    }

    [Fact]
    public void Path_EaseOut_Decelerates()
    {
        var path = Interpolate.Path(new Point(V, 0, 0), new Point(V, 1000, 0),
            TimeSpan.FromMilliseconds(200), Ease.EaseOut);

        int mid = path.Count / 2;
        Assert.True(path[mid].At.X > 500, "easeOut covers more than half the distance by the halfway point.");
    }

    [Fact]
    public void Path_EaseIn_Accelerates()
    {
        var path = Interpolate.Path(new Point(V, 0, 0), new Point(V, 1000, 0),
            TimeSpan.FromMilliseconds(200), Ease.EaseIn);

        int mid = path.Count / 2;
        Assert.True(path[mid].At.X < 500, "easeIn covers less than half the distance by the halfway point.");
    }

    [Fact]
    public void Path_AcrossFrames_Throws()
    {
        Assert.Throws<ArgumentException>(() => Interpolate.Path(
            new Point(Frame.Parse("monitor:1"), 0, 0),
            new Point(Frame.Parse("monitor:2"), 10, 10),
            TimeSpan.FromMilliseconds(100), Ease.Linear));
    }

    [Fact]
    public void Path_NegativeCoordinates_AreNotClamped()
    {
        // A point above a frame's own origin is negative in that frame. Interpolation is pure
        // arithmetic and validates nothing — Normalize is where the addressable range is
        // enforced — so a path through negative space must behave like any other.
        Frame m = Frame.Parse("monitor:1");

        var path = Interpolate.Path(new Point(m, 0, 0), new Point(m, 0, -1000),
            TimeSpan.FromMilliseconds(100), Ease.Linear);

        Assert.Equal(new Point(m, 0, -1000), path[^1].At);
        Assert.Contains(path, p => p.At.Y < 0);
    }
}
