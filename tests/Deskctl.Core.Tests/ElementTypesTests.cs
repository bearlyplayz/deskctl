using System.Text.Json;
using Deskctl.Core.Frames;
using Deskctl.Core.Json;
using Deskctl.Core.Uia;

namespace Deskctl.Core.Tests;

public class ElementTypesTests
{
    private static readonly FrameRect AnyRect = new(Frame.Parse("elem:btn-save"), 10, 20, 80, 24);

    private static ElementNode Leaf(string handle, string name) =>
        new(handle, "button", name, null, AnyRect, true, false, ["invoke"], []);

    // These assert field-by-field rather than with record equality. A record's synthesized
    // Equals compares IReadOnlyList members by reference, so a round-tripped node can never
    // equal its original no matter how faithful the serializer is.

    [Fact]
    public void ElementNode_RoundTripsThroughJson()
    {
        ElementNode original = new(
            "btn-save", "button", "Save", "saveBtn", AnyRect, true, false, ["invoke"],
            [Leaf("btn-inner", "Inner")]);

        string json = JsonSerializer.Serialize(original, DeskctlJson.Options);
        ElementNode? back = JsonSerializer.Deserialize<ElementNode>(json, DeskctlJson.Options);

        Assert.NotNull(back);
        Assert.Equal(original.Handle, back.Handle);
        Assert.Equal(original.ControlType, back.ControlType);
        Assert.Equal(original.Name, back.Name);
        Assert.Equal(original.AutomationId, back.AutomationId);
        Assert.Equal(original.Rect, back.Rect);
        Assert.Equal(original.IsEnabled, back.IsEnabled);
        Assert.Equal(original.IsOffscreen, back.IsOffscreen);
        Assert.Equal(original.Patterns, back.Patterns);

        // The tree shape is the payload; a round trip that flattened it would be useless.
        ElementNode child = Assert.Single(back.Children);
        Assert.Equal(original.Children[0].Handle, child.Handle);
        Assert.Equal(original.Children[0].Name, child.Name);
    }

    [Fact]
    public void ElementSelector_RoundTripsThroughJson()
    {
        ElementSelector original = new("button", "Save", "saveBtn", ["window:App", "pane:Main"]);

        string json = JsonSerializer.Serialize(original, DeskctlJson.Options);
        ElementSelector? back = JsonSerializer.Deserialize<ElementSelector>(json, DeskctlJson.Options);

        Assert.NotNull(back);
        Assert.Equal(original.ControlType, back.ControlType);
        Assert.Equal(original.Name, back.Name);
        Assert.Equal(original.AutomationId, back.AutomationId);

        // Ancestry is what disambiguates two identically-named siblings; losing its order
        // would silently widen every re-resolution.
        Assert.Equal(original.Ancestry, back.Ancestry);
    }

    [Fact]
    public void SnapshotInput_DefaultsToTheSemanticTree()
    {
        // The element tree is the default; --vision is the opt-in. A full-desktop capture on a
        // wide display is megabytes a vision model downscales into uselessness.
        SnapshotInput input = new("win:100");
        Assert.False(input.Vision);
        Assert.True(input.InteractiveOnly);
    }

    [Fact]
    public void SnapshotResult_ReportsTruncation()
    {
        // A truncated tree that does not say so is a tree the caller will believe is complete,
        // and they will conclude the element they wanted does not exist.
        SnapshotResult result = new(AnyRect, Leaf("a", "A"), ElementCount: 500, Truncated: true);
        Assert.True(result.Truncated);
    }
}
