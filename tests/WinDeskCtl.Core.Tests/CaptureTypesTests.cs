using System.Text.Json;
using WinDeskCtl.Core.Capture;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Json;

namespace WinDeskCtl.Core.Tests;

public class CaptureTypesTests
{
    [Fact]
    public void CaptureInput_DefaultsToPng()
    {
        // PNG is the default because JPEG is lossy AND larger on UI content, and token cost
        // tracks pixels rather than bytes.
        CaptureInput input = new(Frame.Parse("monitor:1"));
        Assert.Equal(ImageFormat.Png, input.Format);
        Assert.Null(input.MaxWidth);
    }

    [Fact]
    public void CaptureInput_RoundTripsThroughJson()
    {
        CaptureInput original = new(
            Frame.Parse("win:12345"),
            Region: new CropBox(10, 20, 300, 400),
            MaxWidth: 960,
            Format: ImageFormat.Jpeg,
            Quality: 75);

        string json = JsonSerializer.Serialize(original, WinDeskCtlJson.Options);
        CaptureInput? back = JsonSerializer.Deserialize<CaptureInput>(json, WinDeskCtlJson.Options);

        Assert.Equal(original, back);
    }

    [Fact]
    public void CaptureInput_FormatSerializesAsAString()
    {
        // The consumer is an LLM reading the schema; "jpeg" is legible, 1 is not.
        string json = JsonSerializer.Serialize(
            new CaptureInput(Frame.Parse("virtual"), Format: ImageFormat.Jpeg), WinDeskCtlJson.Options);
        Assert.Contains("\"jpeg\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MimeType_MatchesTheFormat()
    {
        FrameRect rect = new(Frame.Parse("monitor:1"), 0, 0, 1, 1);
        Assert.Equal("image/png", new CaptureResult(rect, ImageFormat.Png, [1]).MimeType);
        Assert.Equal("image/jpeg", new CaptureResult(rect, ImageFormat.Jpeg, [1]).MimeType);
    }

    /// <summary>
    /// Validation lives on the record because both surfaces build it, so neither can route
    /// around it. WIC takes quality as a raw ratio and does not reject a nonsensical one — it
    /// silently encodes a ruined image.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(101)]
    [InlineData(9999)]
    public void CaptureInput_RejectsQualityOutsideOneToHundred(int quality)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CaptureInput(new Frame.Virtual(), Quality: quality));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void CaptureInput_AcceptsQualityAtTheBounds(int quality)
    {
        Assert.Equal(quality, new CaptureInput(new Frame.Virtual(), Quality: quality).Quality);
    }

    [Fact]
    public void CaptureInput_RejectsANonPositiveDownscaleCap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureInput(new Frame.Virtual(), MaxWidth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureInput(new Frame.Virtual(), MaxHeight: -1));
    }
}
