using Deskctl.Core.Frames;

namespace Deskctl.Core.Input;

/// <summary>
/// Parses a frame-qualified point: "win:100@400,200", or "elem:btn-save" for the element's centre.
/// </summary>
/// <remarks>
/// Omitting the coordinates is the common case and the safest one — clicking an element's centre
/// needs no arithmetic from the caller and cannot drift when the element moves.
/// </remarks>
public static class PointRef
{
    /// <summary>Sentinel meaning "the centre of the frame", resolved once the frame's rect is known.</summary>
    public const int Centre = int.MinValue;

    public static (Frame Frame, int X, int Y) Parse(string s)
    {
        if (string.IsNullOrEmpty(s)) throw new FormatException("A point reference is required.");

        int at = s.LastIndexOf('@');
        if (at < 0) return (Frame.Parse(s), Centre, Centre);
        if (at == 0) throw new FormatException($"'{s}' has no frame before the '@'.");

        Frame frame = Frame.Parse(s[..at]);
        string[] parts = s[(at + 1)..].Split(',');

        if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
        {
            throw new FormatException(
                $"'{s}' is not a point. Expected '<frame>@<x>,<y>' — for example 'win:100@400,200' — " +
                "or just '<frame>' for its centre.");
        }

        return (frame, x, y);
    }

    public static bool IsCentre((Frame Frame, int X, int Y) p) => p.X == Centre && p.Y == Centre;
}
