using Deskctl.Core.Frames;
using Deskctl.Core.Windows;

namespace Deskctl.Core.Tests;

public class WindowFrameTests
{
    private static readonly Frame W = Frame.Parse("win:100");

    // Illustrative figures with a typical DWM border: the raw rect overhangs the visible one by
    // 7px on the left, right, and bottom, and by 0 at the top. Not this machine's measurements.
    private static readonly FrameRect Raw = new(W, OriginX: 93, OriginY: 100, W: 814, H: 607);
    private static readonly FrameRect Visible = new(W, OriginX: 100, OriginY: 100, W: 800, H: 600);

    [Fact]
    public void Measure_ReportsThePerEdgeOverhang()
    {
        BorderDelta d = WindowFrame.Measure(Raw, Visible);

        Assert.Equal(7, d.Left);     // 100 - 93
        Assert.Equal(0, d.Top);      // DWM adds no border at the top: the title bar is real content
        Assert.Equal(7, d.Right);    // (93+814) - (100+800)
        Assert.Equal(7, d.Bottom);
    }

    [Fact]
    public void Measure_HandlesAZeroBorder()
    {
        // A borderless window (many Electron and game windows) has identical rects.
        BorderDelta d = WindowFrame.Measure(Visible, Visible);
        Assert.Equal(new BorderDelta(0, 0, 0, 0), d);
    }

    [Fact]
    public void VisibleToRaw_ExpandsByTheDelta()
    {
        BorderDelta d = WindowFrame.Measure(Raw, Visible);
        Assert.Equal(Raw, WindowFrame.VisibleToRaw(Visible, d));
    }

    [Fact]
    public void RawToVisible_ShrinksByTheDelta()
    {
        BorderDelta d = WindowFrame.Measure(Raw, Visible);
        Assert.Equal(Visible, WindowFrame.RawToVisible(Raw, d));
    }

    [Fact]
    public void VisibleToRaw_RoundTrips()
    {
        BorderDelta d = WindowFrame.Measure(Raw, Visible);
        Assert.Equal(Visible, WindowFrame.RawToVisible(WindowFrame.VisibleToRaw(Visible, d), d));
    }

    [Fact]
    public void VisibleToRaw_IsWhatMakesAMoveLandWhereAsked()
    {
        // The payoff. Asking to place the window's VISIBLE left edge at x=500 means telling
        // SetWindowPos 493, because SetWindowPos speaks raw-rect space. Passing 500 straight
        // through puts the visible edge at 507 — the off-by-7 that R4 is about.
        BorderDelta d = WindowFrame.Measure(Raw, Visible);
        FrameRect wanted = Visible with { OriginX = 500, OriginY = 300 };

        FrameRect send = WindowFrame.VisibleToRaw(wanted, d);

        Assert.Equal(493, send.OriginX);
        Assert.Equal(300, send.OriginY);       // top delta is 0, so y passes through
        Assert.Equal(814, send.W);
    }

    [Fact]
    public void NegativeOrigins_SurviveTheConversion()
    {
        // A window on a monitor above primary has a negative origin; the delta is unaffected.
        BorderDelta d = new(7, 0, 7, 7);
        FrameRect visible = new(W, OriginX: -100, OriginY: -1100, W: 800, H: 600);

        FrameRect raw = WindowFrame.VisibleToRaw(visible, d);

        Assert.Equal(-107, raw.OriginX);
        Assert.Equal(-1100, raw.OriginY);
        Assert.Equal(visible, WindowFrame.RawToVisible(raw, d));
    }

    [Fact]
    public void Measure_AcrossDifferentFrames_Throws()
    {
        Assert.Throws<ArgumentException>(() => WindowFrame.Measure(
            Raw, Visible with { Frame = Frame.Parse("win:999") }));
    }
}
