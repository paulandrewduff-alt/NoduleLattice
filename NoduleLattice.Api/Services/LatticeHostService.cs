using System.Text;
using System.Text.Json;
using NoduleLattice.Api.Models;

namespace NoduleLattice.Api.Services;

/// <summary>
/// Minimal, functional host for the lattice.
///
/// Entry 013f:
/// - Thalamus Phase 1: channels + gates + routing
/// - Stimulus routes through thalamus into target regions
/// - Gates are explicit, but modulators bias effective throughput (state)
/// </summary>
public sealed class LatticeHostService
{
    private readonly object _gate = new();
    private readonly Random _rng = new(12345);

    private long _step;
    private readonly List<NodeState> _nodes = new();
    private readonly List<EdgeState> _edges = new();

    private ModulatorsRequest _mods = new();
    private ThalamusState _thal = new();

    public LatticeHostService()
    {
        // Small 3D cloud (sanity view)
        const int n = 240;

        for (int i = 0; i < n; i++)
        {
            _nodes.Add(new NodeState
            {
                Id = i + 1,
                X = _rng.Next(-10, 11),
                Y = _rng.Next(-6, 7),
                Z = _rng.Next(-10, 11),
                V = (float)(_rng.NextDouble() * 2.0 - 1.0),
                Rate = 0f,
                Spiked = false
            });
        }

        // Random sparse edges
        long eid = 1;
        for (int i = 0; i < n * 4; i++)
        {
            int pre = _rng.Next(1, n + 1);
            int post = _rng.Next(1, n + 1);
            if (pre == post) continue;

            _edges.Add(new EdgeState
            {
                Id = eid++,
                Pre = pre,
                Post = post,
                Kind = _rng.NextDouble() < 0.75 ? 0 : 1,
                W = (float)(_rng.NextDouble() * 1.2)
            });
        }

        // Default thalamus gates: sensory open, internal partially open
        _thal = new ThalamusState
        {
            VisionGate = 1.0f,
            AudioGate = 1.0f,
            BodyGate = 1.0f,
            InternalGate = 0.35f
        };
    }

    public LatticeSnapshotDto GetSnapshot()
    {
        lock (_gate)
        {
            return new LatticeSnapshotDto
            {
                StepIndex = _step,
                Nodes = _nodes.Select(n => new NodeSnapDto
                {
                    Id = n.Id,
                    Pos = new Pos3Dto { X = n.X, Y = n.Y, Z = n.Z },
                    V = n.V,
                    Rate = n.Rate,
                    Spiked = n.Spiked
                }).ToList(),
                Synapses = _edges.Select(e => new EdgeSnapDto
                {
                    Id = e.Id,
                    Pre = e.Pre,
                    Post = e.Post,
                    W = e.W,
                    Kind = e.Kind
                }).ToList()
            };
        }
    }

    public void Step(int steps)
    {
        if (steps <= 0) return;
        lock (_gate) StepInternal_NoLock(steps);
    }

    public void Inject(InjectRequest req)
    {
        lock (_gate)
        {
            var n = _nodes.FirstOrDefault(x => x.Id == req.NodeId);
            if (n is null) return;

            n.V += req.Exc;
            n.V -= req.Inh;
        }
    }

    public void SetModulators(ModulatorsRequest req)
    {
        lock (_gate)
        {
            _mods = req;
        }
    }

    public void SetThalamusGates(ThalamusGatesRequest req)
    {
        lock (_gate)
        {
            _thal.VisionGate = Clamp01(req.VisionGate);
            _thal.AudioGate = Clamp01(req.AudioGate);
            _thal.BodyGate = Clamp01(req.BodyGate);
            _thal.InternalGate = Clamp01(req.InternalGate);
        }
    }

