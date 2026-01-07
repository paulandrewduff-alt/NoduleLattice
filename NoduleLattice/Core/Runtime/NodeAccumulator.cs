namespace NoduleLattice.Core.Runtime;

public sealed class NodeAccumulator
{
    public float ExcSum;
    public float InhSum;

    public void Reset()
    {
        ExcSum = 0f;
        InhSum = 0f;
    }
}
