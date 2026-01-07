namespace NoduleLattice.Core.Topology;

public interface ITopologyManagerSerializable
{
    void WriteState(BinaryWriter bw);
    void ReadState(BinaryReader br);
}