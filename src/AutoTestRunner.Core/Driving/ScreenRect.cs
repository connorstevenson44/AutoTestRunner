namespace AutoTestRunner.Driving;

/// <summary>A rectangle on the screen, in pixels.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public bool Overlaps(ScreenRect other) =>
        !(other.Right < Left || other.Left > Right || other.Bottom < Top || other.Top > Bottom);

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}
