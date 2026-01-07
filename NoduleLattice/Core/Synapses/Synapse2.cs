using NoduleLattice.Abstractions.Modulation;
using NoduleLattice.Abstractions.Nodes;
using NoduleLattice.Abstractions.Synapses;

namespace NoduleLattice.Core.Synapses;

public sealed class Synapse2 : ISynapse2, ISynapse2Serializable
{
    private readonly Synapse2Config _cfg;
    private readonly Synapse2State _s;

    public SynapseId Id { get; }
    public NoduleId Pre { get; }
    public NoduleId Post { get; }
    public SynapseKind Kind { get; }

    public float Weight
    {
        get => _s.Weight;
        set => _s.Weight = Clamp(value, -_cfg.WeightClamp, _cfg.WeightClamp);
    }

    public float Gain
    {
        get => _s.Gain;
        set => _s.Gain = value;
    }

    public int DelaySteps
    {
        get => _s.DelaySteps;
        set => _s.DelaySteps = System.Math.Max(0, value);
    }

    public Synapse2(
        SynapseId id,
        NoduleId pre,
        NoduleId post,
        SynapseKind kind,
        Synapse2Config cfg,
        float initialWeight = 0.0f,
        float initialGain = 1.0f,
        int delaySteps = 0,
        Synapse2State? state = null)
    {
        Id = id;
        Pre = pre;
        Post = post;
        Kind = kind;
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _s = state ?? new Synapse2State();

        _s.Gain = initialGain;
        _s.DelaySteps = delaySteps;
        Weight = initialWeight;

        _s.GrowthBudget = Clamp(_s.GrowthBudget, 0f, _cfg.GrowthBudgetMax);
    }

    // Phase 1 (pure): compute output only
    public float ComputeOutput(in NodeActivity preActivity, in ModulatorVector modulators)
    {
        float pre = preActivity.Rate;
        if (preActivity.Spiked) pre = System.MathF.Min(1.0f, pre + _cfg.SpikeBonus);

        float alert = modulators.Alerting;
        float effGain = _cfg.BaseGain * _s.Gain * (1.0f + 0.10f * alert);

        float signedW = _s.Weight;
        if (Kind == SynapseKind.Inhibitory) signedW = -System.MathF.Abs(signedW);
        else if (Kind == SynapseKind.Excitatory) signedW = System.MathF.Abs(signedW);

        // resources scale (read-only here)
        float outSignal = pre * signedW * effGain * Clamp01(_s.R);
        return outSignal;
    }

    // Phase 3 (mutating): short-term dynamics + eligibility decay + eligibility capture
    // Eligibility capture must be called after emission is known (Entry 006 clarified).
    public void AdvanceFastState(in NodeActivity preActivity, in NodeActivity postActivity)
    {
        // Short-term resources update (depression)
        float pre = preActivity.Rate;
        if (preActivity.Spiked) pre = System.MathF.Min(1.0f, pre + _cfg.SpikeBonus);

        _s.R = _s.R + (1.0f - _s.R) * _cfg.TauR;
        _s.R = Clamp01(_s.R);

        float consumed = _cfg.Utilisation * pre;
        _s.R = Clamp01(_s.R - consumed);

        // Eligibility decay
        _s.E *= _cfg.EligibilityDecay;

        // Eligibility capture (simple hybrid proxy)
        float delta;
        if (preActivity.Spiked && postActivity.Spiked) delta = 1.0f;
        else delta = preActivity.Rate * postActivity.Rate;

        _s.E += _cfg.EligibilityScale * delta;
        _s.E = Clamp(_s.E, -1.0f, 1.0f);
    }

    // Phase 6: permissioned consolidation
    public void Consolidate(in ModulatorVector modulators)
    {
        _s.P *= _cfg.PlasticityDecay;

        // Salience boosts plasticity readiness
        _s.P = Clamp01(_s.P + modulators.Salience * _cfg.PlasticityBoostFromSalience);

        float gate = ComputeLearningGate(modulators);
        if (gate <= 0.0001f) return;

        float dw = _cfg.LearningRate * gate * _s.E * (0.25f + 0.75f * _s.P);
        dw = Clamp(dw, -_cfg.ConsolidationClamp, _cfg.ConsolidationClamp);

        Weight = _s.Weight + dw;

        // small eligibility consumption
        _s.E *= 0.95f;
    }

