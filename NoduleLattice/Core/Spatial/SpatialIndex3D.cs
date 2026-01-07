using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Core.Spatial;

/// <summary>
/// Deterministic grid bucket index for lattice neighbour lookups.
/// Buckets are keyed by integer cell coordinates. No randomness.
/// Designed for correctness first, speed second.
/// </summary>
public sealed class SpatialIndex3D
{
    private readonly Dictionary<Int3, List<INodule>> _buckets = new();


    public void Clear() => _buckets.Clear();


    public void Add(INodule n)
    {
        if (!_buckets.TryGetValue(n.Position, out var list))
        {
            list = new List<INodule>();
            _buckets[n.Position] = list;
        }


        list.Add(n);
    }


    public IReadOnlyList<INodule> QueryManhattan(in Int3 center, int radius)
    {
        if (radius <= 0)
        {
            if (_buckets.TryGetValue(center, out var exact))
                return exact.OrderBy(x => x.Id.Value).ToList();
            return Array.Empty<INodule>();
        }


        var results = new List<INodule>();


        // Enumerate all Int3 positions within manhattan radius deterministically.
        // Complexity: O(r^3) positions in worst case; but r is small by policy.
        for (int dx = -radius; dx <= radius; dx++)
        {
            int rem1 = radius - System.Math.Abs(dx);
            for (int dy = -rem1; dy <= rem1; dy++)
            {
                int rem2 = rem1 - System.Math.Abs(dy);
                for (int dz = -rem2; dz <= rem2; dz++)
                {
                    var p = new Int3(center.X + dx, center.Y + dy, center.Z + dz);
                    if (_buckets.TryGetValue(p, out var list))
                        results.AddRange(list);
                }
            }
        }


        results.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        return results;
    }
}