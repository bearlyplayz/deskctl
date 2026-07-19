using WinDeskCtl.Core.Commands;
using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Tests;

public class DoctorReportTests
{
    private static readonly FrameRect AnyBounds =
        new(Frame.Parse("virtual"), 0, 0, 1920, 1080);

    private static DoctorReport ReportWith(params DoctorCheck[] checks) =>
        new(AnyBounds, [], checks);

    [Fact]
    public void Ok_IsTrue_WhenAllChecksPass()
    {
        Assert.True(ReportWith(new DoctorCheck("a", DoctorStatus.Pass, "")).Ok);
    }

    [Fact]
    public void Ok_IsFalse_WhenAnyCheckFails()
    {
        Assert.False(ReportWith(
            new DoctorCheck("a", DoctorStatus.Pass, ""),
            new DoctorCheck("b", DoctorStatus.Fail, "boom")).Ok);
    }

    [Fact]
    public void Ok_IsTrue_WhenChecksAreSkipped()
    {
        // A skip is "not applicable to this box", not a failure. But it must never be
        // silently treated as a pass either — a skip stays visible in the report.
        Assert.True(ReportWith(new DoctorCheck("b", DoctorStatus.Skip, "no such monitor")).Ok);
    }

    [Fact]
    public void Ok_IsTrue_WhenThereAreNoChecks()
    {
        Assert.True(ReportWith().Ok);
    }
}