    // Phase 7: deficits + receptors + budget regen
    public void SlowHooks(in ModulatorVector modulators, in SynapseDemand demand)
    {
        // regen growth budget (meta scaled)
        float regen = _cfg.GrowthBudgetRegen * Clamp01(_s.GrowthRegenScale);
        _s.GrowthBudget = Clamp(_s.GrowthBudget + regen, 0f, _cfg.GrowthBudgetMax);

        // deficits and receptor expression with competition/budget
        UpdateDeficitAndReceptor(ModulatorId.Reward, demand.Reward, ref _s.D_Reward, ref _s.R_Reward, _s.C_Reward);
        UpdateDeficitAndReceptor(ModulatorId.Salience, demand.Salience, ref _s.D_Salience, ref _s.R_Salience, _s.C_Salience);
        UpdateDeficitAndReceptor(ModulatorId.Stability, demand.Stability, ref _s.D_Stability, ref _s.R_Stability, _s.C_Stability);
        UpdateDeficitAndReceptor(ModulatorId.Alerting, demand.Alerting, ref _s.D_Alerting, ref _s.R_Alerting, _s.C_Alerting);
        UpdateDeficitAndReceptor(ModulatorId.Curiosity, demand.Curiosity, ref _s.D_Curiosity, ref _s.R_Curiosity, _s.C_Curiosity);
        UpdateDeficitAndReceptor(ModulatorId.Goal, demand.Goal, ref _s.D_Goal, ref _s.R_Goal, _s.C_Goal);

        EnforceReceptorBudget();
    }

    public SynapseGrowthRequest? ConsiderGrowthRequest(in ModulatorVector modulators)
    {
        float threshold = _cfg.GrowthThreshold * Clamp01(_s.GrowthThresholdScale <= 0f ? 1f : _s.GrowthThresholdScale);
        if (_s.GrowthBudget < _cfg.GrowthBudgetCost) return null;

        // Choose highest deficit where receptor is near cap (Stage A exhausted)
        (ModulatorId id, float score) best = default;
        best.score = 0f;

        Consider(ModulatorId.Reward, _s.D_Reward, _s.R_Reward, _s.C_Reward, modulators.Reward, threshold, ref best);
        Consider(ModulatorId.Salience, _s.D_Salience, _s.R_Salience, _s.C_Salience, modulators.Salience, threshold, ref best);
        Consider(ModulatorId.Stability, _s.D_Stability, _s.R_Stability, _s.C_Stability, modulators.Stability, threshold, ref best);
        Consider(ModulatorId.Alerting, _s.D_Alerting, _s.R_Alerting, _s.C_Alerting, modulators.Alerting, threshold, ref best);
        Consider(ModulatorId.Curiosity, _s.D_Curiosity, _s.R_Curiosity, _s.C_Curiosity, modulators.Curiosity, threshold, ref best);
        Consider(ModulatorId.Goal, _s.D_Goal, _s.R_Goal, _s.C_Goal, modulators.Goal, threshold, ref best);

        if (best.score <= 0f) return null;

        _s.GrowthBudget = Clamp(_s.GrowthBudget - _cfg.GrowthBudgetCost, 0f, _cfg.GrowthBudgetMax);

        float urgency = Clamp01(best.score * _cfg.GrowthUrgencyScale);

        return new SynapseGrowthRequest(
            SourceSynapse: Id,
            PostNodule: Post,
            NeededModulator: best.id,
            Urgency: urgency
        );
    }

