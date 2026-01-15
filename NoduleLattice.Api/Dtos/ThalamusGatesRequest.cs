// ============================================================================
// FILE: NoduleLattice.Api/Dtos/ThalamusGatesRequest.cs
// PURPOSE: Canon request type (moved out of Models to avoid ambiguity).
// ============================================================================
namespace NoduleLattice.Api.Dtos;

/// <summary>
/// Explicit thalamus gate settings (TRN-like per-channel gating).
/// Values are clamped to [0..1].
/// </summary>
public sealed class ThalamusGatesRequest
{
    public float VisionGate { get; init; } = 1f;
    public float AudioGate { get; init; } = 1f;
    public float BodyGate { get; init; } = 1f;
    public float InternalGate { get; init; } = 0.35f;
}
