namespace WinDeskCtl.Core.Frames;

/// <summary>
/// A coordinate space. Every image carries one and every input names one, so an
/// image and an input can never disagree about which space they are in.
/// </summary>
/// <remarks>
/// The wire format is a string because it crosses JSON to an LLM, which composes it
/// by hand. A closed hierarchy with a private constructor keeps the set of frames
/// exhaustive: adding a case is a deliberate edit here, not an accident elsewhere.
/// </remarks>
public abstract record Frame
{
    private Frame() { }

    /// <summary>The whole virtual desktop. Its origin is negative whenever a monitor sits above or left of primary.</summary>
    public sealed record Virtual : Frame
    {
        public override string ToString() => "virtual";
    }

    public sealed record Monitor(string Id) : Frame
    {
        public override string ToString() => $"monitor:{Id}";
    }

    public sealed record Window(long Hwnd) : Frame
    {
        public override string ToString() => $"win:{Hwnd}";
    }

    /// <summary>
    /// A UIA element. The handle is an opaque session-scoped token minted by Snapshot, never a
    /// RuntimeId — RuntimeIds churn when the tree rebuilds. Callers must not construct these.
    /// </summary>
    public sealed record Element(string Handle) : Frame
    {
        public override string ToString() => $"elem:{Handle}";
    }

    /// <summary>
    /// A captured image. The handle is an opaque session-scoped token minted by a capture; a
    /// point in this frame is a pixel coordinate read directly off that image, and translation
    /// applies the capture's recorded scale — the caller never converts image pixels to screen
    /// pixels itself. The rect describes where the target was at capture time, so a window that
    /// moves afterwards makes the frame stale. Callers must not construct these.
    /// </summary>
    public sealed record Image(string Handle) : Frame
    {
        public override string ToString() => $"img:{Handle}";
    }

    public static bool TryParse(string s, out Frame? frame)
    {
        frame = null;
        if (string.IsNullOrEmpty(s)) return false;

        if (s == "virtual")
        {
            frame = new Virtual();
            return true;
        }

        int colon = s.IndexOf(':');
        if (colon <= 0 || colon == s.Length - 1) return false;

        string prefix = s[..colon];
        string arg = s[(colon + 1)..];

        switch (prefix)
        {
            case "monitor":
                frame = new Monitor(arg);
                return true;
            case "win":
                if (!long.TryParse(arg, out long hwnd)) return false;
                frame = new Window(hwnd);
                return true;
            case "elem":
                frame = new Element(arg);
                return true;
            case "img":
                frame = new Image(arg);
                return true;
            default:
                return false;
        }
    }

    public static Frame Parse(string s) =>
        TryParse(s, out Frame? f) && f is not null
            ? f
            : throw new FormatException(
                $"'{s}' is not a frame. Expected 'virtual', 'monitor:<id>', 'win:<hwnd>', " +
                "'elem:<handle>', or 'img:<handle>'.");
}
