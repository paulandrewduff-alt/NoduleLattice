// ============================================================================
// FILE: NoduleLattice/Core/Runtime/Hippocampus/HippocampusMemory.cs
// PURPOSE:
//   Episodic memory capture + recall injection, aligned to canon types.
// FIX:
//   - No public API leaks internal HippocampusEpisode.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Core.Runtime.Hippocampus;

public sealed class HippocampusMemory
{
    private HippocampusConfig _cfg;

    private readonly List<HippocampusEpisode> _episodes = new();

    private long _lastStatStep;
    private int _capturedSinceStat;

    private long _lastStoredStep;
    private float _lastStoredScore;

    public HippocampusMemory(HippocampusConfig cfg)
    {
        _cfg = cfg ?? new HippocampusConfig();
        SetConfig(_cfg);
    }

    public void SetConfig(HippocampusConfig cfg)
    {
        _cfg = cfg ?? new HippocampusConfig();

        _cfg.CapacityEpisodes = Math.Clamp(_cfg.CapacityEpisodes, 8, 200_000);
        _cfg.StoreTopKNodes = Math.Clamp(_cfg.StoreTopKNodes, 8, 200_000);

        _cfg.MinRateToStore = Math.Max(0f, _cfg.MinRateToStore);
        _cfg.MinSalienceToStore = Math.Max(0f, _cfg.MinSalienceToStore);
        _cfg.MinSpikeFractionToStore = Math.Max(0f, _cfg.MinSpikeFractionToStore);
        _cfg.MinScoreToStore = Math.Max(0f, _cfg.MinScoreToStore);

        _cfg.DefaultRecallGain = Math.Max(0f, _cfg.DefaultRecallGain);
        _cfg.DefaultRecallEpisodes = Math.Clamp(_cfg.DefaultRecallEpisodes, 1, 64);

        _cfg.RecencyHalfLifeSteps = Math.Max(1, _cfg.RecencyHalfLifeSteps);

        _cfg.CortexRoleBoost = Math.Max(0f, _cfg.CortexRoleBoost);
        _cfg.RelayRoleBoost = Math.Max(0f, _cfg.RelayRoleBoost);
        _cfg.ModSourceRoleBoost = Math.Max(0f, _cfg.ModSourceRoleBoost);
    }

    public void ResetStats()
    {
        _lastStatStep = 0;
        _capturedSinceStat = 0;
        _lastStoredStep = 0;
        _lastStoredScore = 0f;
    }

    public void ClearAll()
    {
        _episodes.Clear();
        ResetStats();
    }

    public HippocampusStats GetStats(long nowStep)
    {
        long oldest = _episodes.Count == 0 ? 0 : _episodes[0].StepIndex;
        long newest = _episodes.Count == 0 ? 0 : _episodes[^1].StepIndex;

        float rate = 0f;
        if (_lastStatStep > 0)
        {
            var dt = Math.Max(1, nowStep - _lastStatStep);
            rate = (_capturedSinceStat / (dt / 1000f));
        }

        return new HippocampusStats(
            Episodes: _episodes.Count,
            OldestStep: oldest,
            NewestStep: newest,
            LastStoredStep: _lastStoredStep,
            LastStoredScore: _lastStoredScore,
            RecentCaptureRatePerKSteps: rate);
    }

    public void MaybeStoreEpisode(long stepIndex, IReadOnlyCollection<INodule> nodes, ModulatorVector globalMods)
    {
        if (!_cfg.Enabled) return;
        if (nodes.Count == 0) return;

        int spiked = 0;
        float sumRate = 0f;

        foreach (var n in nodes)
        {
            var a = n.Activity;
            sumRate += a.Rate;
            if (a.Spiked) spiked++;
        }

        float meanRate = sumRate / nodes.Count;
        float spikeFrac = spiked / (float)nodes.Count;

        float score =
            (globalMods.Salience * 0.55f) +
            (globalMods.Curiosity * 0.15f) +
            (globalMods.Reward * 0.15f) +
            (globalMods.Alerting * 0.10f) +
            (spikeFrac * 0.60f) +
            (Clamp01(meanRate) * 0.20f);

        bool trigger =
            (globalMods.Salience >= _cfg.MinSalienceToStore) ||
            (spikeFrac >= _cfg.MinSpikeFractionToStore) ||
            (score >= _cfg.MinScoreToStore);

        if (!trigger) return;

        var top = nodes
            .Select(n => (n.Id, n.Role, Rate: n.Activity.Rate))
            .Where(t => t.Rate >= _cfg.MinRateToStore)
            .OrderByDescending(t => t.Rate * RoleBoost(t.Role))
            .ThenByDescending(t => t.Rate)
            .ThenBy(t => t.Id.Value)
            .Take(_cfg.StoreTopKNodes)
            .ToArray();

        if (top.Length == 0) return;

        var ep = new HippocampusEpisode
        {
            StepIndex = stepIndex,
            Score = score,
            Modulators = globalMods,
            NodeIds = top.Select(t => t.Id).ToArray(),
            Rates = top.Select(t => t.Rate).ToArray()
        };

        _episodes.Add(ep);

        int cap = _cfg.CapacityEpisodes;
        if (_episodes.Count > cap)
        {
            int remove = _episodes.Count - cap;
            _episodes.RemoveRange(0, remove);
        }

        _lastStoredStep = stepIndex;
        _lastStoredScore = score;

        if (_lastStatStep == 0) _lastStatStep = stepIndex;
        _capturedSinceStat++;

        if (stepIndex - _lastStatStep >= 1000)
        {
            _lastStatStep = stepIndex;
            _capturedSinceStat = 0;
        }
    }

    public void RecallInject(NoduleLatticeEngine engine, long nowStep, int episodes, float gain)
    {
        if (!_cfg.Enabled) return;
        if (_episodes.Count == 0) return;

        episodes = Math.Clamp(episodes, 1, 64);
        gain = Math.Max(0f, gain);

        var selected = SelectTopEpisodes(nowStep, episodes);
        foreach (var ep in selected)
        {
            float g = gain * (0.5f + (ep.Score * 0.5f));

            int len = Math.Min(ep.NodeIds.Length, ep.Rates.Length);
            for (int i = 0; i < len; i++)
                engine.InjectInput(ep.NodeIds[i], ep.Rates[i] * g);
        }
    }

    private IReadOnlyList<HippocampusEpisode> SelectTopEpisodes(long nowStep, int k)
    {
        if (_episodes.Count == 0) return Array.Empty<HippocampusEpisode>();

        double halfLife = Math.Max(1, _cfg.RecencyHalfLifeSteps);

        return _episodes
            .Select(ep => (ep, w: Weight(ep, nowStep, halfLife)))
            .OrderByDescending(t => t.w)
            .ThenByDescending(t => t.ep.StepIndex)
            .Take(k)
            .Select(t => t.ep)
            .ToArray();
    }

    private float RoleBoost(NoduleRole role)
    {
        return role switch
        {
            NoduleRole.Relay => _cfg.RelayRoleBoost,
            NoduleRole.ModulatorySource => _cfg.ModSourceRoleBoost,
            _ => _cfg.CortexRoleBoost
        };
    }

    private static double Weight(HippocampusEpisode ep, long nowStep, double halfLife)
    {
        double dt = Math.Max(0, nowStep - ep.StepIndex);
        double decay = Math.Exp(-dt / halfLife);
        return ep.Score * decay;
    }

    private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
}
