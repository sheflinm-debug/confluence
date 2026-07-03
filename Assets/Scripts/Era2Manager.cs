using System.Collections.Generic;
using UnityEngine;

// ── Era 2 Player Decision Layer enums (§6) ───────────────────────────────────

/// §6.2 — Communication medium chosen by the player during Era 2.
public enum CommunicationMedium
{
    Unset,
    VocalAuditory,           // requires vocal apparatus; oral → recorded arc
    VisualGestural,          // requires vision trait > 10
    ChemicalPheromonal,      // always available; slower codification potential
    BioluminescentElectrical // requires specific xenobiology backbones or aquatic environment
}

/// §6.3 — Niche construction strategy.
public enum NicheConstructionOrientation
{
    Unset,
    ToolBased,              // object manufacture; synergizes with Manipulation
    EnvironmentModification,// dam/mound/nest niche construction
    SocialTransmissionOnly, // teaching-only; no physical artifacts
}

/// §6.5 — Social structure; feeds §5.1 Sociality multiplier and seeds Era 3 governance.
public enum SocialStructureType
{
    Unset,
    PairBonded,             // nuclear-unit; moderate II, Era 3 → structured governance
    MultiMemberTroop,       // group stability; high Sociality multiplier
    FissionFusion,          // flexible composition; highest variance, Era 3 → decentralized
    SolitaryTerritorial,    // low social overhead; penalizes group Sociality multiplier
}

// ── Cognitive Architecture ────────────────────────────────────────────────────

/// Assigned at Era 1→Era 2 boundary via Fork 1 (locomotion read) + Fork 2 (social event).
public enum CognitiveArchitecture
{
    Unresolved,   // not yet at Era 2
    Individuated, // mobile lineage; did NOT qualify for Collective at Fork 2
    Distributed,  // sessile lineage; routed straight from Fork 1
    Collective,   // mobile lineage that crossed Fork 2 (Route A or B)
}

/// Sub-track for Individuated architecture (§5.1).
public enum IndividuatedSubTrack
{
    Unresolved,
    A1_SocialForaging,          // social brain / corvid-cetacean style
    A2_SolitaryManipulative,    // cephalopod style; short-lived high individual learning
    A3_BulkBrain,               // high neuron count, rich social ceiling, tool plateau
}

// ── Per-community intelligence record ────────────────────────────────────────

public class CommunityIntelligence
{
    public int communityId;
    public CognitiveArchitecture Architecture = CognitiveArchitecture.Unresolved;
    public IndividuatedSubTrack SubTrack      = IndividuatedSubTrack.Unresolved;

    // Raw Intelligence Index (0+ open-ended). Updated every second.
    public float II;

    // Fork 2 eligibility flags
    public bool Fork2Eligible;
    public bool Fork2Fired;

    // Era-1 attribute snapshots (averaged across community at era boundary)
    public ManipulationLevel  Manipulation   = ManipulationLevel.None;
    public SocialityBaseline  Sociality      = SocialityBaseline.Solitary;
    public NeuralComplexityStage NeuralStage = NeuralComplexityStage.DiffuseSignaling;
    public MetabolismType     EnergyStrategy = MetabolismType.Chemosynthetic;
    public bool               HasMotility;

    // End-of-Era-2 threshold flags (seed Era 3 Idea system)
    public bool ThresholdLLFP;
    public bool ThresholdFireMastery;
    public bool ThresholdCommunicationCodeified;
    public bool ThresholdLaborFormalized;
    public bool ThresholdCumulativeCulture;
    /// True once at least one §8 threshold has been crossed — used by Era 2→3 gate.
    public bool HasCrossedEndOfEra2Threshold =>
        ThresholdLLFP || ThresholdFireMastery || ThresholdCommunicationCodeified
        || ThresholdLaborFormalized || ThresholdCumulativeCulture;

    // ── Era 2 Player Decision Layer (§6) ──────────────────────────────────────
    // Set by gene events fired during Era 2; default = Unset / neutral multiplier.

    /// §6.1 Cognitive Investment Strategy: A1/A2/A3 sub-track weighting.
    /// Multiplier applied on top of sub-track II formula. Default 1.0.
    public float CognitiveInvestmentMult = 1.0f;

