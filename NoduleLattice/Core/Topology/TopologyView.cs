using NoduleLattice.Abstractions.Math;
using NoduleLattice.Abstractions.Nodes;
using NoduleLattice.Abstractions.Synapses;
using NoduleLattice.Abstractions.Topology;
using NoduleLattice.Core.Spatial;

namespace NoduleLattice.Core.Topology;

public sealed class TopologyView : ITopologyView
{
    private readonly IReadOnlyDictionary<NoduleId, INodule> _nodes;
    private readonly IReadOnlyDictionary<NoduleId, List<ISynapse2>> _incoming;
    private readonly IReadOnlyDictionary<NoduleId, List<ISynapse2>> _outgoing;
    private readonly List<ISynapse2> _all;
    private readonly SpatialIndex3D _spatial;

    public long StepIndex { get; }

    public TopologyView(
    long stepIndex,
    IReadOnlyDictionary<NoduleId, INodule> nodes,
    IReadOnlyDictionary<NoduleId, List<ISynapse2>> incoming,
    IReadOnlyDictionary<NoduleId, List<ISynapse2>> outgoing,
    List<ISynapse2> all,
    SpatialIndex3D spatial)
    {
        StepIndex = stepIndex;
        _nodes = nodes;
        _incoming = incoming;
        _outgoing = outgoing;
        _all = all;
        _spatial = spatial;
    }

    public IReadOnlyList<ISynapse2> AllSynapses => _all;

    public INodule GetNodule(NoduleId id) => _nodes[id];

    public IReadOnlyList<INodule> GetNeighbours(in Int3 pos, int radiusManhattan)
    => _spatial.QueryManhattan(pos, radiusManhattan);

    public IReadOnlyList<ISynapse2> GetIncoming(NoduleId id)
    => _incoming.TryGetValue(id, out var l) ? l : Array.Empty<ISynapse2>();

    public IReadOnlyList<ISynapse2> GetOutgoing(NoduleId id)
    => _outgoing.TryGetValue(id, out var l) ? l : Array.Empty<ISynapse2>();
}