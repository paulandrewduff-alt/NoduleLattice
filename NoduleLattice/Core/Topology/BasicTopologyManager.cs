using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Synapses;
using NoduleLattice.Abstractions.Topology;
using NoduleLattice.Core.Determinism;
using NoduleLattice.Core.Synapses;

namespace NoduleLattice.Core.Topology;

public sealed class BasicTopologyManager : ITopologyManager, ITopologyManagerSerializable
{
    // (Same fields as Entry 010)
    private readonly StructuralPolicy _policy;
    private readonly Synapse2Config _synCfg;
    private readonly DeterministicRng _rng;

    private readonly List<SynapseGrowthRequest> _queue = new();
    private readonly Dictionary<SynapseId, ProbationEdge> _probation = new();

    private long _nextSynapseId = 10_000;

    public BasicTopologyManager(StructuralPolicy policy, Synapse2Config synCfg, DeterministicRng rng)
    {
        _policy = policy;
        _synCfg = synCfg;
        _rng = rng;
    }

    public void EnqueueGrowthRequest(in SynapseGrowthRequest request) => _queue.Add(request);

    public void RecordUtilitySample(SynapseId id, in UtilitySample sample)
    {
        if (_probation.TryGetValue(id, out var pe))
            pe.ApplyEma(sample, _policy.UtilityEmaDecay);
    }

    public void StructuralTick(ITopologyView view)
    {
        _queue.Sort((a, b) =>
        {
            int c = b.Urgency.CompareTo(a.Urgency);
            if (c != 0) return c;
            return a.PostNodule.Value.CompareTo(b.PostNodule.Value);
        });

        foreach (var req in _queue)
        {
            var incoming = view.GetIncoming(req.PostNodule);
            if (incoming.Count >= _policy.MaxIncomingPerNode) continue;

            var postNode = view.GetNodule(req.PostNodule);
            var candidates = view.GetNeighbours(postNode.Position, _policy.NeighbourRadiusManhattan);
            if (candidates.Count == 0) continue;

            // Choose a PRE deterministically using RNG but biased by distance.
            var preNode = SelectPreCandidate(postNode.Position, candidates);
            if (preNode is null) continue;

            if (view.GetOutgoing(preNode.Id).Count >= _policy.MaxOutgoingPerNode) continue;

            var sid = new SynapseId(_nextSynapseId++);
            var syn = new Synapse2(
                id: sid,
                pre: preNode.Id,
                post: req.PostNodule,
                kind: SynapseKind.Excitatory,
                cfg: _synCfg,
                initialWeight: _policy.NewEdgeWeight,
                initialGain: 1.0f,
                delaySteps: 0);

            _pendingAdds.Add(syn);

            _probation[sid] = new ProbationEdge
            {
                SynapseId = sid,
                Request = req,
                CreatedStep = view.StepIndex,
                ExpiresStep = view.StepIndex + _policy.ProbationSteps
            };
        }

        _queue.Clear();

        var toRemove = new List<SynapseId>();
        foreach (var kvp in _probation)
        {
            var pe = kvp.Value;
            if (view.StepIndex < pe.ExpiresStep) continue;

            float sum =
                pe.U_Contribution +
                pe.U_Stability +
                pe.U_Redundancy +
                pe.U_Energy +
                pe.U_Persistence +
                pe.U_Mismatch +
                pe.U_Goal +
                pe.U_Compression;

            pe.CombinedUtility = sum / 8f;
            bool keep = pe.CombinedUtility >= _policy.UtilityKeepThreshold;

            _pendingFeedback.Add(new SynapseGrowthFeedback(
                pe.Request.SourceSynapse,
                Success: keep,
                UtilityScore: pe.CombinedUtility));

            if (!keep)
                _pendingPrunes.Add(pe.SynapseId);

            toRemove.Add(pe.SynapseId);
        }

        foreach (var id in toRemove)
            _probation.Remove(id);
    }

    private NoduleLattice.Abstractions.Nodes.INodule? SelectPreCandidate(Int3 postPos, IReadOnlyList<NoduleLattice.Abstractions.Nodes.INodule> candidates)
    {
        // Remove self-candidate if present.
        var list = candidates.Where(c => !c.Position.Equals(postPos)).ToList();
        if (list.Count == 0) return null;

        if (!_policy.UseDistanceWeightedGrowth)
        {
            int k = System.Math.Min(12, list.Count);
            int pick = _rng.NextInt(0, k);
            return list[pick];
        }

        // Deterministic weighted roulette selection
        // weight ~ exp(-decay * d) * (1 + jitter * u)
        float total = 0f;
        Span<float> weights = list.Count <= 256 ? stackalloc float[list.Count] : new float[list.Count];

        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            int d = Manhattan(postPos, c.Position);
            float w = System.MathF.Exp(-_policy.DistanceDecay * d);

            // Mild exploration jitter using deterministic RNG
            float u = _rng.NextFloat01();
            w *= (1f + _policy.ExplorationJitter * (u - 0.5f) * 2f);

            if (w < 0.0001f) w = 0.0001f;
            weights[i] = w;
            total += w;
        }

