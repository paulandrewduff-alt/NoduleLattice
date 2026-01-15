namespace NoduleLattice.Api.Dtos;

public sealed class RunRequest
{
    public int TargetHz { get; set; } = 30;
    public int StepsPerTick { get; set; } = 2;
}
