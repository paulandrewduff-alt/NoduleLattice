namespace NoduleLattice.Core.Topology;

public sealed class StructuralPolicy
{
    public int NeighbourRadiusManhattan { get; init; } = 2;

    public int MaxIncomingPerNode { get; init; } = 48;
    public int MaxOutgoingPerNode { get; init; } = 48;

    public int ProbationSteps { get; init; } = 512;

    public float UtilityKeepThreshold { get; init; } = 0.35f;
    public float UtilityEmaDecay { get; init; } = 0.995f;

    public float NewEdgeWeight { get; init; } = 0.05f;

    // NEW: distance-weighted growth
    public bool UseDistanceWeightedGrowth { get; init; } = true;

    /// <summary>
    /// Larger -> stronger preference for nearby nodes.
    /// weight ~ exp(-DistanceDecay * d)
    /// </summary>
    public float DistanceDecay { get; init; } = 0.65f;

    /// <summary>
    /// Optional mild exploration noise in selection weights.
    /// </summary>
    public float ExplorationJitter { get; init; } = 0.10f;
}