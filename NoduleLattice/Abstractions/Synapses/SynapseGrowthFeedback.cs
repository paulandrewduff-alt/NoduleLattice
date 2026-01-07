namespace NoduleLattice.Abstractions.Synapses;

public readonly record struct SynapseGrowthFeedback(
    SynapseId SourceSynapse,
    bool Success,
    float UtilityScore
);
