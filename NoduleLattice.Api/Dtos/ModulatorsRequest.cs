namespace NoduleLattice.Api.Dtos;

public sealed class ModulatorsRequest
{
    public float Reward { get; init; }
    public float Salience { get; init; }
    public float Stability { get; init; }
    public float Alerting { get; init; }
    public float Curiosity { get; init; }
    public float Goal { get; init; }
}
