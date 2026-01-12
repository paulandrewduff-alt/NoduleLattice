using System;

namespace NoduleLattice.Core.Cortex;

/// <summary>
/// Entry 017 — Artificial Corpus Callosum
/// Bandwidth-limited, lossy, delayed projections between two cortical sides.
/// </summary>
public sealed class CallosalConfig
{
    /// <summary>How many scalar signals can cross per step (per direction).</summary>
    public int BandwidthPerStep { get; init; } = 64;

    /// <summary>Probability [0..1] that a packet is dropped.</summary>
    public float DropoutProbability { get; init; } = 0.18f;

    /// <summary>Noise standard deviation applied to transmitted value.</summary>
    public float GaussianNoiseStd { get; init; } = 0.015f;

    /// <summary>Fixed integer step latency for packets.</summary>
    public int LatencySteps { get; init; } = 2;

    /// <summary>Clamp absolute transmitted value after noise.</summary>
    public float ValueClampAbs { get; init; } = 2.0f;

    public void Validate()
    {
        if (BandwidthPerStep < 1) throw new ArgumentOutOfRangeException(nameof(BandwidthPerStep));
        if (DropoutProbability < 0f || DropoutProbability > 1f) throw new ArgumentOutOfRangeException(nameof(DropoutProbability));
        if (GaussianNoiseStd < 0f || GaussianNoiseStd > 1f) throw new ArgumentOutOfRangeException(nameof(GaussianNoiseStd));
        if (LatencySteps < 0 || LatencySteps > 128) throw new ArgumentOutOfRangeException(nameof(LatencySteps));
        if (ValueClampAbs <= 0f) throw new ArgumentOutOfRangeException(nameof(ValueClampAbs));
    }
}
