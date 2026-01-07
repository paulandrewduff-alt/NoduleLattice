namespace NoduleLattice.Core.Determinism;

/// <summary>
/// Simple deterministic PRNG (xorshift32).
/// Stable across platforms.
/// </summary>
public sealed class DeterministicRng
{
    private uint _state;

    public DeterministicRng(uint seed = 0xC0FFEEu)
    {
        _state = seed == 0 ? 0xC0FFEEu : seed;
    }

    public uint NextU()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    public float NextFloat01()
    {
        // 24-bit mantissa fraction
        return (NextU() & 0x00FFFFFF) / 16777215f;
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        uint r = NextU();
        int span = maxExclusive - minInclusive;
        return minInclusive + (int)(r % (uint)span);
    }
}
