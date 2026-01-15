// ============================================================================
// FILE: NoduleLattice.Blazor/Models/ThalamusGatesRequestModel.cs
// PURPOSE:
//   Blazor-side request model for /api/lattice/thalamus
//   Mirrors API JSON shape without referencing API layer.
// ============================================================================
namespace NoduleLattice.Blazor.Models;

public sealed class ThalamusGatesRequestModel
{
    public float VisionGate { get; set; } = 1f;
    public float AudioGate { get; set; } = 1f;
    public float BodyGate { get; set; } = 1f;
    public float InternalGate { get; set; } = 0.35f;
}
