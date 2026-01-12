using System;

namespace NoduleLattice.Core.Runtime.Memory;

/// <summary>
/// Hippocampus configuration (explicit).
/// </summary>
public sealed class HippocampusConfig
{
    public bool CaptureEnabled { get; init; } = true;

    public float CaptureSalienceThreshold { get; init; } = 0.60f;
    public float CaptureAlertingThreshold { get; init; } = 0.40f;

    public int TopK { get; init; } = 18;

    public int MaxEpisodes { get; init; } = 256;

    /// <summary>Multiplicative decay: strength *= (1 - DecayPerStep) each step.</summary>
    public float DecayPerStep { get; init; } = 0.0015f;

    public float MinStrength { get; init; } = 0.06f;

    public void Validate()
    {
        if (TopK < 1) throw new ArgumentOutOfRangeException(nameof(TopK));
        if (MaxEpisodes < 1) throw new ArgumentOutOfRangeException(nameof(MaxEpisodes));

        if (CaptureSalienceThreshold < 0f || CaptureSalienceThreshold > 1f)
            throw new ArgumentOutOfRangeException(nameof(CaptureSalienceThreshold));

        if (CaptureAlertingThreshold < 0f || CaptureAlertingThreshold > 1f)
            throw new ArgumentOutOfRangeException(nameof(CaptureAlertingThreshold));

        if (DecayPerStep < 0f || DecayPerStep >= 1f)
            throw new ArgumentOutOfRangeException(nameof(DecayPerStep));

        if (MinStrength < 0f || MinStrength > 1f)
            throw new ArgumentOutOfRangeException(nameof(MinStrength));
    }
}
