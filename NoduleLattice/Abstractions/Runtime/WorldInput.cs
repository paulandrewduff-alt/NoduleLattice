using NoduleLattice.Abstractions.Math;

namespace NoduleLattice.Abstractions.Runtime;

public readonly record struct WorldInput(
    Int3 Target,
    float Signal,
    int Channel = 0
);
