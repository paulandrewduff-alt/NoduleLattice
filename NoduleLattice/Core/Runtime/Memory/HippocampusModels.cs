using System;
using System.Collections.Generic;
using NoduleLattice.Abstractions.Modulation;

namespace NoduleLattice.Core.Runtime.Memory;

public sealed class HippocampusEpisode
{
    public long Id { get; init; }
    public long CapturedAtStep { get; init; }

    /// <summary>0..1, decays each step.</summary>
    public float Strength { get; set; }

    public int[] NodeIds { get; init; } = Array.Empty<int>();
    public float[] Values { get; init; } = Array.Empty<float>();

    public ModulatorVector ContextMods { get; init; }
    public InputGates ContextGates { get; init; }
}

public sealed class HippocampusEpisodeList
{
    public long CurrentStep { get; init; }
    public List<HippocampusEpisode> Episodes { get; init; } = new();
}

public sealed class HippocampusState
{
    public HippocampusConfig Config { get; init; } = new();
    public long NextId { get; init; } = 1;
    public List<HippocampusEpisode> Episodes { get; init; } = new();
}