    /// §6.2 Communication medium chosen by the player.
    public CommunicationMedium CommMedium = CommunicationMedium.Unset;

    /// §6.3 Niche construction orientation.
    public NicheConstructionOrientation NicheOrientation = NicheConstructionOrientation.Unset;

    /// §6.4 Metabolic allocation: brain investment weight.
    /// > 1.0 = brain-heavy (accelerates II, costs robustness); < 1.0 = somatic.
    public float MetabolicBrainWeight = 1.0f;

    /// §6.5 Social structure. Modifies effective Sociality multiplier.
    public SocialStructureType SocialStructure = SocialStructureType.Unset;
}

// ── Era2Manager ───────────────────────────────────────────────────────────────

/// Manages the Era 2 "Age of Intelligence" layer: Intelligence Index accumulation,
/// Cognitive Architecture Fork (§3–4), Player Decision Layer (§6), and End-of-Era-2
/// threshold evaluation (§8). Activates automatically once DeepTimeClock enters Era 2.
///
/// Per the spec (§0), eras run on a deterministic timetable. Era 2 here is modeled
/// as a post-Cambrian era that begins when EraManager.CurrentEra reaches 6 (one
/// beyond the current 0-5 Cambrian range). In practice this becomes active once
/// SimulationBootstrap wires it in after the Cambrian Explosion era.
public class Era2Manager : MonoBehaviour
{
    public static Era2Manager Instance { get; private set; }

    private AgentSpawner _spawner;
    private float _era2Elapsed;     // seconds since Era 2 started
    private float _updateTimer;     // throttle the community scan to once/sec
    private bool  _era2Active;

    // Intelligence records keyed by communityId.
    private readonly Dictionary<int, CommunityIntelligence> _records
        = new Dictionary<int, CommunityIntelligence>();

    // Fork 2 fires early-to-mid Era 2.
    [SerializeField] private float fork2WindowStart = 20f;  // seconds into Era 2
    [SerializeField] private float fork2WindowEnd   = 60f;

    // Base rate curve: II grows slowly at era start, faster mid-era.
    private float BaseRate(float t) => Mathf.Clamp01(t / 120f) * 2f; // 0→2 over 2 min

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Init(AgentSpawner spawner)
    {
        _spawner = spawner;
        _era2Active = false;
        _era2Elapsed = 0f;
        Debug.Log("[Era2Manager] Initialized — waiting for Era 2 start signal.");
    }

    /// Called by EraManager (or SimulationBootstrap) when the Cambrian Explosion
    /// era ends and the game transitions to Era 2.
    public void BeginEra2()
    {
        if (_era2Active) return;
        _era2Active  = true;
        _era2Elapsed = 0f;
        SnapshotCommunityAttributes();
        AssignFork1();

        // Sweep all live agents and silently auto-apply any Era 1 genes they missed.
        // This prevents prerequisite-chain breakage for communities that started late.
        if (_spawner != null)
        {
            foreach (var agent in _spawner.ActiveAgents)
            {
                if (agent != null)
                    GeneEvolutionManager.ForceApplyOutstandingEra1Events(agent);
            }
        }

        EraPostProcessManager.Instance?.OnEra2Begin();
        AudioManager.Instance?.OnEraShiftToEra2();

        Debug.Log("[Era2Manager] Era 2 — Age of Intelligence — BEGINS.");
    }

    void Update()
    {
        if (!_era2Active) return;
        _era2Elapsed += Time.deltaTime;

        _updateTimer -= Time.deltaTime;
        if (_updateTimer > 0f) return;
        _updateTimer = 1f; // update every second

        // Refresh community membership (populations change as organisms die/reproduce).
        RefreshCommunityRecords();

        // Fork 2 evaluation window.
        if (_era2Elapsed >= fork2WindowStart && _era2Elapsed <= fork2WindowEnd)
            TryFork2();

        // Update Intelligence Index for every community.
        foreach (var rec in _records.Values)
            UpdateII(rec);

        // End-of-Era-2 threshold check (near the end — after 90% of Era 2 runtime).
        // Placeholder: currently fires once at 100s into Era 2.
        if (_era2Elapsed >= 100f)
            EvaluateEndOfEraThresholds();

        // Era 2→3 biology gate (§4 environmental-pressure-triggers spec):
        // AND-gate: SocialArchitectureFork + at least one §8 threshold + CommunicationMedium.
        // 120s ceiling — same ratchet pattern as Era 1→2.
        if (_era2Elapsed >= 60f && !_era3GateFired)
            CheckEra2To3Gate();
    }

