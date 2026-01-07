namespace NoduleLattice.Abstractions.Archive;

public interface IArchiveStore
{
    byte[] Save();
    void Load(ReadOnlySpan<byte> data);
}
