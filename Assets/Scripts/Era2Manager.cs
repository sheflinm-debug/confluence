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
    EusocialColonial,       // caste-like superorganism (ant/bee analog); always TerritorialityStrictness.StrictSite
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
    /// Majority-vote: is this community's population actually LAND-COLONIZED (AgentController.IsAquatic
    /// == false), not merely an aquatic organism standing on dry ground. Gates land-only content (Fire
    /// Mastery) — combustion doesn't work underwater, so an aquatic species mastering fire is a bug.
    public bool                IsTerrestrial;

    // End-of-Era-2 threshold flags (seed Era 3 Idea system). §8.2-8.5 are the SOCIAL/tool track
    // (→ Commerce Engine). §8.6-8.9 are the ECOLOGICAL-dominance tracks so non-social archetypes also
    // reach Era 3: colonial superorganisms, sessile terraformers, mobile blooms, apex predators.
    public bool ThresholdLLFP;
    public bool ThresholdFireMastery;
    public bool ThresholdCommunicationCodeified;
    public bool ThresholdLaborFormalized;
    public bool ThresholdCumulativeCulture;
    public bool ThresholdColonialEngineering;   // §8.6 — Distributed + colonial → LivingReef
    public bool ThresholdBiosphereTerraforming; // §8.7 — producer biomass footprint → Terraformer
    public bool ThresholdBloomDominance;        // §8.8 — motile producer bloom → Bloom Front
    public bool ThresholdTrophicApex;           // §8.9 — motile consumer dominance → Apex Predator
    /// True once at least one §8 threshold has been crossed — used by Era 2→3 gate (any ONE enters Era 3).
    public bool HasCrossedEndOfEra2Threshold =>
        ThresholdLLFP || ThresholdFireMastery || ThresholdCommunicationCodeified
        || ThresholdLaborFormalized || ThresholdCumulativeCulture
        || ThresholdColonialEngineering || ThresholdBiosphereTerraforming
        || ThresholdBloomDominance || ThresholdTrophicApex;
    /// How many of the nine §8 thresholds this community has latched.
    public int ThresholdCount =>
        (ThresholdLLFP ? 1 : 0) + (ThresholdFireMastery ? 1 : 0) + (ThresholdCumulativeCulture ? 1 : 0)
        + (ThresholdCommunicationCodeified ? 1 : 0) + (ThresholdLaborFormalized ? 1 : 0)
        + (ThresholdColonialEngineering ? 1 : 0) + (ThresholdBiosphereTerraforming ? 1 : 0)
        + (ThresholdBloomDominance ? 1 : 0) + (ThresholdTrophicApex ? 1 : 0);
    /// One-shot guard so the "first end-of-era threshold" stinger/log fires once, not every re-eval.
    public bool EndOfEraLogged;

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
        GameLog.LogGlobal("Era 2 — Age of Intelligence — BEGINS");
        GameLog.Snapshot(_spawner);
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

        // Continuously (re)evaluate the §8 thresholds through Era 2 rather than a single one-shot at
        // 100s. A trait or II level that matures later still latches its threshold, so the Era 2→3
        // achievement window is no longer a ~20-second knife-edge before the ceiling. Throttled to 1 Hz
        // (only a handful of community records); flags latch true and never un-cross (see evaluator).
        _thresholdEvalTimer -= Time.deltaTime;
        if (_thresholdEvalTimer <= 0f)
        {
            _thresholdEvalTimer = 1f;
            EvaluateEndOfEraThresholds();
        }

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

            // Fixed: these previously checked gene IDs ("SocialArchitectureFork",
            // "CommunicationMediumEmergence") that are never actually granted anywhere in the
            // codebase — Fork 1/Fork 2 resolve CommunityIntelligence.Architecture directly rather
            // than adding a gene string, and the real registered gene is "CommunicationMedium", not
            // "...Emergence". That meant hasFork/hasComm could never both be true, so Era 3 was only
            // ever reachable via the 120s hard ceiling fallback, never via genuine achievement.
            _records.TryGetValue(agent.communityId, out var rec);
            bool hasFork   = rec != null && rec.Architecture != CognitiveArchitecture.Unresolved;
            // Community-level, not per-agent: CommunicationMedium is applied to the community record
            // (ApplyCommunicationMedium sets rec.CommMedium). The old per-agent AcquiredGenes check
            // read whichever community-0 agent the loop happened to hit first — a newborn that hadn't
            // inherited the flag yet could spuriously fail the gate. Reading the record is robust.
            bool hasComm   = rec != null && rec.CommMedium != CommunicationMedium.Unset;
            bool hasThresh = rec != null && rec.HasCrossedEndOfEra2Threshold;

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

    /// DEBUG: instantly run Era 2's whole development for every community — resolve cognitive
    /// architecture (Fork 1 + Fork 2 + sub-track), push Intelligence Index past all thresholds, grant
    /// a communication medium, and latch the §8 end-of-era thresholds. Used by "Skip to Era 3" so the
    /// species arrive in Era 3 as fully-developed intelligences/civilizations instead of blank slates.
    public void DebugForceComplete()
    {
        if (!_era2Active) return;
        AssignFork1();
        TryFork2();
        foreach (var rec in _records.Values)
        {
            if (rec.Architecture == CognitiveArchitecture.Individuated
                && rec.SubTrack == IndividuatedSubTrack.Unresolved)
                AssignSubTrack(rec);
            rec.II = Mathf.Max(rec.II, 60f);                        // well past the highest threshold (II ≥ 10)
            if (rec.CommMedium == CommunicationMedium.Unset)
                ApplyCommunicationMedium(rec.communityId, CommunicationMedium.VocalAuditory);

            // Debug skip is for TESTING Era 3 content, which is gated on the §8 thresholds. Random Era 1
            // gene paths won't reliably produce every categorical trait a threshold needs (a solitary
            // drifter crosses none), so just GRANT most of them here — the point is to exercise Era 3,
            // not to realistically evolve into it. (The normal, un-skipped path still earns them for real.)
            rec.ThresholdLLFP = rec.ThresholdCumulativeCulture =
                rec.ThresholdCommunicationCodeified = rec.ThresholdLaborFormalized =
                rec.ThresholdColonialEngineering = rec.ThresholdBiosphereTerraforming =
                rec.ThresholdBloomDominance = rec.ThresholdTrophicApex = true;
            // Fire Mastery is the one exception: it's gated on the population being genuinely
            // LAND-COLONIZED (IsTerrestrial — combustion can't happen underwater), which the debug skip
            // must still respect. Force-granting it regardless would recreate the exact bug this fix
            // exists for (an aquatic species reporting Fire Mastery). Only latch it if actually earned.
            rec.ThresholdFireMastery |= rec.HasMotility && rec.IsTerrestrial
                                     && rec.Manipulation >= ManipulationLevel.Articulated;
            rec.EndOfEraLogged = true;
        }
        Debug.Log("[DebugSkip] Era 2 force-completed (architecture, II, comm medium, all thresholds) for all communities.");
    }

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

    // ── Fork 2 helpers ────────────────────────────────────────────────────────

    private int GetCommunitySize(int communityId)
    {
        if (_spawner == null) return 0;
        int count = 0;
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == communityId) count++;
        return count;
    }

    private float GetCommunityScarcity(int communityId)
    {
        if (_spawner == null) return 0f;
        float sum = 0f; int n = 0;
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == communityId) { sum += a.ResourceScarcity; n++; }
        return n > 0 ? sum / n : 0f;
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

            // Route A — Kin-selection / group forming (high sociality).
            bool routeA = rec.Sociality == SocialityBaseline.GroupForming
                          && Random.value < 0.35f;

            // Route B — Maternal coercion (high manipulation tier).
            bool routeB = rec.Manipulation >= ManipulationLevel.Articulated
                          && Random.value < 0.25f;

            float communityScarcity = GetCommunityScarcity(rec.communityId);
            float communitySize = GetCommunitySize(rec.communityId);

            // Route C — Resource-scarcity Collective: high local scarcity drives pooled foraging.
            // Reachable even at low sociality — economic pressure, not social predisposition.
            bool routeC = communityScarcity >= 0.55f && rec.Sociality >= SocialityBaseline.Aggregating
                          && Random.value < 0.20f;

            // Route D — Density-driven Distributed: overlapping home ranges force network coordination.
            // This is the only route that produces Distributed from Individuated (normally Fork 1
            // handles sessile → Distributed; this catches motile lineages that achieve high density).
            bool routeD = communitySize >= 8f && Random.value < 0.30f;

            rec.Fork2Fired = true;
            if (routeD)
            {
                rec.Architecture = CognitiveArchitecture.Distributed;
                Debug.Log($"[Era2] Community {rec.communityId} → Distributed (Fork 2, Route D — density={communitySize:F0}).");
            }
            else if (routeA || routeB || routeC)
            {
                rec.Architecture = CognitiveArchitecture.Collective;
                string route = routeA ? "A" : (routeB ? "B" : $"C scarcity={communityScarcity:F2}");
                Debug.Log($"[Era2] Community {rec.communityId} → Collective (Fork 2, Route {route}).");
            }
            else
            {
                // Stays Individuated — assign sub-track.
                AssignSubTrack(rec);
                Debug.Log($"[Era2] Community {rec.communityId} → Individuated/{rec.SubTrack} (Fork 2 resolved).");
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

    // Population (biomass/footprint proxy) needed for the §8.6-8.9 ecological-dominance thresholds.
    // TUNABLE — these are "this lineage dominates its niche" scales, not intelligence bars.
    private const int EcoColonyDominancePop  = 12; // §8.6 colonial superorganism
    private const int EcoBiomassDominancePop = 15; // §8.7/8.8 producer biomass / bloom
    private const int EcoApexDominancePop    = 10; // §8.9 apex predator (fewer individuals dominate)

    private float _thresholdEvalTimer;
    private void EvaluateEndOfEraThresholds()
    {
        foreach (var rec in _records.Values)
        {
            // Each flag LATCHES ( |= ): once a community has crossed a threshold it stays crossed,
            // even if II or a snapshot trait later dips. This is what makes continuous re-evaluation
            // safe — a threshold reached at any point in Era 2 counts, and it never un-crosses.

            // §8.2 LLFP (Low-Level Food Production): heterotroph/mixotroph consumer + can manipulate.
            rec.ThresholdLLFP |= (rec.EnergyStrategy == MetabolismType.Heterotrophic
                                 || rec.EnergyStrategy == MetabolismType.Mixotrophic)
                                && rec.Manipulation >= ManipulationLevel.Simple
                                && rec.II >= 5f;

            // §8.3 Fire/Heat Mastery: mobile + articulated manipulation + actually LAND-COLONIZED.
            // Combustion requires atmospheric oxygen contact — an aquatic species (however motile and
            // dexterous) cannot master fire underwater. This was the bug behind a sea species showing
            // "Fire Mastery": the threshold never checked medium at all.
            rec.ThresholdFireMastery |= rec.HasMotility && rec.IsTerrestrial
                                    && rec.Manipulation >= ManipulationLevel.Articulated
                                    && rec.II >= 8f;

            // §8.5b Cumulative Culture: sociality ≥ aggregating + enough II.
            rec.ThresholdCumulativeCulture |= rec.Sociality >= SocialityBaseline.Aggregating
                                           && rec.II >= 6f;

            // §8.4 Communication Codification: requires cumulative culture first (latched above).
            rec.ThresholdCommunicationCodeified |= rec.ThresholdCumulativeCulture && rec.II >= 10f;

            // §8.5 Labor Formalization: Distributed or Collective architecture + manipulation ≥ simple.
            rec.ThresholdLaborFormalized |= (rec.Architecture == CognitiveArchitecture.Distributed
                                         || rec.Architecture == CognitiveArchitecture.Collective)
                                         && rec.Manipulation >= ManipulationLevel.Simple
                                         && rec.II >= 4f;

            // ── §8.6-8.9 ECOLOGICAL-dominance tracks (population/biomass, NOT intelligence) ──────
            // These let non-social archetypes reach Era 3 as ecological powers. The dominance metric is
            // community size (a biomass/footprint proxy), gated by the defining archetype trait.
            int pop = CountCommunityMembers(rec.communityId);
            bool isProducer = rec.EnergyStrategy == MetabolismType.Chemosynthetic
                           || rec.EnergyStrategy == MetabolismType.Phototrophic;
            bool isConsumer = rec.EnergyStrategy == MetabolismType.Heterotrophic
                           || rec.EnergyStrategy == MetabolismType.Mixotrophic;

            // §8.6 Colonial Ecosystem Engineering: a Distributed, aggregating colony at scale.
            rec.ThresholdColonialEngineering |= rec.Architecture == CognitiveArchitecture.Distributed
                                             && rec.Sociality >= SocialityBaseline.Aggregating
                                             && pop >= EcoColonyDominancePop;

            // §8.7 Biosphere Terraforming: a producer whose biomass footprint reshapes the planet.
            rec.ThresholdBiosphereTerraforming |= isProducer && pop >= EcoBiomassDominancePop;

            // §8.8 Bloom Dominance: a MOBILE producer at bloom scale.
            rec.ThresholdBloomDominance |= isProducer && rec.HasMotility && pop >= EcoBiomassDominancePop;

            // §8.9 Trophic Apex: a MOBILE consumer that dominates the food web.
            rec.ThresholdTrophicApex |= isConsumer && rec.HasMotility && pop >= EcoApexDominancePop;

            // One-shot per community: the first-threshold log + player stinger fire once, not every
            // 1 Hz re-evaluation tick.
            if (!rec.EndOfEraLogged && rec.HasCrossedEndOfEra2Threshold)
            {
                rec.EndOfEraLogged = true;
                Debug.Log($"[Era2] Community {rec.communityId} crossed its first end-of-era threshold: "
                        + $"II={rec.II:F1}, {rec.ThresholdCount}/9. Arch={rec.Architecture}, Sub={rec.SubTrack}");
                if (rec.communityId == 0)
                    AudioManager.Instance?.OnEndOfEra2Threshold();
            }
        }
    }

    // ── Era 2→3 gate diagnostics (read by GameHUD) ───────────────────────────────
    // (Era2Elapsed is already exposed elsewhere in this class.)
    public static float Era3Ceiling => Era3CeilingSeconds;
    public bool Era3GateFired => _era3GateFired;

    /// Reports the player community's (id 0) progress toward the Era 2→3 achievement gate so the HUD
    /// can show exactly what's still missing. Returns false if there's no player record yet.
    public bool TryGetPlayerEra3Gate(out bool hasFork, out bool hasComm, out bool hasThresh, out int thresholdCount)
    {
        hasFork = hasComm = hasThresh = false; thresholdCount = 0;
        if (!_records.TryGetValue(0, out var rec) || rec == null) return false;
        hasFork      = rec.Architecture != CognitiveArchitecture.Unresolved;
        hasComm      = rec.CommMedium != CommunicationMedium.Unset;
        hasThresh    = rec.HasCrossedEndOfEra2Threshold;
        thresholdCount = rec.ThresholdCount;
        return true;
    }

    // ── Community bookkeeping ─────────────────────────────────────────────────

    private void SnapshotCommunityAttributes()
    {
        if (_spawner == null) return;
        // Build per-community attribute averages from the live population.
        var counts = new Dictionary<int, int>();
        var motile = new Dictionary<int, bool>();
        var terrestrial = new Dictionary<int, int>(); // count of NOT-aquatic members, for majority vote
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
            if (!agent.IsAquatic) terrestrial[cid] = terrestrial.GetValueOrDefault(cid) + 1;
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
            rec.IsTerrestrial  = terrestrial.GetValueOrDefault(cid) * 2 > n; // majority-vote: not just "standing on dry ground," genuinely land-colonized
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
        if (_spawner == null) return;

        // Ensure EVERY living community has a record. Previously records were created only at
        // the BeginEra2 boundary snapshot, so any community that speciated into existence during
        // Era 2 (or had zero live members at the exact boundary tick) permanently had no record.
        // With no record, GetRecord(cid) returns null and every Era 2 decision gene's
        // `GetRecord(cid)?.Field == Unset` eligibility test evaluates false — silently disabling
        // ALL Era 2 popups for that community. Re-snapshotting here keeps them alive.
        bool anyNew = false;
        var seen = new HashSet<int>();
        foreach (var agent in _spawner.ActiveAgents)
        {
            if (agent == null) continue;
            int cid = agent.communityId;
            if (!seen.Add(cid)) continue;
            if (!_records.ContainsKey(cid)) anyNew = true;
        }

        if (anyNew)
        {
            // Rebuild attribute averages (also creates missing records) then assign Fork 1
            // architecture to any freshly-created record so architecture-gated genes can fire.
            SnapshotCommunityAttributes();
            AssignFork1();
        }
        else
        {
            // Lightweight motility refresh for existing records.
            foreach (var agent in _spawner.ActiveAgents)
            {
                if (agent == null) continue;
                if (agent.HasMotility && _records.TryGetValue(agent.communityId, out var rec))
                    rec.HasMotility = true;
            }
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
            SocialStructureType.EusocialColonial    => 1.20f,  // superorganism collective problem-solving (real termite/bee-colony phenomenon)
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
            NeuralComplexityStage.HighlyCentralized        => 1.6f,
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
