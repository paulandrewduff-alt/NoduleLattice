namespace NoduleLattice.Api.Dtos;

public sealed class CreateLatticeRequest
{
    public int SizeX { get; init; } = 5;
    public int SizeY { get; init; } = 5;
    public int SizeZ { get; init; } = 1;

    public int InitialSynapses { get; init; } = 80;

    public int Seed { get; init; } = 1234;

    public int StructuralPeriodSteps { get; init; } = 128;
    public int MaxDelaySteps { get; init; } = 4;
}