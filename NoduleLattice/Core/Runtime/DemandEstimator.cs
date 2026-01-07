using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Synapses;

namespace NoduleLattice.Core.Runtime;

/// <summary>
/// Minimal demand estimator: maps modulators to "demand" placeholders.
/// Replace later with real novelty/instability/predErr/goal deviation signals.
/// </summary>
public sealed class DemandEstimator
{
    public SynapseDemand Estimate(in ModulatorVector m)
    {
        // Conservative initial mapping:
        // Demand tends to rise with modulators (placeholders).
        return new SynapseDemand
        {
            Reward = m.Reward,
            Salience = m.Salience,
            Stability = m.Stability,
            Alerting = m.Alerting,
            Curiosity = m.Curiosity,
            Goal = m.Goal
        };
    }
}