    public void ApplyGrowthFeedback(in SynapseGrowthFeedback feedback)
    {
        if (feedback.SourceSynapse.Value != Id.Value) return;

        _s.LastGrowthUtility = feedback.UtilityScore;

        // Meta-plasticity:
        // success -> slightly easier future growth, slightly faster regen
        // failure -> slightly harder future growth, slower regen
        if (feedback.Success)
        {
            _s.GrowthThresholdScale = Clamp(_s.GrowthThresholdScale * 0.98f, 0.6f, 1.4f);
            _s.GrowthRegenScale = Clamp(_s.GrowthRegenScale * 1.02f, 0.5f, 2.0f);

            // relieve deficits gently
            _s.D_Reward *= 0.90f;
            _s.D_Salience *= 0.90f;
            _s.D_Stability *= 0.90f;
            _s.D_Alerting *= 0.90f;
            _s.D_Curiosity *= 0.90f;
            _s.D_Goal *= 0.90f;
        }
        else
        {
            _s.GrowthThresholdScale = Clamp(_s.GrowthThresholdScale * 1.02f, 0.6f, 1.8f);
            _s.GrowthRegenScale = Clamp(_s.GrowthRegenScale * 0.98f, 0.25f, 2.0f);
        }
    }

    // --- Canonical state persistence ---

    public void WriteState(BinaryWriter bw)
    {
        // Version marker for Synapse2 state blob
        bw.Write(1);

        bw.Write(_s.Weight);
        bw.Write(_s.Gain);
        bw.Write(_s.DelaySteps);

        bw.Write(_s.R);
        bw.Write(_s.E);
        bw.Write(_s.P);

        // Receptors
        bw.Write(_s.R_Reward);
        bw.Write(_s.R_Salience);
        bw.Write(_s.R_Stability);
        bw.Write(_s.R_Alerting);
        bw.Write(_s.R_Curiosity);
        bw.Write(_s.R_Goal);

        // Caps
        bw.Write(_s.C_Reward);
        bw.Write(_s.C_Salience);
        bw.Write(_s.C_Stability);
        bw.Write(_s.C_Alerting);
        bw.Write(_s.C_Curiosity);
        bw.Write(_s.C_Goal);

        // Deficits
        bw.Write(_s.D_Reward);
        bw.Write(_s.D_Salience);
        bw.Write(_s.D_Stability);
        bw.Write(_s.D_Alerting);
        bw.Write(_s.D_Curiosity);
        bw.Write(_s.D_Goal);

        // Growth/meta
        bw.Write(_s.GrowthBudget);
        bw.Write(_s.GrowthThresholdScale);
        bw.Write(_s.GrowthRegenScale);
        bw.Write(_s.LastGrowthUtility);
    }

    public void ReadState(BinaryReader br)
    {
        int ver = br.ReadInt32();
        if (ver != 1) throw new InvalidOperationException($"Unsupported Synapse2 state version: {ver}");

        _s.Weight = br.ReadSingle();
        _s.Gain = br.ReadSingle();
        _s.DelaySteps = br.ReadInt32();

        _s.R = br.ReadSingle();
        _s.E = br.ReadSingle();
        _s.P = br.ReadSingle();

        _s.R_Reward = br.ReadSingle();
        _s.R_Salience = br.ReadSingle();
        _s.R_Stability = br.ReadSingle();
        _s.R_Alerting = br.ReadSingle();
        _s.R_Curiosity = br.ReadSingle();
        _s.R_Goal = br.ReadSingle();

        _s.C_Reward = br.ReadSingle();
        _s.C_Salience = br.ReadSingle();
        _s.C_Stability = br.ReadSingle();
        _s.C_Alerting = br.ReadSingle();
        _s.C_Curiosity = br.ReadSingle();
        _s.C_Goal = br.ReadSingle();

        _s.D_Reward = br.ReadSingle();
        _s.D_Salience = br.ReadSingle();
        _s.D_Stability = br.ReadSingle();
        _s.D_Alerting = br.ReadSingle();
        _s.D_Curiosity = br.ReadSingle();
        _s.D_Goal = br.ReadSingle();

        _s.GrowthBudget = Clamp(br.ReadSingle(), 0f, _cfg.GrowthBudgetMax);
        _s.GrowthThresholdScale = Clamp(br.ReadSingle(), 0.25f, 4.0f);
        _s.GrowthRegenScale = Clamp(br.ReadSingle(), 0.05f, 10.0f);
        _s.LastGrowthUtility = br.ReadSingle();

        // keep invariants sane
        EnforceReceptorBudget();
    }

    // ---------------- internal helpers ----------------