    public void SleepReplay(bool run)
    {
        if (!run) return;

        // Minimal placeholder: close sensory gates, increase internal gate slightly.
        ModulatorsRequest savedMods;
        ThalamusState savedThal;

        lock (_gate)
        {
            savedMods = _mods;
            savedThal = _thal;

            _mods = new ModulatorsRequest
            {
                Reward = savedMods.Reward,
                Salience = savedMods.Salience,
                Stability = MathF.Max(savedMods.Stability, 0.85f),
                Alerting = MathF.Min(savedMods.Alerting, 0.15f),
                Curiosity = savedMods.Curiosity,
                Goal = savedMods.Goal
            };

            _thal = new ThalamusState
            {
                VisionGate = 0.05f,
                AudioGate = 0.05f,
                BodyGate = 0.05f,
                InternalGate = MathF.Max(savedThal.InternalGate, 0.55f)
            };

            StepInternal_NoLock(96);

            _mods = savedMods;
            _thal = savedThal;
        }
    }

    /// <summary>
    /// Explicit sensory stimulation routed through thalamus.
    /// Group: 0=Vision, 1=Audio, 2=Body, 3=Internal (reserved).
    ///
    /// Implementation:
    /// - Generate Poisson events from the channel source band
    /// - Route each event into the channel target region, scaled by EffectiveGate
    /// - Advance dynamics each tick
    /// </summary>
    public void Stimulus(StimulusRequest req)
    {
        lock (_gate)
        {
            var sources = SelectSourceBand(req.Group);
            var targets = SelectTargetRegion(req.Group);
            if (targets.Count == 0) return;

            // Poisson per tick: p = rateHz * dt; choose dt=0.02 (~50Hz)
            float dt = 0.02f;
            float p = Math.Clamp(req.RateHz * dt, 0f, 1f);
            float baseAmp = req.Strength;

            int steps = Math.Max(1, req.Steps);

            for (int t = 0; t < steps; t++)
            {
                float gEff = EffectiveGate(req.Group);
                if (gEff > 0.0001f)
                {
                    // if a channel has no external sources (internal), we synthesize a small number of events
                    int eventCount = sources.Count > 0 ? sources.Count : 12;

                    for (int i = 0; i < eventCount; i++)
                    {
                        if (_rng.NextDouble() >= p) continue;

                        // pick a target and inject
                        var tgt = targets[_rng.Next(targets.Count)];

                        // precision: higher stability -> less injection noise
                        float precision = 0.55f + 0.40f * Clamp01(_mods.Stability);
                        float noise = ((float)_rng.NextDouble() * 2f - 1f) * (1f - precision) * 0.08f;

                        tgt.V += (baseAmp * gEff) + noise;
                    }
                }

                StepInternal_NoLock(1);
            }
        }
    }

    public ArchiveDto SaveArchive()
    {
        lock (_gate)
        {
            var payload = new ArchivePayload
            {
                Step = _step,
                Mods = _mods,
                Thal = _thal,
                Nodes = _nodes,
                Edges = _edges
            };

            var json = JsonSerializer.Serialize(payload);
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            return new ArchiveDto { Base64 = b64 };
        }
    }

