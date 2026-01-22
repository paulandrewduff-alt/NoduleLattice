using System;

namespace NoduleLattice.Core.Builders;

/// <summary>
/// Defines a simple 2-hemisphere cortex slab with an inter-hemispheric gap,
/// plus corpus callosum parameters.
///
/// Canon notes:
/// - Hemisphere identity is inferred from X position (no new enum in NoduleRole).
/// - Callosal fibers are represented as regular synapses (SynapseKind.Excitatory by default).
/// </summary>
public sealed class CortexHemispheresConfig
{
    /// <summary>Number of columns in the left hemisphere.</summary>
    public int LeftWidthX { get; set; } = 12;

    /// <summary>Number of columns in the right hemisphere.</summary>
    public int RightWidthX { get; set; } = 12;

    /// <summary>Gap between hemispheres (empty columns).</summary>
    public int InterHemisphericGapX { get; set; } = 2;

    /// <summary>Height of cortex slab in Y.</summary>
    public int HeightY { get; set; } = 24;

    /// <summary>Number of lamina layers in Z.</summary>
    public int LayersZ { get; set; } = 6;

    /// <summary>
    /// Optional base coordinate offset applied to all created nodes.
    /// Useful when composing cortex with thalamus/nuclei elsewhere.
    /// </summary>
    public int OriginX { get; set; } = 0;
    public int OriginY { get; set; } = 0;
    public int OriginZ { get; set; } = 0;

    // --------------------------------------------------------------------
    // Callosum
    // --------------------------------------------------------------------

    /// <summary>Enable/disable corpus callosum tract creation.</summary>
    public bool EnableCorpusCallosum { get; set; } = true;

    /// <summary>
    /// Probability (0..1) of creating a callosal fiber for a given (y,z) pair.
    /// </summary>
    public float CallosumDensity { get; set; } = 0.35f;

    /// <summary>
    /// Create bidirectional fibers (L→R and R→L).
    /// </summary>
    public bool CallosumBidirectional { get; set; } = true;

    /// <summary>
    /// Base synaptic weight for callosal fibers.
    /// </summary>
    public float CallosumBaseWeight { get; set; } = 0.12f;

    /// <summary>
    /// Random jitter added to callosal weight: w = base + jitter*(2u-1).
    /// </summary>
    public float CallosumWeightJitter { get; set; } = 0.06f;

    /// <summary>
    /// Optional chance of inhibitory callosal fibers (rare).
    /// </summary>
    public float CallosumInhibitoryChance { get; set; } = 0.02f;

    /// <summary>
    /// Optional additional delay steps for callosal fibers.
    /// </summary>
    public int CallosumDelaySteps { get; set; } = 1;

    /// <summary>
    /// Validate and clamp parameters to safe ranges.
    /// </summary>
    public void Normalize()
    {
        LeftWidthX = Math.Max(1, LeftWidthX);
        RightWidthX = Math.Max(1, RightWidthX);
        InterHemisphericGapX = Math.Max(0, InterHemisphericGapX);
        HeightY = Math.Max(1, HeightY);
        LayersZ = Math.Max(1, LayersZ);

        CallosumDensity = Clamp01(CallosumDensity);
        CallosumInhibitoryChance = Clamp01(CallosumInhibitoryChance);
        CallosumDelaySteps = Math.Max(0, CallosumDelaySteps);
    }

    public int TotalWidthX => LeftWidthX + InterHemisphericGapX + RightWidthX;

    public int LeftStartX => OriginX;
    public int LeftEndXInclusive => OriginX + LeftWidthX - 1;

    public int RightStartX => OriginX + LeftWidthX + InterHemisphericGapX;
    public int RightEndXInclusive => RightStartX + RightWidthX - 1;

    public int MidLeftX => LeftEndXInclusive;
    public int MidRightX => RightStartX;

    private static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }
}