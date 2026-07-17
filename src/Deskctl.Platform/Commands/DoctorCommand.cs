using System.Runtime.InteropServices;
using Deskctl.Core.Capture;
using Deskctl.Core.Commands;
using Deskctl.Core.Frames;
using Deskctl.Core.Input;
using Deskctl.Core.Uia;
using Deskctl.Core.Windows;
using Deskctl.Platform.Displays;
using Deskctl.Platform.Interop;
using Deskctl.Platform.Windows;
using Point = Deskctl.Core.Frames.Point;

namespace Deskctl.Platform.Commands;

/// <summary>
/// Self-test against the live machine. Display topology, DPI, and drag thresholds are
/// physical-world variables that must be measured rather than hardcoded, so this
/// is the one place machine-specific facts are allowed to exist — at runtime, never in the repo.
/// </summary>
public sealed partial class DoctorCommand : ICommand<DoctorInput, DoctorReport>
{
    public async Task<DoctorReport> RunAsync(DoctorInput input, CancellationToken ct)
    {
        DisplayEnumerator.EnsurePerMonitorV2();

        FrameRect virtualBounds = DisplayEnumerator.GetVirtualBounds();
        IReadOnlyList<MonitorInfo> monitors = DisplayEnumerator.GetMonitors();

        List<DoctorCheck> checks =
        [
            CheckVirtualBoundsContainAllMonitors(virtualBounds, monitors),
            CheckNegativeOriginMonitorIsReachable(virtualBounds, monitors),
            ReportDragThresholds(),
            ReportPointerAcceleration(),
            CheckWgcSupported(),
            await CheckOccludedCaptureIsNotBlackAsync(ct),
            CheckDwmBorderDelta(),
            CheckStuckModifiers(),
            await CheckUiaTreeIsRicherThanHwndsAsync(ct),
        ];

        checks.AddRange(monitors.Select(m => CheckCursorRoundTrip(m, virtualBounds)));

        if (input.IncludeIntrusive)
        {
            checks.Add(await CheckUnwindSideEffectAsync(ct));
        }

        return new DoctorReport(virtualBounds, monitors, checks);
    }

    private static DoctorCheck CheckWgcSupported() =>
        Capture.WgcCapture.IsSupported
            ? new DoctorCheck("wgc-supported", DoctorStatus.Pass, "Windows.Graphics.Capture available")
            : new DoctorCheck("wgc-supported", DoctorStatus.Fail,
                "Windows.Graphics.Capture unavailable — requires Windows 10 1903 or later");

