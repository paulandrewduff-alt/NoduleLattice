namespace NoduleLattice.Blazor.Models;

public sealed class RunRequestModel
{
    public int TargetHz { get; set; } = 30;
    public int StepsPerTick { get; set; } = 2;
}
