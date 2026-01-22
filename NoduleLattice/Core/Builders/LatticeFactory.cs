using NoduleLattice.Core.Determinism;
using NoduleLattice.Core.Modulation;
using NoduleLattice.Core.Runtime;
using NoduleLattice.Core.Runtime.Sleep;
using NoduleLattice.Core.Synapses;
using NoduleLattice.Core.Time;
using NoduleLattice.Core.Topology;

namespace NoduleLattice.Core.Builders;

/// <summary>
/// Canon-safe factory for creating a fully-wired engine instance.
///
/// NOTE:
/// - Engine subsystem wiring lives here.
/// - Brain structures (nodes/synapses) are built via builder helpers.
/// </summary>
public static class LatticeFactory
{
    /// <summary>
    /// Create a new engine with fresh deterministic subsystems.
    /// </summary>
    public static NoduleLatticeEngine CreateEngine(
        int seed = 12345,
        float deltaTime = 1.0f,
        int structuralPeriodSteps = 128,
        int maxDelaySteps = 4,
        SleepReplayConfig? sleep = null,
        bool enableParallel = true,
        int? maxDegreeOfParallelism = null,
        int synapseChunkSize = 4096)
    {
        var time = new FixedTimebase(deltaTime);
        var field = new UniformModulatorField();

        var rng = new DeterministicRng((uint)seed);
        var synCfg = new Synapse2Config();
        var policy = new StructuralPolicy();
        var topology = new BasicTopologyManager(policy, synCfg, rng);

        var membrane = new MembraneIntegrator();
        var demand = new DemandEstimator();
        var probe = new UtilityProbe(new UtilityProbeConfig());

        return new NoduleLatticeEngine(
            time,
            field,
            topology,
            membrane,
            demand,
            probe,
            structuralPeriodSteps: structuralPeriodSteps,
            maxDelaySteps: maxDelaySteps,
            sleep: sleep,
            enableParallel: enableParallel,
            maxDegreeOfParallelism: maxDegreeOfParallelism,
            synapseChunkSize: synapseChunkSize);
    }

    /// <summary>
    /// Convenience alias for default engine creation.
    /// </summary>
    public static NoduleLatticeEngine CreateDefault() => CreateEngine();

    /// <summary>
    /// Create a default engine and immediately build a two-hemisphere cortex with corpus callosum.
    /// </summary>
    public static NoduleLatticeEngine CreateDefaultWithHemispheres(
        CortexHemispheresConfig? cortex = null,
        int seed = 12345)
    {
        var engine = CreateEngine(seed: seed);

        // Build hemisphere cortex slab + callosum using deterministic RNG.
        cortex ??= new CortexHemispheresConfig();
        cortex.Normalize();

        var rng = new DeterministicRng((uint)seed);
        var synCfg = new Synapse2Config();

        CortexHemisphereBuilder.Build(
            engine,
            cortex,
            synCfg,
            rng,
            startingNodeId: 1,
            startingSynapseId: 1);

        return engine;
    }
}