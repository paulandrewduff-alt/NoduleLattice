using System.Text;
using System.Text.Json;
using NoduleLattice.Api.Models;

namespace NoduleLattice.Api.Services;

public sealed class LatticeHostService
{
    private readonly object _gate = new();
    private readonly Random _rng = new(12345);

    private long _step;
    private readonly List<NodeState> _nodes = new();
    private readonly List<EdgeState> _edges = new();

    private ModulatorsRequest _mods = new();
    private ThalamusState _thal = new();

    private HippocampusConfigRequest _hipCfg = new();
    private long _hipNextId = 1;
    private readonly List<Episode> _episodes = new();

    public LatticeHostService()
    {
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

        _thal = new ThalamusState
        {
            VisionGate = 1.0f,
            AudioGate = 1.0f,
            BodyGate = 1.0f,
            InternalGate = 0.35f
        };

        _hipCfg = new HippocampusConfigRequest();
    }

    // -----------------------
    // Snapshot
    // -----------------------

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
                    V = SafeFiniteOr(n.V, 0f),
                    Rate = SafeFiniteOr(n.Rate, 0f),
                    Spiked = n.Spiked
                }).ToList(),
                Synapses = _edges.Select(e => new EdgeSnapDto
                {
                    Id = e.Id,
                    Pre = e.Pre,
                    Post = e.Post,
                    W = SafeFiniteOr(e.W, 0f),
                    Kind = e.Kind
                }).ToList()
            };
        }
    }

    // -----------------------
    // Core controls
    // -----------------------

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

            n.V = SafeFiniteOr(n.V, 0f);
            n.V += SafeFiniteOr(req.Exc, 0f);
            n.V -= SafeFiniteOr(req.Inh, 0f);
            n.V = StabilizeV(n.V);
        }
    }

    public void SetModulators(ModulatorsRequest req)
    {
        lock (_gate)
        {
            // sanitize inputs so they can't poison state
            _mods = new ModulatorsRequest
            {
                Reward = Safe01(req.Reward),
                Salience = Safe01(req.Salience),
                Stability = Safe01(req.Stability),
                Alerting = Safe01(req.Alerting),
                Curiosity = Safe01(req.Curiosity),
                Goal = Safe01(req.Goal)
            };
        }
    }

    public void SetThalamusGates(ThalamusGatesRequest req)
    {
        lock (_gate)
        {
            _thal.VisionGate = Safe01(req.VisionGate);
            _thal.AudioGate = Safe01(req.AudioGate);
            _thal.BodyGate = Safe01(req.BodyGate);
            _thal.InternalGate = Safe01(req.InternalGate);
        }
    }

    public void SleepReplay(bool run)
    {
        if (!run) return;

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

            // Run internal replays if present.
            ReplayHippocampus_NoLock(new HippocampusReplayRequest { Count = 6, Gain = 1.0f, StepsPerEpisode = 12 });

            StepInternal_NoLock(48);

            _mods = savedMods;
            _thal = savedThal;
        }
    }

    // -----------------------
    // Stimulus via thalamus
    // -----------------------

    public void Stimulus(StimulusRequest req)
    {
        lock (_gate)
        {
            var sources = SelectSourceBand(req.Group);
            var targets = SelectTargetRegion(req.Group);
            if (targets.Count == 0) return;

            float dt = 0.02f;
            float rateHz = SafeFiniteOr(req.RateHz, 0f);
            float p = Math.Clamp(rateHz * dt, 0f, 1f);
            float baseAmp = SafeFiniteOr(req.Strength, 0f);

            int steps = Math.Max(1, req.Steps);

            for (int t = 0; t < steps; t++)
            {
                float gEff = EffectiveGate(req.Group);
                if (gEff > 0.0001f)
                {
                    int eventCount = sources.Count > 0 ? sources.Count : 12;

                    for (int i = 0; i < eventCount; i++)
                    {
                        if (_rng.NextDouble() >= p) continue;

                        var tgt = targets[_rng.Next(targets.Count)];

                        float precision = 0.55f + 0.40f * Safe01(_mods.Stability);
                        float noise = ((float)_rng.NextDouble() * 2f - 1f) * (1f - precision) * 0.08f;
                        noise = SafeFiniteOr(noise, 0f);

                        tgt.V = SafeFiniteOr(tgt.V, 0f);
                        tgt.V += (baseAmp * gEff) + noise;
                        tgt.V = StabilizeV(tgt.V);
                    }
                }

                StepInternal_NoLock(1);
            }
        }
    }

    // -----------------------
    // Hippocampus API
    // -----------------------

    public void SetHippocampusConfig(HippocampusConfigRequest req)
    {
        lock (_gate)
        {
            _hipCfg = new HippocampusConfigRequest
            {
                CaptureEnabled = req.CaptureEnabled,
                CaptureSalienceThreshold = Safe01(req.CaptureSalienceThreshold),
                CaptureAlertingThreshold = Safe01(req.CaptureAlertingThreshold),
                TopK = Math.Clamp(req.TopK, 6, 64),
                MaxEpisodes = Math.Clamp(req.MaxEpisodes, 8, 2048),
                DecayPerStep = Math.Clamp(SafeFiniteOr(req.DecayPerStep, 0.0015f), 0.0001f, 0.05f),
                MinStrength = Math.Clamp(SafeFiniteOr(req.MinStrength, 0.06f), 0.001f, 1.0f)
            };

            EnforceEpisodeCapacity_NoLock();
        }
    }

    public HippocampusEpisodeListDto GetHippocampusEpisodes()
    {
        lock (_gate)
        {
            // Extra safety: purge anything non-finite before emitting
            SanitizeEpisodes_NoLock();

            return new HippocampusEpisodeListDto
            {
                CurrentStep = _step,
                Episodes = _episodes
                    .OrderByDescending(e => e.Strength)
                    .Select(e => new HippocampusEpisodeDto
                    {
                        Id = e.Id,
                        CapturedAtStep = e.CapturedAtStep,
                        Strength = SafeFiniteOr(e.Strength, 0f),

                        NodeIds = e.NodeIds.ToArray(),
                        Values = e.Values.Select(v => SafeFiniteOr(v, 0f)).ToArray(),

                        ContextMods = e.ContextMods,
                        ContextThalamus = e.ContextThalamus
                    })
                    .ToList()
            };
        }
    }

    public void ClearHippocampus()
    {
        lock (_gate)
        {
            _episodes.Clear();
        }
    }

    public void ReplayHippocampus(HippocampusReplayRequest req)
    {
        lock (_gate)
        {
            ReplayHippocampus_NoLock(req);
        }
    }

    private void ReplayHippocampus_NoLock(HippocampusReplayRequest req)
    {
        if (_episodes.Count == 0) return;

        SanitizeEpisodes_NoLock();

        int count = Math.Clamp(req.Count, 1, 128);
        float gain = Math.Clamp(SafeFiniteOr(req.Gain, 1f), 0.1f, 5.0f);
        int stepsPer = Math.Clamp(req.StepsPerEpisode, 1, 64);

        var chosen = _episodes
            .OrderByDescending(e =>
            {
                var age = (float)(_step - e.CapturedAtStep);
                if (age < 0) age = 0;
                float recency = 1.0f / (1.0f + age);
                return (e.Strength * 0.70f) + (recency * 0.30f);
            })
            .Take(count)
            .ToList();

        var coreTargets = SelectTargetRegion(3);
        if (coreTargets.Count == 0) return;

        foreach (var ep in chosen)
        {
            float epStr = SafeFiniteOr(ep.Strength, 0f);
            if (epStr <= 0.0001f) continue;

            int n = Math.Min(ep.NodeIds.Count, ep.Values.Count);

            for (int i = 0; i < n; i++)
            {
                var tgt = coreTargets[_rng.Next(coreTargets.Count)];
                float v = SafeFiniteOr(ep.Values[i], 0f);

                tgt.V = SafeFiniteOr(tgt.V, 0f);
                tgt.V += v * epStr * gain;
                tgt.V = StabilizeV(tgt.V);
            }

            StepInternal_NoLock(stepsPer);
        }
    }

    // -----------------------
    // Dynamics
    // -----------------------

    private void StepInternal_NoLock(int steps)
    {
        for (int s = 0; s < steps; s++)
        {
            _step++;

            // (A) Hippocampus decay each tick
            DecayEpisodes_NoLock();

            foreach (var n in _nodes)
                n.Spiked = false;

            var byId = _nodes.ToDictionary(x => x.Id);

            foreach (var e in _edges)
            {
                if (!byId.TryGetValue(e.Pre, out var pre)) continue;
                if (!byId.TryGetValue(e.Post, out var post)) continue;

                float preV = SafeFiniteOr(pre.V, 0f);
                float w = SafeFiniteOr(e.W, 0f);

                float current = preV * w * 0.08f;
                if (e.Kind == 1) current = -current;

                current = SafeFiniteOr(current, 0f);

                post.V = SafeFiniteOr(post.V, 0f);
                post.V += current;
                post.V = StabilizeV(post.V);
            }

            float reward = Safe01(_mods.Reward);
            float sal = Safe01(_mods.Salience);
            float stab = Safe01(_mods.Stability);
            float alert = Safe01(_mods.Alerting);
            float curiosity = Safe01(_mods.Curiosity);
            float goal = Safe01(_mods.Goal);

            float gain = 1.0f + 0.35f * alert + 0.25f * sal + 0.15f * curiosity + 0.10f * goal;
            gain = SafeFiniteOr(gain, 1f);

            float noiseAmp = 0.08f * (1f - 0.70f * stab);
            noiseAmp = SafeFiniteOr(noiseAmp, 0.05f);

            foreach (var n in _nodes)
            {
                float noise = ((float)_rng.NextDouble() * 2f - 1f) * noiseAmp;
                noise = SafeFiniteOr(noise, 0f);

                n.V = SafeFiniteOr(n.V, 0f);

                // keep values bounded so they can't drift into overflow
                n.V = (n.V * 0.93f + noise) * gain;
                n.V = StabilizeV(n.V);

                float thr = 1.0f - (0.12f * reward + 0.08f * sal);
                thr = SafeFiniteOr(thr, 1f);

                if (n.V > thr)
                {
                    n.Spiked = true;
                    n.V = -0.65f;
                    n.Rate = Safe01(n.Rate * 0.85f + 0.35f);
                }
                else
                {
                    float vpos = MathF.Max(n.V, 0f);
                    n.Rate = Safe01(n.Rate * 0.92f + (vpos * 0.02f));
                }
            }

            // (B) Hippocampus capture at end of tick
            MaybeCaptureEpisode_NoLock();
        }
    }

    // -----------------------
    // Hippocampus internals
    // -----------------------

    private void MaybeCaptureEpisode_NoLock()
    {
        if (!_hipCfg.CaptureEnabled) return;

        float sal = Safe01(_mods.Salience);
        float alert = Safe01(_mods.Alerting);

        if (sal < _hipCfg.CaptureSalienceThreshold) return;
        if (alert < _hipCfg.CaptureAlertingThreshold) return;

        int k = Math.Clamp(_hipCfg.TopK, 6, 64);

        var top = _nodes
            .OrderByDescending(n => SafeFiniteOr(n.V, 0f))
            .Take(k)
            .ToList();

        if (top.Count < 6) return;

        var nodeIds = top.Select(n => n.Id).ToList();

        var values = new List<float>(top.Count);
        foreach (var n in top)
        {
            float v = SafeFiniteOr(n.V, 0f);
            v = Math.Clamp(v, -1.2f, 1.2f);
            values.Add(v);
        }

        float strength = Safe01(0.55f * sal + 0.45f * alert);

        var ep = new Episode
        {
            Id = _hipNextId++,
            CapturedAtStep = _step,
            Strength = strength,
            NodeIds = nodeIds,
            Values = values,
            ContextMods = _mods,
            ContextThalamus = new ThalamusGatesRequest
            {
                VisionGate = Safe01(_thal.VisionGate),
                AudioGate = Safe01(_thal.AudioGate),
                BodyGate = Safe01(_thal.BodyGate),
                InternalGate = Safe01(_thal.InternalGate)
            }
        };

        _episodes.Add(ep);
        EnforceEpisodeCapacity_NoLock();
    }

    private void DecayEpisodes_NoLock()
    {
        if (_episodes.Count == 0) return;

        float decay = Math.Clamp(SafeFiniteOr(_hipCfg.DecayPerStep, 0.0015f), 0.0001f, 0.05f);
        float min = Math.Clamp(SafeFiniteOr(_hipCfg.MinStrength, 0.06f), 0.001f, 1.0f);

        float mult = 1f - decay;

        for (int i = _episodes.Count - 1; i >= 0; i--)
        {
            var e = _episodes[i];

            if (!float.IsFinite(e.Strength))
            {
                _episodes.RemoveAt(i);
                continue;
            }

            e.Strength *= mult;

            if (!float.IsFinite(e.Strength) || e.Strength < min)
                _episodes.RemoveAt(i);
        }

        EnforceEpisodeCapacity_NoLock();
    }

    private void EnforceEpisodeCapacity_NoLock()
    {
        int cap = Math.Clamp(_hipCfg.MaxEpisodes, 8, 2048);
        if (_episodes.Count <= cap) return;

        _episodes.Sort((a, b) => a.Strength.CompareTo(b.Strength));
        while (_episodes.Count > cap)
            _episodes.RemoveAt(0);
    }

    private void SanitizeEpisodes_NoLock()
    {
        for (int i = _episodes.Count - 1; i >= 0; i--)
        {
            var e = _episodes[i];

            if (!float.IsFinite(e.Strength))
            {
                _episodes.RemoveAt(i);
                continue;
            }

            int n = Math.Min(e.NodeIds.Count, e.Values.Count);
            if (n <= 0)
            {
                _episodes.RemoveAt(i);
                continue;
            }

            // Trim mismatches (defensive)
            if (e.NodeIds.Count != n) e.NodeIds = e.NodeIds.Take(n).ToList();
            if (e.Values.Count != n) e.Values = e.Values.Take(n).ToList();

            for (int j = 0; j < e.Values.Count; j++)
                e.Values[j] = SafeFiniteOr(e.Values[j], 0f);

            e.Strength = Math.Clamp(SafeFiniteOr(e.Strength, 0f), 0f, 1f);
        }
    }

    // -----------------------
    // Thalamus helpers
    // -----------------------

    private float EffectiveGate(int group)
    {
        float baseGate = group switch
        {
            0 => _thal.VisionGate,
            1 => _thal.AudioGate,
            2 => _thal.BodyGate,
            3 => _thal.InternalGate,
            _ => 1f
        };

        float alert = Safe01(_mods.Alerting);
        float stab = Safe01(_mods.Stability);

        float stateFactor = (0.55f + 0.60f * alert) * (0.80f + 0.20f * stab);
        stateFactor = SafeFiniteOr(stateFactor, 1f);

        float goal = Safe01(_mods.Goal);
        float sal = Safe01(_mods.Salience);

        float bias = 1.0f + 0.25f * goal + 0.20f * sal;
        bias = SafeFiniteOr(bias, 1f);

        return Safe01(baseGate * stateFactor * bias);
    }

    private List<NodeState> SelectSourceBand(int group)
    {
        return group switch
        {
            0 => _nodes.Where(n => n.X <= -7).ToList(),
            1 => _nodes.Where(n => n.Z <= -7).ToList(),
            2 => _nodes.Where(n => n.Y <= -4).ToList(),
            3 => new List<NodeState>(),
            _ => _nodes.Where(n => n.X <= -7).ToList()
        };
    }

    private List<NodeState> SelectTargetRegion(int group)
    {
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
    // Archive
    // -----------------------

    public ArchiveDto SaveArchive()
    {
        lock (_gate)
        {
            SanitizeEpisodes_NoLock();

            var payload = new ArchivePayload
            {
                Step = _step,
                Mods = _mods,
                Thal = _thal,
                HipCfg = _hipCfg,
                HipNextId = _hipNextId,
                Episodes = _episodes,
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

            _hipCfg = payload.HipCfg ?? new HippocampusConfigRequest();
            _hipNextId = payload.HipNextId <= 0 ? 1 : payload.HipNextId;

            _episodes.Clear();
            _episodes.AddRange(payload.Episodes ?? new List<Episode>());

            _nodes.Clear();
            _nodes.AddRange(payload.Nodes ?? new List<NodeState>());

            _edges.Clear();
            _edges.AddRange(payload.Edges ?? new List<EdgeState>());

            SanitizeEpisodes_NoLock();
            EnforceEpisodeCapacity_NoLock();
        }
    }

    // -----------------------
    // Numeric safety helpers
    // -----------------------

    private static float Safe01(float v)
    {
        if (!float.IsFinite(v)) return 0f;
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }

    private static float SafeFiniteOr(float v, float fallback)
        => float.IsFinite(v) ? v : fallback;

    private static float StabilizeV(float v)
    {
        if (!float.IsFinite(v)) return 0f;
        // Hard clamp prevents runaway and protects episode capture/serialization.
        if (v > 6f) return 6f;
        if (v < -6f) return -6f;
        return v;
    }

    // -----------------------
    // Payload + internal models
    // -----------------------

    private sealed class ArchivePayload
    {
        public long Step { get; set; }
        public ModulatorsRequest? Mods { get; set; }
        public ThalamusState? Thal { get; set; }

        public HippocampusConfigRequest? HipCfg { get; set; }
        public long HipNextId { get; set; }
        public List<Episode>? Episodes { get; set; }

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

    private sealed class Episode
    {
        public long Id { get; set; }
        public long CapturedAtStep { get; set; }
        public float Strength { get; set; }

        public List<int> NodeIds { get; set; } = new();
        public List<float> Values { get; set; } = new();

        public ModulatorsRequest ContextMods { get; set; } = new();
        public ThalamusGatesRequest ContextThalamus { get; set; } = new();
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