    public void LoadArchive(ArchiveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Base64)) return;

        lock (_gate)
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(dto.Base64.Trim()));
            var payload = JsonSerializer.Deserialize<ArchivePayload>(json);
            if (payload is null) return;

            _step = payload.Step;
            _mods = payload.Mods ?? new ModulatorsRequest();
            _thal = payload.Thal ?? new ThalamusState();

            _nodes.Clear();
            _nodes.AddRange(payload.Nodes ?? new List<NodeState>());

            _edges.Clear();
            _edges.AddRange(payload.Edges ?? new List<EdgeState>());
        }
    }

    // -----------------------
    // Thalamus helpers
    // -----------------------

    private float EffectiveGate(int group)
    {
        // explicit base gates
        float baseGate = group switch
        {
            0 => _thal.VisionGate,
            1 => _thal.AudioGate,
            2 => _thal.BodyGate,
            3 => _thal.InternalGate,
            _ => 1f
        };

        // state effect: alerting opens throughput, stability reduces disruptive variability
        float alert = Clamp01(_mods.Alerting);
        float stab = Clamp01(_mods.Stability);

        float stateFactor = (0.55f + 0.60f * alert) * (0.80f + 0.20f * stab);

        // top-down bias: goal opens sensory lanes selectively; salience opens whichever lane is being used
        float goal = Clamp01(_mods.Goal);
        float sal = Clamp01(_mods.Salience);

        float bias = 1.0f + 0.25f * goal + 0.20f * sal;

        return Clamp01(baseGate * stateFactor * bias);
    }

    private List<NodeState> SelectSourceBand(int group)
    {
        // External input interfaces.
        return group switch
        {
            0 => _nodes.Where(n => n.X <= -7).ToList(), // Vision afferents
            1 => _nodes.Where(n => n.Z <= -7).ToList(), // Audio afferents
            2 => _nodes.Where(n => n.Y <= -4).ToList(), // Body afferents
            3 => new List<NodeState>(),                 // Internal has no external source band
            _ => _nodes.Where(n => n.X <= -7).ToList()
        };
    }

    private List<NodeState> SelectTargetRegion(int group)
    {
        // Cortical entry zones.
        return group switch
        {
            0 => _nodes.Where(n => n.X >= +6).ToList(),
            1 => _nodes.Where(n => n.Z >= +6).ToList(),
            2 => _nodes.Where(n => n.Y >= +3).ToList(),
            3 => _nodes.Where(n => Math.Abs(n.X) <= 2 && Math.Abs(n.Y) <= 2 && Math.Abs(n.Z) <= 2).ToList(),
            _ => _nodes.Where(n => Math.Abs(n.X) <= 2 && Math.Abs(n.Y) <= 2 && Math.Abs(n.Z) <= 2).ToList()
        };
    }

    // -----------------------
    // Dynamics
    // -----------------------

    private void StepInternal_NoLock(int steps)
    {
        for (int s = 0; s < steps; s++)
        {
            _step++;

            foreach (var n in _nodes)
                n.Spiked = false;

            var byId = _nodes.ToDictionary(x => x.Id);

            // 1) Synaptic integration (strong)
            foreach (var e in _edges)
            {
                if (!byId.TryGetValue(e.Pre, out var pre)) continue;
                if (!byId.TryGetValue(e.Post, out var post)) continue;

                float current = pre.V * e.W * 0.08f;
                if (e.Kind == 1) current = -current;

                post.V += current;
            }

            // 2) Modulators influence gain/noise/threshold
            float reward = Clamp01(_mods.Reward);
            float sal = Clamp01(_mods.Salience);
            float stab = Clamp01(_mods.Stability);
            float alert = Clamp01(_mods.Alerting);
            float curiosity = Clamp01(_mods.Curiosity);
            float goal = Clamp01(_mods.Goal);

            float gain = 1.0f + 0.35f * alert + 0.25f * sal + 0.15f * curiosity + 0.10f * goal;
            float noiseAmp = 0.08f * (1f - 0.70f * stab);

            foreach (var n in _nodes)
            {
                float noise = ((float)_rng.NextDouble() * 2f - 1f) * noiseAmp;

                n.V = (n.V * 0.93f + noise) * gain;

                float thr = 1.0f - (0.12f * reward + 0.08f * sal);

                if (n.V > thr)
                {
                    n.Spiked = true;
                    n.V = -0.65f;
                    n.Rate = Clamp01(n.Rate * 0.85f + 0.35f);
                }
                else
                {
                    n.Rate = Clamp01(n.Rate * 0.92f + (MathF.Max(n.V, 0f) * 0.02f));
                }
            }
        }
    }

    // -----------------------
    // Archive + helpers
    // -----------------------

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    private sealed class ArchivePayload
    {
        public long Step { get; set; }
        public ModulatorsRequest? Mods { get; set; }
        public ThalamusState? Thal { get; set; }
        public List<NodeState>? Nodes { get; set; }
        public List<EdgeState>? Edges { get; set; }
    }

    private sealed class ThalamusState
    {
        public float VisionGate { get; set; } = 1f;
        public float AudioGate { get; set; } = 1f;
        public float BodyGate { get; set; } = 1f;
        public float InternalGate { get; set; } = 0.35f;
    }

    private sealed class NodeState
    {
        public int Id { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public float V { get; set; }
        public float Rate { get; set; }
        public bool Spiked { get; set; }
    }

    private sealed class EdgeState
    {
        public long Id { get; set; }
        public int Pre { get; set; }
        public int Post { get; set; }
        public float W { get; set; }
        public int Kind { get; set; }
    }
}