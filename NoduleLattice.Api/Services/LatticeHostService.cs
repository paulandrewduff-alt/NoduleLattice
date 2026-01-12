using System.Text;
using System.Text.Json;
using NoduleLattice.Api.Models;
using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Core.Determinism;
using NoduleLattice.Core.Modulation;
using NoduleLattice.Core.Nodes;
using NoduleLattice.Core.Runtime;
using NoduleLattice.Core.Runtime.Memory;
using NoduleLattice.Core.Synapses;
using NoduleLattice.Core.Time;
using NoduleLattice.Core.Topology;

namespace NoduleLattice.Api.Services;

/// <summary>
/// Canon host facade:
/// - owns a single in-process NoduleLatticeEngine
/// - exposes safe API operations for the Blazor simulator
/// </summary>
public sealed class LatticeHostService
{
    private readonly object _gate = new();
    private readonly Random _rng = new(12345);

    private readonly NoduleLatticeEngine _engine;

    private ModulatorsRequest _mods = new();
    private ThalamusGatesRequest _gates = new();

    private HippocampusConfigRequest _hipCfg = new();

    // Cached regions for stimulus routing (built once after lattice creation)
    private int[] _visionSources = Array.Empty<int>();
    private int[] _visionTargets = Array.Empty<int>();

    private int[] _audioSources = Array.Empty<int>();
    private int[] _audioTargets = Array.Empty<int>();

    private int[] _bodySources = Array.Empty<int>();
    private int[] _bodyTargets = Array.Empty<int>();

    private int[] _coreTargets = Array.Empty<int>();

    public LatticeHostService()
    {
        // --- Build canonical engine instance ---
        var rng = new DeterministicRng(12345);
        var time = new FixedTimebase(deltaTime: 1.0f, initialStepIndex: 0);
        var field = new UniformModulatorField();

        var synCfg = new Synapse2Config();
        var policy = new StructuralPolicy();
        var topology = new BasicTopologyManager(policy, synCfg, rng);

        var membrane = new NoduleLattice.Core.Runtime.MembraneIntegrator();
        var demand = new NoduleLattice.Core.Runtime.DemandEstimator();
        var probe = new UtilityProbe(new UtilityProbeConfig());

        _engine = new NoduleLatticeEngine(
            time: time,
            field: field,
            topology: topology,
            membrane: membrane,
            demand: demand,
            probe: probe,
            structuralPeriodSteps: 128,
            maxDelaySteps: 4,
            sleep: null);

        // Create initial lattice deterministically (mirrors your toy constructor, but using real engine)
        CreateInitialLattice_NoLock(nodeCount: 240, edgeFactor: 4);

        // Defaults
        _gates = new ThalamusGatesRequest
        {
            VisionGate = 1.0f,
            AudioGate = 1.0f,
            BodyGate = 1.0f,
            InternalGate = 0.35f
        };

        _mods = new ModulatorsRequest();
        ApplyModsAndGates_NoLock();

        _hipCfg = new HippocampusConfigRequest();
        ApplyHippocampusConfig_NoLock(_hipCfg);

        CacheRegions_NoLock();
    }

    // -----------------------
    // Snapshot
    // -----------------------

