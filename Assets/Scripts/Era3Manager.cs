using System;
using System.Collections.Generic;
using UnityEngine;

/// Era 3 — The Commerce Engine.
///
/// Manages the civilization-level simulation layer: the e3_ auto event graph,
/// d3_ decision callbacks, trade engine (§4), crisis/independent events (§10),
/// resilience (§9), and NPC civ AI.
///
/// Decision events (d3_*) are registered in GeneCatalog as GeneDefinition entries
/// whose IsEligible checks PlayerCiv.Has(prereq). Apply lambdas call back into
/// this class via OnDecisionResolved() and the setter methods below.
public class Era3Manager : MonoBehaviour
{
    public static Era3Manager Instance { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    public bool IsActive => _active;
    private bool  _active;
    private float _elapsed;   // seconds since Era 3 started

    public CivilizationState PlayerCiv { get; private set; }
    public List<CivilizationState> NpcCivs { get; } = new List<CivilizationState>();
    private List<CivilizationState> _allCivs = new List<CivilizationState>();

    private AgentSpawner _spawner;

    // Event log for HUD.
    public readonly List<(float Time, string Msg)> EventLog = new List<(float, string)>();

    // ── Auto event definitions ────────────────────────────────────────────────

    private struct Era3AutoEvent
    {
        public string   Id;
        public string[] Prereqs;        // all must be in civ.AcquiredEvents
        public float    MinTime;
        public Func<CivilizationState, bool> ExtraGate;
        public Action<Era3Manager, CivilizationState> OnFire;
    }

    private List<Era3AutoEvent> _autoEvents;

    // ── Crisis timer ──────────────────────────────────────────────────────────
    private float _crisisTimer;
    private const float CrisisInterval = 30f;

    // ── Trade engine ──────────────────────────────────────────────────────────
    private float _tradeTimer;
    private const float TradeTickInterval = 3f;

    // ── §7 Tunable constants (all TUNABLE per formulae-spec §7) ──────────────

    // §1 structure investment
    private const float BuildRate  = 0.02f;   // per trade tick
    private const float DecayRate  = 0.005f;  // per trade tick when unmaintained

    // §1.5 source multipliers
    private const float CrossDomainPenalty  = 0.35f;
    private const float DependencyDiscount  = 0.80f;

    // §1.4 kinetic mobility factors
    private static readonly float[] MobilityFactor =
        { 1.0f, 0.3f, 0.9f };  // Individuated, Distributed, Collective

    // §2 resilience
    // channel_severity_weight[ch]: Coercive=1.0, Genetic=0.9, Economic=0.6, Info=0.5, Exist=0.4
    private static readonly float[] ChannelSeverityWeight = { 0.6f, 0.9f, 0.5f, 0.4f, 1.0f };
    private const float RecoveryRate      = 0.01f;   // per trade tick, passive
    private const float CollapseThreshold = 0.10f;
    private const float CrisisWindow      = 120f;    // seconds (≈4 crisis-roll cycles)
    private const int   CrisisStackNeeded = 2;

    // §2.3 behavioral fidelity / narrative plasticity (complementarity spec §2.3 — TUNABLE)
    // Effectiveness score applied inversely to crisis drain (higher = less drain taken).
    private const float NovelBonusK         = 0.20f;  // Individuated bonus on novel crises
    private const float NovelPenaltyK       = 0.20f;  // Dist/Collective penalty on novel crises
    private const float PrecedentedBonusK   = 0.20f;  // Dist/Collective bonus on precedented crises
    private const float PrecedentedPenaltyK = 0.15f;  // Individuated penalty on precedented crises

    // §3.1 exchange rate
    private const float Elasticity = 0.5f;
    private const float LambdaRS   = 0.15f;  // reward/sanction EMA smoothing

    // §3.2 trade health
    private const float LambdaTH  = 0.10f;
    private const int   NDrift    = 15;      // ticks below -0.5 → parasitism-drift eligible
    private const int   NCollapse = 30;      // ticks below -0.9 → collapse drain

    // §4.1 magnitude
    private const float Gamma    = 0.7f;    // diminishing-returns exponent
    private const float MMaxAll  = 1.0f;    // per-channel magnitude scale (same for all)
    private const float MBaseAll = 0.25f;   // card action base magnitude

    // §4.2 recipient modifier
    private const float DMin     = 0.15f;   // capability floor
    private const float KDiffuse = 0.25f;   // diffuse-ring cap
    private const float ThetaDiffuse = 0.10f; // min connection strength to feel diffuse

    // ── Crisis stacking: rolling log of (time, source) per civ ───────────────
    private readonly Dictionary<int, List<(float T, string Src)>> _crisisLog
        = new Dictionary<int, List<(float, string)>>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Init(AgentSpawner spawner)
    {
        _spawner = spawner;
        _active  = false;
        Debug.Log("[Era3Manager] Initialized — waiting for Era 3 start signal.");
    }

    public void BeginEra3()
    {
        if (_active) return;
        _active  = true;
        _elapsed = 0f;

        // Seed player civ from Era 2 record.
        var era2Rec = Era2Manager.Instance?.GetRecord(0);
        var playerArch = era2Rec?.Architecture ?? CognitiveArchitecture.Individuated;
        PlayerCiv = new CivilizationState
        {
            CommunityId  = 0,
            Name         = GenerateCivName(playerArch, 0),
            Architecture = playerArch,
            IsPlayer     = true,
            // Sub-track inheritance from Era 2 record (§4 content matrix gating).
            Subtrack      = era2Rec?.SubTrack       ?? IndividuatedSubTrack.Unresolved,
            CommMedium    = era2Rec?.CommMedium      ?? CommunicationMedium.Unset,
            SocialStructure = era2Rec?.SocialStructure ?? SocialStructureType.Unset,
            // Distributed connectivity/bandwidth seeded from Era 2 II score proxy.
            NetworkConnectivityTier = playerArch == CognitiveArchitecture.Distributed
                ? Mathf.Clamp(Mathf.FloorToInt((era2Rec?.II ?? 50f) / 40f), 0, 2) : 1,
            SignalBandwidthTier     = playerArch == CognitiveArchitecture.Distributed
                ? Mathf.Clamp(Mathf.FloorToInt((era2Rec?.II ?? 50f) / 55f), 0, 2) : 1,
            // Collective differentiation seeded from Era 2 threshold flags.
            CasteDiff = playerArch == CognitiveArchitecture.Collective
                && (era2Rec?.ThresholdLaborFormalized ?? false)
                ? CasteDifferentiation.Polymorphic : CasteDifferentiation.BasicSplit,
            RepMode = CasteDifferentiation.Monomorphic == CasteDifferentiation.Monomorphic
                ? ReproductiveMode.Polygyne : ReproductiveMode.Monogyne,  // default polygyne
        };
        PlayerCiv.InitNativeDomains();

        // Seed 2 NPC civs with architectures different from the player's.
        var npcArchs = new[] { CognitiveArchitecture.Distributed, CognitiveArchitecture.Collective };
        for (int i = 0; i < 2; i++)
        {
            var arch = (PlayerCiv.Architecture == npcArchs[i])
                     ? CognitiveArchitecture.Individuated
                     : npcArchs[i];
            var npc = new CivilizationState
            {
                CommunityId  = 10 + i,
                Name         = GenerateCivName(arch, 10 + i),
                Architecture = arch,
                IsPlayer     = false,
            };
            npc.InitNativeDomains();
            NpcCivs.Add(npc);
        }

        _allCivs.Clear();
        _allCivs.Add(PlayerCiv);
        _allCivs.AddRange(NpcCivs);

        AudioManager.Instance?.OnCivFounded();

        BuildAutoEventDefs();
        EraPostProcessManager.Instance?.OnEra3Begin();
        AudioManager.Instance?.OnEraShiftToEra3();

        LogEvent("Era 3 begins — The Commerce Engine.");
        Debug.Log($"[Era3Manager] Era 3 BEGINS. Player: {PlayerCiv.Name} ({PlayerCiv.Architecture}).");
    }

    void Update()
    {
        if (!_active) return;
        _elapsed += Time.deltaTime;

        // ── Auto event graph ──────────────────────────────────────────────────
        for (int e = 0; e < _autoEvents.Count; e++)
        {
            var ev = _autoEvents[e];
            if (PlayerCiv.Has(ev.Id)) continue;
            if (_elapsed < ev.MinTime) continue;
            if (!AllPrereqsMet(PlayerCiv, ev.Prereqs)) continue;
            if (ev.ExtraGate != null && !ev.ExtraGate(PlayerCiv)) continue;

            PlayerCiv.Acquire(ev.Id);
            ev.OnFire?.Invoke(this, PlayerCiv);
            LogEvent(FriendlyName(ev.Id));
            Debug.Log($"[Era3] Auto: {ev.Id}");
            FireAutoEventSfx(ev.Id);

            // NPC civs advance through the same event immediately (no time gates for them).
            foreach (var npc in NpcCivs)
            {
                if (!npc.Has(ev.Id))
                {
                    npc.Acquire(ev.Id);
                    ev.OnFire?.Invoke(this, npc);
                }
            }
        }

        // ── Trade engine tick ─────────────────────────────────────────────────
        _tradeTimer += Time.deltaTime;
        if (_tradeTimer >= TradeTickInterval)
        {
            _tradeTimer = 0f;
            TickTradeEngine();
        }

        // ── Crisis / independent events (§10) ────────────────────────────────
        _crisisTimer += Time.deltaTime;
        if (_crisisTimer >= CrisisInterval)
        {
            _crisisTimer = 0f;
            TriggerCrisisRoll();
        }

        // Passive recovery is now handled inside TickTradeEngine (per trade tick).
    }

    // ── Decision resolution callback ──────────────────────────────────────────
    // Called by GeneCatalog Apply lambdas after the player makes a d3_ choice.

    public void OnDecisionResolved(string decisionId, CivilizationState civ = null)
    {
        var target = civ ?? PlayerCiv;
        target.Acquire(decisionId);
        LogEvent(FriendlyName(decisionId));
        Debug.Log($"[Era3] Decision resolved: {decisionId}");
    }

    // ── Setter API (called from GeneCatalog Apply lambdas) ───────────────────

    public void SetTradePolicy(int communityId, float tariff, float openness)
    {
        var civ = GetCiv(communityId);
        if (civ == null) return;
        civ.ForeignOpenness  = Mathf.Clamp01(openness);
        if (tariff < 0.2f) civ.FormalTradeActive = true;
    }

    public void SetGovernment(int communityId, GovernmentType gov)
    {
        var civ = GetCiv(communityId);
        if (civ != null) civ.Government = gov;
    }

    public void SetKinship(int communityId, KinshipPolicy policy)
    {
        var civ = GetCiv(communityId);
        if (civ != null) civ.Kinship = policy;
    }

    public void SetIdeaPatronage(int communityId, IdeaPatronageType patronage)
    {
        var civ = GetCiv(communityId);
        if (civ == null) return;
        civ.IdeaPatronage = patronage;
        if (patronage == IdeaPatronageType.Religion
            && civ.Architecture == CognitiveArchitecture.Individuated)
        {
            civ.HasOrganizedReligion = true;
            civ.BeliefTier = Mathf.Max(civ.BeliefTier, 3);
        }
        else if (patronage == IdeaPatronageType.Science)
            civ.InvestInformation = Mathf.Min(civ.InvestInformation + 0.10f, 1f);
        else if (patronage == IdeaPatronageType.Military)
            civ.DomainKinetic = Mathf.Min(civ.DomainKinetic + 0.15f, 1f);
    }

    public void SetWarPath(int communityId)
    {
        var civ = GetCiv(communityId);
        if (civ == null) return;
        civ.Acquire("e3_warfare_organized");
        civ.InvestCoercive = Mathf.Min(civ.InvestCoercive + 0.15f, 1f);
        LogEvent("Organized warfare doctrine");
    }

    public void SetDiplomacyPath(int communityId)
    {
        var civ = GetCiv(communityId);
        if (civ == null) return;
        civ.Acquire("e3_diplomacy");
        civ.FormalAllianceActive = true;
        civ.ForeignOpenness = Mathf.Min(civ.ForeignOpenness + 0.20f, 1f);
        LogEvent("Diplomatic institutions form");
    }

    public void ApplyDomainInvestment(int communityId, float kinetic, float biochem, float info, float econ)
    {
        var civ = GetCiv(communityId);
        if (civ == null) return;
        civ.DomainKinetic       = Mathf.Clamp01(civ.DomainKinetic       + kinetic);
        civ.DomainBiochemical   = Mathf.Clamp01(civ.DomainBiochemical   + biochem);
        civ.DomainInformational = Mathf.Clamp01(civ.DomainInformational + info);
        civ.DomainEconomic      = Mathf.Clamp01(civ.DomainEconomic      + econ);
    }

    public void SetSectorAllocation(int communityId, float prod, float mil, float cult)
    {
        var civ = GetCiv(communityId);
        if (civ == null) return;
        civ.SectorProduction = prod; civ.SectorMilitary = mil; civ.SectorCulture = cult;
    }

    public void SetCasteAllocation(int communityId, float forager, float builder, float soldier)
    {
        var civ = GetCiv(communityId);
        if (civ == null) return;
        civ.CasteForager = forager; civ.CasteBuilder = builder; civ.CasteSoldier = soldier;
    }

    public void SetNetworkTopology(int communityId, bool centralized)
    {
        var civ = GetCiv(communityId);
        if (civ != null) civ.NetworkCentralized = centralized;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // §1 CAPABILITY FORMULAE
    // ══════════════════════════════════════════════════════════════════════════

    // §1.2 SubtrackModifier(civ, channel) — pure table lookup per architecture.
    // channel: 0=Economic, 1=Genetic/Bio, 2=Informational, 3=Existential, 4=Kinetic
    private static float SubtrackModifier(CivilizationState civ, int ch)
    {
        // Connectivity/scale proxies: tier maps to factor (isolated=0.4, local=1.0, basin=1.6)
        float connectivity = civ.NetworkConnectivityTier switch { 0 => 0.4f, 1 => 1.0f, _ => 1.6f };
        float signal       = civ.SignalBandwidthTier     switch { 0 => 0.5f, 1 => 1.0f, _ => 1.5f };
        // Resource affordability proxy: economic investment bracket
        float resource     = civ.InvestEconomic < 0.3f ? 0.6f : civ.InvestEconomic < 0.7f ? 1.0f : 1.4f;
        // Caste differentiation proxy
        float casteMod     = civ.CasteDiff switch
            { CasteDifferentiation.Monomorphic => 0.6f, CasteDifferentiation.Polymorphic => 1.4f, _ => 1.0f };
        // Colony scale: use network connectivity tier as proxy
        float colony       = connectivity;
        // Reproductive bottleneck: Monogyne reduces bio capability
        float repMod       = civ.RepMode == ReproductiveMode.Monogyne ? 0.7f : 1.0f;
        // Stigmergic bandwidth contribution
        float stigmergic   = 0.5f + civ.StigmergicBandwidth;

        // §2.2 (complementarity spec) — non-Individuated Existential tier gating.
        // Tier 1/2 ritual/attachment behaviors scale with complexity; Tier 3 stays hard-gated.
        float connFrac = connectivity / 1.6f;  // normalize [0.4,1.6] → [0.25,1]
        float collectiveComplexity = civ.CasteDiff switch
        {
            CasteDifferentiation.Monomorphic => 0f,
            CasteDifferentiation.Polymorphic => 1f,
            _                                => 0.5f,
        };
        float DistExist = (civ.BeliefTier <= 0 || civ.BeliefTier >= 3) ? 0f
            : civ.BeliefTier == 1 ? Mathf.Lerp(0.6f, 0.8f, connFrac)
            : Mathf.Lerp(0.5f, 0.7f, connFrac);
        float CollExist = (civ.BeliefTier <= 0 || civ.BeliefTier >= 3) ? 0f
            : civ.BeliefTier == 1 ? Mathf.Lerp(0.6f, 0.8f, collectiveComplexity)
            : Mathf.Lerp(0.5f, 0.7f, collectiveComplexity);

        switch (civ.Architecture)
        {
            case CognitiveArchitecture.Individuated:
                return ch switch
                {
                    0 /* Economic */    => civ.Subtrack == IndividuatedSubTrack.A2_SolitaryManipulative ? 0.7f : 1.0f,
                    1 /* Bio */         => civ.Subtrack == IndividuatedSubTrack.A1_SocialForaging       ? 1.2f : 1.0f,
                    2 /* Info */        => civ.CommMedium switch
                    {
                        CommunicationMedium.Chemical    => 0.8f,
                        CommunicationMedium.Bioelectric => 1.1f,
                        _                               => 1.0f,
                    },
                    3 /* Existential */ => civ.BeliefTier >= 3 ? 1.0f : 0.0f,  // Tier 3 hard gate §1.2
                    _ /* Kinetic */     => 1.0f,
                };
            case CognitiveArchitecture.Distributed:
                return ch switch
                {
                    0 /* Economic */    => connectivity * resource,
                    1 /* Bio */         => connectivity * resource,
                    2 /* Info */        => signal * connectivity,
                    3 /* Existential */ => DistExist,  // §2.2: Tier 1/2 available, Tier 3 hard-gated
                    _ /* Kinetic */     => MobilityFactor[1],
                };
            case CognitiveArchitecture.Collective:
                return ch switch
                {
                    0 /* Economic */    => casteMod * colony,
                    1 /* Bio */         => repMod   * colony,
                    2 /* Info */        => stigmergic * colony,
                    3 /* Existential */ => CollExist,  // §2.2: Tier 1/2 available, Tier 3 hard-gated
                    _ /* Kinetic */     => MobilityFactor[2],
                };
            default: return 1.0f;
        }
    }

    // §1.1 capability(civ, channel) = StructureInvestment × SubtrackModifier
    public static float Capability(CivilizationState civ, int ch)
        => civ.StructureInvest[ch] * SubtrackModifier(civ, ch);

    // §1.3 ritual_capability (tier-1/2 universal, bypasses tier-3 gate)
    private static float RitualCapability(CivilizationState civ)
        => civ.RitualInvestment * 0.5f;

    // §1.4 kinetic capability
    private static float KineticCapability(CivilizationState civ)
    {
        int archIdx = civ.Architecture switch
        {
            CognitiveArchitecture.Distributed => 1,
            CognitiveArchitecture.Collective  => 2,
            _                                 => 0,
        };
        return civ.StructureInvest[4] * MobilityFactor[archIdx];
    }

    // §1.5 effective capability with source multiplier
    public static float EffectiveCapability(CivilizationState civ, int ch,
        bool crossDomain = false, CivilizationState borrowFrom = null)
    {
        float cap = ch == 4 ? KineticCapability(civ) : Capability(civ, ch);
        if (borrowFrom != null) return Capability(borrowFrom, ch) * DependencyDiscount;
        return crossDomain ? cap * CrossDomainPenalty : cap;
    }

    // ── Tick structure investments (called each trade tick) ───────────────────
    private void TickStructureInvestments()
    {
        foreach (var civ in _allCivs)
        {
            float[] dialAlloc =
            {
                civ.InvestEconomic,
                civ.InvestBiological,
                civ.InvestInformation,
                civ.InvestReligion,
                civ.InvestCoercive,
            };
            for (int ch = 0; ch < 5; ch++)
            {
                civ.StructureInvest[ch] = Mathf.Clamp(
                    civ.StructureInvest[ch] + dialAlloc[ch] * BuildRate - DecayRate,
                    0f, 2f);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // §2 RESILIENCE — weighted crisis drain + stacking collapse
    // ══════════════════════════════════════════════════════════════════════════

    // ── Behavioral Fidelity / Narrative Plasticity (complementarity spec §2.3) ──

    // Returns [0,1] effectiveness score for Distributed/Collective civs.
    // Reuses existing complexity scalars — no new data sources.
    private static float ComputeBehavioralFidelity(CivilizationState civ)
    {
        if (civ.Architecture == CognitiveArchitecture.Individuated) return 0f;
        float complexity, frictionCost;
        if (civ.Architecture == CognitiveArchitecture.Distributed)
        {
            float conn = civ.NetworkConnectivityTier switch { 0 => 0.4f, 1 => 1.0f, _ => 1.6f };
            complexity    = conn / 1.6f;
            frictionCost  = (1f - civ.NetworkTopologySlider) * 0.5f;
        }
        else  // Collective
        {
            float casteMod = civ.CasteDiff switch
                { CasteDifferentiation.Monomorphic => 0.4f, CasteDifferentiation.Polymorphic => 1.4f, _ => 1.0f };
            complexity    = casteMod / 1.4f;
            frictionCost  = (1f - civ.CommandCentralization) * 0.5f;
        }
        const float ConsensusMechanism = 0.8f;  // default; no per-species flag yet
        return Mathf.Clamp01(complexity * ConsensusMechanism * (1f - frictionCost));
    }

    // Returns [0,1] narrative plasticity for Individuated civs only.
    // Mirrors Tier 3 belief capability — zero until organized religion exists.
    private static float ComputeNarrativePlasticity(CivilizationState civ)
    {
        if (civ.Architecture != CognitiveArchitecture.Individuated) return 0f;
        return civ.BeliefTier >= 3 ? Mathf.Clamp01(civ.StructureInvest[3] / 2f) : 0f;
    }

    private void UpdateFidelityStats()
    {
        foreach (var civ in _allCivs)
        {
            civ.BehavioralFidelity  = ComputeBehavioralFidelity(civ);
            civ.NarrativePlasticity = ComputeNarrativePlasticity(civ);
        }
    }

    // Effectiveness score applied inversely to drain (higher = less drain taken).
    // novel=true: Individuated handled better; Distributed/Collective handled worse.
    // novel=false (precedented): Distributed/Collective bonus; Individuated slight penalty.
    private static float CrisisResponseMultiplier(CivilizationState civ, bool novel)
    {
        float np = civ.NarrativePlasticity;
        float bf = civ.BehavioralFidelity;
        return novel
            ? (civ.Architecture == CognitiveArchitecture.Individuated
                ? 1f + np * NovelBonusK
                : 1f - bf * NovelPenaltyK)
            : (civ.Architecture == CognitiveArchitecture.Individuated
                ? 1f - np * PrecedentedPenaltyK
                : 1f + bf * PrecedentedBonusK);
    }

    // Drains resilience weighted by channel_severity_weight and logs crisis for
    // stacking-cascade eligibility check (§2.1).
    // channel: 0=Economic,1=Bio,2=Info,3=Exist,4=Coercive
    // novel: true for first-encounter crisis types (see complementarity spec §2.3).
    private void DrainWeighted(CivilizationState civ, string crisisSource, float rawAmount, int channel,
        bool novel = false)
    {
        // Apply crisis response modifier (complementarity spec §2.3): CRM is an
        // effectiveness score — higher means the civ handles it better (less drain).
        float crm = Mathf.Max(CrisisResponseMultiplier(civ, novel), 0.5f);
        float weighted = (rawAmount / crm) * ChannelSeverityWeight[channel];
        civ.DrainResilience(weighted);

        if (!_crisisLog.ContainsKey(civ.CommunityId))
            _crisisLog[civ.CommunityId] = new List<(float, string)>();
        _crisisLog[civ.CommunityId].Add((_elapsed, crisisSource));

        CheckCollapseEligibility(civ);
    }

    private void CheckCollapseEligibility(CivilizationState civ)
    {
        if (civ.HasCollapsed || civ.Resilience > CollapseThreshold) return;

        if (!_crisisLog.TryGetValue(civ.CommunityId, out var log)) return;

        // Prune entries outside the rolling window.
        log.RemoveAll(e => _elapsed - e.T > CrisisWindow);

        // Count distinct crisis sources in window.
        var distinct = new System.Collections.Generic.HashSet<string>();
        foreach (var (_, src) in log) distinct.Add(src);

        if (distinct.Count < CrisisStackNeeded) return;

        // §2.1 named civs: Decision Card, not immediate collapse.
        if (civ.IsPlayer)
        {
            if (!civ.Has("e3_collapse_imminent"))
            {
                civ.Acquire("e3_collapse_imminent");
                AudioManager.Instance?.OnCrisisWarning();
                LogEvent("⚠ COLLAPSE IMMINENT — crisis cascade");
                Debug.LogWarning("[Era3] Player collapse imminent.");
            }
        }
        else
        {
            civ.HasCollapsed = true;
            AudioManager.Instance?.OnCivCollapse(named: false);
            LogEvent($"{civ.Name} has collapsed.");
            Debug.Log($"[Era3] NPC civ {civ.Name} collapsed.");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // §3 TRADE ENGINE — spec-faithful exchange rate + trade health
    // ══════════════════════════════════════════════════════════════════════════

    public void EnsureTradeInit(CivilizationState a, int bId)
    {
        if (!a.ExchangeRate.ContainsKey(bId))  a.ExchangeRate[bId]  = 1f;
        if (!a.TradeHealth.ContainsKey(bId))   a.TradeHealth[bId]   = 0f;
        if (!a.RewardAccum.ContainsKey(bId))   a.RewardAccum[bId]   = 0f;
        if (!a.SanctionAccum.ContainsKey(bId)) a.SanctionAccum[bId] = 0f;
        if (!a.DriftTicks.ContainsKey(bId))    a.DriftTicks[bId]    = 0;
        if (!a.CollapseTicks.ContainsKey(bId)) a.CollapseTicks[bId] = 0;
    }

    // §3.1 fair_rate = base_rate(1.0) × (demand/supply)^elasticity
    private static float FairRate(CivilizationState a, CivilizationState b)
    {
        float demandSupply = (b.InvestEconomic * Mathf.Max(b.ForeignOpenness, 0.05f))
                           / Mathf.Max(a.InvestEconomic * Mathf.Max(a.ForeignOpenness, 0.05f), 0.01f);
        return Mathf.Pow(demandSupply, Elasticity);
    }

    // Returns best rate available to A from all partners excluding B (for partner_choice_pressure).
    private float BestAlternativeRate(CivilizationState a, int excludeId)
    {
        float best = 1f;
        foreach (var civ in _allCivs)
        {
            if (civ.CommunityId == a.CommunityId || civ.CommunityId == excludeId) continue;
            if (a.ExchangeRate.TryGetValue(civ.CommunityId, out float r)) best = Mathf.Max(best, r);
        }
        return best;
    }

    // ── Event definitions ─────────────────────────────────────────────────────

    private void BuildAutoEventDefs()
    {
        _autoEvents = new List<Era3AutoEvent>
        {
            // §4.6 — Exchange contact pre-dates agriculture and settlement.
            new Era3AutoEvent
            {
                Id = "e3_exchange_contact", MinTime = 5f, Prereqs = new string[0],
                OnFire = (mgr, civ) =>
                {
                    civ.ForeignOpenness = Mathf.Max(civ.ForeignOpenness, 0.35f);
                    civ.BeliefTier = Mathf.Max(civ.BeliefTier, 1);  // tier-1 ritual
                    if (civ.IsPlayer)
                    {
                        foreach (var npc in mgr.NpcCivs)
                        {
                            // §3.2 trade_health range is [-1, 1]; 0 = neutral at first contact.
                            mgr.EnsureTradeInit(civ, npc.CommunityId);
                            mgr.EnsureTradeInit(npc, civ.CommunityId);
                        }
                    }
                }
            },

            // Agriculture — seeded from Era 2 LLFP / heterotroph threshold.
            new Era3AutoEvent
            {
                Id = "e3_agriculture", MinTime = 15f, Prereqs = new string[0],
                OnFire = (mgr, civ) =>
                {
                    civ.InvestEconomic = Mathf.Min(civ.InvestEconomic + 0.10f, 1f);
                    civ.Stockpile += 0.1f;
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_permanent_settlement", MinTime = 25f,
                Prereqs = new[] { "e3_agriculture" },
                OnFire = (mgr, civ) =>
                {
                    civ.BeliefTier = Mathf.Max(civ.BeliefTier, 2);   // tier-2 attachment
                    civ.RitualInvestment = Mathf.Min(civ.RitualInvestment + 0.2f, 1f);
                }
            },

            // §4.6 OR-gate: trade_network can fire independently of surplus.
            new Era3AutoEvent
            {
                Id = "e3_trade_network", MinTime = 30f,
                Prereqs = new[] { "e3_exchange_contact" },
                OnFire = (mgr, civ) =>
                {
                    civ.InvestEconomic = Mathf.Min(civ.InvestEconomic + 0.15f, 1f);
                    civ.DomainEconomic = Mathf.Min(civ.DomainEconomic + 0.10f, 1f);
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_surplus_economy", MinTime = 40f,
                Prereqs = new[] { "e3_permanent_settlement" },
                OnFire = (mgr, civ) =>
                {
                    civ.Stockpile += 0.3f;
                    civ.FormalTradeActive = true;
                }
            },

            // Specialized economy: OR-gate §4.6
            new Era3AutoEvent
            {
                Id = "e3_specialized_economy", MinTime = 50f, Prereqs = new string[0],
                ExtraGate = civ => civ.Has("e3_trade_network") || civ.Has("e3_surplus_economy"),
                OnFire = (mgr, civ) =>
                {
                    civ.SectorProduction = Mathf.Min(civ.SectorProduction + 0.10f, 1f);
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_social_stratification", MinTime = 50f,
                Prereqs = new[] { "e3_surplus_economy" },
                OnFire = (mgr, civ) => { /* opens government + kinship decisions */ }
            },

            new Era3AutoEvent
            {
                Id = "e3_family_norms_emerge", MinTime = 55f,
                Prereqs = new[] { "e3_social_stratification" },
                OnFire = (mgr, civ) => { /* kinship decision opens */ }
            },

            new Era3AutoEvent
            {
                Id = "e3_chiefdom", MinTime = 65f,
                Prereqs = new[] { "e3_social_stratification" },
                OnFire = (mgr, civ) => { civ.Government = GovernmentType.Chiefdom; }
            },

            // Organized religion — Individuated only (§5, tier-3 requires theory-of-mind).
            new Era3AutoEvent
            {
                Id = "e3_religion_organized", MinTime = 70f,
                Prereqs = new[] { "e3_social_stratification" },
                ExtraGate = civ => civ.Architecture == CognitiveArchitecture.Individuated,
                OnFire = (mgr, civ) =>
                {
                    civ.BeliefTier = Mathf.Max(civ.BeliefTier, 3);
                    civ.HasOrganizedReligion = true;
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_writing", MinTime = 90f,
                Prereqs = new[] { "e3_chiefdom", "e3_surplus_economy" },
                OnFire = (mgr, civ) =>
                {
                    civ.InvestInformation   = Mathf.Min(civ.InvestInformation   + 0.10f, 1f);
                    civ.DomainInformational = Mathf.Min(civ.DomainInformational + 0.10f, 1f);
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_state_formation", MinTime = 100f,
                Prereqs = new[] { "e3_chiefdom", "e3_writing" },
                OnFire = (mgr, civ) =>
                {
                    civ.InvestCoercive = Mathf.Min(civ.InvestCoercive + 0.10f, 1f);
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_empire", MinTime = 120f,
                Prereqs = new[] { "e3_diplomacy" },
                OnFire = (mgr, civ) =>
                {
                    civ.Government    = GovernmentType.Empire;
                    civ.ForeignOpenness = Mathf.Min(civ.ForeignOpenness + 0.20f, 1f);
                    civ.RecoverResilience(0.10f);
                }
            },
        };
    }

    // ── Trade engine §3 ───────────────────────────────────────────────────────

    private void TickTradeEngine()
    {
        // §1 structure investments accumulate each trade tick.
        TickStructureInvestments();
        // Update BF/NP derived stats each trade tick (cheap recompute).
        UpdateFidelityStats();

        if (!PlayerCiv.Has("e3_exchange_contact")) goto stockpile;

        foreach (var npc in NpcCivs)
        {
            if (!npc.Has("e3_exchange_contact")) continue;
            TickTradePair(PlayerCiv, npc);
            TickTradePair(npc, PlayerCiv);  // symmetric — both directions updated
        }

        // §2 passive resilience recovery (only when no crisis drain this tick).
        // RecoveryRate per tick; scaled like structure invest (per trade tick).
        foreach (var civ in _allCivs)
            if (!civ.HasCollapsed)
                civ.RecoverResilience(RecoveryRate);

        stockpile:
        // §4.2 stockpile accumulation.
        foreach (var civ in _allCivs)
        {
            if (!civ.Has("e3_surplus_economy")) continue;
            float surplus = (civ.SectorProduction + civ.InvestEconomic * 0.5f) * 0.03f;
            civ.Stockpile = Mathf.Min(civ.Stockpile + surplus, 5f);
        }
    }

    // Ticks A's perspective on trading with B (§3.1, §3.2).
    // Call for each direction separately: TickTradePair(A,B) then TickTradePair(B,A).
    private void TickTradePair(CivilizationState a, CivilizationState b)
    {
        int bId = b.CommunityId;
        EnsureTradeInit(a, bId);

        // §3.1 fair_rate = base_rate(1) × (demand_B/supply_A)^elasticity
        float fairRate = FairRate(a, b);
        fairRate = Mathf.Max(fairRate, 0.01f);

        // partner_choice_pressure = best_alternative / current_rate_offered_by_B
        float bestAlt = BestAlternativeRate(a, bId);
        float currentOffered = Mathf.Max(b.ExchangeRate.TryGetValue(a.CommunityId, out float br) ? br : fairRate, 0.01f);
        float pcp = bestAlt / currentOffered;

        // Update reward/sanction EMA from current trade health (last tick's value).
        float prevHealth = a.TradeHealth[bId];
        if (prevHealth > 0f)
        {
            a.RewardAccum[bId]   = Mathf.Clamp(Mathf.Lerp(a.RewardAccum[bId],   prevHealth * 0.5f, LambdaRS), 0f, 0.5f);
            a.SanctionAccum[bId] = Mathf.Lerp(a.SanctionAccum[bId], 0f, LambdaRS * 0.5f);
        }
        else
        {
            a.SanctionAccum[bId] = Mathf.Clamp(Mathf.Lerp(a.SanctionAccum[bId], -prevHealth * 0.5f, LambdaRS), 0f, 0.5f);
            a.RewardAccum[bId]   = Mathf.Lerp(a.RewardAccum[bId], 0f, LambdaRS * 0.5f);
        }

        // §3.1 full exchange rate formula.
        float rate = fairRate
            * Mathf.Clamp(pcp, 0.5f, 2.0f)
            * (1f + a.RewardAccum[bId] - a.SanctionAccum[bId]);
        rate = Mathf.Clamp(rate, 0f, 3f);
        a.ExchangeRate[bId] = rate;

        // §3.2 favorability = clamp((rate/fair_rate) − 1, -1, 1)
        float favorability = Mathf.Clamp((rate / fairRate) - 1f, -1f, 1f);

        // §3.2 trade_health EMA
        a.TradeHealth[bId] = a.TradeHealth[bId] * (1f - LambdaTH) + LambdaTH * favorability;
        float health = a.TradeHealth[bId];

        // §3.2 drift threshold tracking.
        if (health < -0.5f)
        {
            a.DriftTicks[bId]++;
            if (health < -0.9f)
            {
                a.CollapseTicks[bId]++;
                // Sustained extreme parasitism drains resilience of disadvantaged party.
                if (a.CollapseTicks[bId] >= NCollapse)
                    DrainWeighted(a, "trade_parasitism", 0.004f, 0 /* Economic */);
            }
            else a.CollapseTicks[bId] = 0;

            if (a.DriftTicks[bId] == NDrift)  // fire once at threshold, not every tick
            {
                string key = $"e3_trade_parasitism_{bId}";
                if (!a.Has(key)) { a.Acquire(key); LogEvent($"Parasitism-drift — {b.Name}"); }
            }
        }
        else
        {
            a.DriftTicks[bId]    = 0;
            a.CollapseTicks[bId] = 0;
        }

        // §4.4 mutualism/parasitism effects.
        if (health >= 0.25f)
        {
            // Mutualism: both parties stockpile.
            a.Stockpile = Mathf.Min(a.Stockpile + 0.005f, 5f);
        }
        else if (health < -0.25f)
        {
            // Parasitism: disadvantaged party loses resilience slowly.
            DrainWeighted(a, "trade_parasitism_slow", 0.001f, 0 /* Economic */);
        }

        // §4.5 Arbitrage — high-info party exploits low-openness partner.
        if (a.ForeignOpenness < 0.25f && b.InvestInformation > b.InvestEconomic)
            b.Stockpile = Mathf.Min(b.Stockpile + 0.015f, 5f);
    }

    // ── Crisis / independent events §10 ───────────────────────────────────────

    private void TriggerCrisisRoll()
    {
        if (PlayerCiv.HasCollapsed) return;
        float roll = UnityEngine.Random.value;

        // §1 plague/pandemic — Bio channel (ch=1, weight=0.9). Severity modulated by architecture.
        if (roll < 0.12f)
        {
            float drain = 0.05f + UnityEngine.Random.Range(0f, 0.04f);
            if (PlayerCiv.Has("e3_state_formation")) drain *= 0.5f;
            if (PlayerCiv.Architecture == CognitiveArchitecture.Collective)  drain *= 1.3f;
            if (PlayerCiv.Architecture == CognitiveArchitecture.Distributed)
                drain *= Mathf.Lerp(1f, 0.4f, PlayerCiv.CompartmentInvest);
            // Public health investment (§4.2 content matrix) reduces severity.
            drain *= Mathf.Lerp(1f, 0.5f, PlayerCiv.PublicHealthInvest);
            DrainWeighted(PlayerCiv, "plague", drain, 1 /* Bio */, novel: true);
            PlayerCiv.AcquiredEvents.Remove("d3_plague_response");
            PlayerCiv.Acquire("e3_plague_active");
            LogEvent($"Plague outbreak  (weighted drain ≈{drain * ChannelSeverityWeight[1]:P0})");
        }
        // §1 famine — Economic channel (ch=0, weight=0.6), gated on stockpile.
        else if (roll < 0.22f && PlayerCiv.Has("e3_trade_network"))
        {
            if (PlayerCiv.Stockpile < 0.3f)
            {
                DrainWeighted(PlayerCiv, "famine", 0.08f, 0 /* Economic */);
                LogEvent("Famine — stockpile exhausted");
            }
            else
            {
                DrainWeighted(PlayerCiv, "trade_disruption", 0.04f, 0 /* Economic */);
                LogEvent("Trade route disrupted");
            }
        }
        // §1 climate shock — Economic channel; stockpile cushions. Novel: outside historical range.
        else if (roll < 0.30f)
        {
            float drain = 0.06f;
            if (PlayerCiv.Stockpile > 0.5f) { drain *= 0.5f; PlayerCiv.Stockpile -= 0.2f; }
            DrainWeighted(PlayerCiv, "climate_shock", drain, 0 /* Economic */, novel: true);
            LogEvent($"Climate shock  (stockpile cushioned: {PlayerCiv.Stockpile < 0.5f})");
        }
        // §1 succession crisis — Coercive channel (ch=4, weight=1.0), Individuated.
        else if (roll < 0.38f && PlayerCiv.Has("e3_chiefdom")
            && PlayerCiv.Architecture == CognitiveArchitecture.Individuated)
        {
            DrainWeighted(PlayerCiv, "succession_crisis", 0.02f, 4 /* Coercive */);
            PlayerCiv.AcquiredEvents.Remove("d3_succession_crisis");
            PlayerCiv.Acquire("e3_succession_active");
            LogEvent("Succession crisis — leadership unstable");
        }
        // §1 secession — Coercive channel, resilience-gated.
        else if (roll < 0.44f && PlayerCiv.Has("e3_state_formation")
            && PlayerCiv.Resilience < 0.5f)
        {
            DrainWeighted(PlayerCiv, "secession", 0.03f, 4 /* Coercive */);
            PlayerCiv.AcquiredEvents.Remove("d3_secession_crisis");
            PlayerCiv.Acquire("e3_secession_active");
            LogEvent("Fragmentation pressure — secession risk");
        }
        // §1 queen succession — Coercive, Collective monogyne.
        else if (roll < 0.50f
            && PlayerCiv.Architecture == CognitiveArchitecture.Collective
            && PlayerCiv.RepMode == ReproductiveMode.Monogyne)
        {
            DrainWeighted(PlayerCiv, "queen_succession", 0.04f, 4 /* Coercive */);
            PlayerCiv.AcquiredEvents.Remove("d3_queen_succession");
            PlayerCiv.Acquire("e3_succession_active");
            LogEvent("Queen succession crisis");
        }
        // §1 schism — Existential channel (ch=3, weight=0.4), Individuated. Novel: first doctrinal split.
        else if (roll < 0.55f
            && PlayerCiv.HasOrganizedReligion
            && PlayerCiv.Architecture == CognitiveArchitecture.Individuated)
        {
            DrainWeighted(PlayerCiv, "schism", 0.03f, 3 /* Existential */, novel: true);
            PlayerCiv.AcquiredEvents.Remove("d3_schism_response");
            PlayerCiv.Acquire("e3_schism_active");
            AudioManager.Instance?.OnSchism();
            LogEvent("Religious schism — doctrine split");
        }

        // §1 golden age — triggered by sustained high trade health.
        CheckGoldenAge();
    }

    private float _goldenAgeTimer = 0f;

    private void CheckGoldenAge()
    {
        if (PlayerCiv.Has("e3_golden_age_active") || !PlayerCiv.Has("e3_trade_network")) return;

        bool sustained = true;
        foreach (var npc in NpcCivs)
        {
            if (!PlayerCiv.TradeHealth.TryGetValue(npc.CommunityId, out float h) || h < 0.75f)
            { sustained = false; break; }
        }
        if (!sustained) { _goldenAgeTimer = 0f; return; }

        _goldenAgeTimer += CrisisInterval;
        if (_goldenAgeTimer >= CrisisInterval * 2f)
        {
            _goldenAgeTimer = 0f;
            PlayerCiv.AcquiredEvents.Remove("d3_golden_age_response");
            PlayerCiv.Acquire("e3_golden_age_active");
            PlayerCiv.RecoverResilience(0.05f);
            LogEvent("Golden Age — sustained mutualism flourishing");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool AllPrereqsMet(CivilizationState civ, string[] prereqs)
    {
        foreach (var p in prereqs)
            if (!civ.Has(p)) return false;
        return true;
    }

    public CivilizationState GetCiv(int communityId)
    {
        if (communityId == 0) return PlayerCiv;
        foreach (var npc in NpcCivs)
            if (npc.CommunityId == communityId) return npc;
        return null;
    }

    private void LogEvent(string msg)
    {
        EventLog.Add((_elapsed, msg));
        if (EventLog.Count > 24) EventLog.RemoveAt(0);
    }

    // Maps auto-event ids to SFX triggers. Only player-civ events fire audio.
    private static void FireAutoEventSfx(string eventId)
    {
        var am = AudioManager.Instance;
        if (am == null) return;
        switch (eventId)
        {
            case "e3_exchange_contact":  am.OnExchangeContact();  break;
            case "e3_trade_network":     am.OnTradeAgreement();   break;
            case "e3_religion_organized": am.OnReligionFounded(); break;
        }
    }

    // Simple deterministic civ name generator so we don't depend on external classes.
    private static readonly string[] _civPrefixes =
    {
        "Thal", "Ven", "Aur", "Mer", "Kol", "Sel", "Orn", "Dav", "Cal", "Ys",
        "Elu", "Brak", "Tor", "Zan", "Wyr", "Ixal", "Phas", "Glor", "Ryn", "Fael",
    };
    private static readonly string[] _civSuffixes =
    {
        "ian", "ori", "eth", "usk", "yal", "ect", "ari", "ond", "ek", "ash",
        "eum", "ath", "oon", "ix", "ene", "ara", "ior", "orn", "yx", "el",
    };
    private static string GenerateCivName(CognitiveArchitecture arch, int seed)
    {
        int s = seed + (int)arch * 7;
        string prefix = _civPrefixes[Mathf.Abs(s) % _civPrefixes.Length];
        string suffix = _civSuffixes[Mathf.Abs(s * 3 + 5) % _civSuffixes.Length];
        string archTag = arch switch
        {
            CognitiveArchitecture.Distributed => " Network",
            CognitiveArchitecture.Collective  => " Collective",
            _                                 => " Hegemony",
        };
        return prefix + suffix + archTag;
    }

    private static string FriendlyName(string id) => id switch
    {
        "e3_exchange_contact"      => "Exchange contact established",
        "e3_agriculture"           => "Agriculture emerges",
        "e3_permanent_settlement"  => "Permanent settlements form",
        "e3_surplus_economy"       => "Surplus economy develops",
        "e3_trade_network"         => "Trade network formalized",
        "e3_specialized_economy"   => "Craft / specialized economy",
        "e3_social_stratification" => "Social stratification",
        "e3_family_norms_emerge"   => "Family/kinship norms crystallize",
        "e3_chiefdom"              => "Chiefdom government",
        "e3_religion_organized"    => "Organized religion (tier 3)",
        "e3_writing"               => "Writing system",
        "e3_state_formation"       => "State formation",
        "e3_warfare_organized"     => "Organized warfare doctrine",
        "e3_diplomacy"             => "Diplomatic institutions",
        "e3_empire"                => "Empire — hegemonic expansion",
        "d3_trade_policy"          => "▸ Trade policy decided",
        "d3_caste_labor"           => "▸ Labor/caste allocation decided",
        "d3_kinship_policy"        => "▸ Kinship policy established",
        "d3_government_transition" => "▸ Government form chosen",
        "d3_idea_patronage"        => "▸ Idea patronage directed",
        "d3_war_or_diplomacy"      => "▸ War or diplomacy chosen",
        "d3_domain_investment"     => "▸ War domain investment set",
        "d3_bioweapon_option"      => "▸ Biochemical doctrine",
        "d3_large_initiative_1"    => "▸ Large initiative launched (I)",
        "d3_large_initiative_2"    => "▸ Large initiative launched (II)",
        _                          => id,
    };
}
