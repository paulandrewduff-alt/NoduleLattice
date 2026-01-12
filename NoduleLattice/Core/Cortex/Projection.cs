namespace NoduleLattice.Core.Cortex;

/// <summary>
/// A minimal scalar projection (source key → target key) sent across the callosum.
/// Keys are arbitrary ints in this sketch (e.g., node ids, region ids, feature ids).
/// </summary>
public readonly record struct Projection(int Key, float Value);
