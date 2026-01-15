namespace NoduleLattice.Blazor.Models;

public sealed class RunStatusDto
{
    public bool Running { get; set; }
    public int TargetHz { get; set; }
    public int StepsPerTick { get; set; }
    public long Ticks { get; set; }
    public string? LastError { get; set; }
}
