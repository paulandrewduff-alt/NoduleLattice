// ============================================================================
// FILE: NoduleLattice/Core/Runtime/Hippocampus/HippocampusEpisode.cs
// PURPOSE:
//   Compact episodic record: sparse set of nodules + their rates.
// ============================================================================

using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;

namespace NoduleLattice.Core.Runtime.Hippocampus;

internal sealed class HippocampusEpisode
{
    public long StepIndex;
    public float Score;
    public ModulatorVector Modulators;

    public NoduleId[] NodeIds = Array.Empty<NoduleId>();
    public float[] Rates = Array.Empty<float>();
}
