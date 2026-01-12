using System.Collections.Generic;

namespace NoduleLattice.Core.Cortex;

/// <summary>
/// Minimal interface for a cortical side.
/// In the real engine, this will adapt to the lattice (node activity, modulators, etc.).
/// </summary>
public interface ICortexSide
{
    CortexSideId Side { get; }

    /// <summary>
    /// Called once per step to produce outbound projections.
    /// </summary>
    IReadOnlyList<Projection> EmitProjections(long stepIndex);

    /// <summary>
    /// Called once per step to consume inbound projections.
    /// </summary>
    void ApplyProjections(long stepIndex, IReadOnlyList<Projection> inbound);
}
