namespace NoduleLattice.Abstractions.Synapses;

/// <summary>
/// Optional interface for synapses that can persist full internal state.
/// </summary>
public interface ISynapse2Serializable
{
    void WriteState(BinaryWriter bw);
    void ReadState(BinaryReader br);
}