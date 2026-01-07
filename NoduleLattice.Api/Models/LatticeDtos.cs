namespace NoduleLattice.Api.Models;

// Requests
public sealed class StepRequest
{
    public int Steps { get; init; } = 1;
}

public sealed class InjectRequest
{
    public int NodeId { get; init; }
    public float Exc { get; init; }
    public float Inh { get; init; }
}

public sealed class ModulatorsRequest
{
    public float Reward { get; init; }
    public float Salience { get; init; }
    public float Stability { get; init; }
    public float Alerting { get; init; }
    public float Curiosity { get; init; }
    public float Goal { get; init; }
}

public sealed class SleepReplayRequest
{
    public bool Run { get; init; } = true;
}

public sealed class ArchiveDto
{
    public string Base64 { get; init; } = string.Empty;
}

/// <summary>
/// Explicit sensory stimulation request.
/// Group is reserved for future modality routing; currently Group=0 targets a default \"input band\".
/// </summary>
public sealed class StimulusRequest
{
    public int Group { get; init; } = 0;
    public float RateHz { get; init; } = 8f;
    public float Strength { get; init; } = 0.25f;
    public int Steps { get; init; } = 32;
}

// Snapshot
public sealed class LatticeSnapshotDto
{
    public long StepIndex { get; set; }
    public List<NodeSnapDto> Nodes { get; set; } = new();
    public List<EdgeSnapDto> Synapses { get; set; } = new();
}

public sealed class NodeSnapDto
{
    public int Id { get; set; }
    public Pos3Dto Pos { get; set; } = new();
    public float V { get; set; }
    public float Rate { get; set; }
    public bool Spiked { get; set; }
}

public sealed class EdgeSnapDto
{
    public long Id { get; set; }
    public int Pre { get; set; }
    public int Post { get; set; }
    public float W { get; set; }
    public int Kind { get; set; } // 0 excit, 1 inhib
}

public sealed class Pos3Dto
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}
