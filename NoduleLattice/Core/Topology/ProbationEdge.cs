using NoduleLattice.Abstractions.Synapses;

namespace NoduleLattice.Core.Topology;

public sealed class ProbationEdge
{
    public SynapseId SynapseId { get; init; }
    public SynapseGrowthRequest Request { get; init; }
    public long CreatedStep { get; init; }
    public long ExpiresStep { get; init; }

    // Rolling EMA utilities (0..1)
    public float U_Contribution;
    public float U_Stability;
    public float U_Redundancy;
    public float U_Energy;
    public float U_Persistence;
    public float U_Mismatch;
    public float U_Goal;
    public float U_Compression;

    public float CombinedUtility { get; set; }

    // NEW: correlation contribution trace
    public ContributionTrace Contribution { get; } = new();

    public void ApplyEma(in UtilitySample s, float decay)
    {
        U_Contribution = U_Contribution * decay + s.Contribution * (1f - decay);
        U_Stability = U_Stability * decay + s.Stability * (1f - decay);
        U_Redundancy = U_Redundancy * decay + s.Redundancy * (1f - decay);
        U_Energy = U_Energy * decay + s.Energy * (1f - decay);
        U_Persistence = U_Persistence * decay + s.Persistence * (1f - decay);
        U_Mismatch = U_Mismatch * decay + s.Mismatch * (1f - decay);
        U_Goal = U_Goal * decay + s.Goal * (1f - decay);
        U_Compression = U_Compression * decay + s.Compression * (1f - decay);
    }
}