namespace NoduleLattice.Core.Synapses;

public sealed class Synapse2State
{
    public float Weight;
    public float Gain;
    public int DelaySteps;

    // Short-term resources (depression)
    public float R = 1.0f;

    // Eligibility
    public float E;

    // Plasticity readiness
    public float P;

    // Receptors (6)
    public float R_Reward = 0.10f;
    public float R_Salience = 0.10f;
    public float R_Stability = 0.10f;
    public float R_Alerting = 0.10f;
    public float R_Curiosity = 0.10f;
    public float R_Goal = 0.10f;

    // Caps (6)
    public float C_Reward = 0.85f;
    public float C_Salience = 0.85f;
    public float C_Stability = 0.85f;
    public float C_Alerting = 0.85f;
    public float C_Curiosity = 0.85f;
    public float C_Goal = 0.85f;

    // Deficits (6)
    public float D_Reward;
    public float D_Salience;
    public float D_Stability;
    public float D_Alerting;
    public float D_Curiosity;
    public float D_Goal;

    // Growth
    public float GrowthBudget = 1.0f;
    public float GrowthThresholdScale = 1.0f; // meta-plasticity from feedback
    public float GrowthRegenScale = 1.0f;

    public float LastGrowthUtility;
}
