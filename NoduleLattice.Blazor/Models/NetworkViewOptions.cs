// ============================================================================
// FILE: NoduleLattice.Blazor/Models/NetworkViewOptions.cs
// PURPOSE:
//   View configuration passed from Blazor -> JS renderer (network3d.js).
//   MUST include new Notch #4 fields used by Home.razor:
//     - BundlingStrength
//     - HighlightThalProjection
//     - ThalProjectionBoost
// NOTES:
//   Keep names PascalCase for C#; JS normalizer supports both cases.
// ============================================================================

namespace NoduleLattice.Blazor.Models;

public sealed class NetworkViewOptions
{
    // Layout
    public float SpacingXY { get; set; } = 1.20f;
    public float ZScale { get; set; } = 1.70f;

    // Nodes
    public float NodeSize { get; set; } = 0.10f;
    public float NodeOpacity { get; set; } = 0.92f;

    // Edges
    public float EdgeSoftFadeLen { get; set; } = 4.2f;
    public float EdgeHardCullLen { get; set; } = 6.2f;
    public float EdgeOpacity { get; set; } = 0.28f;

    // Overlays
    public bool ShowGrid { get; set; } = true;
    public bool ShowAxes { get; set; } = false;
    public bool ShowLaminarPlanes { get; set; } = true;

    // Atmosphere
    public bool EnableFog { get; set; } = true;
    public float FogDensity { get; set; } = 0.020f;

    // Interaction / emphasis
    public float HoverPointThreshold { get; set; } = 0.55f;
    public float ColumnBoost { get; set; } = 0.55f;
    public float HoverBoost { get; set; } = 0.90f;

    // Regions / cortex look
    public bool EnableRegions { get; set; } = true;
    public float ThalamusRadiusXY { get; set; } = 0.28f;
    public float ThalamusRadiusZ { get; set; } = 0.38f;
    public int NucleiCount { get; set; } = 6;
    public float NucleiRadius { get; set; } = 0.10f;

    // Column banding
    public bool EnableColumnBanding { get; set; } = true;
    public int ColumnBandPeriod { get; set; } = 2;
    public float ColumnBandStrength { get; set; } = 0.18f;

    // ------------------------------------------------------------------------
    // Notch #4: tract readability
    // ------------------------------------------------------------------------

    // 0..1 : how strongly long edges are bent toward bundle paths
    public float BundlingStrength { get; set; } = 0.35f;

    // Highlight thalamus -> cortex projections (heuristic, renderer-side)
    public bool HighlightThalProjection { get; set; } = true;

    // Brightness multiplier for thalamocortical projection edges
    public float ThalProjectionBoost { get; set; } = 1.8f;
}
