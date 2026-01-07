using NoduleLattice.Abstractions.Math;

namespace NoduleLattice.Core.Runtime.Snapshots;

public sealed class LatticeSnapshot
{
    public long StepIndex { get; init; }
    public List<NodeSnap> Nodes { get; init; } = new();
    public List<EdgeSnap> Synapses { get; init; } = new();
}

public sealed class NodeSnap
{
    public int Id { get; init; }
    public Int3 Pos { get; init; }
    public float V { get; init; }
    public float Rate { get; init; }
    public bool Spiked { get; init; }
}

public sealed class EdgeSnap
{
    public long Id { get; init; }
    public int Pre { get; init; }
    public int Post { get; init; }
    public float W { get; init; }
    public int Kind { get; init; }
}
