using System.Text.Json;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Json;

namespace WinDeskCtl.Core.Tests;

public class JsonContextTests
{
    [Fact]
    public void Frame_SerializesAsItsWireString()
    {
        string json = JsonSerializer.Serialize(Frame.Parse("monitor:1"), WinDeskCtlJson.Options);
        Assert.Equal("\"monitor:1\"", json);
    }

    [Fact]
    public void Frame_RoundTrips()
    {
        Frame original = Frame.Parse("win:12345");
        string json = JsonSerializer.Serialize(original, WinDeskCtlJson.Options);
        Frame? back = JsonSerializer.Deserialize<Frame>(json, WinDeskCtlJson.Options);
        Assert.Equal(original, back);
    }

    [Fact]
    public void FrameRect_RoundTrips()
    {
        FrameRect original = new(Frame.Parse("monitor:2"), -1200, -1200, 1920, 1200, 0.5);
        string json = JsonSerializer.Serialize(original, WinDeskCtlJson.Options);
        FrameRect? back = JsonSerializer.Deserialize<FrameRect>(json, WinDeskCtlJson.Options);
        Assert.Equal(original, back);
    }

    [Fact]
    public void Frame_MalformedString_Throws()
    {
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<Frame>("\"bogus\"", WinDeskCtlJson.Options));
    }
}
