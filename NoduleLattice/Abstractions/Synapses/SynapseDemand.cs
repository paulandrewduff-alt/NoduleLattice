using NoduleLattice.Abstractions.Modulation;

namespace NoduleLattice.Abstractions.Synapses;

/// <summary>
/// Demand is "what modulator would have been useful".
/// Fixed-size struct to avoid allocations.
/// </summary>
public struct SynapseDemand
{
    public float Reward;
    public float Salience;
    public float Stability;
    public float Alerting;
    public float Curiosity;
    public float Goal;

    public float this[ModulatorId id]
    {
        get => id switch
        {
            ModulatorId.Reward => Reward,
            ModulatorId.Salience => Salience,
            ModulatorId.Stability => Stability,
            ModulatorId.Alerting => Alerting,
            ModulatorId.Curiosity => Curiosity,
            ModulatorId.Goal => Goal,
            _ => 0f
        };
        init
        {
            switch (id)
            {
                case ModulatorId.Reward: Reward = value; break;
                case ModulatorId.Salience: Salience = value; break;
                case ModulatorId.Stability: Stability = value; break;
                case ModulatorId.Alerting: Alerting = value; break;
                case ModulatorId.Curiosity: Curiosity = value; break;
                case ModulatorId.Goal: Goal = value; break;
            }
        }
    }

    public static SynapseDemand Zero() => new();
}
