// ============================================================================
// FILE: NoduleLattice/Core/Runtime/NoduleLatticeEngine.cs
// PURPOSE:
//   Canon engine with:
//     - Parallel stepping path (enableParallel=true)
//     - StepAsync for API/background runner usage
//     - Canonical archive Save/Load (IArchiveStore) via byte[] / ReadOnlySpan<byte>
//     - Snapshot builder via Core.Runtime.Snapshots.LatticeSnapshot / NodeSnap / EdgeSnap (canon)
//     - Hippocampus episodic memory (capture + recall injection)
// NOTES:
//   - Hippocampus is Core structure.
//   - Hippocampus not persisted in archive (by design, for now).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoduleLattice.Abstractions.Archive;
using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;
using NoduleLattice.Abstractions.Synapses;
using NoduleLattice.Abstractions.Time;
using NoduleLattice.Core.Determinism;
using NoduleLattice.Core.Modulation;
using NoduleLattice.Core.Runtime.Hippocampus;
using NoduleLattice.Core.Runtime.IdleDrive;
using NoduleLattice.Core.Runtime.Sleep;
using NoduleLattice.Core.Runtime.Snapshots;
using NoduleLattice.Core.Spatial;
using NoduleLattice.Core.Synapses;
using NoduleLattice.Core.Topology;

namespace NoduleLattice.Core.Runtime;

public sealed class NoduleLatticeEngine : IArchiveStore
{
    private readonly IResettableTimebase _time;
    private readonly UniformModulatorField _field;
    private readonly BasicTopologyManager _topology;
    private readonly MembraneIntegrator _membrane;
    private readonly DemandEstimator _demand;
    private readonly UtilityProbe _probe;

    // IdleDrive (intrinsic excitation/noise)
    private IdleDriveConfig _idleDrive = IdleDriveConfig.Disabled;
    private readonly DeterministicRng _idleRng;

    private readonly SpatialIndex3D _spatial = new();

    private readonly Dictionary<NoduleId, INodule> _nodes = new();
    private readonly Dictionary<NoduleId, NodeAccumulator> _acc = new();

    private readonly Dictionary<SynapseId, ISynapse2> _synapses = new();

    private readonly Dictionary<NoduleId, List<ISynapse2>> _incoming = new();
    private readonly Dictionary<NoduleId, List<ISynapse2>> _outgoing = new();

    private readonly int _maxDelay;
    private readonly Dictionary<NoduleId, NodeActivity[]> _activityDelay = new();

    private readonly int _structuralPeriodSteps;

    // probation output capture
    private readonly Dictionary<long, float> _probationOutputs = new();

    // sleep replay
    private readonly SleepReplayConfig _sleep;
    private readonly ReplayBuffer _replay;

    // hippocampus (episodic memory)
    private readonly HippocampusMemory _hippocampus;

    // Parallel controls
    private readonly bool _enableParallel;
    private readonly int _maxDegree;
    private readonly int _synapseChunkSize;

    public NoduleLatticeEngine(
        IResettableTimebase time,
        UniformModulatorField field,
        BasicTopologyManager topology,
        MembraneIntegrator membrane,
        DemandEstimator demand,
        UtilityProbe probe,
        int structuralPeriodSteps = 128,
        int maxDelaySteps = 4,
        SleepReplayConfig? sleep = null,
        bool enableParallel = true,
        int? maxDegreeOfParallelism = null,
        int synapseChunkSize = 4096)
    {
        _time = time;
        _field = field;
        _topology = topology;
        _membrane = membrane;
        _demand = demand;
        _probe = probe;

        // Dedicated deterministic RNG for idle drive.
        // Seeded from topology RNG once to stay run-to-run deterministic without
        // perturbing topology RNG on every step.
        _idleRng = new DeterministicRng(_topology.Rng.NextU());

        _structuralPeriodSteps = Math.Max(1, structuralPeriodSteps);
        _maxDelay = Math.Max(0, maxDelaySteps);

        _sleep = sleep ?? new SleepReplayConfig();
        _replay = new ReplayBuffer(_sleep.HistorySteps);
        _replay.Reset(Array.Empty<NoduleId>());

        _hippocampus = new HippocampusMemory(new HippocampusConfig());

        _enableParallel = enableParallel;
        _maxDegree = Math.Max(1, maxDegreeOfParallelism ?? Environment.ProcessorCount);
        _synapseChunkSize = Math.Max(256, synapseChunkSize);
    }

