// ============================================================================
// FILE: NoduleLattice.Blazor/Models/NetworkViewOptions.cs
// PURPOSE:
//   UI-tunable visual controls for the 3D renderer.
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
}
