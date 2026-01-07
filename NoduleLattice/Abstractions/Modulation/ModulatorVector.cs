namespace NoduleLattice.Abstractions.Modulation;

/// <summary>
/// Fixed-size modulators vector: avoids allocations and indexing ambiguity.
/// </summary>
public struct ModulatorVector
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
        set
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

    public void Clamp01()
    {
        Reward = Clamp01(Reward);
        Salience = Clamp01(Salience);
        Stability = Clamp01(Stability);
        Alerting = Clamp01(Alerting);
        Curiosity = Clamp01(Curiosity);
        Goal = Clamp01(Goal);
    }

    private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

    public static ModulatorVector Zero() => new();
}
