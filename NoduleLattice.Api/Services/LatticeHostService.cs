using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;
using NoduleLattice.Abstractions.Synapses;
using NoduleLattice.Api.Dtos;
using NoduleLattice.Core.Builders;
using NoduleLattice.Core.Determinism;
using NoduleLattice.Core.Modulation;
using NoduleLattice.Core.Runtime;
using NoduleLattice.Core.Runtime.Snapshots;
using NoduleLattice.Core.Synapses;
using NoduleLattice.Core.Time;
using NoduleLattice.Core.Topology;
using System.Collections.Concurrent;

namespace NoduleLattice.Api.Services;

public sealed class LatticeHostService
{
    private readonly object _gate = new();

    private readonly UniformModulatorField _mods = new();
    private readonly FixedTimebase _time = new();

    private DeterministicRng _rng = new(0xC0FFEEu);
    private Synapse2Config _synCfg = new();
    private StructuralPolicy _policy = new();

    private BasicTopologyManager _topology;
    private readonly MembraneIntegrator _membrane = new();
    private readonly DemandEstimator _demand = new();
    private UtilityProbe _probe;

    private NoduleLatticeEngine _engine;

    private long _nextSynapseId = 1;

    private ThalamusGates _thalamus = ThalamusGates.Default;

    private readonly ConcurrentDictionary<int, int[]> _groups = new();

    // 1-based id -> position (best-effort; rebuilt on archive load)
    private Int3[] _posById = Array.Empty<Int3>();

    public LatticeHostService()
    {
        _topology = new BasicTopologyManager(_policy, _synCfg, _rng);
        _probe = new UtilityProbe(new UtilityProbeConfig());

        _engine = new NoduleLatticeEngine(
            _time,
            _mods,
            _topology,
            _membrane,
            _demand,
            _probe);

        Create(new CreateLatticeRequest());
    }

    public LatticeSnapshotDto Create(CreateLatticeRequest req)
    {
        lock (_gate)
        {
            _time.Reset(0);

            _rng = new DeterministicRng((uint)req.Seed);
            _nextSynapseId = 1;

            _synCfg = new Synapse2Config();
            _policy = new StructuralPolicy();
            _topology = new BasicTopologyManager(_policy, _synCfg, _rng);
            _probe = new UtilityProbe(new UtilityProbeConfig());

            _engine = new NoduleLatticeEngine(
                _time,
                _mods,
                _topology,
                _membrane,
                _demand,
                _probe,
                structuralPeriodSteps: System.Math.Max(8, req.StructuralPeriodSteps),
                maxDelaySteps: System.Math.Max(1, req.MaxDelaySteps));

            _engine.ClearAll();
            _groups.Clear();

            // ------------------------------------------------------------------
            // Canon: hemispheres + corpus callosum are built by Core builders.
            // API only orchestrates.
            //
            // Interpret req.SizeX as total width including an inter-hemispheric gap.
            // We split into left/right as evenly as possible.
            // ------------------------------------------------------------------

            int totalX = System.Math.Max(4, req.SizeX);
            int sy = System.Math.Max(1, req.SizeY);
            int sz = System.Math.Max(1, req.SizeZ);

            var cortex = BuildDefaultHemispheresConfig(totalX, sy, sz);

            // Build cortex nodes + corpus callosum synapses in Core
            CortexHemisphereBuilder.Build(
                engine: _engine,
                cfg: cortex,
                synCfg: _synCfg,
                rng: _rng,
                startingNodeId: 1,
                startingSynapseId: _nextSynapseId);

            // Re-index positions from snapshot (supports gap + any future structures)
            var snapAfterBuild = _engine.BuildSnapshot();
            EnsurePosIndexFromSnapshot(snapAfterBuild);

            // Local intra-hemisphere / laminar wiring (still orchestrated here)
            // IMPORTANT: This must respect the inter-hemispheric gap.
            SeedCortexConnectivitySparse(
                totalSynapses: System.Math.Max(0, req.InitialSynapses),
                localRadiusXY: System.Math.Max(1, req.LocalRadiusXY),
                columnLinksPerNode: System.Math.Max(0, req.ColumnLinksPerNode),
                feedForwardLinksPerNode: System.Math.Max(0, req.FeedForwardLinksPerNode),
                maxOutPerNode: System.Math.Max(1, req.MaxOutPerNode),
                maxDelaySteps: System.Math.Max(1, req.MaxDelaySteps));

            // Groups by X-span across the built cortex (vision/audio/body thirds)
            RebuildGroupsFromSnapshot(_engine.BuildSnapshot());

            return Map(_engine.BuildSnapshot(), edgesOverride: null);
        }
    }

