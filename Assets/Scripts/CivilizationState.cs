using System.Collections.Generic;
using UnityEngine;

/// Per-civilization state for Era 3 — The Commerce Engine (spec §0–§9).
/// One instance per tracked community: player + NPC civs.
public class CivilizationState
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public int    CommunityId;
    public string Name;
    public CognitiveArchitecture Architecture;
    public bool   IsPlayer;

    // ── Event graph ───────────────────────────────────────────────────────────
    // Both e3_ auto events and d3_ decision resolutions land here.
    public HashSet<string> AcquiredEvents = new HashSet<string>();
    public bool Has(string ev)    => AcquiredEvents.Contains(ev);
    public void Acquire(string ev) => AcquiredEvents.Add(ev);

    // ── Five channel investments §0 (each 0–1, independent) ──────────────────
    public float InvestEconomic    = 0.20f;
    public float InvestBiological  = 0.10f;
    public float InvestInformation = 0.20f;
    public float InvestReligion    = 0.10f;
    public float InvestCoercive    = 0.20f;

    // ── Policy axes §2, §3 ────────────────────────────────────────────────────
    public float DomesticOpenness = 0.5f;   // 0 = controlled ↔ 1 = open
    public float ForeignOpenness  = 0.5f;   // 0 = isolationist ↔ 1 = open trade
    public bool  FormalTradeActive;
    public bool  FormalAllianceActive;

    // ── Government §1.1 politics row ─────────────────────────────────────────
    public GovernmentType Government = GovernmentType.Chiefdom;

    // ── War domain coverage §7.3 (native + cross-domain investment + borrowed) ─
    public float DomainKinetic;
    public float DomainBiochemical;
    public float DomainInformational;
    public float DomainEconomic;

    // ── Capability model §1 ───────────────────────────────────────────────────
    // Per-channel structure investment, bounded [0, 2].
    // Index: 0=Economic, 1=Genetic/Bio, 2=Informational, 3=Existential, 4=Kinetic
    public float[] StructureInvest = new float[5];

    // ── Trade engine §3/§4 ────────────────────────────────────────────────────
    // exchange_rate: computed each tick via §3.1 formula
    // trade_health:  EMA of favorability, range [-1, 1] (0 = neutral)
    public Dictionary<int, float> ExchangeRate  = new Dictionary<int, float>();
    public Dictionary<int, float> TradeHealth   = new Dictionary<int, float>();
    // Reward / sanction accumulators for §3.1 formula, bounded [0, 0.5] each.
    public Dictionary<int, float> RewardAccum   = new Dictionary<int, float>();
    public Dictionary<int, float> SanctionAccum = new Dictionary<int, float>();
    // Drift threshold tracking: ticks spent below -0.5 / -0.9 respectively.
    public Dictionary<int, int> DriftTicks    = new Dictionary<int, int>();
    public Dictionary<int, int> CollapseTicks = new Dictionary<int, int>();

    public float Stockpile = 0f;   // §4.2 warehousing surplus

    public TradeHealthLabel GetTradeLabel(int partnerId)
    {
        if (!TradeHealth.TryGetValue(partnerId, out float h)) return TradeHealthLabel.None;
        // trade_health range is [-1, 1]; remap thresholds accordingly.
        return h >= 0.25f  ? TradeHealthLabel.Mutualism
             : h >= -0.25f ? TradeHealthLabel.Neutral
                           : TradeHealthLabel.Parasitism;
    }

    // ── Belief §5 ─────────────────────────────────────────────────────────────
    public int   BeliefTier           = 0;   // 0=none, 1=ritual, 2=attachment, 3=cosmological
    public float RitualInvestment     = 0f;
    public bool  HasOrganizedReligion = false;

    // ── Informational channel §6 ──────────────────────────────────────────────
    public float DisinfoCapability   = 0f;   // Distributed civs start higher
    public float DetectionCapability = 0f;

    // ── Resilience §9 ────────────────────────────────────────────────────────
    public float Resilience   = 1f;   // 0–1; collapse at 0
    public bool  HasCollapsed = false;

    // ── Behavioral Fidelity / Narrative Plasticity (complementarity spec §2.3) ─
    // BehavioralFidelity: Distributed/Collective only — coordination convergence
    // speed and execution consistency. Updated each trade tick by Era3Manager. [0,1].
    public float BehavioralFidelity  = 0f;
    // NarrativePlasticity: Individuated only — mirrors Tier 3 belief capability;
    // drives creative adaptation advantage on novel crises. [0,1].
    public float NarrativePlasticity = 0f;

    public void DrainResilience(float amount)
    {
        if (HasCollapsed) return;
        Resilience = Mathf.Clamp01(Resilience - amount);
        if (Resilience <= 0f) HasCollapsed = true;
    }

    public void RecoverResilience(float amount)
    {
        if (!HasCollapsed) Resilience = Mathf.Clamp01(Resilience + amount);
    }

    // ── Structures §8 ────────────────────────────────────────────────────────
    public HashSet<string> BuiltStructures = new HashSet<string>();

    // ── Architecture-specific allocation §1.1 economic row ───────────────────
    // Individuated: sector allocation
    public float SectorProduction = 0.4f;
    public float SectorMilitary   = 0.3f;
    public float SectorCulture    = 0.3f;
    // Distributed: network topology
    public bool NetworkCentralized = false;
    // Collective: caste ratios
    public float CasteForager = 0.5f;
    public float CasteBuilder = 0.3f;
    public float CasteSoldier = 0.2f;

    // ── Kinship / family policy §2 ────────────────────────────────────────────
    public KinshipPolicy Kinship = KinshipPolicy.Unset;

    // ── Idea patronage §5/§6 ─────────────────────────────────────────────────
    public IdeaPatronageType IdeaPatronage = IdeaPatronageType.Unset;

    // ── Era 2 sub-track inheritance (§4 content matrix gating) ───────────────
    // Seeded from CommunityIntelligence in Era3Manager.BeginEra3().

    // Individuated: A1/A2/A3 sub-track from CognitiveInvestmentStrategy gene.
    public IndividuatedSubTrack Subtrack = IndividuatedSubTrack.Unresolved;

    // Communication medium (gates Informational Idea card types).
    public CommunicationMedium CommMedium = CommunicationMedium.Unset;

    // Social structure from Era 2 (gates government-type card options in Coercive).
    public SocialStructureType SocialStructure = SocialStructureType.Unset;

    // Distributed-specific sub-track proxies (§4 col 2 row gating).
    // Tier 0 = isolated / patch-only, 1 = local network, 2 = basin-wide.
    public int NetworkConnectivityTier = 1;
    public int SignalBandwidthTier     = 1;

    // Collective-specific sub-track proxies (§4 col 3 row gating).
    public CasteDifferentiation CasteDiff   = CasteDifferentiation.BasicSplit;
    public ReproductiveMode     RepMode     = ReproductiveMode.Polygyne;
    public DecisionVelocity     DecVelocity = DecisionVelocity.Moderate;

    // ── Per-tab dials added by §4 content matrix ─────────────────────────────

    // Economic — additional dials.
    public float TariffRate        = 0.30f;  // Individuated tariff
    public float ExchangePosture   = 0.50f;  // Distributed: 0=sanction ↔ 1=reward
    public float CasteTrader       = 0.05f;  // Collective trade caste (gated by CasteDiff)
    public float StockpileTarget   = 0.50f;  // Collective biomass stockpile target

    // Genetic/Bio — additional dials.
    public float PublicHealthInvest       = 0.10f;  // Individuated
    public float ParentalInvestment       = 0.30f;  // Individuated A1 only
    public float GraftCompatThreshold     = 0.50f;  // Distributed: 0=tight ↔ 1=permissive
    public float CompartmentInvest        = 0.10f;  // Distributed CODIT
    public float ReproductiveSuppressRatio = 0.90f; // Collective: fraction of workers suppressed
    public float ImmuneCasteInvest        = 0.10f;  // Collective

    // Informational — additional dials.
    public float CensorshipLevel          = 0.30f;  // Individuated
    public float CommInfraInvest          = 0.20f;  // Individuated
    public float SignalLegibility         = 0.60f;  // Distributed: 0=encrypted ↔ 1=open
    public float HonestSignalWeight       = 0.50f;  // Distributed: 0=disinfo ↔ 1=honest
    public float StigmergicBandwidth      = 0.30f;  // Collective
    public float PheroMemoryInvest        = 0.20f;  // Collective / Distributed ritual-memory

    // Existential — additional dials.
    public float OrthodoxyLevel           = 0.50f;  // Individuated: 0=pluralism ↔ 1=orthodox
    public float ProselytizePosture       = 0.20f;  // Individuated

    // Coercive — additional dials.
    public float DomesticSecurityLevel    = 0.30f;  // Individuated
    public float DiplomaticPosture        = 0.50f;  // Individuated: 0=isolationist ↔ 1=expansive
    public float CommandCentralization    = 0.50f;  // Collective: 0=nest-cluster ↔ 1=single-queen
    public float NetworkTopologySlider    = 0.50f;  // Distributed: 0=mesh ↔ 1=hub

    // Initialise native war-domain coverage from architecture (§7.3 "native" column).
    public void InitNativeDomains()
    {
        switch (Architecture)
        {
            case CognitiveArchitecture.Individuated:
                DomainKinetic = 0.8f; DomainBiochemical = 0.2f;
                DomainInformational = 0.4f; DomainEconomic = 0.5f;
                break;
            case CognitiveArchitecture.Distributed:
                DomainKinetic = 0.2f; DomainBiochemical = 0.9f;
                DomainInformational = 0.7f; DomainEconomic = 0.5f;
                DisinfoCapability = 0.6f; DetectionCapability = 0.5f;   // §6.1
                break;
            case CognitiveArchitecture.Collective:
                DomainKinetic = 0.6f; DomainBiochemical = 0.5f;
                DomainInformational = 0.3f; DomainEconomic = 0.4f;
                break;
        }
    }
}

// ── Supporting enums ──────────────────────────────────────────────────────────

public enum GovernmentType
{
    Chiefdom,
    Monarchy, Oligarchy, Democracy, Theocracy, Empire,   // Individuated
    HubNetwork, MeshNetwork,                              // Distributed analogs
    SingleQueen, NestCluster,                             // Collective analogs
}

public enum KinshipPolicy
{
    Unset,
    Nuclear,       // tight unit, high internal investment
    Extended,      // broader kin networks, moderate trade openness
    Clan,          // kin coalitions, factionalism risk
    CrossLineage,  // intermarriage / inter-civ exchange
}

public enum IdeaPatronageType
{
    Unset,
    Culture,    // art, oral tradition, norms
    Religion,   // tier-3 belief (Individuated only for full effect)
    Science,    // proto-science / natural philosophy
    Military,   // tactical doctrine
}

public enum TradeHealthLabel { None, Parasitism, Neutral, Mutualism }

public enum CasteDifferentiation { Monomorphic, BasicSplit, Polymorphic }
public enum ReproductiveMode     { Monogyne, Polygyne }
public enum DecisionVelocity     { Slow, Moderate, Fast }
