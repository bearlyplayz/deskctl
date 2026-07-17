namespace Deskctl.Core.Frames;

/// <summary>A point that knows which coordinate space it lives in.</summary>
public readonly record struct Point(Frame Frame, int X, int Y)
{
    public override string ToString() => $"{Frame}@{X},{Y}";
}