    public LatticeSnapshotDto GetSnapshot()
    {
        lock (_gate)
        {
            var s = _engine.GetSnapshot();
            return new LatticeSnapshotDto
            {
                StepIndex = s.StepIndex,
                Nodes = s.Nodes.Select(n => new NodeSnapDto
                {
                    Id = n.Id,
                    Pos = new Pos3Dto { X = n.Pos.X, Y = n.Pos.Y, Z = n.Pos.Z },
                    V = SafeFiniteOr(n.V, 0f),
                    Rate = SafeFiniteOr(n.Rate, 0f),
                    Spiked = n.Spiked
                }).ToList(),
                Synapses = s.Synapses.Select(e => new EdgeSnapDto
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
        lock (_gate)
        {
            ApplyModsAndGates_NoLock();
            _engine.Step(steps);
        }
    }

    public void Inject(InjectRequest req)
    {
        lock (_gate)
        {
            var id = new NoduleLattice.Abstractions.Nodes.NoduleId(req.NodeId);
            _engine.InjectInput(id, SafeFiniteOr(req.Exc, 0f), SafeFiniteOr(req.Inh, 0f));
        }
    }

    public void SetModulators(ModulatorsRequest req)
    {
        lock (_gate)
        {
            _mods = new ModulatorsRequest
            {
                Reward = Safe01(req.Reward),
                Salience = Safe01(req.Salience),
                Stability = Safe01(req.Stability),
                Alerting = Safe01(req.Alerting),
                Curiosity = Safe01(req.Curiosity),
                Goal = Safe01(req.Goal)
            };

            ApplyModsAndGates_NoLock();
        }
    }

    public void SetThalamusGates(ThalamusGatesRequest req)
    {
        lock (_gate)
        {
            _gates = new ThalamusGatesRequest
            {
                VisionGate = Safe01(req.VisionGate),
                AudioGate = Safe01(req.AudioGate),
                BodyGate = Safe01(req.BodyGate),
                InternalGate = Safe01(req.InternalGate)
            };

            ApplyModsAndGates_NoLock();
        }
    }

    public void SleepReplay(bool run)
    {
        if (!run) return;

        lock (_gate)
        {
            // mimic prior behaviour: sleep shifts stability high, alerting low, and gates mostly closed
            var savedMods = _mods;
            var savedGates = _gates;

            _mods = new ModulatorsRequest
            {
                Reward = savedMods.Reward,
                Salience = savedMods.Salience,
                Stability = MathF.Max(savedMods.Stability, 0.85f),
                Alerting = MathF.Min(savedMods.Alerting, 0.15f),
                Curiosity = savedMods.Curiosity,
                Goal = savedMods.Goal
            };

            _gates = new ThalamusGatesRequest
            {
                VisionGate = 0.05f,
                AudioGate = 0.05f,
                BodyGate = 0.05f,
                InternalGate = MathF.Max(savedGates.InternalGate, 0.55f)
            };

            ApplyModsAndGates_NoLock();

            // Optional hippocampus bursts into core before sleep
            ReplayHippocampus_NoLock(new HippocampusReplayRequest { Count = 6, Gain = 1.0f, StepsPerEpisode = 12 });

            _engine.SleepReplay();

            _mods = savedMods;
            _gates = savedGates;
            ApplyModsAndGates_NoLock();
        }
    }

    // -----------------------
    // Stimulus via gates
    // -----------------------

    public void Stimulus(StimulusRequest req)
    {
        lock (_gate)
        {
            var sources = SelectSourceBand(req.Group);
            var targets = SelectTargetRegion(req.Group);
            if (targets.Length == 0) return;

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
                    int eventCount = sources.Length > 0 ? sources.Length : 12;

                    for (int i = 0; i < eventCount; i++)
                    {
                        if (_rng.NextDouble() >= p) continue;

                        int tgtId = targets[_rng.Next(targets.Length)];

                        float precision = 0.55f + 0.40f * Safe01(_mods.Stability);
                        float noise = ((float)_rng.NextDouble() * 2f - 1f) * (1f - precision) * 0.08f;
                        noise = SafeFiniteOr(noise, 0f);

                        float amp = (baseAmp * gEff) + noise;
                        if (amp >= 0)
                            _engine.InjectInput(new NoduleLattice.Abstractions.Nodes.NoduleId(tgtId), amp, 0f);
                        else
                            _engine.InjectInput(new NoduleLattice.Abstractions.Nodes.NoduleId(tgtId), 0f, -amp);
                    }
                }

                ApplyModsAndGates_NoLock();
                _engine.Step(1);
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
                TopK = Math.Clamp(req.TopK, 6, 128),
                MaxEpisodes = Math.Clamp(req.MaxEpisodes, 8, 4096),
                DecayPerStep = Math.Clamp(SafeFiniteOr(req.DecayPerStep, 0.0015f), 0.0001f, 0.05f),
                MinStrength = Math.Clamp(SafeFiniteOr(req.MinStrength, 0.06f), 0.001f, 1.0f)
            };

            ApplyHippocampusConfig_NoLock(_hipCfg);
        }
    }

    public HippocampusEpisodeListDto GetHippocampusEpisodes()
    {
        lock (_gate)
        {
            var list = _engine.GetHippocampusEpisodes();

            return new HippocampusEpisodeListDto
            {
                CurrentStep = list.CurrentStep,
                Episodes = list.Episodes
                    .OrderByDescending(e => e.Strength)
                    .Select(e => new HippocampusEpisodeDto
                    {
                        Id = e.Id,
                        CapturedAtStep = e.CapturedAtStep,
                        Strength = SafeFiniteOr(e.Strength, 0f),

                        NodeIds = e.NodeIds.ToArray(),
                        Values = e.Values.Select(v => SafeFiniteOr(v, 0f)).ToArray(),

                        ContextMods = new ModulatorsRequest
                        {
                            Reward = e.ContextMods.Reward,
                            Salience = e.ContextMods.Salience,
                            Stability = e.ContextMods.Stability,
                            Alerting = e.ContextMods.Alerting,
                            Curiosity = e.ContextMods.Curiosity,
                            Goal = e.ContextMods.Goal
                        },

                        ContextThalamus = new ThalamusGatesRequest
                        {
                            VisionGate = e.ContextGates.Vision,
                            AudioGate = e.ContextGates.Audio,
                            BodyGate = e.ContextGates.Body,
                            InternalGate = e.ContextGates.Internal
                        }
                    })
                    .ToList()
            };
        }
    }

    public void ClearHippocampus()
    {
        lock (_gate)
        {
            _engine.ClearHippocampus();
        }
    }

    public void ReplayHippocampus(HippocampusReplayRequest req)
    {
        lock (_gate)
        {
            ReplayHippocampus_NoLock(req);
        }
    }

    public void ReplayHippocampusEpisode(long episodeId, HippocampusReplayOneRequest req)
    {
        lock (_gate)
        {
            float gain = Math.Clamp(SafeFiniteOr(req.Gain, 1f), 0.1f, 5.0f);
            int steps = Math.Clamp(req.Steps, 1, 64);

            if (!_engine.ReplayHippocampusEpisode(episodeId, gain))
                return;

            ApplyModsAndGates_NoLock();
            _engine.Step(steps);
        }
    }

    private void ReplayHippocampus_NoLock(HippocampusReplayRequest req)
    {
        var list = _engine.GetHippocampusEpisodes();
        if (list.Episodes.Count == 0) return;

        int count = Math.Clamp(req.Count, 1, 128);
        float gain = Math.Clamp(SafeFiniteOr(req.Gain, 1f), 0.1f, 5.0f);
        int stepsPer = Math.Clamp(req.StepsPerEpisode, 1, 64);

        var chosen = list.Episodes
            .OrderByDescending(e =>
            {
                float age = (float)(list.CurrentStep - e.CapturedAtStep);
                if (age < 0) age = 0;
                float recency = 1.0f / (1.0f + age);
                return (e.Strength * 0.70f) + (recency * 0.30f);
            })
            .Take(count)
            .ToList();

        foreach (var ep in chosen)
        {
            if (ep.Strength <= 0.0001f) continue;

            _engine.ReplayHippocampusEpisode(ep.Id, gain);
            ApplyModsAndGates_NoLock();
            _engine.Step(stepsPer);
        }
    }

    // -----------------------
    // Archive
    // -----------------------

    public ArchiveDto SaveArchive()
    {
        lock (_gate)
        {
            var engineBytes = _engine.Save();
            var engineB64 = Convert.ToBase64String(engineBytes);

            var hipState = _engine.ExportHippocampusState();

            var payload = new ArchivePayload
            {
                EngineBase64 = engineB64,
                Mods = _mods,
                Gates = _gates,
                HipCfg = _hipCfg,
                HipState = ToDtoHipState(hipState)
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

            if (!string.IsNullOrWhiteSpace(payload.EngineBase64))
            {
                var bytes = Convert.FromBase64String(payload.EngineBase64);
                _engine.Load(bytes);
            }

            _mods = payload.Mods ?? new ModulatorsRequest();
            _gates = payload.Gates ?? new ThalamusGatesRequest();
            _hipCfg = payload.HipCfg ?? new HippocampusConfigRequest();

            ApplyModsAndGates_NoLock();
            ApplyHippocampusConfig_NoLock(_hipCfg);

            if (payload.HipState is not null)
            {
                _engine.ImportHippocampusState(FromDtoHipState(payload.HipState));
            }

            CacheRegions_NoLock();
        }
    }

    // -----------------------
    // Internal helpers
    // -----------------------

    private void ApplyModsAndGates_NoLock()
    {
        _engine.SetModulators(new ModulatorVector
        {
            Reward = Safe01(_mods.Reward),
            Salience = Safe01(_mods.Salience),
            Stability = Safe01(_mods.Stability),
            Alerting = Safe01(_mods.Alerting),
            Curiosity = Safe01(_mods.Curiosity),
            Goal = Safe01(_mods.Goal)
        });

        _engine.SetInputGates(new InputGates(
            Vision: Safe01(_gates.VisionGate),
            Audio: Safe01(_gates.AudioGate),
            Body: Safe01(_gates.BodyGate),
            Internal: Safe01(_gates.InternalGate)
        ));
    }

    private void ApplyHippocampusConfig_NoLock(HippocampusConfigRequest cfg)
    {
        _engine.SetHippocampusConfig(new HippocampusConfig
        {
            CaptureEnabled = cfg.CaptureEnabled,
            CaptureSalienceThreshold = Safe01(cfg.CaptureSalienceThreshold),
            CaptureAlertingThreshold = Safe01(cfg.CaptureAlertingThreshold),
            TopK = Math.Clamp(cfg.TopK, 6, 128),
            MaxEpisodes = Math.Clamp(cfg.MaxEpisodes, 8, 4096),
            DecayPerStep = Math.Clamp(SafeFiniteOr(cfg.DecayPerStep, 0.0015f), 0.0001f, 0.05f),
            MinStrength = Math.Clamp(SafeFiniteOr(cfg.MinStrength, 0.06f), 0.001f, 1.0f)
        });
    }

    private float EffectiveGate(int group)
    {
        float baseGate = group switch
        {
            0 => _gates.VisionGate,
            1 => _gates.AudioGate,
            2 => _gates.BodyGate,
            3 => _gates.InternalGate,
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

    private int[] SelectSourceBand(int group)
        => group switch
        {
            0 => _visionSources,
            1 => _audioSources,
            2 => _bodySources,
            3 => Array.Empty<int>(),
            _ => _visionSources
        };

    private int[] SelectTargetRegion(int group)
        => group switch
        {
            0 => _visionTargets,
            1 => _audioTargets,
            2 => _bodyTargets,
            3 => _coreTargets,
            _ => _coreTargets
        };

    private void CacheRegions_NoLock()
    {
        var snap = _engine.GetSnapshot();

        _visionSources = snap.Nodes.Where(n => n.Pos.X <= -7).Select(n => n.Id).ToArray();
        _visionTargets = snap.Nodes.Where(n => n.Pos.X >= +6).Select(n => n.Id).ToArray();

        _audioSources = snap.Nodes.Where(n => n.Pos.Z <= -7).Select(n => n.Id).ToArray();
        _audioTargets = snap.Nodes.Where(n => n.Pos.Z >= +6).Select(n => n.Id).ToArray();

        _bodySources = snap.Nodes.Where(n => n.Pos.Y <= -4).Select(n => n.Id).ToArray();
        _bodyTargets = snap.Nodes.Where(n => n.Pos.Y >= +3).Select(n => n.Id).ToArray();

        _coreTargets = snap.Nodes.Where(n => Math.Abs(n.Pos.X) <= 2 && Math.Abs(n.Pos.Y) <= 2 && Math.Abs(n.Pos.Z) <= 2).Select(n => n.Id).ToArray();
    }

    private void CreateInitialLattice_NoLock(int nodeCount, int edgeFactor)
    {
        for (int i = 0; i < nodeCount; i++)
        {
            var id = new NoduleLattice.Abstractions.Nodes.NoduleId(i + 1);
            var pos = new Int3(
                _rng.Next(-10, 11),
                _rng.Next(-6, 7),
                _rng.Next(-10, 11));

            var node = new Nodule(id, pos);

            var m = node.Membrane;
            m.Potential = (float)(_rng.NextDouble() * 2.0 - 1.0);
            node.Membrane = m;

            _engine.AddNode(node);
        }

        long eid = 1;
        var synCfg = new Synapse2Config();

        for (int i = 0; i < nodeCount * edgeFactor; i++)
        {
            int pre = _rng.Next(1, nodeCount + 1);
            int post = _rng.Next(1, nodeCount + 1);
            if (pre == post) continue;

            var sid = new NoduleLattice.Abstractions.Synapses.SynapseId(eid++);
            var syn = new Synapse2(
                id: sid,
                pre: new NoduleLattice.Abstractions.Nodes.NoduleId(pre),
                post: new NoduleLattice.Abstractions.Nodes.NoduleId(post),
                kind: _rng.NextDouble() < 0.75 ? NoduleLattice.Abstractions.Synapses.SynapseKind.Excitatory : NoduleLattice.Abstractions.Synapses.SynapseKind.Inhibitory,
                cfg: synCfg,
                initialWeight: (float)(_rng.NextDouble() * 1.2),
                initialGain: 1.0f,
                delaySteps: 0);

            _engine.AddSynapse(syn);
        }
    }

    // -----------------------
    // Archive payload model
    // -----------------------

    private sealed class ArchivePayload
    {
        public string EngineBase64 { get; set; } = string.Empty;
        public ModulatorsRequest? Mods { get; set; }
        public ThalamusGatesRequest? Gates { get; set; }

        public HippocampusConfigRequest? HipCfg { get; set; }
        public HipStateDto? HipState { get; set; }
    }

    private sealed class HipStateDto
    {
        public HippocampusConfigRequest? Config { get; set; }
        public long NextId { get; set; }
        public List<HipEpisodeDto> Episodes { get; set; } = new();
    }

    private sealed class HipEpisodeDto
    {
        public long Id { get; set; }
        public long CapturedAtStep { get; set; }
        public float Strength { get; set; }

        public int[] NodeIds { get; set; } = Array.Empty<int>();
        public float[] Values { get; set; } = Array.Empty<float>();

        public ModulatorsRequest ContextMods { get; set; } = new();
        public ThalamusGatesRequest ContextThalamus { get; set; } = new();
    }

    private static HipStateDto ToDtoHipState(HippocampusState s)
        => new()
        {
            Config = new HippocampusConfigRequest
            {
                CaptureEnabled = s.Config.CaptureEnabled,
                CaptureSalienceThreshold = s.Config.CaptureSalienceThreshold,
                CaptureAlertingThreshold = s.Config.CaptureAlertingThreshold,
                TopK = s.Config.TopK,
                MaxEpisodes = s.Config.MaxEpisodes,
                DecayPerStep = s.Config.DecayPerStep,
                MinStrength = s.Config.MinStrength
            },
            NextId = s.NextId,
            Episodes = s.Episodes.Select(e => new HipEpisodeDto
            {
                Id = e.Id,
                CapturedAtStep = e.CapturedAtStep,
                Strength = e.Strength,
                NodeIds = e.NodeIds.ToArray(),
                Values = e.Values.ToArray(),
                ContextMods = new ModulatorsRequest
                {
                    Reward = e.ContextMods.Reward,
                    Salience = e.ContextMods.Salience,
                    Stability = e.ContextMods.Stability,
                    Alerting = e.ContextMods.Alerting,
                    Curiosity = e.ContextMods.Curiosity,
                    Goal = e.ContextMods.Goal
                },
                ContextThalamus = new ThalamusGatesRequest
                {
                    VisionGate = e.ContextGates.Vision,
                    AudioGate = e.ContextGates.Audio,
                    BodyGate = e.ContextGates.Body,
                    InternalGate = e.ContextGates.Internal
                }
            }).ToList()
        };

    private static HippocampusState FromDtoHipState(HipStateDto d)
    {
        var cfg = d.Config ?? new HippocampusConfigRequest();

        return new HippocampusState
        {
            Config = new HippocampusConfig
            {
                CaptureEnabled = cfg.CaptureEnabled,
                CaptureSalienceThreshold = Math.Clamp(cfg.CaptureSalienceThreshold, 0f, 1f),
                CaptureAlertingThreshold = Math.Clamp(cfg.CaptureAlertingThreshold, 0f, 1f),
                TopK = Math.Clamp(cfg.TopK, 6, 128),
                MaxEpisodes = Math.Clamp(cfg.MaxEpisodes, 8, 4096),
                DecayPerStep = Math.Clamp(cfg.DecayPerStep, 0.0001f, 0.05f),
                MinStrength = Math.Clamp(cfg.MinStrength, 0.001f, 1.0f)
            },
            NextId = d.NextId <= 0 ? 1 : d.NextId,
            Episodes = d.Episodes.Select(e => new HippocampusEpisode
            {
                Id = e.Id,
                CapturedAtStep = e.CapturedAtStep,
                Strength = Math.Clamp(float.IsFinite(e.Strength) ? e.Strength : 0f, 0f, 1f),
                NodeIds = e.NodeIds ?? Array.Empty<int>(),
                Values = (e.Values ?? Array.Empty<float>()).Select(v => float.IsFinite(v) ? v : 0f).ToArray(),
                ContextMods = new ModulatorVector
                {
                    Reward = e.ContextMods.Reward,
                    Salience = e.ContextMods.Salience,
                    Stability = e.ContextMods.Stability,
                    Alerting = e.ContextMods.Alerting,
                    Curiosity = e.ContextMods.Curiosity,
                    Goal = e.ContextMods.Goal
                },
                ContextGates = new InputGates(
                    Vision: e.ContextThalamus.VisionGate,
                    Audio: e.ContextThalamus.AudioGate,
                    Body: e.ContextThalamus.BodyGate,
                    Internal: e.ContextThalamus.InternalGate)
            }).ToList()
        };
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
}
