using Deskctl.Core.Frames;

namespace Deskctl.Core.Commands;

public enum DoctorStatus
{
    Pass,
    Fail,

    /// <summary>
    /// The check does not apply to this machine — e.g. a negative-origin monitor probe on a
    /// single-monitor box. Reported explicitly rather than passed silently: a check that
    /// never ran is not a check that succeeded.
    /// </summary>
    Skip,
}

public sealed record DoctorCheck(string Name, DoctorStatus Status, string Detail);

public sealed record MonitorInfo(string Id, FrameRect Bounds, bool IsPrimary, int Dpi);

public sealed record DoctorInput(bool IncludeIntrusive = false);

/// <summary>
/// The result of a self-test against real hardware. Every fact here is measured on the machine
/// it ran on — DPI, display topology, drag thresholds — so the report is machine-specific and
/// git-ignored by design.
/// </summary>
public sealed record DoctorReport(
    FrameRect VirtualBounds,
    IReadOnlyList<MonitorInfo> Monitors,
    IReadOnlyList<DoctorCheck> Checks)
{
    public bool Ok => Checks.All(c => c.Status != DoctorStatus.Fail);
}
