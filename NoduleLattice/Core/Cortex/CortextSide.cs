using System;
using System.Collections.Generic;

namespace NoduleLattice.Core.Cortex;

/// <summary>
/// A tiny reference implementation of a cortical side.
/// This sketch intentionally avoids touching your lattice types.
/// It maintains a "belief" vector keyed by int.
/// </summary>
public sealed class CortexSide : ICortexSide
{
    private readonly CortexSideId _side;

    // "Belief" state keyed by feature id. In the real integration this is where
    // you map to a subset of lattice nodes / region aggregates.
    private readonly Dictionary<int, float> _belief = new();

    // Bias knobs for Entry 017 semantics.
    // Left: sequential/stable => lower learning rate, higher smoothing.
    // Right: associative/novel => higher learning rate, lower smoothing.
    public float AssimilationRate { get; set; }
    public float Smoothing { get; set; }

    private readonly List<Projection> _out = new(256);

    public CortexSideId Side => _side;

    public CortexSide(CortexSideId side)
    {
        _side = side;

        if (side == CortexSideId.Left)
        {
            AssimilationRate = 0.08f;
            Smoothing = 0.85f;
        }
        else
        {
            AssimilationRate = 0.18f;
            Smoothing = 0.65f;
        }
    }

    public IReadOnlyList<Projection> EmitProjections(long stepIndex)
    {
        // Minimal: emit top-N magnitude beliefs.
        // In real integration: emit region summaries, goal vectors, or prediction errors.
        _out.Clear();

        int count = 0;
        foreach (var kvp in _belief)
        {
            float v = kvp.Value;
            if (MathF.Abs(v) < 0.001f) continue;

            _out.Add(new Projection(kvp.Key, v));
            count++;
            if (count >= 256) break; // hard ceiling in the sketch
        }

        return _out;
    }

    public void ApplyProjections(long stepIndex, IReadOnlyList<Projection> inbound)
    {
        // Minimal: blend inbound into belief state.
        // Left side will smooth more; right side will assimilate more quickly.
        foreach (var p in inbound)
        {
            if (!_belief.TryGetValue(p.Key, out var cur)) cur = 0f;

            float blended = (cur * Smoothing) + (p.Value * (1f - Smoothing));
            float next = cur + (blended - cur) * AssimilationRate;

            if (float.IsFinite(next))
                _belief[p.Key] = next;
        }

        // Slow decay of beliefs so callosal traffic doesn't accumulate forever.
        // Left decays slower (more stability), right decays faster.
        float decay = _side == CortexSideId.Left ? 0.9992f : 0.9985f;

        if (_belief.Count > 0)
        {
            var keys = new int[_belief.Count];
            int i = 0;
            foreach (var k in _belief.Keys) keys[i++] = k;

            foreach (var k in keys)
            {
                float v = _belief[k] * decay;
                if (MathF.Abs(v) < 0.0005f) _belief.Remove(k);
                else _belief[k] = v;
            }
        }
    }

    // Convenience for discussion/testing
    public void SetBelief(int key, float value) => _belief[key] = value;
    public bool TryGetBelief(int key, out float value) => _belief.TryGetValue(key, out value);
}
