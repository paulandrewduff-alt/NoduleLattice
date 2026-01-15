// ============================================================================
// FILE: NoduleLattice.Blazor/Models/CreateLatticeRequestModel.cs
// PURPOSE:
//   Blazor-side request model for lattice creation.
//   Shape mirrors API DTO but does NOT reference it.
// ============================================================================

namespace NoduleLattice.Blazor.Models;

public sealed class CreateLatticeRequestModel
{
    public int SizeX { get; set; }
    public int SizeY { get; set; }
    public int SizeZ { get; set; }

    public int InitialSynapses { get; set; }
    public int Seed { get; set; }

    public int StructuralPeriodSteps { get; set; }
    public int MaxDelaySteps { get; set; }

    // Cortex look controls
    public int LocalRadiusXY { get; set; }
    public int ColumnLinksPerNode { get; set; }
    public int FeedForwardLinksPerNode { get; set; }
    public int MaxOutPerNode { get; set; }
    public float Jitter { get; set; }
}
