using NoduleLattice.Abstractions.Math;

namespace NoduleLattice.Abstractions.Nodes;

public interface INodule
{
    NoduleId Id { get; }
    Int3 Position { get; }
    NoduleRole Role { get; set; }
    MembraneState Membrane { get; set; }
    NodeActivity Activity { get; set; }
}
