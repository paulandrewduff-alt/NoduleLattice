namespace NoduleLattice.Abstractions.Math;

public readonly record struct Int3(int X, int Y, int Z)
{
    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public int ManhattanDistance(in Int3 other)
        => System.Math.Abs(X - other.X) + System.Math.Abs(Y - other.Y) + System.Math.Abs(Z - other.Z);
}