    private bool _era3GateFired;
    private const float Era3CeilingSeconds = 120f;

    private void CheckEra2To3Gate()
    {
        if (_spawner == null) return;
        bool allPlayersMeetGate = true;
        bool anyPlayerFound     = false;

        foreach (var agent in _spawner.ActiveAgents)
        {
            if (agent == null || agent.communityId != 0) continue; // player = community 0
            anyPlayerFound = true;

            bool hasFork   = agent.AcquiredGenes.Contains("SocialArchitectureFork");
            bool hasComm   = agent.AcquiredGenes.Contains("CommunicationMediumEmergence");
            bool hasThresh = _records.TryGetValue(agent.communityId, out var rec)
                             && rec.HasCrossedEndOfEra2Threshold;

            if (!hasFork || !hasComm || !hasThresh)
                allPlayersMeetGate = false;
            break;
        }

        bool ceiling = _era2Elapsed >= Era3CeilingSeconds;
        if (!anyPlayerFound) return;

        if (allPlayersMeetGate || ceiling)
        {
            _era3GateFired = true;
            Debug.Log($"[Era2Manager] Era 2→3 gate fired (ceiling={ceiling}, elapsed={_era2Elapsed:F0}s)");
            Era3Manager.Instance?.BeginEra3();
        }
    }

    // ── Fork 1: locomotion read (instant at Era 2 start) ─────────────────────

    private void AssignFork1()
    {
        foreach (var rec in _records.Values)
        {
            if (rec.Architecture != CognitiveArchitecture.Unresolved) continue;

            if (!rec.HasMotility)
            {
                // Sessile → Distributed, skip Fork 2.
                rec.Architecture = CognitiveArchitecture.Distributed;
                Debug.Log($"[Era2] Community {rec.communityId} → Distributed (sessile via Fork 1).");
            }
            else
            {
                // Temporarily Individuated; Fork 2 may override to Collective.
                rec.Architecture = CognitiveArchitecture.Individuated;
            }
        }
    }

    // ── Fork 2: social architecture fork (early-mid Era 2) ───────────────────

    private void TryFork2()
    {
        foreach (var rec in _records.Values)
        {
            if (rec.Architecture != CognitiveArchitecture.Individuated) continue;
            if (rec.Fork2Fired) continue;

            // Hard precondition: sociality must be aggregating or higher.
            if (rec.Sociality == SocialityBaseline.Solitary) continue;

            // Route A — Kin-selection / monogamy (approximated by group-forming + high sociality).
            bool routeA = rec.Sociality == SocialityBaseline.GroupForming
                          && Random.value < 0.35f;

            // Route B — Maternal coercion (approximated by high manipulation tier).
            bool routeB = rec.Manipulation >= ManipulationLevel.Articulated
                          && Random.value < 0.25f;

            rec.Fork2Fired = true;
            if (routeA || routeB)
            {
                rec.Architecture = CognitiveArchitecture.Collective;
                Debug.Log($"[Era2] Community {rec.communityId} → Collective (Fork 2, Route {(routeA ? "A" : "B")}).");
            }
            else
            {
                // Stays Individuated — assign sub-track.
                AssignSubTrack(rec);
                Debug.Log($"[Era2] Community {rec.communityId} → Individuated/{rec.SubTrack} (Fork 2 resolved, no Collective route).");
            }
        }
    }

