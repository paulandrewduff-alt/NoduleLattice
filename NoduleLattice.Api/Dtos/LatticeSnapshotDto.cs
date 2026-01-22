namespace NoduleLattice.Api.Dtos;

public sealed class LatticeSnapshotDto
{
    public long StepIndex { get; set; }
    public List<NodeSnapDto> Nodes { get; set; } = new();
    public List<EdgeSnapDto> Synapses { get; set; } = new();
}

public sealed class Pos3Dto
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
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

    /// <summary>Canon: synapse weight (maps from Core EdgeSnap.W)</summary>
    public float W { get; set; }

    /// <summary>Canon: integer kind (maps from Core EdgeSnap.Kind)</summary>
    public int Kind { get; set; }
}