using System.Collections.Generic;
using UnityEngine;

/// The Era 3 "dominance path" a civilization follows. CommerceEngine is the social/tool track
/// (existing content); the rest are ecological-dominance tracks so non-social archetypes reach Era 3
/// with their own identity, settlement re-skin, and (later) policy/war choices.
public enum Era3Path
{
    CommerceEngine,   // social + tools/culture/predation → trade, settlements, polity
    LivingReef,    // colonial Distributed network → reef/mat/mycelial growth-nodes
    Terraformer,      // sessile producer → reshapes planetary chemistry
    BloomFront,       // mobile producer → migrating mega-blooms
    ApexPredator,     // mobile consumer → food-web control / hunting ranges
}

/// Per-civilization state for Era 3 — The Commerce Engine (spec §0–§9).
/// One instance per tracked community: player + NPC civs.
public class CivilizationState
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public int    CommunityId;
    public string Name;
    public CognitiveArchitecture Architecture;
    public bool   IsPlayer;
    public Era3Path Path = Era3Path.CommerceEngine; // dominance path, set at BeginEra3 from archetype

    // ── Founder biology snapshot (era3-mine-pane fix) ──────────────────────────────────────────
    // Once every organism is absorbed into settlements, there's no live AgentController left to read
    // Kingdom/Backbone/Metabolism/etc. from — the "MY COMMUNITY" HUD pane used to just go blank at
    // that point. Snapshotted once from a representative live member at BuildCivFromCommunity time
    // (before absorption), so a player can still tell "am I a colony of mushrooms" after the fact.
    public string FounderKingdom = "—";
    public string FounderBackbone = "—";
    public MetabolismType FounderMetabolism = MetabolismType.Chemosynthetic;
    public string FounderBreathedGas = "—";
    public string FounderExpelledGas = "—";
    public string FounderLiquidKind = "—";

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

    /// Settlement admission policy (see d3_settlement_admission_policy): when true, this civ's
    /// settlements can absorb nearby members of OTHER recognized civilizations, not just their own
    /// founding species — bigger population pool, faster growth, but a real cohesion cost (see the
    /// decision's Apply). Default false (species-locked) — the conservative, cohesive starting point.
    public bool MultispeciesSettlements;

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

    // Stockpile deleted (era3-systems-implementation-spec §6) — retired entirely; its seven
    // confirmed consumers redirected to CivilizationEconomy sector output/stock (see Era3Manager.cs).

    // ── Cultural influence (abstract model) ───────────────────────────────────
    // Maps sourceId → fraction [0,1] of this civ's population with cultural
    // affinity for that source civ. Updated by Era3Manager.TickCultureSpread().
    public Dictionary<int, float> CulturalInfluence = new Dictionary<int, float>();

    /// Returns the strongest incoming cultural influence — source id and fraction.
    public (int sourceId, float fraction) DominantExternalCulture()
    {
        int best = -1; float bestF = 0f;
        foreach (var kv in CulturalInfluence)
            if (kv.Value > bestF) { best = kv.Key; bestF = kv.Value; }
        return (best, bestF);
    }

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

    // ── Vassal status ─────────────────────────────────────────────────────────
    public int   SuzerainId    = -1;  // -1 = sovereign; ≥0 = id of controlling civ
    public float VassalLoyalty = 1f;  // 0–1; drops under tribute burden / military pressure

    // ── Host/Guest trigger surface (host-guest-trigger-spec.md §3) ────────────
    // Collective architecture gets no target-specific Card (never has discrete placement/targeting
    // control anywhere else in this design) — a single coarse dial instead. When on, Era3Manager's
    // AI-autonomous evaluation (Trigger Path B) runs on this civ's behalf against all eligible
    // neighbors, same as it does for NPCs.
    public bool  SeekSymbioticHosts          = false;
    public float SeekSymbioticHostsIntensity = 0.5f; // scales propose-chance in the AI evaluation

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

    // ── Structures (appearance-generation-spec §4.7) ────────────────────────────────────────
    /// One discrete structure instance — Commerce Engine/Apex Predator tracks only (§4.2; Living
    /// Reef/Terraformer/Bloom Front have no discrete-building concept, see §4.4/§4.5). Category is
    /// the StructureInvest channel index (0=Economic, 2=Informational, 3=Existential, 4=Coercive;
    /// 1=Biological is skipped — that channel is Layer 1 "residential density," not a building
    /// category, per §4.7.3). Age drives the §4.7.4 hazard-driven rebuild roll.
    public class StructureInstance
    {
        public string Name;
        public int Category;
        public float Age;
        public int HeightTier; // 0=Low-rise .. capped by the civ's tech tier's max height-tier
    }

    public List<StructureInstance> BuiltStructures = new List<StructureInstance>();

    /// EU4-style per-slider locks for the N-way allocation slider groups (Sector Allocation, Caste
    /// Allocation, Policy Sectors) — keyed "groupKey:index" by Era3HUD.AllocationDial. A locked
    /// slider is protected from being rebalanced when a SIBLING slider moves, but can still be
    /// dragged directly. UI preference, not simulation state, but stored per-civ since Era3HUD
    /// itself has no per-civ identity of its own (one HUD instance draws every civ's panel).
    public readonly Dictionary<string, bool> AllocationLocks = new Dictionary<string, bool>();

    // ── Policy-allocation economy (policy-allocation-spec §0–§9) ─────────────
    // Initialized lazily by Era3Manager.BeginEra3 so Architecture is set first.
    public CivilizationEconomy Economy;

    // ── Architecture-specific allocation §1.1 economic row ───────────────────
    // Individuated: sector allocation
    // SectorMilitary deleted (era3-systems-implementation-spec §2) — superseded by real
    // Economy.Allocation["Military"]; never read by anything but its own dial/readout.
    public float SectorProduction = 0.4f;
    public float SectorCulture    = 0.3f;
    // Distributed: network topology
    public bool NetworkCentralized = false;
    // Collective caste ratios (CasteForager/Builder/Soldier/Trader) deleted (era3-systems-
    // implementation-spec §2) — redundant with Policy Sectors (Economy.Allocation), which already
    // applies to Collective same as every other architecture.

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

    // Economic — additional dials. (era3-adaptation-trees-spec §1.1 retires Tariff Rate/Exchange
    // Posture — both were unconsumed anywhere but their own slider — in favor of the gated Trade
    // Posture policy slot, which drives ConnectionStrength for real.)
    // CasteTrader/StockpileTarget deleted (era3-systems-implementation-spec §2/§6) — Trader caste
    // redundant with Policy Sectors same as the other three castes; StockpileTarget moot now that
    // Stockpile itself is retired.

    // Genetic/Bio — additional dials. (Public Health Investment retired — superseded by the gated
    // Public Health Investment/Immune Caste Investment/Quarantine Regime policies, which drive
    // GenDMin for real; see the plague-crisis roll.)
    public float ParentalInvestment       = 0.30f;  // Individuated A1 only
    public float GraftCompatThreshold     = 0.50f;  // Distributed: 0=tight ↔ 1=permissive
    public float CompartmentInvest        = 0.10f;  // Distributed CODIT
    public float ReproductiveSuppressRatio = 0.90f; // Collective: fraction of workers suppressed
    // ImmuneCasteInvest deleted (era3-systems-implementation-spec §2) — real effect already
    // delivered by a Policy Catalog option on GenDMin.

    // Informational — additional dials. (Censorship Level retired — superseded by State Doctrine
    // Control/Open Academy's SignalLegibility effect; see TickCultureSpread's resistance term.)
    public float CommInfraInvest          = 0.20f;  // Individuated
    // SignalLegibility dial deleted (era3-systems-implementation-spec §2) — distinct from, and
    // superseded by, the real Era3PolicyCatalog.Var.SignalLegibility (fully policy-computed already;
    // untouched by this deletion). PheroMemoryInvest deleted — RitualInvestment (Existential tab)
    // already covers ritual/pheromone memory for every architecture.
    public float HonestSignalWeight       = 0.50f;  // Distributed: 0=disinfo ↔ 1=honest
    public float StigmergicBandwidth      = 0.30f;  // Collective

    // Existential — additional dials.
    public float OrthodoxyLevel           = 0.50f;  // Individuated: 0=pluralism ↔ 1=orthodox
    public float ProselytizePosture       = 0.20f;  // Individuated

    // Coercive — additional dials. (Domestic Security retired — was unconsumed anywhere but its own
    // slider; superseded by Garrison State/Codified Legalism's UpkeepCost/MaxSustainableForce/
    // VariationScore effects.)
    public float DiplomaticPosture        = 0.50f;  // Individuated: 0=isolationist ↔ 1=expansive
    public float CommandCentralization    = 0.50f;  // Collective: 0=nest-cluster ↔ 1=single-queen
    public float NetworkTopologySlider    = 0.50f;  // Distributed: 0=mesh ↔ 1=hub

    // ── Era 3 Ecological Paths (era3-ecological-paths-spec §1-§4) ────────────────────────────
    // Non-CommerceEngine, non-symbiotic-LivingReef paths get simple, always-RESELECTABLE
    // posture choices instead of one-shot Cards — "dials only, no Cards, no treaties, no
    // Representative" per the mediation spectrum (§1: there is no treaty layer for a planetary-
    // dominance lineage, and building one would contradict what these paths are). -1 = unset
    // (falls back to a conservative default in Era3EcologicalPaths). Index into that path's own
    // named option list (Era3EcologicalPaths.ResourcePolicyOptions etc.) — different paths have
    // different option lists (Terraformer's "Oxygenate/Acidify/Stabilize" isn't the same list as
    // Bloom Front's "Boom-Bust/Sustainable Cropping/Seasonal Following"), selected by civ.Path.
    public int EcoResourcePolicy  = -1;  // §4.2-4.5 row 1 (Growth/Atmosphere/Bloom/Predation policy)
    public int EcoConflictPosture = -1;  // conflict maneuver row
    public int EcoOrganization    = -1;  // organization/structure row

    /// Terraformer runaway-risk accumulator (§3): rises while the atmosphere dial is held at
    /// extremity, decays otherwise. Feeds runaway_probability — see Era3EcologicalPaths.
    public float RunawayExposure = 0f;

    /// era3-systems-implementation-spec §9: e3_state_formation's density-based trigger accumulator —
    /// same shape as RunawayExposure. Rises while a settlement's biomass/K_effective density exceeds
    /// the threshold, decays otherwise. See Era3Manager's "e3_state_formation" auto-event ExtraGate.
    public float StateFormationPressure = 0f;

    /// era3-systems-implementation-spec §8: Large Initiative — universal across all five tracks now,
    /// gated by I4b (track-flavored). 30-year/6-tick commitment: ongoing per-tick cost while Active,
    /// one-shot permanent bonus via Completed once TicksRemaining reaches 0. Track-specific effect
    /// sites read civ.Path directly rather than storing a redundant copy of which track committed.
    public bool LargeInitiativeActive    = false;
    public int  LargeInitiativeTicksRemaining = 0;
    public bool LargeInitiativeCompleted = false;

    // ── Era 3 Polity Model (era3-polity-model-spec §2-§4) ─────────────────────────────────
    // AdministrativeReach: how large a population+settlement spread this polity can coherently
    // govern — recomputed every polity tick (Era3Manager.TickPolity) from settlement count,
    // InvestInformation, and architecture. SplinterPressure: 0-1, rises when reach demand exceeds
    // capacity, decays otherwise; past a threshold it surfaces the Administrative Crisis card.
    public float AdministrativeReach = 3f;
    public float SplinterPressure    = 0f;
    // Permanent capacity multiplier granted by resolving an Administrative Crisis with
    // "Decentralize" (trades some central cohesion for a durable reach cushion).
    public float DecentralizeBonus   = 0f;

    /// Population roster: which founding communities (= species, 1:1 in this codebase) actually
    /// make up this polity's population, and what fraction each holds. A sovereign civ starts
    /// 100% its own founding community; Vassalization keeps the vassal's own roster separate
    /// (tribute relationship, no merge — see TryVassalize), while Federation (TryFederate) merges
    /// both civs' rosters into one. Recomputed by Era3Manager.RecomputeRoster from each owned
    /// settlement's CivPopulation Cohort biomass, grouped by lineage.
    public readonly List<Era3Polity.RosterEntry> Roster = new List<Era3Polity.RosterEntry>();

    // ── Era 3 Tech/Idea Tree (era3-tech-idea-trees-spec §7) ───────────────────────────────────
    public readonly Dictionary<string, float> ResearchProgress = new Dictionary<string, float>();
    public readonly HashSet<string> UnlockedNodes = new HashSet<string>();
    /// The node currently sponsored by a played d3_idea_patronage / d3_tech_patronage card, if any
    /// (§7.1 patronage_multiplier). Cleared once PatronageExpiryTick passes.
    public string PatronageNodeId = null;
    public int PatronageExpiryTick = -1;

    // ── Era 3 Adaptation Tree (era3-adaptation-trees-spec §2) ─────────────────────────────────
    // The ecological tracks' third tree — evolved, not learned; separate progress/unlock sets from
    // the Tech/Idea tree above since it uses its own acquisition formula (Era3AdaptationTree).
    public readonly Dictionary<string, float> AdaptationProgress = new Dictionary<string, float>();
    public readonly HashSet<string> UnlockedAdaptations = new HashSet<string>();

    // ── Era 3 Warfare (era3-warfare-mechanics-spec) ───────────────────────────────────────────
    public float StandingForce   = 0f;  // continuous, post-I2b/writing (Era3Warfare.IsStandingForcePhase)
    public float UpkeepCost      = 0f;  // recomputed each war tick — super-linear in StandingForce
    public float ProjectionRange = 1f;  // recomputed each war tick from unlocked Tech/Idea nodes
    /// [0,1] temporary drag on VariationScore while at war — armies conform (§4 cultural cost).
    /// Relaxes back to 0 once no active war remains, unlike a permanent stat mutation.
    public float WarVariationSuppression = 0f;
    public Era3Warfare.WarSubsystem WarTargetSubsystem = Era3Warfare.WarSubsystem.Population;

    // ── Era 3 Policy Catalog (era3-policy-catalog-spec) ───────────────────────────────────────
    // One PolicySlotState per active slot (Era3PolicyCatalog.SlotsForTrack determines which slots
    // this civ's track actually has). Populated lazily by Era3Manager.EnsurePolicyDefaults the first
    // time a civ is ticked, rather than at construction, so it always reflects Path/Architecture
    // (set slightly after the constructor in BuildCivFromCommunity).
    public readonly Dictionary<Era3PolicyCatalog.PolicySlot, PolicySlotState> PolicySlots
        = new Dictionary<Era3PolicyCatalog.PolicySlot, PolicySlotState>();

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