    private void AssignSubTrack(CommunityIntelligence rec)
    {
        // A1: social-foraging — favours high sociality + volatility.
        // A2: solitary-manipulative — favours high manipulation + solitary/low sociality.
        // A3: bulk-brain — default high-neuron-count route.
        float a1 = (rec.Sociality == SocialityBaseline.GroupForming ? 1.5f : 0.5f);
        float a2 = (rec.Manipulation >= ManipulationLevel.Articulated ? 1.4f : 0.3f)
                   * (rec.Sociality == SocialityBaseline.Solitary ? 1.5f : 0.7f);
        float a3 = (int)rec.NeuralStage >= 3 ? 1.2f : 0.5f;
        float total = a1 + a2 + a3;
        float roll = Random.value * total;
        if      (roll < a1)       rec.SubTrack = IndividuatedSubTrack.A1_SocialForaging;
        else if (roll < a1 + a2)  rec.SubTrack = IndividuatedSubTrack.A2_SolitaryManipulative;
        else                       rec.SubTrack = IndividuatedSubTrack.A3_BulkBrain;
    }

    // ── Intelligence Index update ─────────────────────────────────────────────

    private void UpdateII(CommunityIntelligence rec)
    {
        float base_ = BaseRate(_era2Elapsed);
        float ii = 0f;

        switch (rec.Architecture)
        {
            case CognitiveArchitecture.Individuated:
            case CognitiveArchitecture.Collective:
                ii = base_
                     * NeuralSubstrateMultiplier(rec)
                     * ManipulationMultiplier(rec.Manipulation)
                     * SocialityMultiplier(rec)           // uses SocialStructure override
                     * EnergyStrategyMultiplier(rec.EnergyStrategy)
                     * rec.MetabolicBrainWeight           // §6.4
                     * rec.CognitiveInvestmentMult        // §6.1
                     * NicheOrientationMultiplier(rec)    // §6.3
                     * Random.Range(0.9f, 1.1f);
                break;

            case CognitiveArchitecture.Distributed:
                // Distributed formula (§5.2): network connectivity + colony scale proxies.
                float networkConn   = rec.Sociality == SocialityBaseline.GroupForming ? 1.6f
                                    : rec.Sociality == SocialityBaseline.Aggregating  ? 1.0f : 0.4f;
                float signalBW      = (int)rec.NeuralStage >= 2 ? 1.5f
                                    : (int)rec.NeuralStage >= 1 ? 1.1f : 0.5f;
                int communitySize   = CountCommunityMembers(rec.communityId);
                float colonyScale   = communitySize >= 50 ? 1.8f : communitySize >= 20 ? 1.2f : 0.6f;
                ii = base_ * networkConn * signalBW * colonyScale * Random.Range(0.9f, 1.1f);
                break;
        }

        // II accumulates; we clamp it to 0 so it can't go negative.
        rec.II = Mathf.Max(rec.II + ii * Time.deltaTime, 0f);
    }

    // ── End-of-Era-2 thresholds (§8) ─────────────────────────────────────────

    private bool _thresholdsEvaluated;
    private void EvaluateEndOfEraThresholds()
    {
        if (_thresholdsEvaluated) return;
        _thresholdsEvaluated = true;

        foreach (var rec in _records.Values)
        {
            // §8.2 LLFP: heterotrophic/mixotrophic + not tool-only.
            rec.ThresholdLLFP = (rec.EnergyStrategy == MetabolismType.Heterotrophic
                                 || rec.EnergyStrategy == MetabolismType.Mixotrophic)
                                && rec.Manipulation >= ManipulationLevel.Simple
                                && rec.II >= 5f;

            // §8.3 Fire/Heat Mastery: manipulation ≥ articulated + mobile.
            rec.ThresholdFireMastery = rec.HasMotility
                                    && rec.Manipulation >= ManipulationLevel.Articulated
                                    && rec.II >= 8f;

            // §8.5b Cumulative Culture: sociality ≥ aggregating + enough II.
            rec.ThresholdCumulativeCulture = rec.Sociality >= SocialityBaseline.Aggregating
                                           && rec.II >= 6f;

            // §8.4 Communication Codification: requires cumulative culture first.
            rec.ThresholdCommunicationCodeified = rec.ThresholdCumulativeCulture && rec.II >= 10f;

            // §8.5 Labor Formalization: Distributed or Collective + manipulation ≥ simple.
            rec.ThresholdLaborFormalized = (rec.Architecture == CognitiveArchitecture.Distributed
                                         || rec.Architecture == CognitiveArchitecture.Collective)
                                         && rec.Manipulation >= ManipulationLevel.Simple
                                         && rec.II >= 4f;

            int flags = (rec.ThresholdLLFP ? 1 : 0) + (rec.ThresholdFireMastery ? 1 : 0)
                      + (rec.ThresholdCumulativeCulture ? 1 : 0)
                      + (rec.ThresholdCommunicationCodeified ? 1 : 0)
                      + (rec.ThresholdLaborFormalized ? 1 : 0);

            Debug.Log($"[Era2] Community {rec.communityId} end-of-era: II={rec.II:F1}, {flags}/5 thresholds crossed. "
                    + $"Arch={rec.Architecture}, Sub={rec.SubTrack}");

            // Player civ crossing end-of-era is the biggest narrative beat — distinct stinger.
            if (rec.communityId == 0 && rec.HasCrossedEndOfEra2Threshold)
                AudioManager.Instance?.OnEndOfEra2Threshold();
        }
    }

