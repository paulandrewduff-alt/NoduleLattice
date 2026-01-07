namespace NoduleLattice.Core.Topology;

public sealed class UtilityProbeConfig
{
    /// <summary>
    /// EMA smoothing: closer to 1 means slower changes.
    /// </summary>
    public float EmaDecay { get; init; } = 0.995f;

    /// <summary>
    /// Counts as "modulatory silence" if max( Salience, Alerting ) is below this.
    /// </summary>
    public float SilenceThreshold { get; init; } = 0.15f;

    /// <summary>
    /// Max expected absolute membrane potential for normalisation.
    /// </summary>
    public float VNorm { get; init; } = 5.0f;

    /// <summary>
    /// Degree sweet spot for compression proxy.
    /// </summary>
    public float DegreeTarget { get; init; } = 20.0f;

    public float DegreeRange { get; init; } = 80.0f;
}