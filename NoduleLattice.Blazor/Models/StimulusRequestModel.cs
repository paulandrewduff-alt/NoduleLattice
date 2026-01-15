// ============================================================================
// FILE: NoduleLattice.Blazor/Models/StimulusRequestModel.cs
// PURPOSE:
//   Blazor-side request model for /api/lattice/stimulus
// ============================================================================
namespace NoduleLattice.Blazor.Models;

public sealed class StimulusRequestModel
{
    // 0=Vision, 1=Audio, 2=Body, 3=Internal
    public int Group { get; set; } = 0;

    // pulses per second-ish (server scales/injects)
    public float RateHz { get; set; } = 8f;

    // overall strength scaling
    public float Strength { get; set; } = 0.25f;

    // optional “burst” steps to advance after injection
    public int Steps { get; set; } = 32;
}
