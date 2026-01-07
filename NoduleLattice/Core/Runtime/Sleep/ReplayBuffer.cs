using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Core.Runtime.Sleep;

/// <summary>
/// Ring buffer of recent node activities (rate only) for offline replay.
/// Deterministic (no random sampling here).
/// </summary>
public sealed class ReplayBuffer
{
    private readonly int _steps;
    private readonly Dictionary<NoduleId, float[]> _rates = new();

    private int _head;

    public ReplayBuffer(int steps)
    {
        _steps = System.Math.Max(8, steps);
    }

    public void Reset(IEnumerable<NoduleId> ids)
    {
        _rates.Clear();
        foreach (var id in ids)
            _rates[id] = new float[_steps];
        _head = 0;
    }

    public void Push(NoduleId id, float rate)
    {
        if (!_rates.TryGetValue(id, out var buf)) return;
        buf[_head] = rate;
    }

    public void AdvanceStep()
    {
        _head++;
        if (_head >= _steps) _head = 0;
    }

    public float GetReplayRate(NoduleId id, int lookback)
    {
        if (!_rates.TryGetValue(id, out var buf)) return 0f;
        lookback = System.Math.Clamp(lookback, 0, _steps - 1);

        int idx = _head - lookback;
        if (idx < 0) idx += _steps;
        return buf[idx];
    }
}