    public long StepIndex => _time.StepIndex;

    public void SetModulators(ModulatorVector v) => _field.Set(v);

    // ------------------------------------------------------------------------
    // IdleDrive public surface (Core-only)
    // ------------------------------------------------------------------------

    public void SetIdleDriveConfig(IdleDriveConfig cfg) => _idleDrive = cfg ?? IdleDriveConfig.Disabled;

    public IdleDriveConfig GetIdleDriveConfig() => _idleDrive;

    // ------------------------------------------------------------------------
    // Hippocampus public surface (Core-only)
    // ------------------------------------------------------------------------

    public void SetHippocampusConfig(HippocampusConfig cfg) => _hippocampus.SetConfig(cfg);

    public HippocampusStats GetHippocampusStats() => _hippocampus.GetStats(_time.StepIndex);

    public void HippocampusRecallInject(int episodes = 1, float gain = 0.6f)
        => _hippocampus.RecallInject(this, _time.StepIndex, episodes, gain);

    // ------------------------------------------------------------------------

    public void AddNode(INodule node)
    {
        _nodes[node.Id] = node;
        _acc[node.Id] = new NodeAccumulator();

        _incoming[node.Id] = new List<ISynapse2>();
        _outgoing[node.Id] = new List<ISynapse2>();

        _activityDelay[node.Id] = new NodeActivity[_maxDelay + 1];

        _spatial.Add(node);

        _replay.Reset(_nodes.Keys);
    }

    public void AddSynapse(ISynapse2 syn)
    {
        _synapses[syn.Id] = syn;
        _outgoing[syn.Pre].Add(syn);
        _incoming[syn.Post].Add(syn);
    }

    public bool RemoveSynapse(SynapseId id)
    {
        if (!_synapses.TryGetValue(id, out var syn)) return false;

        _synapses.Remove(id);
        _outgoing[syn.Pre].Remove(syn);
        _incoming[syn.Post].Remove(syn);
        return true;
    }

    public void ClearAll()
    {
        _nodes.Clear();
        _acc.Clear();
        _synapses.Clear();
        _incoming.Clear();
        _outgoing.Clear();
        _activityDelay.Clear();
        _spatial.Clear();
        _probationOutputs.Clear();
        _time.Reset(0);
        _replay.Reset(Array.Empty<NoduleId>());

        // Align to “new brain”: clear episodic memory.
        _hippocampus.ClearAll();
    }

    public void InjectInput(NoduleId target, float signalExc, float signalInh = 0f)
    {
        if (_acc.TryGetValue(target, out var a))
        {
            a.ExcSum += signalExc;
            a.InhSum += signalInh;
        }
    }

    // ------------------------------------------------------------------------
    // IdleDrive injection (intrinsic excitation/noise)
    // ------------------------------------------------------------------------

