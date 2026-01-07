namespace NoduleLattice.Core.Runtime.Sleep;

public sealed class SleepReplayConfig
{
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// How many past steps of node activity to retain for replay.
    /// </summary>
    public int HistorySteps { get; init; } = 512;

    /// <summary>
    /// Number of replay cycles to run when requested.
    /// </summary>
    public int ReplaySteps { get; init; } = 256;

    /// <summary>
    /// Scalar applied to replayed activity when injected.
    /// </summary>
    public float ReplayGain { get; init; } = 0.20f;

    /// <summary>
    /// During sleep, modulators are forced toward "silence".
    /// </summary>
    public float SleepModulatorScale { get; init; } = 0.10f;
}