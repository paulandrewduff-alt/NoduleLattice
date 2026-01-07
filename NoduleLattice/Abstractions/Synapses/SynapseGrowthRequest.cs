using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Abstractions.Synapses;

public readonly record struct SynapseGrowthRequest(
    SynapseId SourceSynapse,
    NoduleId PostNodule,
    ModulatorId NeededModulator,
    float Urgency
);