    private void ApplyIdleDrive()
    {
        if (!_idleDrive.Enabled) return;
        if (_idleDrive.Probability <= 0f) return;
        if (_idleDrive.Amplitude <= 0f) return;
        if (_nodes.Count == 0) return;

        float p = Clamp01(_idleDrive.Probability);
        float amp = MathF.Abs(_idleDrive.Amplitude);
        float inhBias = Clamp01(_idleDrive.InhibitoryBias);

        // Deterministic iteration order (independent of Dictionary enumeration)
        foreach (var id in _nodes.Keys.OrderBy(k => k.Value))
        {
            if (_idleRng.NextFloat01() > p) continue;

            bool inhibitory = _idleRng.NextFloat01() < inhBias;
            float mag = _idleRng.NextFloat01() * amp;

            if (!_acc.TryGetValue(id, out var acc)) continue;

            if (inhibitory) acc.InhSum += mag;
            else acc.ExcSum += mag;
        }
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    public void Step(int steps = 1)
    {
        steps = Math.Max(1, steps);

        for (int i = 0; i < steps; i++)
        {
            if (_enableParallel) StepOneParallel();
            else StepOneSequential();
        }
    }

    public Task StepAsync(int steps, CancellationToken ct = default)
    {
        steps = Math.Max(1, steps);

        return Task.Run(() =>
        {
            for (int i = 0; i < steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (_enableParallel) StepOneParallel();
                else StepOneSequential();
            }
        }, ct);
    }

    public void SleepReplay()
    {
        if (!_sleep.Enabled) return;

        var cur = _field.Current;
        var sleepMods = new ModulatorVector
        {
            Reward = cur.Reward * _sleep.SleepModulatorScale,
            Salience = cur.Salience * _sleep.SleepModulatorScale,
            Stability = cur.Stability * _sleep.SleepModulatorScale,
            Alerting = cur.Alerting * _sleep.SleepModulatorScale,
            Curiosity = cur.Curiosity * _sleep.SleepModulatorScale,
            Goal = cur.Goal * _sleep.SleepModulatorScale
        };

        _field.Set(sleepMods);

        for (int i = 1; i <= _sleep.ReplaySteps; i++)
        {
            foreach (var id in _nodes.Keys)
            {
                float r = _replay.GetReplayRate(id, i % Math.Max(1, _sleep.HistorySteps));
                if (r <= 0.0001f) continue;
                InjectInput(id, r * _sleep.ReplayGain);
            }

            if (_enableParallel) StepOneParallel();
            else StepOneSequential();
        }
    }

    // ------------------------ Sequential ------------------------

    private void StepOneSequential()
    {
        foreach (var n in _nodes.Values)
        {
            var act = n.Activity;
            act.ResetStepFlags();
            n.Activity = act;

            _acc[n.Id].Reset();
        }

        ApplyIdleDrive();

        var perNodeMod = new Dictionary<NoduleId, ModulatorVector>(_nodes.Count);
        foreach (var n in _nodes.Values)
            perNodeMod[n.Id] = _field.Sample(n.Position);

        _probationOutputs.Clear();

        foreach (var syn in _synapses.Values)
        {
            var preAct = GetDelayedActivity(syn.Pre, syn.DelaySteps);
            var m = perNodeMod[syn.Post];

            float outSig = syn.ComputeOutput(preAct, m);

            if (_topology.TryGetProbation(syn.Id, out _))
                _probationOutputs[syn.Id.Value] = outSig;

            var acc = _acc[syn.Post];
            if (outSig >= 0) acc.ExcSum += outSig;
            else acc.InhSum += -outSig;
        }

        foreach (var n in _nodes.Values)
        {
            var m = n.Membrane;
            _membrane.Integrate(ref m, _acc[n.Id]);
            n.Membrane = m;
        }

        foreach (var n in _nodes.Values)
        {
            var m = n.Membrane;
            var a = n.Activity;
            _membrane.Emit(_time.StepIndex, ref m, ref a);
            n.Membrane = m;
            n.Activity = a;
        }

        foreach (var syn in _synapses.Values)
        {
            var preAct = GetDelayedActivity(syn.Pre, syn.DelaySteps);
            var postAct = _nodes[syn.Post].Activity;
            syn.AdvanceFastState(preAct, postAct);
        }

        foreach (var syn in _synapses.Values)
        {
            var m = perNodeMod[syn.Post];
            syn.Consolidate(m);
        }

        foreach (var syn in _synapses.Values)
        {
            var m = perNodeMod[syn.Post];
            var demand = _demand.Estimate(m);
            syn.SlowHooks(m, demand);

            var req = syn.ConsiderGrowthRequest(m);
            if (req.HasValue)
                _topology.EnqueueGrowthRequest(req.Value);
        }

        var all = _synapses.Values.ToList();
        var view = new TopologyView(_time.StepIndex, _nodes, _incoming, _outgoing, all, _spatial);

        _probe.ObserveStep(view, _topology, id => perNodeMod[id], _probationOutputs);

        // Hippocampus capture at the end of the step, before time advance.
        _hippocampus.MaybeStoreEpisode(_time.StepIndex, _nodes.Values.ToArray(), _field.Current);

        _time.Advance();

        foreach (var n in _nodes.Values)
        {
            PushActivityDelay(n.Id, n.Activity);
            _replay.Push(n.Id, n.Activity.Rate);
        }

        _replay.AdvanceStep();

        if ((_time.StepIndex % _structuralPeriodSteps) == 0)
            StructuralTick();
    }

    // ------------------------ Parallel ------------------------

    private void StepOneParallel()
    {
        var nodeArr = _nodes.Values.ToArray();
        var synArr = _synapses.Values.OrderBy(s => s.Id.Value).ToArray();

        if (nodeArr.Length == 0)
        {
            _time.Advance();
            return;
        }

        int maxNodeId = 0;
        for (int i = 0; i < nodeArr.Length; i++)
            maxNodeId = Math.Max(maxNodeId, nodeArr[i].Id.Value);

        // Phase 0 reset
        Parallel.For(0, nodeArr.Length, new ParallelOptions { MaxDegreeOfParallelism = _maxDegree }, i =>
        {
            var n = nodeArr[i];
            var act = n.Activity;
            act.ResetStepFlags();
            n.Activity = act;
            _acc[n.Id].Reset();
        });

        ApplyIdleDrive();

        // Per-node modulators
        var perNodeModArr = new ModulatorVector[maxNodeId + 1];
        Parallel.For(0, nodeArr.Length, new ParallelOptions { MaxDegreeOfParallelism = _maxDegree }, i =>
        {
            var n = nodeArr[i];
            perNodeModArr[n.Id.Value] = _field.Sample(n.Position);
        });

        _probationOutputs.Clear();

        // Synapse chunks
        var ranges = BuildRanges(synArr.Length, _synapseChunkSize);
        var localExc = new float[ranges.Length][];
        var localInh = new float[ranges.Length][];
        var localProb = new Dictionary<long, float>[ranges.Length];

        // Phase 1 synapse outputs
        Parallel.For(0, ranges.Length, new ParallelOptions { MaxDegreeOfParallelism = _maxDegree }, r =>
        {
            var (start, end) = ranges[r];

            var exc = new float[maxNodeId + 1];
            var inh = new float[maxNodeId + 1];
            var prob = new Dictionary<long, float>();

            for (int i = start; i < end; i++)
            {
                var syn = synArr[i];

                var preAct = GetDelayedActivity(syn.Pre, syn.DelaySteps);
                var m = perNodeModArr[syn.Post.Value];

                float outSig = syn.ComputeOutput(preAct, m);

                if (_topology.TryGetProbation(syn.Id, out _))
                    prob[syn.Id.Value] = outSig;

                int post = syn.Post.Value;
                if (outSig >= 0) exc[post] += outSig;
                else inh[post] += -outSig;
            }

            localExc[r] = exc;
            localInh[r] = inh;
            localProb[r] = prob;
        });

        // Deterministic-ish reduction
        for (int post = 1; post <= maxNodeId; post++)
        {
            float ex = 0f, ih = 0f;
            for (int r = 0; r < ranges.Length; r++)
            {
                ex += localExc[r][post];
                ih += localInh[r][post];
            }

            var id = new NoduleId(post);
            if (_acc.TryGetValue(id, out var a))
            {
                a.ExcSum += ex;
                a.InhSum += ih;
            }
        }

        // Merge probation outputs
        foreach (var d in localProb)
            foreach (var kvp in d)
                _probationOutputs[kvp.Key] = kvp.Value;

        // Phase 2 integrate
        Parallel.For(0, nodeArr.Length, new ParallelOptions { MaxDegreeOfParallelism = _maxDegree }, i =>
        {
            var n = nodeArr[i];
            var m = n.Membrane;
            _membrane.Integrate(ref m, _acc[n.Id]);
            n.Membrane = m;
        });

        // Phase 3 emit
        Parallel.For(0, nodeArr.Length, new ParallelOptions { MaxDegreeOfParallelism = _maxDegree }, i =>
        {
            var n = nodeArr[i];
            var m = n.Membrane;
            var a = n.Activity;
            _membrane.Emit(_time.StepIndex, ref m, ref a);
            n.Membrane = m;
            n.Activity = a;
        });

        // Phase 4 advance fast state
        Parallel.For(0, synArr.Length, new ParallelOptions { MaxDegreeOfParallelism = _maxDegree }, i =>
        {
            var syn = synArr[i];
            var preAct = GetDelayedActivity(syn.Pre, syn.DelaySteps);
            var postAct = _nodes[syn.Post].Activity;
            syn.AdvanceFastState(preAct, postAct);
        });

        // Phase 5 consolidate
        Parallel.For(0, synArr.Length, new ParallelOptions { MaxDegreeOfParallelism = _maxDegree }, i =>
        {
            var syn = synArr[i];
            var m = perNodeModArr[syn.Post.Value];
            syn.Consolidate(m);
        });

        // Phase 6 slow hooks + growth
        var growth = new List<SynapseGrowthRequest>();
        for (int i = 0; i < synArr.Length; i++)
        {
            var syn = synArr[i];
            var m = perNodeModArr[syn.Post.Value];
            var demand = _demand.Estimate(m);
            syn.SlowHooks(m, demand);

            var req = syn.ConsiderGrowthRequest(m);
            if (req.HasValue)
                growth.Add(req.Value);
        }

        for (int i = 0; i < growth.Count; i++)
            _topology.EnqueueGrowthRequest(growth[i]);

        var all = _synapses.Values.ToList();
        var view = new TopologyView(_time.StepIndex, _nodes, _incoming, _outgoing, all, _spatial);

        _probe.ObserveStep(view, _topology, id => perNodeModArr[id.Value], _probationOutputs);

        _hippocampus.MaybeStoreEpisode(_time.StepIndex, nodeArr, _field.Current);

        _time.Advance();

        for (int i = 0; i < nodeArr.Length; i++)
        {
            var n = nodeArr[i];
            PushActivityDelay(n.Id, n.Activity);
            _replay.Push(n.Id, n.Activity.Rate);
        }

        _replay.AdvanceStep();

        if ((_time.StepIndex % _structuralPeriodSteps) == 0)
            StructuralTick();
    }

    private static (int start, int end)[] BuildRanges(int len, int chunk)
    {
        if (len <= 0) return Array.Empty<(int, int)>();
        if (chunk <= 0) chunk = len;

        int count = (len + chunk - 1) / chunk;
        var ranges = new (int start, int end)[count];

        int idx = 0;
        for (int start = 0; start < len; start += chunk)
        {
            int end = Math.Min(len, start + chunk);
            ranges[idx++] = (start, end);
        }

        return ranges;
    }

    private void StructuralTick()
    {
        var all = _synapses.Values.ToList();
        var view = new TopologyView(_time.StepIndex, _nodes, _incoming, _outgoing, all, _spatial);

        _topology.StructuralTick(view);

        foreach (var add in _topology.ConsumePendingAdds())
            AddSynapse(add);

        foreach (var prune in _topology.ConsumePendingPrunes())
            RemoveSynapse(prune);

        foreach (var fb in _topology.ConsumePendingFeedback())
        {
            if (_synapses.TryGetValue(fb.SourceSynapse, out var src))
                src.ApplyGrowthFeedback(fb);
        }
    }

    private NodeActivity GetDelayedActivity(NoduleId id, int delaySteps)
    {
        if (!_activityDelay.TryGetValue(id, out var buf)) return default;

        delaySteps = Math.Clamp(delaySteps, 0, _maxDelay);
        return buf[delaySteps];
    }

    private void PushActivityDelay(NoduleId id, NodeActivity act)
    {
        if (!_activityDelay.TryGetValue(id, out var buf)) return;

        for (int d = _maxDelay; d >= 1; d--)
            buf[d] = buf[d - 1];

        buf[0] = act;
    }

    // ------------------------------------------------------------------------
    // Snapshot builder (canon Core snapshot)
    // ------------------------------------------------------------------------

    public LatticeSnapshot BuildSnapshot()
    {
        return new LatticeSnapshot
        {
            StepIndex = _time.StepIndex,
            Nodes = _nodes.Values.Select(n => new NodeSnap
            {
                Id = n.Id.Value,
                Pos = n.Position,
                V = n.Membrane.Potential,
                Rate = n.Activity.Rate,
                Spiked = n.Activity.Spiked
            }).ToList(),
            Synapses = _synapses.Values.Select(s => new EdgeSnap
            {
                Id = s.Id.Value,
                Pre = s.Pre.Value,
                Post = s.Post.Value,
                W = s.Weight,
                Kind = (int)s.Kind
            }).ToList()
        };
    }

    // ------------------------------------------------------------------------
    // Canonical archive save/load (aligned to your existing types)
    // ------------------------------------------------------------------------

    public byte[] Save()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(3);
        bw.Write(_time.StepIndex);

        // Nodes
        bw.Write(_nodes.Count);
        foreach (var n in _nodes.Values.OrderBy(x => x.Id.Value))
        {
            bw.Write(n.Id.Value);
            bw.Write(n.Position.X);
            bw.Write(n.Position.Y);
            bw.Write(n.Position.Z);

            bw.Write((int)n.Role);

            bw.Write(n.Membrane.Potential);
            bw.Write(n.Membrane.Threshold);
            bw.Write(n.Membrane.Leak);
            bw.Write(n.Membrane.Refractory);
            bw.Write(n.Membrane.Bias);

            bw.Write(n.Activity.Rate);
            bw.Write(n.Activity.Spiked);
            bw.Write(n.Activity.LastSpikeStep);
        }

        // Synapses
        bw.Write(_synapses.Count);
        foreach (var s in _synapses.Values.OrderBy(x => x.Id.Value))
        {
            bw.Write(s.Id.Value);
            bw.Write(s.Pre.Value);
            bw.Write(s.Post.Value);
            bw.Write((int)s.Kind);

            if (s is ISynapse2Serializable ser)
            {
                bw.Write(true);
                ser.WriteState(bw);
            }
            else
            {
                bw.Write(false);
                bw.Write(s.Weight);
                bw.Write(s.Gain);
                bw.Write(s.DelaySteps);
            }
        }

        // Topology state
        bw.Write(true);
        _topology.WriteState(bw);

        // IdleDrive config (optional tail for archive v3)
        bw.Write(true);
        bw.Write(_idleDrive.Enabled);
        bw.Write(_idleDrive.Amplitude);
        bw.Write(_idleDrive.Probability);
        bw.Write(_idleDrive.InhibitoryBias);

        return ms.ToArray();
    }

