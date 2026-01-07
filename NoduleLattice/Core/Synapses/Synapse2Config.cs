using NoduleLattice.Abstractions.Modulation;

namespace NoduleLattice.Core.Synapses;

public sealed class Synapse2Config
{
    public float BaseGain { get; init; } = 1.0f;
    public float WeightClamp { get; init; } = 3.0f;

    // Fast short-term depression
    public float Utilisation { get; init; } = 0.15f;
    public float TauR { get; init; } = 0.02f;

    // Eligibility
    public float EligibilityDecay { get; init; } = 0.98f;
    public float EligibilityScale { get; init; } = 0.10f;
    public float SpikeBonus { get; init; } = 0.20f;

    // Learning
    public float LearningRate { get; init; } = 0.01f;
    public float ConsolidationClamp { get; init; } = 0.05f;

    // Plasticity readiness
    public float PlasticityDecay { get; init; } = 0.995f;
    public float PlasticityBoostFromSalience { get; init; } = 0.05f;

    // Deficits & receptors
    public float DeficitDecay { get; init; } = 0.995f;
    public float ReceptorGrowthRate { get; init; } = 0.0015f;

    // Receptor budget/competition (005a)
    public float ReceptorTotalMax { get; init; } = 2.2f;  // sum of 6 receptors <= this
    public float ReceptorCompetition { get; init; } = 0.15f; // portion redistributed away from others

    // Growth triggers
    public float GrowthThreshold { get; init; } = 0.35f;
    public float GrowthUrgencyScale { get; init; } = 1.0f;

    // Growth budgeting
    public float GrowthBudgetMax { get; init; } = 1.0f;
    public float GrowthBudgetRegen { get; init; } = 0.0005f;
    public float GrowthBudgetCost { get; init; } = 0.35f;

    // Gate weights (005a)
    public ModulatorId PrimaryLearningGate { get; init; } = ModulatorId.Salience;
    public ModulatorId SecondaryLearningGate { get; init; } = ModulatorId.Reward;

    public float GatePrimaryWeight { get; init; } = 1.0f;
    public float GateSecondaryWeight { get; init; } = 1.0f;
    public float StabilityBrakeWeight { get; init; } = 0.35f;
}
