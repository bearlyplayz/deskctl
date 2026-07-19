using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Tests;

public class FrameTests
{
    [Theory]
    [InlineData("virtual")]
    [InlineData("monitor:1")]
    [InlineData("monitor:DISPLAY2")]
    [InlineData("win:12345")]
    [InlineData("elem:btn-save")]
    public void Parse_RoundTripsThroughToString(string wire)
    {
        Assert.Equal(wire, Frame.Parse(wire).ToString());
    }

    [Fact]
    public void Parse_Virtual_YieldsVirtual()
    {
        Assert.IsType<Frame.Virtual>(Frame.Parse("virtual"));
    }

    [Fact]
    public void Parse_Window_ExtractsHwnd()
    {
        Frame.Window w = Assert.IsType<Frame.Window>(Frame.Parse("win:12345"));
        Assert.Equal(12345L, w.Hwnd);
    }

    [Fact]
    public void Parse_Element_HandleMayContainHyphens()
    {
        Frame.Element e = Assert.IsType<Frame.Element>(Frame.Parse("elem:btn-save"));
        Assert.Equal("btn-save", e.Handle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("monitor:")]          // empty id
    [InlineData("win:")]              // empty hwnd
    [InlineData("win:notanumber")]
    [InlineData("elem:")]             // empty handle
    [InlineData("virtual:1")]         // virtual takes no argument
    public void Parse_Malformed_Throws(string wire)
    {
        Assert.Throws<FormatException>(() => Frame.Parse(wire));
    }

    [Fact]
    public void Records_CompareByValue()
    {
        // Frames are used as dictionary keys and compared in Translate; value
        // equality is relied upon rather than incidental.
        Assert.Equal(Frame.Parse("monitor:1"), Frame.Parse("monitor:1"));
        Assert.NotEqual(Frame.Parse("monitor:1"), Frame.Parse("monitor:2"));
    }
}
