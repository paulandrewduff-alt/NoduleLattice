namespace NoduleLattice.Api.Dtos;

public sealed class RunRequest
{
    public int TargetHz { get; init; } = 30;
    public int StepsPerTick { get; init; } = 2;
}
