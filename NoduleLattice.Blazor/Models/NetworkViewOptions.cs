// ============================================================================
// FILE: NoduleLattice.Blazor/Models/NetworkViewOptions.cs
// PURPOSE:
//   UI-tunable visual controls for the 3D renderer.
//   This version adds "region tinting" + "minicolumn banding" controls.
// ============================================================================

namespace NoduleLattice.Blazor.Models;

public sealed class NetworkViewOptions
{
    // Geometry scaling
    public float SpacingXY { get; set; } = 1.35f;   // spreads cortex out so it reads as a slab
    public float ZScale { get; set; } = 1.80f;      // exaggerate laminar separation

    // Nodes
    public float NodeSize { get; set; } = 0.26f;
    public float NodeOpacity { get; set; } = 0.95f;

    // Edges (length in *scaled* space)
    public float EdgeSoftFadeLen { get; set; } = 5.0f;
    public float EdgeHardCullLen { get; set; } = 8.0f;
    public float EdgeOpacity { get; set; } = 0.55f;

    // Overlays
    public bool ShowGrid { get; set; } = true;
    public bool ShowAxes { get; set; } = false;
    public bool ShowLaminarPlanes { get; set; } = true;

    // Scene feel
    public bool EnableFog { get; set; } = true;
    public float FogDensity { get; set; } = 0.018f;

    // Hover
    public float HoverPointThreshold { get; set; } = 0.55f;
    public float ColumnBoost { get; set; } = 0.55f;
    public float HoverBoost { get; set; } = 0.90f;

    // ------------------------------------------------------------------------
    // Cortex-look notch #2: Regions + columnar banding (purely visual)
    // ------------------------------------------------------------------------

    // Region tinting (cortex vs thalamus core vs nuclei clusters)
    public bool EnableRegions { get; set; } = true;

    // Strength of region tint overlays (0..1-ish)
    public float CortexTintStrength { get; set; } = 0.20f;
    public float ThalamusTintStrength { get; set; } = 0.55f;
    public float NucleiTintStrength { get; set; } = 0.65f;

    // Thalamus core "size" (ellipsoid fraction of bounds)
    public float ThalamusRadiusXY { get; set; } = 0.28f;
    public float ThalamusRadiusZ { get; set; } = 0.38f;

    // Nuclei clusters: how many and how tight
    public int NucleiCount { get; set; } = 6;
    public float NucleiRadius { get; set; } = 0.10f;

    // Minicolumn banding (alternating stripes in XY)
    public bool EnableColumnBanding { get; set; } = true;
    public int ColumnBandPeriod { get; set; } = 2;       // 2 => alternating columns
    public float ColumnBandStrength { get; set; } = 0.16f; // subtle, don't overdo
}
