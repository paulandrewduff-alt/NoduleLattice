namespace NoduleLattice.Core.Cortex;

/// <summary>
/// Entry 017 minimal sketch:
/// - Left and Right cortical sides
/// - Callosal bridge mediates exchange
/// - StepOnce() orders: emit → enqueue → advance bridge → deliver → apply
///
/// This is deliberately NOT wired to NoduleLatticeEngine yet.
/// The next discussion step is mapping Projection.Key to lattice regions/nodes.
/// </summary>
public sealed class BimodalCortex
{
    public ICortexSide Left { get; }
    public ICortexSide Right { get; }
    public CallosalBridge Callosum { get; }

    public long StepIndex { get; private set; }

    public BimodalCortex(ICortexSide left, ICortexSide right, CallosalBridge callosum)
    {
        Left = left;
        Right = right;
        Callosum = callosum;
    }

    public void StepOnce()
    {
        var leftOut = Left.EmitProjections(StepIndex);
        var rightOut = Right.EmitProjections(StepIndex);

        Callosum.EnqueueLeftToRight(leftOut);
        Callosum.EnqueueRightToLeft(rightOut);

        Callosum.AdvanceStep();

        var toRight = Callosum.DequeueForRight();
        var toLeft = Callosum.DequeueForLeft();

        if (toRight.Count > 0) Right.ApplyProjections(StepIndex, toRight);
        if (toLeft.Count > 0) Left.ApplyProjections(StepIndex, toLeft);

        StepIndex++;
    }

    public static BimodalCortex CreateDefault(CallosalConfig? cfg = null, int seed = 12345)
    {
        cfg ??= new CallosalConfig();
        var callosum = new CallosalBridge(cfg, seed);

        var left = new CortexSide(CortexSideId.Left);
        var right = new CortexSide(CortexSideId.Right);

        return new BimodalCortex(left, right, callosum);
    }
}