    /// <summary>
    /// The check that separates WGC from GDI: capture the desktop's own window, which is always
    /// present and always has other windows in front of it. GDI BitBlt returns black here; WGC
    /// does not. A pass also proves the read-back vtable slots are right — wrong slots surface
    /// as garbage or a crash, never as a build error.
    /// </summary>
    private static async Task<DoctorCheck> CheckOccludedCaptureIsNotBlackAsync(CancellationToken ct)
    {
        const string name = "occluded-capture-not-black";

        nint shell = GetShellWindow();
        if (shell == 0)
        {
            return new DoctorCheck(name, DoctorStatus.Skip, "no shell window on this session");
        }

        try
        {
            using CaptureCommand capture = new();
            CaptureResult result = await capture.RunAsync(
                new CaptureInput(new Frame.Window(shell), MaxWidth: 64), ct);

            // The image is PNG, so "all black" cannot be read from the bytes directly. A black
            // 64px PNG compresses to a few hundred bytes; anything with real content exceeds it.
            return result.Bytes.Length > 512
                ? new DoctorCheck(name, DoctorStatus.Pass,
                    $"captured {result.Rect.W}x{result.Rect.H}, {result.Bytes.Length} bytes")
                : new DoctorCheck(name, DoctorStatus.Fail,
                    $"capture produced {result.Bytes.Length} bytes — likely a blank frame");
        }
        catch (Exception ex)
        {
            return new DoctorCheck(name, DoctorStatus.Fail, ex.Message);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vk);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    /// <summary>
    /// Reports modifiers currently held. This exists for the one case the held-set cannot cover:
    /// a session killed outright, where no handler ran and a key was left down.
    /// </summary>
    /// <remarks>
    /// Reports rather than clears. GetAsyncKeyState cannot distinguish input this process injected
    /// from the user's physical keyboard, so clearing would release a key the user is genuinely
    /// holding right now. A human reading "ctrl is down" can decide; deskctl
    /// cannot.
    /// </remarks>
    private static DoctorCheck CheckStuckModifiers()
    {
        string[] modifiers = ["ctrl", "shift", "alt", "win"];

        List<string> down = [.. modifiers.Where(m => (GetAsyncKeyState(KeyMap.Resolve(m)) & 0x8000) != 0)];

        return down.Count == 0
            ? new DoctorCheck("stuck-modifiers", DoctorStatus.Pass, "no modifiers held")
            : new DoctorCheck("stuck-modifiers", DoctorStatus.Fail,
                $"held: {string.Join(", ", down)} — if you are not physically holding these, a " +
                "previous session died without unwinding; press and release each to clear");
    }

    /// <summary>
    /// Verifies whether an auto-unwound 'win' actually opens the Start menu.
    /// </summary>
    /// <remarks>
    /// Behind an opt-in flag because it is genuinely disruptive: it pops the Start menu and steals
    /// focus. That is also the point — if it does, the unwind is not inert, and the released:
    /// report is load-bearing rather than cosmetic.
    ///
    /// Do NOT "fix" a positive result by suppressing the Start menu. The unwind reproducing real
    /// key behaviour is correct — a human doing the same gets the same — and papering over it
    /// would hide the caller bug that a dangling 'down win' represents.
    /// </remarks>
    private static async Task<DoctorCheck> CheckUnwindSideEffectAsync(CancellationToken ct)
    {
        const string name = "unwind-side-effect";

        nint before = GetForegroundWindow();

        InputResult result = await new InputCommand().RunAsync(
            new InputRequest([new Step.Down(new KeyRef("win"))]), ct);

        // The shell needs a moment to react before the foreground window is re-read.
        await Task.Delay(TimeSpan.FromMilliseconds(400), ct);

        nint after = GetForegroundWindow();
        bool focusMoved = before != after;

        if (focusMoved)
        {
            // Close whatever opened, so doctor leaves the desktop as it found it.
            await new InputCommand().RunAsync(new InputRequest([new Step.Press(new KeyRef("esc"))]), ct);
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }

        string released = string.Join(", ", result.Released);

        return new DoctorCheck(name, DoctorStatus.Pass,
            focusMoved
                ? $"CONFIRMED: unwinding a dangling 'win' ({released}) moved focus — the unwind fires " +
                  "real key behaviour and is not inert. The released: report is how callers learn this."
                : $"unwinding 'win' ({released}) did not move focus on this shell version — but do not " +
                  "rely on it; suppression semantics are shell-version-dependent.");
    }

    /// <summary>
    /// Asserts the premise of the semantic tier: that UIA sees elements where HWND enumeration
    /// sees only window shells.
    /// </summary>
    /// <remarks>
    /// Measured against the foreground window rather than asserted. If the two counts are
    /// comparable, UIA is not attaching and the whole semantic tier is running on nothing.
    ///
    /// Walks the unfiltered tree, not the interactive-only one. The comparison is between two
    /// ways of PERCEIVING a window, so both sides must be unfiltered to mean anything. A classic
    /// Win32 window is the case that makes this matter: every one of its controls is its own
    /// HWND, so EnumChildWindows does well there and an interactive-only UIA count could
    /// legitimately come in lower — failing the check on a window where nothing is wrong.
    /// </remarks>
    private static async Task<DoctorCheck> CheckUiaTreeIsRicherThanHwndsAsync(CancellationToken ct)
    {
        const string name = "uia-tree-richer-than-hwnds";

        nint hwnd = GetForegroundWindow();
        if (hwnd == 0)
        {
            return new DoctorCheck(name, DoctorStatus.Skip, "no foreground window");
        }

        try
        {
            int childHwnds = 0;
            User32.EnumChildWindows(hwnd, (_, _) => { childHwnds++; return 1; }, 0);

            SnapshotResult snapshot = await new SnapshotCommand().RunAsync(
                new SnapshotInput($"win:{hwnd}", MaxDepth: 6, Vision: false, InteractiveOnly: false), ct);

            return snapshot.ElementCount > childHwnds
                ? new DoctorCheck(name, DoctorStatus.Pass,
                    $"UIA found {snapshot.ElementCount} element(s) where EnumChildWindows found {childHwnds} HWND(s)")
                : new DoctorCheck(name, DoctorStatus.Fail,
                    $"UIA found only {snapshot.ElementCount} element(s) against {childHwnds} HWND(s) — " +
                    "UIA is not attaching, or this window genuinely has no tree");
        }
        catch (Exception ex)
        {
            return new DoctorCheck(name, DoctorStatus.Fail, ex.Message);
        }
    }

    /// <summary>
    /// Asserts that DWMWA_EXTENDED_FRAME_BOUNDS actually differs from GetWindowRect, which is the
    /// premise of the whole Windows tier.
    /// </summary>
    /// <remarks>
    /// Measured against a real window rather than asserted from a constant, because the delta
    /// varies with window style: a borderless window has none. The check therefore looks for at
    /// least one window with a non-zero delta rather than requiring a specific number — finding
    /// none on a desktop full of normal windows means DwmGetWindowAttribute is not being honoured
    /// and every reported rect is the oversized one.
    /// </remarks>
    private static DoctorCheck CheckDwmBorderDelta()
    {
        const string name = "dwm-border-delta";

        try
        {
            IReadOnlyList<WindowInfo> windows = WindowEnumerator.List(includeMinimized: false);
            if (windows.Count == 0)
            {
                return new DoctorCheck(name, DoctorStatus.Skip, "no visible windows to measure");
            }

            List<BorderDelta> deltas = [];
            foreach (WindowInfo w in windows)
            {
                try
                {
                    deltas.Add(WindowGeometry.GetBorderDelta((nint)w.Hwnd));
                }
                catch (InvalidOperationException)
                {
                    // The window closed mid-check; the remaining sample is still valid.
                }
            }

            if (deltas.Count == 0)
            {
                return new DoctorCheck(name, DoctorStatus.Skip, "no window survived long enough to measure");
            }

            BorderDelta[] bordered = [.. deltas.Where(d => d.Left != 0 || d.Right != 0 || d.Bottom != 0)];

            if (bordered.Length == 0)
            {
                return new DoctorCheck(name, DoctorStatus.Fail,
                    $"all {deltas.Count} window(s) report a zero border — DWMWA_EXTENDED_FRAME_BOUNDS is " +
                    "not being honoured, so every reported rect is the oversized GetWindowRect one");
            }

            BorderDelta sample = bordered[0];
            return new DoctorCheck(name, DoctorStatus.Pass,
                $"{bordered.Length}/{deltas.Count} window(s) have an invisible border; " +
                $"e.g. l{sample.Left} t{sample.Top} r{sample.Right} b{sample.Bottom}");
        }
        catch (InvalidOperationException ex)
        {
            return new DoctorCheck(name, DoctorStatus.Fail, ex.Message);
        }
    }

    /// <summary>The invariant that makes Translate's virtual-desktop pivot valid.</summary>
    private static DoctorCheck CheckVirtualBoundsContainAllMonitors(
        FrameRect virt, IReadOnlyList<MonitorInfo> monitors)
    {
        List<string> outside = [];
        foreach (MonitorInfo m in monitors)
        {
            bool contained =
                m.Bounds.OriginX >= virt.OriginX &&
                m.Bounds.OriginY >= virt.OriginY &&
                m.Bounds.OriginX + m.Bounds.W <= virt.OriginX + virt.W &&
                m.Bounds.OriginY + m.Bounds.H <= virt.OriginY + virt.H;
            if (!contained) outside.Add(m.Id);
        }

        return outside.Count == 0
            ? new DoctorCheck("virtual-bounds-contain-monitors", DoctorStatus.Pass,
                $"{monitors.Count} monitor(s) within virtual bounds")
            : new DoctorCheck("virtual-bounds-contain-monitors", DoctorStatus.Fail,
                $"monitor(s) outside virtual bounds: {string.Join(", ", outside)}");
    }

    /// <summary>
    /// The exact case the common stack cannot reach: a monitor at a negative virtual origin.
    /// Asserts the point is addressable in virtual space rather than clamping to zero
    ///.
    /// </summary>
    private static DoctorCheck CheckNegativeOriginMonitorIsReachable(
        FrameRect virt, IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo? negative = monitors.FirstOrDefault(
            m => m.Bounds.OriginX < 0 || m.Bounds.OriginY < 0);

        if (negative is null)
        {
            return new DoctorCheck("negative-origin-monitor", DoctorStatus.Skip,
                "no monitor at a negative virtual origin on this machine");
        }

        Point topLeft = new(negative.Bounds.Frame, 0, 0);
        Point inVirtual = Translate.To(topLeft, negative.Bounds, virt);

        return virt.Contains(inVirtual)
            ? new DoctorCheck("negative-origin-monitor", DoctorStatus.Pass,
                $"monitor '{negative.Id}' top-left resolves inside virtual bounds")
            : new DoctorCheck("negative-origin-monitor", DoctorStatus.Fail,
                $"monitor '{negative.Id}' top-left resolves outside virtual bounds — coordinate maths is wrong");
    }

    /// <summary>
    /// Round-trips the cursor through each monitor's centre. This is the check that fails today
    /// on any tool normalizing against the primary display only.
    /// </summary>
    private static DoctorCheck CheckCursorRoundTrip(MonitorInfo m, FrameRect virt)
    {
        string name = $"cursor-round-trip:{m.Id}";

        Point centre = new(m.Bounds.Frame, m.Bounds.W / 2, m.Bounds.H / 2);
        Point target = Translate.To(centre, m.Bounds, virt);
        (int screenX, int screenY) = ScreenCoords.ToScreen(target, virt);

        if (!Cursor.GetCursorPos(out Cursor.POINT original))
        {
            return new DoctorCheck(name, DoctorStatus.Fail, "GetCursorPos failed");
        }

        try
        {
            if (!Cursor.SetCursorPos(screenX, screenY))
            {
                return new DoctorCheck(name, DoctorStatus.Fail, "SetCursorPos failed");
            }
            if (!Cursor.GetCursorPos(out Cursor.POINT actual))
            {
                return new DoctorCheck(name, DoctorStatus.Fail, "GetCursorPos failed after move");
            }

            // Exact equality: SetCursorPos is not subject to pointer acceleration, so any
            // discrepancy is a coordinate-space error, not rounding. Windows clamps a request
            // that lands on no monitor, so a mismatch here means the maths put it in the void.
            return actual.X == screenX && actual.Y == screenY
                ? new DoctorCheck(name, DoctorStatus.Pass, "cursor reached the monitor's centre")
                : new DoctorCheck(name, DoctorStatus.Fail,
                    $"asked for screen {screenX},{screenY} but landed at {actual.X},{actual.Y}");
        }
        finally
        {
            // doctor must not leave the user's cursor parked on another monitor.
            Cursor.SetCursorPos(original.X, original.Y);
        }
    }

    /// <summary>
    /// Drag thresholds are a calibration input, not a constant: a drag does not begin until the
    /// pointer passes them while a button is down. Reported, never asserted.
    /// </summary>
    private static DoctorCheck ReportDragThresholds()
    {
        int cx = User32.GetSystemMetrics(User32.SM_CXDRAGWIDTH);
        int cy = User32.GetSystemMetrics(User32.SM_CYDRAGHEIGHT);
        return new DoctorCheck("drag-thresholds", DoctorStatus.Pass, $"{cx}x{cy} px");
    }

    /// <summary>
    /// Reports pointer acceleration: the speed slider, and whether "enhance pointer precision" is
    /// on along with the thresholds that drive it.
    /// </summary>
    /// <remarks>
    /// Reported, never asserted, and nothing in phase 1 reads it — absolute pointer moves carry
    /// their destination in the event, so acceleration cannot deflect them. It is recorded because
    /// it is the calibration input a relative move would need: moveRel is subject to acceleration,
    /// so it needs a measured factor rather than an assumed 1:1. Measuring it here means
    /// the number is on hand from a real machine when that lands, instead of being guessed.
    /// </remarks>
    private static unsafe DoctorCheck ReportPointerAcceleration()
    {
        const string name = "pointer-acceleration";

        int speed;
        // Three ints: x threshold, y threshold, and the acceleration enable flag.
        int* mouse = stackalloc int[3];

        if (!User32.SystemParametersInfo(User32.SPI_GETMOUSESPEED, 0, &speed, 0) ||
            !User32.SystemParametersInfo(User32.SPI_GETMOUSE, 0, mouse, 0))
        {
            return new DoctorCheck(name, DoctorStatus.Fail,
                $"SystemParametersInfo failed ({Marshal.GetLastWin32Error()})");
        }

        string accel = mouse[2] != 0
            ? $"on (thresholds {mouse[0]}/{mouse[1]})"
            : "off";

        return new DoctorCheck(name, DoctorStatus.Pass, $"speed {speed}/20, enhance-precision {accel}");
    }
}