    public void Step(int steps)
    {
        lock (_gate)
        {
            _engine.Step(System.Math.Max(1, steps));
        }
    }

    public LatticeSnapshotDto GetSnapshot()
    {
        lock (_gate)
        {
            return Map(_engine.BuildSnapshot(), edgesOverride: null);
        }
    }

    // Thin snapshot: cap edges and drop long edges to keep UI real-time.
    // - maxEdges: absolute cap
    // - maxLen: euclidean in *grid* coords (unscaled). (UI does its own scaling.)
    public LatticeSnapshotDto GetSnapshotThin(int maxEdges, float maxLen)
    {
        lock (_gate)
        {
            var snap = _engine.BuildSnapshot();

            // Ensure _posById is valid after archive loads (or any future rebuild)
            EnsurePosIndexFromSnapshot(snap);

            var edges = snap.Synapses;
            if (edges.Count == 0)
                return Map(snap, edgesOverride: Array.Empty<EdgeSnap>());

            float maxLenSq = maxLen * maxLen;

            // 1) Filter by length
            List<EdgeSnap> filtered = new(edges.Count);
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (!TryGetPos(e.Pre, out var a)) continue;
                if (!TryGetPos(e.Post, out var b)) continue;

                int dx = a.X - b.X;
                int dy = a.Y - b.Y;
                int dz = a.Z - b.Z;

                float d2 = (dx * dx) + (dy * dy) + (dz * dz);
                if (d2 <= maxLenSq)
                    filtered.Add(e);
            }

            if (filtered.Count <= maxEdges)
                return Map(snap, edgesOverride: filtered);

            // 2) Take top by |w|
            filtered.Sort(static (x, y) =>
            {
                float ax = System.MathF.Abs(x.W);
                float ay = System.MathF.Abs(y.W);
                return ay.CompareTo(ax);
            });

            var top = filtered.Take(maxEdges).ToList();
            return Map(snap, edgesOverride: top);
        }
    }

    public void Inject(InjectRequest req)
    {
        lock (_gate)
        {
            _engine.InjectInput(new NoduleId(req.NodeId), req.Exc, req.Inh);
        }
    }

    public void SetModulators(ModulatorsRequest req)
    {
        lock (_gate)
        {
            _engine.SetModulators(new ModulatorVector
            {
                Reward = req.Reward,
                Salience = req.Salience,
                Stability = req.Stability,
                Alerting = req.Alerting,
                Curiosity = req.Curiosity,
                Goal = req.Goal
            });
        }
    }

    public void SleepReplay(bool run)
    {
        lock (_gate)
        {
            if (run)
                _engine.SleepReplay();
        }
    }

    public void SetThalamusGates(ThalamusGatesRequest req)
    {
        lock (_gate)
        {
            _thalamus = new ThalamusGates(
                Vision: Clamp01(req.VisionGate),
                Audio: Clamp01(req.AudioGate),
                Body: Clamp01(req.BodyGate),
                Internal: Clamp01(req.InternalGate));
        }
    }

    public void Stimulus(StimulusRequest req)
    {
        lock (_gate)
        {
            if (!_groups.TryGetValue(req.Group, out var ids) || ids.Length == 0)
                return;

            float gate = _thalamus.ForGroup(req.Group);
            float eff = System.Math.Max(0f, req.RateHz) * Clamp01(req.Strength) * gate;

            if (eff <= 0.0001f) return;

            float per = eff / System.Math.Max(1, ids.Length);

            foreach (var id in ids)
                _engine.InjectInput(new NoduleId(id), per);

            int burstSteps = System.Math.Max(0, req.Steps);
            if (burstSteps > 0)
                _engine.Step(burstSteps);
        }
    }

    public ArchiveDto SaveArchive()
    {
        lock (_gate)
        {
            var bytes = _engine.Save();
            return new ArchiveDto { Base64 = Convert.ToBase64String(bytes) };
        }
    }

    public void LoadArchive(ArchiveDto dto)
    {
        lock (_gate)
        {
            var bytes = Convert.FromBase64String(dto.Base64);
            _engine.Load(bytes);

            // rebuild groups and pos index
            var snap = _engine.BuildSnapshot();
            RebuildGroupsFromSnapshot(snap);
            EnsurePosIndexFromSnapshot(snap);
        }
    }

    public object Validate()
    {
        lock (_gate)
        {
            var snap = _engine.BuildSnapshot();
            var errors = new List<string>();

            if (snap.StepIndex < 0) errors.Add("StepIndex < 0");
            if (snap.Nodes.Count == 0) errors.Add("No nodes present");

            foreach (var n in snap.Nodes)
            {
                if (float.IsNaN(n.V) || float.IsInfinity(n.V)) errors.Add($"Node {n.Id} has invalid V");
                if (float.IsNaN(n.Rate) || float.IsInfinity(n.Rate)) errors.Add($"Node {n.Id} has invalid Rate");
            }

            foreach (var e in snap.Synapses)
            {
                if (e.Pre == e.Post) errors.Add($"Self-edge {e.Id} ({e.Pre}->{e.Post})");
                if (float.IsNaN(e.W) || float.IsInfinity(e.W)) errors.Add($"Edge {e.Id} has invalid W");
            }

            return new
            {
                ok = errors.Count == 0,
                step = snap.StepIndex,
                nodes = snap.Nodes.Count,
                synapses = snap.Synapses.Count,
                thalamus = new { _thalamus.Vision, _thalamus.Audio, _thalamus.Body, _thalamus.Internal },
                errors
            };
        }
    }


    // ------------------------ Cortex hemispheres bootstrap ------------------------

    private static CortexHemispheresConfig BuildDefaultHemispheresConfig(int totalX, int heightY, int layersZ)
    {
        // A small default gap makes the separation visible without killing density.
        const int gap = 2;

        int usable = System.Math.Max(2, totalX - gap);
        int left = usable / 2;
        int right = usable - left;

        return new CortexHemispheresConfig
        {
            LeftWidthX = System.Math.Max(1, left),
            RightWidthX = System.Math.Max(1, right),
            InterHemisphericGapX = gap,
            HeightY = System.Math.Max(1, heightY),
            LayersZ = System.Math.Max(1, layersZ),

            EnableCorpusCallosum = true,
            CallosumDensity = 0.35f,
            CallosumBidirectional = true,
            CallosumBaseWeight = 0.12f,
            CallosumWeightJitter = 0.06f,
            CallosumInhibitoryChance = 0.02f,
            CallosumDelaySteps = 1
        };
    }

    // ------------------------ Cortex seeding ------------------------

    private void SeedCortexConnectivity(
        int sx,
        int sy,
        int sz,
        int totalSynapses,
        int localRadiusXY,
        int columnLinksPerNode,
        int feedForwardLinksPerNode,
        int maxOutPerNode,
        int maxDelaySteps)
    {
        if (totalSynapses <= 0) return;

        int nodeCount = sx * sy * sz;
        var outCount = new int[nodeCount + 1];

        // 1) Columnar vertical links
        int made = 0;
        for (int pre = 1; pre <= nodeCount && made < totalSynapses; pre++)
        {
            var p = _posById[pre];
            for (int k = 0; k < columnLinksPerNode && made < totalSynapses; k++)
            {
                if (outCount[pre] >= maxOutPerNode) break;

                int dz = (NextFloat01() < 0.5f) ? 1 : -1;
                int nz = p.Z + dz;
                if (nz < 0 || nz >= sz) continue;

                int post = IdFromXYZ(p.X, p.Y, nz, sx, sy);
                if (post == pre) continue;

                AddSyn(pre, post, outCount, maxOutPerNode, maxDelaySteps);
                made++;
            }
        }

        // 2) Feed-forward laminar links
        for (int pre = 1; pre <= nodeCount && made < totalSynapses; pre++)
        {
            var p = _posById[pre];
            if (p.Z >= sz - 1) continue;

            for (int k = 0; k < feedForwardLinksPerNode && made < totalSynapses; k++)
            {
                if (outCount[pre] >= maxOutPerNode) break;

                int dx = NextInt(-localRadiusXY, localRadiusXY + 1);
                int dy = NextInt(-localRadiusXY, localRadiusXY + 1);

                int nx = p.X + dx;
                int ny = p.Y + dy;
                int nz = p.Z + 1;

                if (nx < 0 || nx >= sx) continue;
                if (ny < 0 || ny >= sy) continue;

                int post = IdFromXYZ(nx, ny, nz, sx, sy);
                if (post == pre) continue;

                AddSyn(pre, post, outCount, maxOutPerNode, maxDelaySteps);
                made++;
            }
        }

        // 3) Within-layer local recurrent fibres
        int attempts = 0;
        int maxAttempts = System.Math.Max(10_000, (totalSynapses - made) * 25);

        while (made < totalSynapses && attempts < maxAttempts)
        {
            attempts++;

            int pre = NextInt(1, nodeCount + 1);
            if (outCount[pre] >= maxOutPerNode) continue;

            var p = _posById[pre];

            int dx = NextInt(-localRadiusXY, localRadiusXY + 1);
            int dy = NextInt(-localRadiusXY, localRadiusXY + 1);
            if (dx == 0 && dy == 0) continue;

            int nx = p.X + dx;
            int ny = p.Y + dy;
            int nz = p.Z;

            if (nx < 0 || nx >= sx) continue;
            if (ny < 0 || ny >= sy) continue;

            int post = IdFromXYZ(nx, ny, nz, sx, sy);
            if (post == pre) continue;

            AddSyn(pre, post, outCount, maxOutPerNode, maxDelaySteps);
            made++;
        }
    }

    private void AddSyn(int pre, int post, int[] outCount, int maxOutPerNode, int maxDelaySteps)
    {
        if (outCount[pre] >= maxOutPerNode) return;

        var kind = (NextFloat01() < 0.80f) ? SynapseKind.Excitatory : SynapseKind.Inhibitory;

        float w = 0.02f + NextFloat01() * 0.16f;
        int delay = NextInt(0, System.Math.Max(1, maxDelaySteps));

        var syn = new Synapse2(
            id: new SynapseId(_nextSynapseId++),
            pre: new NoduleId(pre),
            post: new NoduleId(post),
            kind: kind,
            cfg: _synCfg,
            initialWeight: w,
            initialGain: 1.0f,
            delaySteps: delay,
            state: null);

        _engine.AddSynapse(syn);
        outCount[pre]++;
    }

    private static int IdFromXYZ(int x, int y, int z, int sx, int sy)
        => 1 + x + (y * sx) + (z * sx * sy);

    // Hemisphere-aware cortex seeding:
    // Works with gaps because it only connects when the target coordinate exists.
    private void SeedCortexConnectivitySparse(
        int totalSynapses,
        int localRadiusXY,
        int columnLinksPerNode,
        int feedForwardLinksPerNode,
        int maxOutPerNode,
        int maxDelaySteps)
    {
        if (totalSynapses <= 0) return;

        var snap = _engine.BuildSnapshot();
        if (snap.Nodes.Count == 0) return;

        // Build coordinate -> id map from snapshot (supports gaps and non-rectangular structures).
        var map = new Dictionary<(int x, int y, int z), int>(snap.Nodes.Count);
        int maxId = 0;
        for (int i = 0; i < snap.Nodes.Count; i++)
        {
            var n = snap.Nodes[i];
            map[(n.Pos.X, n.Pos.Y, n.Pos.Z)] = n.Id;
            if (n.Id > maxId) maxId = n.Id;
        }

        if (maxId <= 0) return;

        // Ensure _posById is aligned with current nodes
        EnsurePosIndexFromSnapshot(snap);

        var outCount = new int[maxId + 1];

        int made = 0;

        // 1) Columnar vertical links (within same x,y)
        for (int pre = 1; pre <= maxId && made < totalSynapses; pre++)
        {
            if (!TryGetPos(pre, out var p)) continue;

            for (int k = 0; k < columnLinksPerNode && made < totalSynapses; k++)
            {
                if (outCount[pre] >= maxOutPerNode) break;

                int dz = (NextFloat01() < 0.5f) ? 1 : -1;
                int nz = p.Z + dz;
                if (!map.TryGetValue((p.X, p.Y, nz), out int post)) continue;
                if (post == pre) continue;

                AddSyn(pre, post, outCount, maxOutPerNode, maxDelaySteps);
                made++;
            }
        }

        // 2) Feed-forward laminar links (z -> z+1) with local XY spread
        for (int pre = 1; pre <= maxId && made < totalSynapses; pre++)
        {
            if (!TryGetPos(pre, out var p)) continue;
            int nz = p.Z + 1;

            for (int k = 0; k < feedForwardLinksPerNode && made < totalSynapses; k++)
            {
                if (outCount[pre] >= maxOutPerNode) break;

                int dx = NextInt(-localRadiusXY, localRadiusXY + 1);
                int dy = NextInt(-localRadiusXY, localRadiusXY + 1);

                int nx = p.X + dx;
                int ny = p.Y + dy;

                if (!map.TryGetValue((nx, ny, nz), out int post)) continue;
                if (post == pre) continue;

                AddSyn(pre, post, outCount, maxOutPerNode, maxDelaySteps);
                made++;
            }
        }

        // 3) Within-layer local recurrent fibres
        int attempts = 0;
        int maxAttempts = System.Math.Max(10_000, (totalSynapses - made) * 25);

        while (made < totalSynapses && attempts < maxAttempts)
        {
            attempts++;

            int pre = NextInt(1, maxId + 1);
            if (outCount[pre] >= maxOutPerNode) continue;
            if (!TryGetPos(pre, out var p)) continue;

            int dx = NextInt(-localRadiusXY, localRadiusXY + 1);
            int dy = NextInt(-localRadiusXY, localRadiusXY + 1);
            if (dx == 0 && dy == 0) continue;

            int nx = p.X + dx;
            int ny = p.Y + dy;

            if (!map.TryGetValue((nx, ny, p.Z), out int post)) continue;
            if (post == pre) continue;

            AddSyn(pre, post, outCount, maxOutPerNode, maxDelaySteps);
            made++;
        }
    }

    // ------------------------ grouping ------------------------

    private void BuildDefaultGroups(int sx, int sy, int sz)
    {
        int nodeCount = sx * sy * sz;
        if (nodeCount <= 0) return;

        var vision = new List<int>();
        var audio = new List<int>();
        var body = new List<int>();

        int id = 1;
        for (int z = 0; z < sz; z++)
            for (int y = 0; y < sy; y++)
                for (int x = 0; x < sx; x++)
                {
                    if (x < (sx / 3)) vision.Add(id);
                    else if (x < (2 * sx / 3)) audio.Add(id);
                    else body.Add(id);

                    id++;
                }

        _groups[0] = vision.ToArray();
        _groups[1] = audio.ToArray();
        _groups[2] = body.ToArray();
        _groups[3] = Array.Empty<int>();
    }

    private void RebuildGroupsFromSnapshot(LatticeSnapshot snap)
    {
        if (snap.Nodes.Count == 0) return;

        int minX = snap.Nodes.Min(n => n.Pos.X);
        int maxX = snap.Nodes.Max(n => n.Pos.X);
        int span = System.Math.Max(1, (maxX - minX + 1));

        var vision = new List<int>();
        var audio = new List<int>();
        var body = new List<int>();

        foreach (var n in snap.Nodes)
        {
            float t = (n.Pos.X - minX) / (float)span;
            if (t < 0.33f) vision.Add(n.Id);
            else if (t < 0.66f) audio.Add(n.Id);
            else body.Add(n.Id);
        }

        _groups[0] = vision.ToArray();
        _groups[1] = audio.ToArray();
        _groups[2] = body.ToArray();
        _groups[3] = Array.Empty<int>();
    }

    // ------------------------ position index (thin snapshot helpers) ------------------------

    private void EnsurePosIndexFromSnapshot(LatticeSnapshot snap)
    {
        int maxId = 0;
        for (int i = 0; i < snap.Nodes.Count; i++)
            maxId = System.Math.Max(maxId, snap.Nodes[i].Id);

        if (maxId <= 0) { _posById = Array.Empty<Int3>(); return; }
        if (_posById.Length == maxId + 1)
            return;

        var arr = new Int3[maxId + 1];
        for (int i = 0; i < snap.Nodes.Count; i++)
        {
            var n = snap.Nodes[i];
            if (n.Id >= 0 && n.Id < arr.Length)
                arr[n.Id] = n.Pos;
        }

        _posById = arr;
    }

    private bool TryGetPos(int id, out Int3 pos)
    {
        if (id <= 0 || id >= _posById.Length)
        {
            pos = default;
            return false;
        }

        pos = _posById[id];
        return true;
    }

    // ------------------------ RNG helpers ------------------------

    private int NextInt(int minInclusive, int maxExclusive)
    {
        uint u = _rng.NextU();
        uint span = (uint)System.Math.Max(1, maxExclusive - minInclusive);
        return (int)(minInclusive + (u % span));
    }

    private float NextFloat01()
    {
        uint u = _rng.NextU();
        return (u & 0x00FFFFFF) / 16777215f;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    // ------------------------ Mapping ------------------------

    private static LatticeSnapshotDto Map(LatticeSnapshot snap, IReadOnlyList<EdgeSnap>? edgesOverride)
    {
        var edges = edgesOverride ?? snap.Synapses;

        return new LatticeSnapshotDto
        {
            StepIndex = snap.StepIndex,
            Nodes = snap.Nodes.Select(n => new NodeSnapDto
            {
                Id = n.Id,
                Pos = new Pos3Dto { X = n.Pos.X, Y = n.Pos.Y, Z = n.Pos.Z },
                V = n.V,
                Rate = n.Rate,
                Spiked = n.Spiked
            }).ToList(),
            Synapses = edges.Select(e => new EdgeSnapDto
            {
                Id = e.Id,
                Pre = e.Pre,
                Post = e.Post,
                W = e.W,
                Kind = e.Kind
            }).ToList()
        };
    }

    private readonly record struct ThalamusGates(float Vision, float Audio, float Body, float Internal)
    {
        public static ThalamusGates Default => new(1f, 1f, 1f, 0.35f);

        public float ForGroup(int group) => group switch
        {
            0 => Vision,
            1 => Audio,
            2 => Body,
            3 => Internal,
            _ => 1f
        };
    }
}