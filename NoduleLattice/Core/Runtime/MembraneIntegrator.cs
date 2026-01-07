using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Core.Runtime;

public sealed class MembraneIntegrator
{
    public float ExcGain { get; init; } = 1.0f;
    public float InhGain { get; init; } = 1.0f;
    public int RefractorySteps { get; init; } = 2;

    // Rate smoothing (EMA)
    public float RateDecay { get; init; } = 0.95f;
    public float RateSpikeBoost { get; init; } = 0.25f;

    public void Integrate(ref MembraneState m, NodeAccumulator acc)
    {
        if (m.Refractory > 0)
        {
            m.Refractory--;
            // optional: drift toward rest
            m.Potential *= 0.5f;
            return;
        }

        m.Potential *= m.Leak;

        m.Potential += ExcGain * acc.ExcSum;
        m.Potential -= InhGain * acc.InhSum;

        m.Potential += m.Bias;

        // clamp for stability (keeps numbers sane)
        if (m.Potential > 5f) m.Potential = 5f;
        if (m.Potential < -5f) m.Potential = -5f;
    }

    public void Emit(long stepIndex, ref MembraneState m, ref NodeActivity a)
    {
        bool spiked = m.Refractory == 0 && m.Potential >= m.Threshold;

        a.Spiked = spiked;
        if (spiked)
        {
            a.LastSpikeStep = stepIndex;
            m.Refractory = RefractorySteps;
            m.Potential = 0f;
        }

        // Rate trace
        a.Rate = a.Rate * RateDecay + (spiked ? (1f - RateDecay) * RateSpikeBoost : 0f);
        if (a.Rate > 1f) a.Rate = 1f;
        if (a.Rate < 0f) a.Rate = 0f;
    }
}
