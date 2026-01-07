namespace NoduleLattice.Abstractions.Runtime;

public interface ISnapshotProvider<TSnapshot>
{
    TSnapshot GetSnapshot();
}
