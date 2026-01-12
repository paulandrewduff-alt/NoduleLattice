using System;

namespace NoduleLattice.Core.Runtime.Memory;

/// <summary>
/// Minimal thalamus-like gate set (kept in core so episodic memory can capture context).
/// 0..1 each.
/// </summary>
public readonly record struct InputGates(float Vision, float Audio, float Body, float Internal)
{
    public static InputGates Default => new(1f, 1f, 1f, 0.35f);

    public InputGates Clamp01()
        => new(
            Vision: Math.Clamp(Vision, 0f, 1f),
            Audio: Math.Clamp(Audio, 0f, 1f),
            Body: Math.Clamp(Body, 0f, 1f),
            Internal: Math.Clamp(Internal, 0f, 1f)
        );
}
