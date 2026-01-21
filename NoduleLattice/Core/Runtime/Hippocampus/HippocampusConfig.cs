// ============================================================================
// FILE: NoduleLattice/Core/Runtime/Hippocampus/HippocampusConfig.cs
// PURPOSE:
//   Configuration for hippocampus episodic memory capture + recall injection.
// NOTES:
//   - Uses canon roles (Generic/Sensory/Relay/Hub/Memory/ModulatorySource).
//   - "Relay" is treated as thalamus-like for boost purposes.
//   - "ModulatorySource" is treated as nuclei-like for boost purposes.
// ============================================================================

namespace NoduleLattice.Core.Runtime.Hippocampus;

public sealed class HippocampusConfig
{
    public bool Enabled { get; set; } = true;

    // Storage
    public int CapacityEpisodes { get; set; } = 512;

    // When storing an episode, keep only the top-K nodules by (rate * roleBoost)
    public int StoreTopKNodes { get; set; } = 256;

    // Ignore nodules below this rate when forming a sparse episode
    public float MinRateToStore { get; set; } = 0.04f;

    // Capture trigger thresholds (any can trigger)
    public float MinSalienceToStore { get; set; } = 0.18f;
    public float MinSpikeFractionToStore { get; set; } = 0.02f; // fraction of nodes spiking this step
    public float MinScoreToStore { get; set; } = 0.22f;         // combined heuristic score

    // Recall defaults
    public float DefaultRecallGain { get; set; } = 0.60f;
    public int DefaultRecallEpisodes { get; set; } = 1;

    // Recency weighting: score *= exp(-dt/halfLife)
    public int RecencyHalfLifeSteps { get; set; } = 2048;

    // Role boosts (canon roles)
    public float CortexRoleBoost { get; set; } = 1.00f;      // Generic/Sensory/Hub/Memory
    public float RelayRoleBoost { get; set; } = 1.15f;       // Relay (thalamus-like)
    public float ModSourceRoleBoost { get; set; } = 1.25f;   // ModulatorySource (nuclei-like)
}
