using Deskctl.Core.Frames;
using Deskctl.Core.Input;

namespace Deskctl.Core.Tests;

public class PointRefTests
{
    [Fact]
    public void Parse_SplitsTheFrameFromTheCoordinates()
    {
        (Frame frame, int x, int y) = PointRef.Parse("win:100@400,200");

        Assert.Equal(new Frame.Window(100), frame);
        Assert.Equal(400, x);
        Assert.Equal(200, y);
    }

    [Fact]
    public void Parse_AcceptsNegativeCoordinates()
    {
        // A point may sit above or left of its frame's own origin — a window straddling a
        // monitor boundary has content at negative offsets from the neighbouring frame.
        (Frame frame, int x, int y) = PointRef.Parse("virtual@-100,-1100");

        Assert.IsType<Frame.Virtual>(frame);
        Assert.Equal(-100, x);
        Assert.Equal(-1100, y);
    }

    [Fact]
    public void Parse_WithoutCoordinates_MeansTheFramesCentre()
    {
        // "elem:btn-save" with no @ is the common case: click the middle of the element.
        (Frame frame, int x, int y) = PointRef.Parse("elem:btn-save");

        Assert.Equal(new Frame.Element("btn-save"), frame);
        Assert.Equal(int.MinValue, x);   // the centre sentinel
        Assert.Equal(int.MinValue, y);
    }

    [Fact]
    public void IsCentre_DetectsTheSentinel()
    {
        Assert.True(PointRef.IsCentre(PointRef.Parse("elem:btn-save")));
        Assert.False(PointRef.IsCentre(PointRef.Parse("elem:btn-save@1,1")));
    }

    [Theory]
    [InlineData("win:100@400")]        // one coordinate
    [InlineData("win:100@400,200,3")]  // three
    [InlineData("win:100@a,b")]        // not numbers
    [InlineData("win:100@")]           // empty
    [InlineData("@400,200")]           // no frame
    public void Parse_Malformed_Throws(string s)
    {
        Assert.Throws<FormatException>(() => PointRef.Parse(s));
    }
}