    // ── Community bookkeeping ─────────────────────────────────────────────────

    private void SnapshotCommunityAttributes()
    {
        if (_spawner == null) return;
        // Build per-community attribute averages from the live population.
        var counts = new Dictionary<int, int>();
        var motile = new Dictionary<int, bool>();
        var manip  = new Dictionary<int, float>();
        var social = new Dictionary<int, float>();
        var neural = new Dictionary<int, float>();
        var metab  = new Dictionary<int, Dictionary<MetabolismType, int>>();

        foreach (var agent in _spawner.ActiveAgents)
        {
            if (agent == null) continue;
            int cid = agent.communityId;

            counts[cid] = counts.GetValueOrDefault(cid) + 1;
            if (agent.HasMotility) motile[cid] = true;
            manip[cid]  = manip.GetValueOrDefault(cid) + (int)agent.Manipulation;
            social[cid] = social.GetValueOrDefault(cid) + (int)agent.Sociality;
            neural[cid] = neural.GetValueOrDefault(cid) + (int)agent.NeuralComplexity;

            if (!metab.ContainsKey(cid)) metab[cid] = new Dictionary<MetabolismType, int>();
            var mdict = metab[cid];
            mdict[agent.Metabolism] = mdict.GetValueOrDefault(agent.Metabolism) + 1;
        }

        foreach (var cid in counts.Keys)
        {
            if (!_records.TryGetValue(cid, out var rec))
            {
                rec = new CommunityIntelligence { communityId = cid };
                _records[cid] = rec;
            }

            int n = counts[cid];
            rec.HasMotility    = motile.GetValueOrDefault(cid);
            rec.Manipulation   = (ManipulationLevel)Mathf.RoundToInt(manip.GetValueOrDefault(cid) / n);
            rec.Sociality      = (SocialityBaseline)Mathf.RoundToInt(social.GetValueOrDefault(cid) / n);
            rec.NeuralStage    = (NeuralComplexityStage)Mathf.RoundToInt(neural.GetValueOrDefault(cid) / n);

            // Dominant metabolism = plurality vote.
            if (metab.TryGetValue(cid, out var mdict))
            {
                MetabolismType dom = MetabolismType.Chemosynthetic;
                int domCount = 0;
                foreach (var kv in mdict)
                    if (kv.Value > domCount) { dom = kv.Key; domCount = kv.Value; }
                rec.EnergyStrategy = dom;
            }
        }
    }

    private void RefreshCommunityRecords()
    {
        // Lightweight refresh: just update motility and manipulation from live agents.
        // Full snapshot only happens at era boundary.
        if (_spawner == null) return;
        var motile = new Dictionary<int, bool>();
        foreach (var agent in _spawner.ActiveAgents)
        {
            if (agent == null) continue;
            if (agent.HasMotility) motile[agent.communityId] = true;
        }
        foreach (var kv in motile)
        {
            if (_records.TryGetValue(kv.Key, out var rec))
                rec.HasMotility = kv.Value;
        }
    }

