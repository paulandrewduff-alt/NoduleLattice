using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Modulation;

namespace NoduleLattice.Core.Modulation;

/// <summary>
/// Simple global (uniform) modulator field. Spatially constant.
/// </summary>
public sealed class UniformModulatorField
{
    public ModulatorVector Current { get; private set; }

    public void Set(ModulatorVector v)
    {
        v.Clamp01();
        Current = v;
    }

    public ModulatorVector Sample(in Int3 _) => Current;
}