    public void Load(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var br = new BinaryReader(ms);

        int ver = br.ReadInt32();
        if (ver != 3) throw new InvalidOperationException($"Unsupported engine archive version: {ver}");

        long step = br.ReadInt64();

        ClearAll();

        int nodeCount = br.ReadInt32();
        for (int i = 0; i < nodeCount; i++)
        {
            var id = new NoduleId(br.ReadInt32());
            var pos = new Int3(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            var role = (NoduleRole)br.ReadInt32();

            var n = new NoduleLattice.Core.Nodes.Nodule(id, pos) { Role = role };

            var m = n.Membrane;
            m.Potential = br.ReadSingle();
            m.Threshold = br.ReadSingle();
            m.Leak = br.ReadSingle();
            m.Refractory = br.ReadInt32();
            m.Bias = br.ReadSingle();
            n.Membrane = m;

            var a = n.Activity;
            a.Rate = br.ReadSingle();
            a.Spiked = br.ReadBoolean();
            a.LastSpikeStep = br.ReadInt64();
            n.Activity = a;

            AddNode(n);
        }

        int synCount = br.ReadInt32();
        var synCfg = new Synapse2Config();

        for (int i = 0; i < synCount; i++)
        {
            var sid = new SynapseId(br.ReadInt64());
            var pre = new NoduleId(br.ReadInt32());
            var post = new NoduleId(br.ReadInt32());
            var kind = (SynapseKind)br.ReadInt32();

            bool hasFull = br.ReadBoolean();
            var syn = new Synapse2(sid, pre, post, kind, synCfg);

            if (hasFull)
                ((ISynapse2Serializable)syn).ReadState(br);
            else
            {
                syn.Weight = br.ReadSingle();
                syn.Gain = br.ReadSingle();
                syn.DelaySteps = br.ReadInt32();
            }

            AddSynapse(syn);
        }

        bool hasTopo = br.ReadBoolean();
        if (hasTopo)
            _topology.ReadState(br);

        // IdleDrive optional tail
        if (ms.Position < ms.Length)
        {
            bool hasIdle = br.ReadBoolean();
            if (hasIdle)
            {
                _idleDrive = new IdleDriveConfig
                {
                    Enabled = br.ReadBoolean(),
                    Amplitude = br.ReadSingle(),
                    Probability = br.ReadSingle(),
                    InhibitoryBias = br.ReadSingle()
                };
            }
            else
            {
                _idleDrive = IdleDriveConfig.Disabled;
            }
        }
        else
        {
            _idleDrive = IdleDriveConfig.Disabled;
        }

        _time.Reset(step);

        foreach (var n in _nodes.Values)
        {
            for (int i = 0; i < _activityDelay[n.Id].Length; i++)
                _activityDelay[n.Id][i] = n.Activity;
        }

        _replay.Reset(_nodes.Keys);

        // Hippocampus: treat archive load as "fresh life" for episodic memory.
        _hippocampus.ResetStats();
    }
}