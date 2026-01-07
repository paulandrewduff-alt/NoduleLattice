namespace NoduleLattice.Abstractions.Nodes;

public struct NodeActivity
{
    public float Rate;          // 0..1 (smoothed)
    public bool Spiked;         // spike event this step
    public long LastSpikeStep;  // last spike time

    public void ResetStepFlags() => Spiked = false;
}