        float r = _rng.NextFloat01() * total;
        float acc = 0f;

        for (int i = 0; i < list.Count; i++)
        {
            acc += weights[i];
            if (r <= acc) return list[i];
        }

        return list[^1];
    }

    private static int Manhattan(in Int3 a, in Int3 b)
        => System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y) + System.Math.Abs(a.Z - b.Z);

    // --- Engine integration seam ---
    private readonly List<NoduleLattice.Abstractions.Synapses.ISynapse2> _pendingAdds = new();
    private readonly List<SynapseId> _pendingPrunes = new();
    private readonly List<SynapseGrowthFeedback> _pendingFeedback = new();

    public IReadOnlyList<NoduleLattice.Abstractions.Synapses.ISynapse2> ConsumePendingAdds() { var c = _pendingAdds.ToArray(); _pendingAdds.Clear(); return c; }
    public IReadOnlyList<SynapseId> ConsumePendingPrunes() { var c = _pendingPrunes.ToArray(); _pendingPrunes.Clear(); return c; }
    public IReadOnlyList<SynapseGrowthFeedback> ConsumePendingFeedback() { var c = _pendingFeedback.ToArray(); _pendingFeedback.Clear(); return c; }

    public bool TryGetProbation(SynapseId id, out ProbationEdge edge) => _probation.TryGetValue(id, out edge!);
    public IEnumerable<ProbationEdge> AllProbations() => _probation.Values;

    // --- Persistence (updated to include contribution trace) ---

    public void WriteState(BinaryWriter bw)
    {
        bw.Write(2); // bumped for Entry 011
        bw.Write(_nextSynapseId);

        bw.Write(_probation.Count);
        foreach (var pe in _probation.Values.OrderBy(x => x.SynapseId.Value))
        {
            bw.Write(pe.SynapseId.Value);

            bw.Write(pe.Request.SourceSynapse.Value);
            bw.Write(pe.Request.PostNodule.Value);
            bw.Write((int)pe.Request.NeededModulator);
            bw.Write(pe.Request.Urgency);

            bw.Write(pe.CreatedStep);
            bw.Write(pe.ExpiresStep);

            bw.Write(pe.U_Contribution);
            bw.Write(pe.U_Stability);
            bw.Write(pe.U_Redundancy);
            bw.Write(pe.U_Energy);
            bw.Write(pe.U_Persistence);
            bw.Write(pe.U_Mismatch);
            bw.Write(pe.U_Goal);
            bw.Write(pe.U_Compression);

            bw.Write(pe.CombinedUtility);

            // contribution trace
            bw.Write(pe.Contribution.Ex);
            bw.Write(pe.Contribution.Ey);
            bw.Write(pe.Contribution.Exy);
        }

        bw.Write(_queue.Count);
        foreach (var r in _queue)
        {
            bw.Write(r.SourceSynapse.Value);
            bw.Write(r.PostNodule.Value);
            bw.Write((int)r.NeededModulator);
            bw.Write(r.Urgency);
        }
    }

    public void ReadState(BinaryReader br)
    {
        int ver = br.ReadInt32();
        if (ver != 2) throw new InvalidOperationException($"Unsupported topology manager state version: {ver}");

        _probation.Clear();
        _queue.Clear();

        _nextSynapseId = br.ReadInt64();

        int pCount = br.ReadInt32();
        for (int i = 0; i < pCount; i++)
        {
            var sid = new SynapseId(br.ReadInt64());

            var src = new SynapseId(br.ReadInt64());
            var post = new NoduleLattice.Abstractions.Nodes.NoduleId(br.ReadInt32());
            var mod = (NoduleLattice.Abstractions.Modulation.ModulatorId)br.ReadInt32();
            float urg = br.ReadSingle();

            long created = br.ReadInt64();
            long expires = br.ReadInt64();

            var pe = new ProbationEdge
            {
                SynapseId = sid,
                Request = new SynapseGrowthRequest(src, post, mod, urg),
                CreatedStep = created,
                ExpiresStep = expires
            };

            pe.U_Contribution = br.ReadSingle();
            pe.U_Stability = br.ReadSingle();
            pe.U_Redundancy = br.ReadSingle();
            pe.U_Energy = br.ReadSingle();
            pe.U_Persistence = br.ReadSingle();
            pe.U_Mismatch = br.ReadSingle();
            pe.U_Goal = br.ReadSingle();
            pe.U_Compression = br.ReadSingle();

            pe.CombinedUtility = br.ReadSingle();

            pe.Contribution.Ex = br.ReadSingle();
            pe.Contribution.Ey = br.ReadSingle();
            pe.Contribution.Exy = br.ReadSingle();

            _probation[sid] = pe;
        }

        int qCount = br.ReadInt32();
        for (int i = 0; i < qCount; i++)
        {
            var src = new SynapseId(br.ReadInt64());
            var post = new NoduleLattice.Abstractions.Nodes.NoduleId(br.ReadInt32());
            var mod = (NoduleLattice.Abstractions.Modulation.ModulatorId)br.ReadInt32();
            float urg = br.ReadSingle();

            _queue.Add(new SynapseGrowthRequest(src, post, mod, urg));
        }
    }
}