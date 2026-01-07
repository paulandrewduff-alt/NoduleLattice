namespace NoduleLattice.Api.Dtos;

public sealed class InjectRequest
{
    public int NodeId { get; init; }
    public float Exc { get; init; }
    public float Inh { get; init; }
}
