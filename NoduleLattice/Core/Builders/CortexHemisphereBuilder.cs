using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Nodes;
using NoduleLattice.Abstractions.Synapses;
using NoduleLattice.Core.Determinism;
using NoduleLattice.Core.Nodes;
using NoduleLattice.Core.Runtime;
using NoduleLattice.Core.Synapses;

namespace NoduleLattice.Core.Builders;

/// <summary>
/// Builds a 2-hemisphere cortex slab and (optionally) a corpus callosum.
///
/// Hemisphere identity is implicit:
/// - X in [LeftStartX..LeftEndX] => left hemisphere
/// - X in [RightStartX..RightEndX] => right hemisphere
/// - Gap has no nodes
///
/// The corpus callosum connects the medial boundary columns:
/// - left:  x = MidLeftX
/// - right: x = MidRightX
/// using mirrored (y,z) pairs (plus optional randomized sparsity).
/// </summary>
public static class CortexHemisphereBuilder
{
    public sealed class Result
    {
        public List<NoduleId> LeftCortex { get; } = new();
        public List<NoduleId> RightCortex { get; } = new();
        public int LeftCount => LeftCortex.Count;
        public int RightCount => RightCortex.Count;
        public int CallosumSynapseCount { get; internal set; }
    }

    /// <summary>
    /// Adds cortex nodes and callosal synapses into the engine.
    /// The engine must already exist.
    /// </summary>
    public static Result Build(
        NoduleLatticeEngine engine,
        CortexHemispheresConfig cfg,
        Synapse2Config synCfg,
        DeterministicRng rng,
        int startingNodeId = 1,
        long startingSynapseId = 1)
    {
        if (engine is null) throw new ArgumentNullException(nameof(engine));
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        if (synCfg is null) throw new ArgumentNullException(nameof(synCfg));
        if (rng is null) throw new ArgumentNullException(nameof(rng));

        cfg.Normalize();

        var res = new Result();

        int nextNode = Math.Max(1, startingNodeId);
        long nextSyn = Math.Max(1, startingSynapseId);

        // Build left hemisphere nodes
        for (int z = 0; z < cfg.LayersZ; z++)
        {
            for (int y = 0; y < cfg.HeightY; y++)
            {
                for (int x = cfg.LeftStartX; x <= cfg.LeftEndXInclusive; x++)
                {
                    var id = new NoduleId(nextNode++);
                    var pos = new Int3(x, cfg.OriginY + y, cfg.OriginZ + z);
                    var n = new Nodule(id, pos) { Role = NoduleRole.Generic };
                    engine.AddNode(n);
                    res.LeftCortex.Add(id);
                }
            }
        }

        // Build right hemisphere nodes
        for (int z = 0; z < cfg.LayersZ; z++)
        {
            for (int y = 0; y < cfg.HeightY; y++)
            {
                for (int x = cfg.RightStartX; x <= cfg.RightEndXInclusive; x++)
                {
                    var id = new NoduleId(nextNode++);
                    var pos = new Int3(x, cfg.OriginY + y, cfg.OriginZ + z);
                    var n = new Nodule(id, pos) { Role = NoduleRole.Generic };
                    engine.AddNode(n);
                    res.RightCortex.Add(id);
                }
            }
        }

        if (!cfg.EnableCorpusCallosum)
            return res;

        // Build an index from lattice coordinate to node id for the medial columns.
        // We only need the medial boundary columns for mirrored (y,z).
        var leftMedial = new Dictionary<(int y, int z), NoduleId>(cfg.HeightY * cfg.LayersZ);
        var rightMedial = new Dictionary<(int y, int z), NoduleId>(cfg.HeightY * cfg.LayersZ);

        // We can reconstruct ids deterministically because we created nodes in a strict order,
        // but we keep the mapping explicit to avoid depending on that assumption.
        // Scan all cortex nodes and pick those at medial boundaries.
        foreach (var id in res.LeftCortex)
        {
            // We need the node position; engine stores nodes privately, so we mirror based on construction order.
            // Therefore, we compute medial maps directly while building (below) for correctness.
        }

        // Build medial maps directly (no engine introspection required)
        // left medial: x = MidLeftX
        // right medial: x = MidRightX
        // For each y,z the medial node is the last (or first) x column in that hemisphere.
        // We can compute ids by re-walking build order with the known startingNodeId.

        // Left hemisphere node id layout:
        // for z:0..Z-1, y:0..Y-1, x:leftStart..leftEnd
        // Right hemisphere begins after left.

        int leftPlaneSize = cfg.LeftWidthX;
        int leftRowSize = leftPlaneSize;
        int leftLayerSize = cfg.HeightY * leftRowSize;
        int leftTotal = cfg.LayersZ * leftLayerSize;

        int rightPlaneSize = cfg.RightWidthX;
        int rightRowSize = rightPlaneSize;
        int rightLayerSize = cfg.HeightY * rightRowSize;

        int leftBaseNodeId = Math.Max(1, startingNodeId);
        int rightBaseNodeId = leftBaseNodeId + leftTotal;

        for (int z = 0; z < cfg.LayersZ; z++)
        {
            for (int y = 0; y < cfg.HeightY; y++)
            {
                // left medial is last x in left hemisphere
                int leftLocalIndex = (z * leftLayerSize) + (y * leftRowSize) + (cfg.LeftWidthX - 1);
                var leftId = new NoduleId(leftBaseNodeId + leftLocalIndex);
                leftMedial[(y, z)] = leftId;

                // right medial is first x in right hemisphere
                int rightLocalIndex = (z * rightLayerSize) + (y * rightRowSize) + 0;
                var rightId = new NoduleId(rightBaseNodeId + rightLocalIndex);
                rightMedial[(y, z)] = rightId;
            }
        }

        int created = 0;

        // Create callosal fibers between mirrored medial nodes
        for (int z = 0; z < cfg.LayersZ; z++)
        {
            for (int y = 0; y < cfg.HeightY; y++)
            {
                if (!leftMedial.TryGetValue((y, z), out var l)) continue;
                if (!rightMedial.TryGetValue((y, z), out var r)) continue;

                // Sparsity
                if (rng.NextFloat01() > cfg.CallosumDensity)
                    continue;

                var kind = (rng.NextFloat01() < cfg.CallosumInhibitoryChance)
                    ? SynapseKind.Inhibitory
                    : SynapseKind.Excitatory;

                float w = cfg.CallosumBaseWeight + cfg.CallosumWeightJitter * (2f * rng.NextFloat01() - 1f);

                var s1 = new Synapse2(
                    new SynapseId(nextSyn++),
                    l,
                    r,
                    kind,
                    synCfg,
                    initialWeight: w,
                    initialGain: 1.0f,
                    delaySteps: cfg.CallosumDelaySteps);

                engine.AddSynapse(s1);
                created++;

                if (cfg.CallosumBidirectional)
                {
                    var s2 = new Synapse2(
                        new SynapseId(nextSyn++),
                        r,
                        l,
                        kind,
                        synCfg,
                        initialWeight: w,
                        initialGain: 1.0f,
                        delaySteps: cfg.CallosumDelaySteps);

                    engine.AddSynapse(s2);
                    created++;
                }
            }
        }

        res.CallosumSynapseCount = created;
        return res;
    }
}