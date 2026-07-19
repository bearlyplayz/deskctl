using System.Text.Json;

namespace WinDeskCtl;

/// <summary>
/// Tells a caller's mistake apart from a fault in windeskctl.
/// </summary>
/// <remarks>
/// Shared by both surfaces on purpose. A refusal is part of the contract — snapshotting the whole
/// desktop, aiming a point off the desktop, a handle from a dead element — and the two surfaces
/// classifying it differently is exactly the drift the one-command/two-adapter split exists to
/// prevent. A caller error is reported as its message; anything else is a bug here and
/// keeps its stack trace.
/// </remarks>
internal static class CallerError
{
    public static bool Is(Exception ex) => ex is
        ArgumentException or          // covers ArgumentOutOfRangeException: a point outside the desktop
        FormatException or            // a malformed frame
        JsonException or              // a malformed step grammar
        NotSupportedException or      // a target that has no such capability
        InvalidOperationException or  // a window that vanished mid-call
        TimeoutException or           // an app that stopped answering UIA
        IOException;                  // a capture that could not be written
}
