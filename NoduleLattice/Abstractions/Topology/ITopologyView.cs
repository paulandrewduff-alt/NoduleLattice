using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Nodes;
using NoduleLattice.Abstractions.Synapses;

namespace NoduleLattice.Abstractions.Topology;

public interface ITopologyView
{
    INodule GetNodule(NoduleId id);
    IReadOnlyList<INodule> GetNeighbours(in Int3 pos, int radiusManhattan);

    IReadOnlyList<ISynapse2> GetIncoming(NoduleId id);
    IReadOnlyList<ISynapse2> GetOutgoing(NoduleId id);

    IReadOnlyList<ISynapse2> AllSynapses { get; }
    long StepIndex { get; }
}
