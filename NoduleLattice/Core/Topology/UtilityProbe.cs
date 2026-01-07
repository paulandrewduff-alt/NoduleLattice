using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;
using NoduleLattice.Abstractions.Synapses;
using NoduleLattice.Abstractions.Topology;

namespace NoduleLattice.Core.Topology;

public sealed class UtilityProbe
{
    private readonly UtilityProbeConfig _cfg;

    public UtilityProbe(UtilityProbeConfig cfg) => _cfg = cfg;

    /// <summary>
    /// Observes probationary synapses.
    /// probationOutputBySynapseId must come from Phase 1 ComputeOutput (pure) and is read-only here.
    /// </summary>
    public void ObserveStep(
        ITopologyView view,
        BasicTopologyManager topology,
        Func<NoduleId, ModulatorVector> modForNode,
        IReadOnlyDictionary<long, float> probationOutputBySynapseId)
    {
        foreach (var pe in topology.AllProbations())
        {
            if (!topology.TryGetProbation(pe.SynapseId, out var edge))
                continue;

            if (!TryGetSynapse(view, pe.SynapseId, out var syn))
            {
                topology.RecordUtilitySample(pe.SynapseId, new UtilitySample());
                continue;
            }

            var preNode = view.GetNodule(syn.Pre);
            var post = view.GetNodule(syn.Post);
            var mods = modForNode(post.Id);

            // (1) Local Activity Contribution — now correlation-based
            // x := synapse output (signed)
            // y := post rate
            float x = 0f;
            probationOutputBySynapseId.TryGetValue(syn.Id.Value, out x);

            float y = Clamp01(post.Activity.Rate);

            // Update correlation trace directly in the probation edge
            edge.Contribution.Update(x, y, _cfg.EmaDecay);

            // Map correlation proxy [-1,1] to [0,1]
            float corr = edge.Contribution.CorrProxy();
            float contribution = Clamp01(0.5f + 0.5f * corr);

            // (2) Stability Impact
            float stability = 1f - Clamp01(System.MathF.Abs(post.Membrane.Potential) / _cfg.VNorm);

            // (3) Redundancy Reduction (activity similarity)
            float redundancy = ComputeRedundancyActivitySimilarity(view, syn, preNode.Activity.Rate);

            // (4) Energy / Sparsity
            float energy = 1f - Clamp01(post.Activity.Rate);

            // (5) Persistence under modulatory silence
            float learningSeason = System.MathF.Max(mods.Salience, mods.Alerting);
            float persistence = (learningSeason < _cfg.SilenceThreshold) ? contribution : 0.5f;

            // (6) Mismatch resolution contribution (reframed)
            float mismatch = Clamp01((stability + (1f - Clamp01(mods.Alerting))) * 0.5f);

            // (7) Goal progress correlation
            float goal = Clamp01(mods.Goal * post.Activity.Rate);

            // (8) Compression
            float deg = view.GetIncoming(post.Id).Count + view.GetOutgoing(post.Id).Count;
            float compression = 1f - Clamp01(System.MathF.Abs(deg - _cfg.DegreeTarget) / _cfg.DegreeRange);

            topology.RecordUtilitySample(
                pe.SynapseId,
                new UtilitySample(
                    contribution,
                    stability,
                    redundancy,
                    energy,
                    persistence,
                    mismatch,
                    goal,
                    compression));
        }
    }

    private static float ComputeRedundancyActivitySimilarity(ITopologyView view, ISynapse2 syn, float preRate)
    {
        var incoming = view.GetIncoming(syn.Post);

        float sum = 0f;
        int count = 0;

        foreach (var other in incoming)
        {
            if (other.Id.Value == syn.Id.Value) continue;
            var pre = view.GetNodule(other.Pre);
            sum += Clamp01(pre.Activity.Rate);
            count++;
        }

        if (count == 0) return 1f;

        float mean = sum / count;
        float diff = System.MathF.Abs(Clamp01(preRate) - mean);
        return 0.2f + 0.8f * Clamp01(diff);
    }

    private static bool TryGetSynapse(ITopologyView view, SynapseId id, out ISynapse2 syn)
    {
        foreach (var s in view.AllSynapses)
        {
            if (s.Id.Value == id.Value)
            {
                syn = s;
                return true;
            }
        }

        syn = default!;
        return false;
    }

    private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
}