namespace NoduleLattice.Blazor.Models;

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

public sealed class StimulusRequest
{
    public int Group { get; init; } = 0;
    public float RateHz { get; init; } = 8f;
    public float Strength { get; init; } = 0.25f;
    public int Steps { get; init; } = 32;
}

public sealed class ThalamusGatesRequest
{
    public float VisionGate { get; init; } = 1f;
    public float AudioGate { get; init; } = 1f;
    public float BodyGate { get; init; } = 1f;
    public float InternalGate { get; init; } = 0.35f;
}

public sealed class HippocampusConfigRequest
{
    public bool CaptureEnabled { get; init; } = true;
    public float CaptureSalienceThreshold { get; init; } = 0.60f;
    public float CaptureAlertingThreshold { get; init; } = 0.40f;
    public int TopK { get; init; } = 18;
    public int MaxEpisodes { get; init; } = 256;
    public float DecayPerStep { get; init; } = 0.0015f;
    public float MinStrength { get; init; } = 0.06f;
}

public sealed class HippocampusReplayRequest
{
    public int Count { get; init; } = 8;
    public float Gain { get; init; } = 1.0f;
    public int StepsPerEpisode { get; init; } = 10;
}

public sealed class HippocampusReplayOneRequest
{
    public float Gain { get; init; } = 1.0f;
    public int Steps { get; init; } = 12;
}

public sealed class HippocampusEpisodeListDto
{
    public long CurrentStep { get; set; }
    public List<HippocampusEpisodeDto> Episodes { get; set; } = new();
}

public sealed class HippocampusEpisodeDto
{
    public long Id { get; set; }
    public long CapturedAtStep { get; set; }
    public float Strength { get; set; }

    public int[] NodeIds { get; set; } = Array.Empty<int>();
    public float[] Values { get; set; } = Array.Empty<float>();

    public ModulatorsRequest ContextMods { get; set; } = new();
    public ThalamusGatesRequest ContextThalamus { get; set; } = new();
}

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
    public int Kind { get; set; }
}

public sealed class Pos3Dto
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}