using NoduleLattice.Abstractions.Math;

namespace NoduleLattice.Abstractions.Modulation;

public interface IModulatorField
{
    ModulatorVector Sample(in Int3 latticePos);
}
