using System;
using System.Collections.Generic;

namespace NoduleLattice.Core.Cortex;

/// <summary>
/// A minimal callosal bridge that:
/// - takes projections from each side
/// - applies lossy/bandwidth-limited transmission
/// - delays delivery by LatencySteps
/// - delivers to the opposite side
/// </summary>
public sealed class CallosalBridge
{
    private readonly CallosalConfig _cfg;
    private readonly Random _rng;

    private readonly Queue<Projection>[] _leftToRight;
    private readonly Queue<Projection>[] _rightToLeft;

    public CallosalBridge(CallosalConfig cfg, int seed = 12345)
    {
        cfg.Validate();
        _cfg = cfg;
        _rng = new Random(seed);

        int lanes = _cfg.LatencySteps + 1;
        _leftToRight = new Queue<Projection>[lanes];
        _rightToLeft = new Queue<Projection>[lanes];

        for (int i = 0; i < lanes; i++)
        {
            _leftToRight[i] = new Queue<Projection>(_cfg.BandwidthPerStep * 2);
            _rightToLeft[i] = new Queue<Projection>(_cfg.BandwidthPerStep * 2);
        }
    }

    public void EnqueueLeftToRight(IReadOnlyList<Projection> outgoing)
        => Enqueue(_leftToRight, outgoing);

    public void EnqueueRightToLeft(IReadOnlyList<Projection> outgoing)
        => Enqueue(_rightToLeft, outgoing);

    public IReadOnlyList<Projection> DequeueForRight()
        => Dequeue(_leftToRight);

    public IReadOnlyList<Projection> DequeueForLeft()
        => Dequeue(_rightToLeft);

    public void AdvanceStep()
    {
        Rotate(_leftToRight);
        Rotate(_rightToLeft);
    }

    private void Enqueue(Queue<Projection>[] lanes, IReadOnlyList<Projection> outgoing)
    {
        // Bandwidth per step is enforced at enqueue time.
        int take = Math.Min(_cfg.BandwidthPerStep, outgoing.Count);

        var q = lanes[_cfg.LatencySteps];

        for (int i = 0; i < take; i++)
        {
            // Dropout
            if (_rng.NextDouble() < _cfg.DropoutProbability) continue;

            var p = outgoing[i];
            float v = p.Value;

            // Add noise
            if (_cfg.GaussianNoiseStd > 0f)
                v += NextGaussian() * _cfg.GaussianNoiseStd;

            // Clamp
            if (v > _cfg.ValueClampAbs) v = _cfg.ValueClampAbs;
            else if (v < -_cfg.ValueClampAbs) v = -_cfg.ValueClampAbs;

            q.Enqueue(new Projection(p.Key, v));
        }
    }

    private static void Rotate(Queue<Projection>[] lanes)
    {
        // lanes[0] is the delivery lane. We rotate everything toward 0.
        // Example LatencySteps=2: lanes[2] enqueue → step → lanes[1] → step → lanes[0] deliver.
        var deliver = lanes[0];
        deliver.Clear();

        for (int i = 0; i < lanes.Length - 1; i++)
        {
            var src = lanes[i + 1];
            var dst = lanes[i];

            while (src.Count > 0)
                dst.Enqueue(src.Dequeue());
        }
        // last lane is now empty (drained by loop)
    }

    private IReadOnlyList<Projection> Dequeue(Queue<Projection>[] lanes)
    {
        var q = lanes[0];
        if (q.Count == 0) return Array.Empty<Projection>();

        var arr = new Projection[q.Count];
        int i = 0;
        while (q.Count > 0) arr[i++] = q.Dequeue();
        return arr;
    }

    private float NextGaussian()
    {
        // Box-Muller
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = 1.0 - _rng.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return (float)randStdNormal;
    }
}
