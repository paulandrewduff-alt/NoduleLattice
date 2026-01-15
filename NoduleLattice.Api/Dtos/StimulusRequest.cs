// ============================================================================
// FILE: NoduleLattice.Api/Dtos/StimulusRequest.cs
// PURPOSE: Canon request type (moved out of Models to avoid ambiguity).
// ============================================================================
namespace NoduleLattice.Api.Dtos;

/// <summary>
/// Explicit sensory stimulation request.
/// Group: 0=Vision, 1=Audio, 2=Body, 3=Internal (reserved).
/// </summary>
public sealed class StimulusRequest
{
    public int Group { get; init; } = 0;
    public float RateHz { get; init; } = 8f;
    public float Strength { get; init; } = 0.25f;
    public int Steps { get; init; } = 32;
}
