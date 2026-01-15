// ============================================================================
// FILE: NoduleLattice.Api/Dtos/CreateLatticeRequest.cs
// PURPOSE:
//   Defaults changed from “cube lattice” to a “cortical slab” look.
//   Adds cortex-specific seeding knobs.
// ============================================================================
namespace NoduleLattice.Api.Dtos;

public sealed class CreateLatticeRequest
{
    // Cortical slab: wide sheet, shallow depth (layers)
    public int SizeX { get; init; } = 32;
    public int SizeY { get; init; } = 32;
    public int SizeZ { get; init; } = 6;

    // Total synapses to seed initially (host seeds with cortex-biased rules)
    public int InitialSynapses { get; init; } = 18_000;

    // Deterministic seed
    public int Seed { get; init; } = 1234;

    public int StructuralPeriodSteps { get; init; } = 128;
    public int MaxDelaySteps { get; init; } = 4;

    // ---------------- Cortex look controls ----------------

    // Horizontal (within-layer) local radius in XY (typical 2–4)
    public int LocalRadiusXY { get; init; } = 3;

    // Columnar: vertical links per node (typical 1–3)
    public int ColumnLinksPerNode { get; init; } = 2;

    // Laminar feed-forward: z -> z+1 links per node (typical 1–3)
    public int FeedForwardLinksPerNode { get; init; } = 2;

    // Outgoing cap to prevent “hairball”
    public int MaxOutPerNode { get; init; } = 10;

    // Small position jitter for a more organic cortex “cobblestone” look (0..0.45)
    // Note: stored/used in host only; core stays integer grid.
    public float Jitter { get; init; } = 0.25f;
}
