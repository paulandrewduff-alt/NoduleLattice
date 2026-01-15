namespace NoduleLattice.Api.Dtos;

public sealed class RunStatusDto
{
    public bool Running { get; init; }
    public int TargetHz { get; init; }
    public int StepsPerTick { get; init; }
    public long Ticks { get; init; }
    public string? LastError { get; init; }
}
