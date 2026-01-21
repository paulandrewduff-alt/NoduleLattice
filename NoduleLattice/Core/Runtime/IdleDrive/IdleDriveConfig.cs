namespace NoduleLattice.Core.Runtime.IdleDrive;

/// <summary>
/// Canon IdleDrive: small intrinsic excitation/noise to prevent total freeze.
///
/// Requirements:
/// - Core-only
/// - Deterministic (no ThreadLocal Random, no time-based sources)
/// - Bounded, low-amplitude drift; should not constitute meaningful stimulus
/// - Should allow the network to still settle (Folded Archive 013d)
/// </summary>
public sealed class IdleDriveConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Peak magnitude of an idle injection.
    /// Units match accumulator input units.
    /// </summary>
    public float Amplitude { get; set; } = 0.0025f;

    /// <summary>
    /// Probability per step per node of receiving an idle injection.
    /// </summary>
    public float Probability { get; set; } = 0.01f;

    /// <summary>
    /// Fraction of idle injections that are inhibitory (0..1).
    /// 0.5 => symmetric.
    /// </summary>
    public float InhibitoryBias { get; set; } = 0.5f;

    public static IdleDriveConfig Disabled => new()
    {
        Enabled = false,
        Amplitude = 0f,
        Probability = 0f,
        InhibitoryBias = 0.5f
    };
}