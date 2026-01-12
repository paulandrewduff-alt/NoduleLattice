using System;
using System.Collections.Generic;
using System.Linq;
using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Core.Runtime.Memory;

/// <summary>
/// Episodic hippocampus (Folded Archive Entry 011+).
/// - capture is gated by modulators (salience/alerting)
/// - store Top-K |rate| nodes
/// - decay/prune continuously
/// - supports exporting/importing state for API archive
/// </summary>
public sealed class HippocampusMemory
{
    private HippocampusConfig _cfg = new();
    private InputGates _gates = InputGates.Default;

    private long _nextId = 1;
    private readonly List<HippocampusEpisode> _episodes = new();

    public HippocampusConfig Config => _cfg;

    public void SetConfig(HippocampusConfig cfg)
    {
        cfg.Validate();
        _cfg = cfg;
        EnforceCapacity();
    }

    public void SetInputGates(InputGates gates) => _gates = gates.Clamp01();

    public void Clear()
    {
        _episodes.Clear();
        _nextId = 1;
    }

    public HippocampusEpisodeList Snapshot(long currentStep)
        => new()
        {
            CurrentStep = currentStep,
            Episodes = _episodes
                .OrderByDescending(e => e.Strength)
                .Select(CloneEpisode)
                .ToList()
        };

    public HippocampusState ExportState()
        => new()
        {
            Config = _cfg,
            NextId = _nextId,
            Episodes = _episodes.Select(CloneEpisode).ToList()
        };

    public void ImportState(HippocampusState state)
    {
        state.Config.Validate();
        _cfg = state.Config;
        _nextId = state.NextId <= 0 ? 1 : state.NextId;

        _episodes.Clear();
        if (state.Episodes is not null)
        {
            foreach (var ep in state.Episodes)
            {
                if (ep.NodeIds.Length == 0 || ep.Values.Length == 0) continue;
                var n = Math.Min(ep.NodeIds.Length, ep.Values.Length);

                var ids = ep.NodeIds.Take(n).ToArray();
                var vals = ep.Values.Take(n).Select(v => float.IsFinite(v) ? v : 0f).ToArray();

                var strength = float.IsFinite(ep.Strength) ? ep.Strength : 0f;
                strength = Math.Clamp(strength, 0f, 1f);

                _episodes.Add(new HippocampusEpisode
                {
                    Id = ep.Id,
                    CapturedAtStep = ep.CapturedAtStep,
                    Strength = strength,
                    NodeIds = ids,
                    Values = vals,
                    ContextMods = ep.ContextMods,
                    ContextGates = ep.ContextGates.Clamp01()
                });
            }
        }

        EnforceCapacity();
    }

    public void TickAndMaybeCapture(long stepIndex, ModulatorVector mods, IEnumerable<INodule> nodes)
    {
        // (A) decay/prune
        if (_episodes.Count > 0)
        {
            float mul = 1f - Math.Clamp(_cfg.DecayPerStep, 0f, 0.25f);

            for (int i = _episodes.Count - 1; i >= 0; i--)
            {
                var ep = _episodes[i];
                ep.Strength = float.IsFinite(ep.Strength) ? ep.Strength * mul : 0f;
                if (ep.Strength < _cfg.MinStrength)
                    _episodes.RemoveAt(i);
            }
        }

        if (!_cfg.CaptureEnabled) return;

        // Keep the same semantics as the API prototype: both gates must pass.
        if (mods.Salience < _cfg.CaptureSalienceThreshold) return;
        if (mods.Alerting < _cfg.CaptureAlertingThreshold) return;

        int k = Math.Clamp(_cfg.TopK, 6, 128);

        // Top-K selection by |rate|
        var ids = new int[k];
        var vals = new float[k];
        int filled = 0;

        foreach (var n in nodes)
        {
            float score = Math.Abs(n.Activity.Rate);
            if (score <= 0.00001f) continue;

            int nid = n.Id.Value;

            if (filled < k)
            {
                ids[filled] = nid;
                vals[filled] = score;
                filled++;
                continue;
            }

            int minIdx = 0;
            float minVal = vals[0];
            for (int i = 1; i < k; i++)
            {
                if (vals[i] < minVal)
                {
                    minVal = vals[i];
                    minIdx = i;
                }
            }

            if (score > minVal)
            {
                ids[minIdx] = nid;
                vals[minIdx] = score;
            }
        }

        if (filled < 6) return;

        if (filled < k)
        {
            Array.Resize(ref ids, filled);
            Array.Resize(ref vals, filled);
        }

        float strength = Math.Clamp((mods.Salience * 0.55f) + (mods.Alerting * 0.45f), 0f, 1f);

        _episodes.Add(new HippocampusEpisode
        {
            Id = _nextId++,
            CapturedAtStep = stepIndex,
            Strength = strength,
            NodeIds = ids,
            Values = vals,
            ContextMods = mods,
            ContextGates = _gates
        });

        EnforceCapacity();
    }

    public HippocampusEpisode? GetEpisode(long id)
    {
        for (int i = 0; i < _episodes.Count; i++)
            if (_episodes[i].Id == id) return _episodes[i];
        return null;
    }

    private void EnforceCapacity()
    {
        int cap = Math.Clamp(_cfg.MaxEpisodes, 8, 4096);
        if (_episodes.Count <= cap) return;

        _episodes.Sort((a, b) => a.Strength.CompareTo(b.Strength));
        while (_episodes.Count > cap)
            _episodes.RemoveAt(0);
    }

    private static HippocampusEpisode CloneEpisode(HippocampusEpisode e)
        => new()
        {
            Id = e.Id,
            CapturedAtStep = e.CapturedAtStep,
            Strength = e.Strength,
            NodeIds = (int[])e.NodeIds.Clone(),
            Values = (float[])e.Values.Clone(),
            ContextMods = e.ContextMods,
            ContextGates = e.ContextGates
        };
}
