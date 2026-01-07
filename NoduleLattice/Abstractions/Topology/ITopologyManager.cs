using NoduleLattice.Abstractions.Synapses;

namespace NoduleLattice.Abstractions.Topology;

public interface ITopologyManager
{
    void EnqueueGrowthRequest(in SynapseGrowthRequest request);

    /// <summary>
    /// Applies growth/probation/pruning only on structural ticks.
    /// Must be deterministic.
    /// </summary>
    void StructuralTick(ITopologyView view);
}
