using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Core.Nodes;

public sealed class Nodule : INodule
{
    public NoduleId Id { get; }
    public Int3 Position { get; }
    public NoduleRole Role { get; set; }

    public MembraneState Membrane { get; set; }
    public NodeActivity Activity { get; set; }

    public Nodule(NoduleId id, Int3 position)
    {
        Id = id;
        Position = position;
        Role = NoduleRole.Generic;
        Membrane = MembraneState.Default();
        Activity = new NodeActivity { Rate = 0f, Spiked = false, LastSpikeStep = -1 };
    }
}
