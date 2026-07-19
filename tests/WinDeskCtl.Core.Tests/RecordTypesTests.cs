using WinDeskCtl.Core.Capture;
using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Tests;

public class RecordTypesTests
{
    /// <summary>
    /// The reason presets exist instead of free fps and duration: no pairing can produce a burst
    /// large enough to fill a disk or a context window. If a preset is ever added or retuned past
    /// this ceiling, that guarantee is gone.
    /// </summary>
    [Theory]
    [InlineData(RecordPreset.Slow)]
    [InlineData(RecordPreset.Medium)]
    [InlineData(RecordPreset.Fast)]
    [InlineData(RecordPreset.Instant)]
    public void EveryPreset_StaysWithinTheFrameCap(RecordPreset preset)
    {
        Assert.InRange(preset.FrameCount(), 1, 30);
    }

    [Fact]
    public void FrameCount_IsRateTimesDuration()
    {
        Assert.Equal(30, RecordPreset.Slow.FrameCount());     // 3fps * 10s
        Assert.Equal(30, RecordPreset.Medium.FrameCount());   // 6fps * 5s
        Assert.Equal(9, RecordPreset.Fast.FrameCount());      // 9fps * 1s
        Assert.Equal(6, RecordPreset.Instant.FrameCount());   // 12fps * 0.5s
    }

    [Theory]
    [InlineData(RecordPreset.Slow)]
    [InlineData(RecordPreset.Instant)]
    public void EveryPresetRate_StaysUnderTypicalMonitorRefresh(RecordPreset preset)
    {
        // Below refresh means every frame is a distinct repaint, not a duplicate of the last.
        (int fps, _) = preset.Timing();
        Assert.True(fps <= 60);
    }

    [Fact]
    public void RecordInput_RejectsAnEmptyOutputDir()
    {
        Assert.Throws<ArgumentException>(() => new RecordInput(new Frame.Virtual(), OutputDir: "  "));
    }

    [Fact]
    public void RecordInput_DefaultsToFast()
    {
        Assert.Equal(RecordPreset.Fast, new RecordInput(new Frame.Virtual(), "out").Preset);
    }
}
