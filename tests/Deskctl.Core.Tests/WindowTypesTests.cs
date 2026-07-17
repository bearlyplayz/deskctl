using System.Text.Json;
using Deskctl.Core.Frames;
using Deskctl.Core.Json;
using Deskctl.Core.Windows;

namespace Deskctl.Core.Tests;

public class WindowTypesTests
{
    private static readonly FrameRect AnyRect = new(Frame.Parse("win:100"), 0, 0, 800, 600);

    [Fact]
    public void WindowInfo_RoundTripsThroughJson()
    {
        WindowInfo original = new(100, "t", "p", 42, AnyRect, WindowState.Maximized, IsForeground: true);

        string json = JsonSerializer.Serialize(original, DeskctlJson.Options);
        WindowInfo? back = JsonSerializer.Deserialize<WindowInfo>(json, DeskctlJson.Options);

        Assert.Equal(original, back);
    }

    [Fact]
    public void WindowState_SerializesAsAString()
    {
        // The consumer is an LLM reading the schema; "maximized" is legible, 2 is not.
        string json = JsonSerializer.Serialize(
            new WindowInfo(1, "t", "p", 1, AnyRect, WindowState.Maximized, false), DeskctlJson.Options);
        Assert.Contains("\"maximized\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowListInput_DefaultsToNoFilter()
    {
        WindowListInput input = new();
        Assert.Null(input.TitleContains);
        Assert.Null(input.ProcessName);
        Assert.True(input.IncludeMinimized);
    }

    [Fact]
    public void WindowActionInput_RoundTripsThroughJson()
    {
        WindowActionInput original = new(100, WindowAction.Move, X: 10, Y: -1100);

        string json = JsonSerializer.Serialize(original, DeskctlJson.Options);
        WindowActionInput? back = JsonSerializer.Deserialize<WindowActionInput>(json, DeskctlJson.Options);

        Assert.Equal(original, back);
        Assert.Equal(-1100, back!.Y);   // negative coordinates survive the round trip
    }
}