    private int CountCommunityMembers(int cid)
    {
        if (_spawner == null) return 0;
        int count = 0;
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == cid) count++;
        return count;
    }

    // ── Multiplier helpers ────────────────────────────────────────────────────

    private static float ManipulationMultiplier(ManipulationLevel m) => m switch
    {
        ManipulationLevel.None        => 0.3f,
        ManipulationLevel.Simple      => 0.8f,
        ManipulationLevel.Articulated => 1.4f,
        ManipulationLevel.Dexterous   => 2.0f,
        _                             => 0.3f,
    };

    private static float SocialityMultiplier(SocialityBaseline s) => s switch
    {
        SocialityBaseline.Solitary     => 0.6f,
        SocialityBaseline.Aggregating  => 1.0f,
        SocialityBaseline.GroupForming => 1.6f,
        _                              => 0.6f,
    };

    // §6.5 — SocialStructure overrides / shades the base Sociality multiplier.
    private static float SocialityMultiplier(CommunityIntelligence rec)
    {
        float base_ = SocialityMultiplier(rec.Sociality);
        float adj = rec.SocialStructure switch
        {
            SocialStructureType.MultiMemberTroop   => 1.15f,  // reinforces group dynamics
            SocialStructureType.FissionFusion       => 1.10f,  // flexible → high variance, mean boost
            SocialStructureType.PairBonded          => 1.00f,  // neutral
            SocialStructureType.SolitaryTerritorial => 0.80f,  // discounts group-brain effect
            _                                       => 1.00f,
        };
        return base_ * adj;
    }

    // §6.3 — Niche construction orientation multiplier on II.
    private static float NicheOrientationMultiplier(CommunityIntelligence rec) =>
        rec.NicheOrientation switch
        {
            NicheConstructionOrientation.ToolBased              => rec.Manipulation >= ManipulationLevel.Articulated ? 1.25f : 1.05f,
            NicheConstructionOrientation.EnvironmentModification => 1.10f,
            NicheConstructionOrientation.SocialTransmissionOnly  => rec.Sociality >= SocialityBaseline.GroupForming ? 1.15f : 0.90f,
            _                                                    => 1.00f,
        };

    private static float EnergyStrategyMultiplier(MetabolismType m) => m switch
    {
        MetabolismType.Chemosynthetic => 0.5f,
        MetabolismType.Phototrophic   => 0.4f,
        MetabolismType.Mixotrophic    => 0.9f,
        MetabolismType.Heterotrophic  => 1.3f,
        _                             => 0.5f,
    };

    private static float NeuralSubstrateMultiplier(CommunityIntelligence rec)
    {
        float stage = rec.NeuralStage switch
        {
            NeuralComplexityStage.DiffuseSignaling        => 0.4f,
            NeuralComplexityStage.NerveNet                => 0.4f,
            NeuralComplexityStage.NerveCord               => 0.8f,
            NeuralComplexityStage.GanglionicCephalization => 1.2f,
            _                                             => 0.4f,
        };
        return stage;
    }

    // ── Public setters for Player Decision Layer (called from GeneCatalog gene events) ─

    public void ApplyCognitiveInvestment(int communityId, IndividuatedSubTrack preferredTrack, float mult)
    {
        if (!_records.TryGetValue(communityId, out var rec)) return;
        // Override the sub-track if the architecture supports it.
        if (rec.Architecture == CognitiveArchitecture.Individuated)
            rec.SubTrack = preferredTrack;
        rec.CognitiveInvestmentMult = mult;
    }

    public void ApplyCommunicationMedium(int communityId, CommunicationMedium medium)
    {
        if (_records.TryGetValue(communityId, out var rec))
            rec.CommMedium = medium;
    }

    public void ApplyNicheOrientation(int communityId, NicheConstructionOrientation orient)
    {
        if (_records.TryGetValue(communityId, out var rec))
            rec.NicheOrientation = orient;
    }

    public void ApplyMetabolicAllocation(int communityId, float brainWeight)
    {
        if (_records.TryGetValue(communityId, out var rec))
            rec.MetabolicBrainWeight = brainWeight;
    }

    public void ApplySocialStructure(int communityId, SocialStructureType structure)
    {
        if (_records.TryGetValue(communityId, out var rec))
            rec.SocialStructure = structure;
    }

    // ── Public read accessors (for HUD) ─────────────────────────────────────

    public bool IsActive => _era2Active;
    public float Era2Elapsed => _era2Elapsed;

    public CommunityIntelligence GetRecord(int communityId)
    {
        _records.TryGetValue(communityId, out var rec);
        return rec;
    }

    public IEnumerable<CommunityIntelligence> AllRecords => _records.Values;
}
