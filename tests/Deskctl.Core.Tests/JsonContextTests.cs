using System.Text.Json;
using Deskctl.Core.Frames;
using Deskctl.Core.Json;

namespace Deskctl.Core.Tests;

public class JsonContextTests
{
    [Fact]
    public void Frame_SerializesAsItsWireString()
    {
        string json = JsonSerializer.Serialize(Frame.Parse("monitor:1"), DeskctlJson.Options);
        Assert.Equal("\"monitor:1\"", json);
    }

    [Fact]
    public void Frame_RoundTrips()
    {
        Frame original = Frame.Parse("win:12345");
        string json = JsonSerializer.Serialize(original, DeskctlJson.Options);
        Frame? back = JsonSerializer.Deserialize<Frame>(json, DeskctlJson.Options);
        Assert.Equal(original, back);
    }

    [Fact]
    public void FrameRect_RoundTrips()
    {
        FrameRect original = new(Frame.Parse("monitor:2"), -1200, -1200, 1920, 1200, 0.5);
        string json = JsonSerializer.Serialize(original, DeskctlJson.Options);
        FrameRect? back = JsonSerializer.Deserialize<FrameRect>(json, DeskctlJson.Options);
        Assert.Equal(original, back);
    }

    [Fact]
    public void Frame_MalformedString_Throws()
    {
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<Frame>("\"bogus\"", DeskctlJson.Options));
    }
}
