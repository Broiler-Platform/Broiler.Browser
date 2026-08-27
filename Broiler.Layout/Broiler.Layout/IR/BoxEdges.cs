namespace Broiler.Layout.IR;

public sealed class BoxEdges(double top, double right, double bottom, double left)
{
    public static BoxEdges Zero { get; } = new(0, 0, 0, 0);

    public double Top { get; } = top;
    public double Right { get; } = right;
    public double Bottom { get; } = bottom;
    public double Left { get; } = left;

    public override bool Equals(object? obj) =>
        obj is BoxEdges other &&
        Top == other.Top && Right == other.Right &&
        Bottom == other.Bottom && Left == other.Left;

    public override int GetHashCode() => HashCode.Combine(Top, Right, Bottom, Left);
}
