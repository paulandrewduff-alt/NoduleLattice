using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Abstractions.Synapses;

/// <summary>
/// Phase-pure Synapse2 interface (005a):
/// - ComputeOutput: pure
/// - AdvanceFastState: short-term dynamics + eligibility decay/update
/// - Consolidate: permissioned learning
/// - SlowHooks: deficits/receptors + growth request
/// </summary>
public interface ISynapse2
{
    SynapseId Id { get; }
    NoduleId Pre { get; }
    NoduleId Post { get; }

    SynapseKind Kind { get; }

    float Weight { get; set; }
    float Gain { get; set; }
    int DelaySteps { get; set; }

    float ComputeOutput(in NodeActivity preActivity, in ModulatorVector modulators);

    void AdvanceFastState(in NodeActivity preActivity, in NodeActivity postActivity);

    void Consolidate(in ModulatorVector modulators);

    void SlowHooks(in ModulatorVector modulators, in SynapseDemand demand);

    SynapseGrowthRequest? ConsiderGrowthRequest(in ModulatorVector modulators);

    void ApplyGrowthFeedback(in SynapseGrowthFeedback feedback);
}
