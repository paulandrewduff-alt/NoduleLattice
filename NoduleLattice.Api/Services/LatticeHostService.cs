using System.Text;
using System.Text.Json;
using NoduleLattice.Api.Models;

namespace NoduleLattice.Api.Services;

/// <summary>
/// Minimal, functional host for the lattice.
/// Boot-sanity implementation so API + Swagger + Blazor UI run end-to-end.
/// \n
/// Updated in this entry:
/// - Stronger, visually obvious dynamics (spiking + modulation gain)
/// - Explicit sensory stimulation endpoint (Poisson drive into an input band)
/// </summary>
public sealed class LatticeHostService
{
    private readonly object _gate = new();
    private readonly Random _rng = new(12345);

    private long _step;
    private readonly List<NodeState> _nodes = new();
    private readonly List<EdgeState> _edges = new();

    private ModulatorsRequest _mods = new();

    public LatticeHostService()
    {
        // Create a small 3D cloud (sanity view)
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

        lock (_gate)
        {
            for (int s = 0; s < steps; s++)
            {
                _step++;

                // Clear spike flags for this tick
                foreach (var n in _nodes)
                    n.Spiked = false;

                // Quick lookup
                var byId = _nodes.ToDictionary(x => x.Id);

                // 1) Synaptic integration (stronger; should be visually obvious)
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

                // 3) Per-neuron update (decay + noise + spike/reset)
                foreach (var n in _nodes)
                {
                    float noise = ((float)_rng.NextDouble() * 2f - 1f) * noiseAmp;

                    // decay + gain
                    n.V = (n.V * 0.93f + noise) * gain;

                    // excitability rises with reward/salience
                    float thr = 1.0f - (0.12f * reward + 0.08f * sal);

                    if (n.V > thr)
                    {
                        n.Spiked = true;
                        n.V = -0.65f; // deeper reset to make spikes distinct
                        n.Rate = Clamp01(n.Rate * 0.85f + 0.35f);
                    }
                    else
                    {
                        n.Rate = Clamp01(n.Rate * 0.92f + (MathF.Max(n.V, 0f) * 0.02f));
                    }
                }
            }
        }
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

    public void SleepReplay(bool run)
    {
        if (!run) return;

        // Minimal placeholder: run a chunk with high stability to reduce noise.
        ModulatorsRequest saved;

        lock (_gate)
        {
            saved = _mods;

            _mods = new ModulatorsRequest
            {
                Reward = saved.Reward,
                Salience = saved.Salience,
                Stability = MathF.Max(saved.Stability, 0.85f),
                Alerting = saved.Alerting,
                Curiosity = saved.Curiosity,
                Goal = saved.Goal
            };
        }

        Step(96);

        lock (_gate)
        {
            _mods = saved;
        }
    }

    /// <summary>
    /// Explicit sensory stimulation.
    /// Current mapping: Group=0 targets an \"input band\" (nodes with X <= -5).
    /// Applies a Poisson drive for the specified number of steps and advances dynamics.
    /// </summary>
    public void Stimulus(StimulusRequest req)
    {
        // This method calls Step(1) internally; we avoid nested lock deadlocks by locking once and using an internal stepping routine.
        lock (_gate)
        {
            var band = SelectStimulusGroup(req.Group);
            if (band.Count == 0) return;

            // Poisson drive per tick: p = rateHz * dt; choose dt=0.02 (~50Hz tick)
            float dt = 0.02f;
            float p = Math.Clamp(req.RateHz * dt, 0f, 1f);
            float amp = req.Strength;

            int steps = Math.Max(1, req.Steps);

            for (int i = 0; i < steps; i++)
            {
                foreach (var n in band)
                {
                    if (_rng.NextDouble() < p)
                        n.V += amp;
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

            _nodes.Clear();
            _nodes.AddRange(payload.Nodes ?? new List<NodeState>());

            _edges.Clear();
            _edges.AddRange(payload.Edges ?? new List<EdgeState>());
        }
    }

    // -----------------------
    // Internals
    // -----------------------

    private List<NodeState> SelectStimulusGroup(int group)
    {
        // Reserved for future: group-based modality routing.
        // For now, group 0 = default sensory band on the \"left\" (X <= -5).
        return group switch
        {
            0 => _nodes.Where(n => n.X <= -5).ToList(),
            _ => _nodes.Where(n => n.X <= -5).ToList()
        };
    }

    private void StepInternal_NoLock(int steps)
    {
        // Same as Step, but assumes _gate is already held.
        if (steps <= 0) return;

        for (int s = 0; s < steps; s++)
        {
            _step++;

            foreach (var n in _nodes)
                n.Spiked = false;

            var byId = _nodes.ToDictionary(x => x.Id);

            foreach (var e in _edges)
            {
                if (!byId.TryGetValue(e.Pre, out var pre)) continue;
                if (!byId.TryGetValue(e.Post, out var post)) continue;

                float current = pre.V * e.W * 0.08f;
                if (e.Kind == 1) current = -current;

                post.V += current;
            }

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

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    private sealed class ArchivePayload
    {
        public long Step { get; set; }
        public ModulatorsRequest? Mods { get; set; }
        public List<NodeState>? Nodes { get; set; }
        public List<EdgeState>? Edges { get; set; }
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
