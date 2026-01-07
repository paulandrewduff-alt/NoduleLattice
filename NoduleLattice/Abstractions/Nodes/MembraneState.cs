namespace NoduleLattice.Abstractions.Nodes;

public struct MembraneState
{
    public float Potential;
    public float Threshold;
    public float Leak;        // 0..1
    public int Refractory;    // steps remaining
    public float Bias;

    public static MembraneState Default()
        => new()
        {
            Potential = 0f,
            Threshold = 1f,
            Leak = 0.92f,
            Refractory = 0,
            Bias = 0.01f
        };
}
