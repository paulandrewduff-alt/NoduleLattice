namespace NoduleLattice.Core.Topology;

/// <summary>
/// Per-step utility observations for probationary edges.
/// These are observational and slow-time aggregated (EMA) by UtilityProbe.
/// </summary>
public readonly record struct UtilitySample(
    float Contribution,
    float Stability,
    float Redundancy,
    float Energy,
    float Persistence,
    float Mismatch,
    float Goal,
    float Compression
);