    private void UpdateDeficitAndReceptor(ModulatorId id, float demand, ref float deficitTrace, ref float receptor, float cap)
    {
        float def = demand - receptor;
        if (def < 0) def = 0;

        deficitTrace = deficitTrace * _cfg.DeficitDecay + def;

        if (deficitTrace > 0.05f && receptor < cap)
        {
            receptor = Clamp(receptor + _cfg.ReceptorGrowthRate * deficitTrace, 0f, cap);

            // competition: small drain from other receptors to "pay" for this increase
            ApplyCompetitionDrain(id, _cfg.ReceptorCompetition * _cfg.ReceptorGrowthRate * deficitTrace);
        }
    }

    private void ApplyCompetitionDrain(ModulatorId grown, float amount)
    {
        if (amount <= 0) return;

        float[] refs =
        [
            _s.R_Reward, _s.R_Salience, _s.R_Stability, _s.R_Alerting, _s.R_Curiosity, _s.R_Goal
        ];

        int gi = (int)grown;
        float totalOthers = 0f;
        for (int i = 0; i < refs.Length; i++)
            if (i != gi) totalOthers += refs[i];

        if (totalOthers <= 0.0001f) return;

        for (int i = 0; i < refs.Length; i++)
        {
            if (i == gi) continue;
            float share = refs[i] / totalOthers;
            refs[i] = Clamp01(refs[i] - amount * share);
        }

        _s.R_Reward = refs[0];
        _s.R_Salience = refs[1];
        _s.R_Stability = refs[2];
        _s.R_Alerting = refs[3];
        _s.R_Curiosity = refs[4];
        _s.R_Goal = refs[5];
    }

    private void EnforceReceptorBudget()
    {
        float sum = _s.R_Reward + _s.R_Salience + _s.R_Stability + _s.R_Alerting + _s.R_Curiosity + _s.R_Goal;
        float max = _cfg.ReceptorTotalMax;

        if (sum <= max) return;

        float scale = max / sum;

        _s.R_Reward *= scale;
        _s.R_Salience *= scale;
        _s.R_Stability *= scale;
        _s.R_Alerting *= scale;
        _s.R_Curiosity *= scale;
        _s.R_Goal *= scale;
    }

    private float ComputeLearningGate(in ModulatorVector m)
    {
        float prim = GetMod(m, _cfg.PrimaryLearningGate) * GetReceptor(_cfg.PrimaryLearningGate) * _cfg.GatePrimaryWeight;
        float sec = GetMod(m, _cfg.SecondaryLearningGate) * GetReceptor(_cfg.SecondaryLearningGate) * _cfg.GateSecondaryWeight;

        float gate = prim * (0.5f + 0.5f * sec);

        gate *= (1.0f - _cfg.StabilityBrakeWeight * m.Stability);

        return Clamp01(gate);
    }

    private float GetMod(in ModulatorVector m, ModulatorId id)
        => id switch
        {
            ModulatorId.Reward => m.Reward,
            ModulatorId.Salience => m.Salience,
            ModulatorId.Stability => m.Stability,
            ModulatorId.Alerting => m.Alerting,
            ModulatorId.Curiosity => m.Curiosity,
            ModulatorId.Goal => m.Goal,
            _ => 0f
        };

    private float GetReceptor(ModulatorId id)
        => id switch
        {
            ModulatorId.Reward => _s.R_Reward,
            ModulatorId.Salience => _s.R_Salience,
            ModulatorId.Stability => _s.R_Stability,
            ModulatorId.Alerting => _s.R_Alerting,
            ModulatorId.Curiosity => _s.R_Curiosity,
            ModulatorId.Goal => _s.R_Goal,
            _ => 0f
        };

    private static void Consider(
        ModulatorId id,
        float deficit,
        float receptor,
        float cap,
        float modNow,
        float threshold,
        ref (ModulatorId id, float score) best)
    {
        if (deficit <= threshold) return;
        if (cap <= 0.0001f) return;

        float nearCap = receptor / cap;
        if (nearCap < 0.85f) return;

        float score = deficit * (1.0f + 0.25f * modNow);
        if (score > best.score) best = (id, score);
    }

    private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
    private static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);
}