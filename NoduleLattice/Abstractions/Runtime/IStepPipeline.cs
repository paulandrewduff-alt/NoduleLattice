namespace NoduleLattice.Abstractions.Runtime;

/// <summary>
/// Explicit ordered phases per step. Keeps determinism and debuggability.
/// </summary>
public interface IStepPipeline
{
    void Step();
}
