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

    /// DEBUG: fast-forwards _elapsed so time-gated auto events (agriculture, first settlement, etc.)
    /// fire immediately instead of making a tester wait out their real MinTime delays. Used by the
    /// "Skip to Era 3" button.
    public void DebugFastForwardElapsed(float seconds) => _elapsed = Mathf.Max(_elapsed, seconds);

    public CivilizationState PlayerCiv { get; private set; }
    public List<CivilizationState> NpcCivs { get; } = new List<CivilizationState>();
    private List<CivilizationState> _allCivs = new List<CivilizationState>();
    /// Read-only view of every tracked civ (player + all NPCs) — for HUD/diplomacy code that needs
    /// to scan the whole set (e.g. vassal listings, relation lookups) without exposing mutation.
    public IReadOnlyList<CivilizationState> AllCivsView => _allCivs;

    private AgentSpawner _spawner;
    /// World-space planet center, for camera-focus/direction math against Settlement.Position.
    public Vector3 PlanetCenter => _spawner != null ? _spawner.planetCenter : Vector3.zero;

    // Event log for HUD.
    public readonly List<(float Time, string Msg)> EventLog = new List<(float, string)>();

    // ── Auto event definitions ────────────────────────────────────────────────

    private struct Era3AutoEvent
    {
        public string   Id;
        public string[] Prereqs;
        public float    MinTime;
        public Func<CivilizationState, bool> ExtraGate;
        public Action<Era3Manager, CivilizationState> OnFire;
    }

    private List<Era3AutoEvent> _autoEvents;

    // ── Settlement model ──────────────────────────────────────────────────────
    public enum SettlementTier { Village = 0, Town = 1, City = 2 }

    public class Settlement
    {
        public int           Id;
        public string        Name;
        public SettlementTier Tier;
        public int           FounderCivId;          // civ whose auto-event spawned it
        public int           OwnerCivId;            // -1 = unaffiliated / independent state
        public float         PlayerCultureFraction; // 0-1, maintained by TickSettlements
        public float         Population;            // abstract units; Village=1, Town=5, City=20
        public int           IndependentCivId;      // set when it becomes its own CivilizationState
        public Vector3       Position;              // world-space location on the planet surface (for rendering)
        // Every community whose organisms were absorbed into this settlement (founder + any others
        // via Multispecies admission). Count > 1 means this is genuinely a multispecies settlement —
        // drives the visual "diversity" tell in Era3VisualManager and the inspect popup.
        public readonly HashSet<int> ContributingCommunities = new HashSet<int>();

        // The last civ whose ownership was formally RECOGNIZED (via founding, or a peace treaty that
        // ratified a conquest) — as opposed to OwnerCivId, which changes the instant a conquest lands.
        // While OwnerCivId != RecognizedOwnerCivId, the conquest is militarily real but not yet
        // diplomatically settled: rendered as a hatched/striped claim (see Era3VisualManager) rather
        // than a solid one. A peace treaty either ratifies it (RecognizedOwnerCivId catches up to
        // OwnerCivId, becoming permanent/solid) or the occupier withdraws (OwnerCivId reverts).
        public int RecognizedOwnerCivId = -1;
        public bool IsOccupied => OwnerCivId != RecognizedOwnerCivId;

        // era3-polity-model-spec §4 roster: per-community weighted composition, driving
        // CivilizationState.Roster. ContributingCommunities above only records WHICH communities are
        // present, not how much of the settlement each one is. population-energy-aggregation-spec.md
        // §2.0 migration: this used to be its own flat Dictionary<int,float> counter, updated wherever
        // Population itself was updated (absorption, abstract births); now it's read directly off
        // Cohorts (grouped by LineageId) in RecomputeRoster instead, so there's exactly one number
        // (cohort biomass) behind population everywhere rather than two that could drift apart.

        // population-energy-aggregation-spec.md §2: the real population/energy substrate. Population
        // above is now a plain cache resynced each cohort tick from Σ CivPopulation cohort biomass —
        // still read everywhere else in the codebase unchanged, just no longer the source of truth.
        public readonly List<Cohort> Cohorts = new List<Cohort>();

        // era3-systems-implementation-spec §9: last-computed CivPopulation carrying capacity from
        // TickCohortGroup, cached here so e3_state_formation's density trigger can read it without
        // recomputing the whole Settlement Energy Balance a second time.
        public float LastKEffective = 1f;
    }

    public List<Settlement> Settlements = new List<Settlement>();
    private int _nextSettlementId;

    public Settlement GetSettlementById(int id) => Settlements.Find(s => s.Id == id);

    // population-energy-aggregation-spec.md §3.1: persistent per-cell biological state for the
    // zone-based tracks (Terraformer/Bloom Front), additive to Era3VisualManager's per-frame
    // TierClaimRadius ownership recompute. Keyed by TectonicResult vertex index (CellId) — see
    // TerritoryCell's own header comment for why that's the chosen granularity.
    private readonly Dictionary<int, TerritoryCell> _territoryCells = new Dictionary<int, TerritoryCell>();
    public IReadOnlyDictionary<int, TerritoryCell> TerritoryCells => _territoryCells;

    // Era3Manager has no TectonicResult reference of its own (only Era3VisualManager does — see
    // Init/DI pattern used by every other manager in this codebase); it resolves a cell id to a
    // world position through this callback instead, set once by Era3VisualManager at startup.
    private System.Func<int, Vector3> _cellWorldPosLookup;
    public void SetCellWorldPositionLookup(System.Func<int, Vector3> lookup) => _cellWorldPosLookup = lookup;

    /// population-energy-aggregation-spec.md §3.1: called by Era3VisualManager.RebuildTerritory (same
    /// ~1s cadence it already recomputes per-vertex ownership on) to create/release TerritoryCells for
    /// the zone-based tracks (Terraformer/Bloom Front) — the two tracks appearance-generation-spec
    /// §4.5 renders as a coverage-intensity field instead of discrete settlements. Additive to the
    /// existing TierClaimRadius ownership computation there, not a replacement for it.
    public void SyncTerritoryCells(int[] owner, float[] claimStrength, bool[] contested)
    {
        for (int i = 0; i < owner.Length; i++)
        {
            int civId = owner[i];
            var civ = civId >= 0 ? FindCivById(civId) : null;
            bool zoneBased = civ != null && (civ.Path == Era3Path.Terraformer || civ.Path == Era3Path.BloomFront);

            _territoryCells.TryGetValue(i, out var cell);
            if (zoneBased)
            {
                bool isNew = cell == null;
                if (isNew) { cell = new TerritoryCell { CellId = i }; _territoryCells[i] = cell; }
                cell.OwningCivId = civId;
                cell.ClaimStrength = claimStrength[i];
                cell.IsContested = contested[i];
                // A newly-claimed cell represents this civ's own population reach extending into the
                // zone (appearance-generation-spec.md §4.5 — the coverage field IS civ density, not a
                // separate wild species), additional to its settlement's own core population cohort.
                // Seeded from that settlement's trait_snapshot ("same data, not a duplicate record")
                // rather than a fresh default — this zone-cell population is biologically the same
                // lineage, not a new one.
                if (isNew)
                {
                    var coreCohort = FindCivCoreCohort(civId);
                    var zoneCohort = new Cohort
                    {
                        LineageId = civId,
                        LocationProxy = i,
                        IsZoneBased = true,
                        Role = CohortRole.CivPopulation,
                        ManagementTier = CohortManagementTier.Wild,
                        ManagedByCivId = civId,
                        Biomass = 1f,
                    };
                    if (coreCohort != null) zoneCohort.Traits = coreCohort.Traits.Clone();
                    cell.Cohorts.Add(zoneCohort);

                    // era3-sovereignty-interaction-gaps-spec.md §3/§4: a genuine Wild resource cohort
                    // (not the civ's own population) — the actual thing a boundary contest is over,
                    // and what domestication would target at this cell. Without this, "both civs'
                    // cohorts draw against the same ceiling" has nothing real to refer to.
                    cell.Cohorts.Add(new Cohort
                    {
                        LineageId = -1, LocationProxy = i, IsZoneBased = true,
                        Role = CohortRole.Resource, ManagementTier = CohortManagementTier.Wild,
                        Biomass = 3f,
                    });
                }
            }
            else if (cell != null)
            {
                // Claim receded (or this cell was never zone-based) — release ownership but keep the
                // cell and its cohorts: "cohorts persist/revert-to-unmanaged, not deleted" (§3.1).
                cell.OwningCivId = -1;
                cell.ClaimStrength = 0f;
                cell.IsContested = contested[i];
            }
        }
    }

    /// Finds a civ's own settlement-level CivPopulation cohort (its largest settlement's, if it owns
    /// several) — used as the trait-snapshot seed source for a newly-claimed zone TerritoryCell, so
    /// the zone population starts as a real copy of the civ's actual biology rather than a synthetic
    /// default. Public so GameHUD's "civilized" Mine-tab fallback (DrawMinePageCivilized) can show
    /// this cohort's real, live-evolving trait data instead of only the frozen Founder* snapshot.
    public Cohort FindCivCoreCohort(int civId)
    {
        Settlement best = null;
        foreach (var s in Settlements)
            if (s.OwnerCivId == civId && (best == null || s.Population > best.Population))
                best = s;
        if (best == null) return null;
        foreach (var c in best.Cohorts)
            if (c.Role == CohortRole.CivPopulation && c.LineageId == civId) return c;
        return null;
    }

    /// appearance-generation-spec.md §4.5's cell_intensity = f(doctrine_weight, tech_tier_reach,
    /// local_cohort_health) — this supplies the local_cohort_health term to Era3VisualManager's
    /// existing zone-coverage visual blend. 1.0 = no cohorts yet / nothing to report (neutral,
    /// doesn't drag the visual down before any cohort has had a chance to establish).
    public float GetTerritoryCellHealth(int cellId)
    {
        if (!_territoryCells.TryGetValue(cellId, out var cell) || cell.Cohorts.Count == 0) return 1f;
        float totalBiomass = 0f, weightedHealth = 0f;
        foreach (var c in cell.Cohorts)
        {
            float demandPerBiomass = CohortEnergyModel.ComputeDemandPerBiomass(c.Traits, _cellWorldPosLookup != null ? _cellWorldPosLookup(cellId) : Vector3.zero);
            float yieldPerBiomass = c.MetabolicClass == CohortMetabolicClass.Producer
                ? CohortEnergyModel.ComputeProducerYieldPerBiomass(c.Traits, _cellWorldPosLookup != null ? _cellWorldPosLookup(cellId) : Vector3.zero, _spawner != null ? _spawner.planetCenter : Vector3.zero)
                : demandPerBiomass;
            float health = demandPerBiomass > 0.0001f ? Mathf.Clamp01(yieldPerBiomass / demandPerBiomass) : 1f;
            weightedHealth += health * c.Biomass;
            totalBiomass += c.Biomass;
        }
        return totalBiomass > 0f ? weightedHealth / totalBiomass : 1f;
    }

    // Pending player prompts — HUD polls these
    private Settlement         _pendingSettlementJoin;
    private CivilizationState  _pendingVassalRebellion;

    private float _settlementTimer;
    private const float SettlementTickInterval = 60f;
    private const float VassalRebellionThreshold = 0.20f; // loyalty floor
    // era3-sovereignty-interaction-gaps-spec.md §1.3/§1.6: tribute rate and its conversion into the
    // existing TradeHealth-based loyalty recovery — both flagged tunable, not assumed final.
    private const float TributeRate = 0.05f;
    private const float TributeTradeHealthBoost = 0.02f;

    // ── Crisis timer ──────────────────────────────────────────────────────────
    private float _crisisTimer;
    private const float CrisisInterval = 30f;

    private float _cultureTimer;
    private const float CultureTickInterval  = 30f;  // seconds between culture spread ticks
    private const float ExclaveThreshold     = 0.35f; // fraction that triggers an exclave event
    private const float ExclaveMajorThreshold = 0.60f; // fraction where NPC acts without warning

    // Pending exclave: set when a threshold crossing is detected this tick.
    private CivilizationState _pendingExclaveSource;
    private CivilizationState _pendingExclaveTarget;
    private bool              _pendingExclavePlayerIsSource;

    // ── Trade engine ──────────────────────────────────────────────────────────
    private float _tradeTimer;
    private const float TradeTickInterval = 3f;

    // ── Idea Emergence Engine (idea-emergence-spec §3) ────────────────────────
    public IdeaEmergenceEngine Ideas { get; } = new IdeaEmergenceEngine();
    // Emerged ideas pending player acknowledgement (shown as HUD cards).
    private readonly List<(CivilizationState Civ, IdeaEmergenceEngine.IdeaDef Idea)> _pendingIdeas
        = new();

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

    private int CountLivingMembers(int communityId)
    {
        if (_spawner == null) return 0;
        int n = 0;
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == communityId) n++;
        return n;
    }

    /// Builds a real CivilizationState for a real community, seeded from its ACTUAL Era 2 record —
    /// used for the player at Era 3 onset and for every other community that independently qualifies
    /// (either already-crossed at onset, or promoted later — see PromoteNewlyQualifiedCivs).
    private CivilizationState BuildCivFromCommunity(int communityId, bool isPlayer)
    {
        var rec = Era2Manager.Instance?.GetRecord(communityId);
        var arch = rec?.Architecture ?? CognitiveArchitecture.Individuated;
        var civ = new CivilizationState
        {
            CommunityId  = communityId,
            Name         = GenerateCivName(arch, communityId),
            Architecture = arch,
            IsPlayer     = isPlayer,
            Subtrack        = rec?.SubTrack         ?? IndividuatedSubTrack.Unresolved,
            CommMedium      = rec?.CommMedium        ?? CommunicationMedium.Unset,
            SocialStructure = rec?.SocialStructure   ?? SocialStructureType.Unset,
            NetworkConnectivityTier = arch == CognitiveArchitecture.Distributed
                ? Mathf.Clamp(Mathf.FloorToInt((rec?.II ?? 50f) / 40f), 0, 2) : 1,
            SignalBandwidthTier     = arch == CognitiveArchitecture.Distributed
                ? Mathf.Clamp(Mathf.FloorToInt((rec?.II ?? 50f) / 55f), 0, 2) : 1,
            CasteDiff = arch == CognitiveArchitecture.Collective && (rec?.ThresholdLaborFormalized ?? false)
                ? CasteDifferentiation.Polymorphic : CasteDifferentiation.BasicSplit,
            RepMode = ReproductiveMode.Polygyne,
            Path    = DeterminePath(rec),
        };
        civ.InitNativeDomains();
        GrantStartingBasket(civ);
        civ.Economy = new CivilizationEconomy(civ.Architecture);

        // Snapshot a representative living member's biology NOW, before settlement absorption
        // removes every organism of this community from ActiveAgents — see the fields' own comment.
        if (_spawner != null)
            foreach (var a in _spawner.ActiveAgents)
                if (a != null && a.communityId == communityId)
                {
                    civ.FounderKingdom     = string.IsNullOrEmpty(a.Kingdom) ? "—" : a.Kingdom;
                    civ.FounderBackbone    = a.Backbone.ToString();
                    civ.FounderMetabolism  = a.Metabolism;
                    civ.FounderBreathedGas = string.IsNullOrEmpty(a.BreathedGasName) ? "—" : a.BreathedGasName;
                    civ.FounderExpelledGas = string.IsNullOrEmpty(a.ExpelledGasName) ? "—" : a.ExpelledGasName;
                    civ.FounderLiquidKind  = string.IsNullOrEmpty(a.RequiredLiquidKind) ? "—" : a.RequiredLiquidKind;
                    break;
                }

        return civ;
    }

    // era3-track-parity-gating-spec §4: each of the seven track-flavors gets a small, differently-
    // flavored, roughly-equal head start at civ creation — never the same fixed bonus twice. The
    // signature slot is always "your track's earliest form of internal differentiation" (I2a for
    // architecture tracks — same node this session's d3_idea_patronage gate reuses deliberately, not
    // coincidentally — or A2a for the three purely-ecological tracks), never a raw production or
    // combat stat, so no track's bonus reads as strictly stronger than another's, only differently
    // shaped. Adaptation tiers run ~4x cheaper than Tech/Idea, so a Tier-2 Adaptation node (A2a) is
    // the closer cost-equivalent to a Tech/Idea Tier-1 node than a Tech/Idea Tier-2 would be.
    private static (string[] floor, string signature) GetStartingBasket(CivilizationState civ)
    {
        if (civ.Path == Era3Path.CommerceEngine)
        {
            return civ.Architecture switch
            {
                CognitiveArchitecture.Individuated => (new[] { "T1a", "T1c", "I1c" }, "I2a"),
                CognitiveArchitecture.Distributed  => (new[] { "T1c", "T1b", "I1c" }, "I2a"),
                CognitiveArchitecture.Collective   => (new[] { "T1a", "I1a", "I1b" }, "I2a"),
                _ => (new[] { "T1a", "T1c", "I1c" }, "I2a"),
            };
        }
        // Living Reef mixes one Adaptation floor candidate (A1a) with two Tech candidates since it's
        // the only track that straddles both Tech and Adaptation at Tier 1 with meaningful thematic
        // fits; Terraformer/BloomFront don't draw T1b (unavailable to them per the Tech tree's own
        // gating quirks) while Apex Predator does, since it's the one ecological track T1b applies to.
        return civ.Path switch
        {
            Era3Path.LivingReef   => (new[] { "T1a", "T1c", "A1a" }, "I2a"),
            Era3Path.Terraformer  => (new[] { "T1a", "T1c", "A1c" }, "A2a"),
            Era3Path.BloomFront   => (new[] { "T1a", "T1c", "A1a" }, "A2a"),
            Era3Path.ApexPredator => (new[] { "T1a", "T1b", "A1a" }, "A2a"),
            _ => (new[] { "T1a", "T1c", "A1a" }, "A2a"),
        };
    }

    /// 70%: grant 2 of the 3 Tier-1 floor candidates (random pair). 30%: grant the signature
    /// candidate alone. Two Tier-1 nodes ≈ one signature node in relative research-cost investment,
    /// so the "skipped cost" stays roughly equal across every roll regardless of which branch fires.
    /// Direct HashSet grants (bypassing OnNodeUnlocked's retrofit switch) are safe here: none of the
    /// granted ids (Tier-1 floor nodes, I2a, A2a) match that switch's only two cases (I2b, T2a).
    private void GrantStartingBasket(CivilizationState civ)
    {
        var (floor, signature) = GetStartingBasket(civ);
        void Grant(string nodeId)
        {
            if (nodeId.StartsWith("A")) civ.UnlockedAdaptations.Add(nodeId);
            else civ.UnlockedNodes.Add(nodeId);
        }

        if (UnityEngine.Random.value < 0.70f)
        {
            var pool = new List<string>(floor);
            for (int i = 0; i < 2 && pool.Count > 0; i++)
            {
                int idx = UnityEngine.Random.Range(0, pool.Count);
                Grant(pool[idx]);
                pool.RemoveAt(idx);
            }
        }
        else
        {
            Grant(signature);
        }
    }

    /// Scans for real communities that cross their Era 2 threshold AFTER Era 3 has already begun, and
    /// promotes each to a real civ the moment it qualifies — so "reaching Era 3" is genuinely per
    /// species over time, not a single snapshot taken only at the instant the player got there.
    /// Throttled: this is a full-record scan, cheap given there are only a handful of communities.
    private float _civPromotionTimer;
    private void PromoteNewlyQualifiedCivs()
    {
        _civPromotionTimer -= Time.deltaTime;
        if (_civPromotionTimer > 0f) return;
        _civPromotionTimer = 2f;

        if (Era2Manager.Instance == null) return;
        foreach (var rec in Era2Manager.Instance.AllRecords)
        {
            if (rec.communityId == 0) continue;
            if (!rec.HasCrossedEndOfEra2Threshold) continue;
            if (CountLivingMembers(rec.communityId) <= 0) continue;
            if (_allCivs.Exists(c => c.CommunityId == rec.communityId)) continue; // already a civ

            var civ = BuildCivFromCommunity(rec.communityId, isPlayer: false);
            NpcCivs.Add(civ);
            _allCivs.Add(civ);
            civ.Economy = new CivilizationEconomy(civ.Architecture);
            EnsurePolicyDefaults(civ);

            // Catch this civ up on every auto-event the player has ALREADY acquired — otherwise a
            // civ promoted mid-run (after e.g. e3_permanent_settlement already fired for the player)
            // would be permanently stuck with no settlement, since the normal NPC catch-up path only
            // fires at the moment the PLAYER newly acquires each event, not retroactively.
            foreach (var ev in _autoEvents)
            {
                if (!PlayerCiv.Has(ev.Id) || civ.Has(ev.Id)) continue;
                civ.Acquire(ev.Id);
                ev.OnFire?.Invoke(this, civ);
            }

            LogEvent($"{civ.Name} reaches civilization ({civ.Path}).");
            Debug.Log($"[Era3] Community {rec.communityId} promoted to a real civ: '{civ.Name}' ({civ.Path}).");
        }
    }

    public void BeginEra3()
    {
        if (_active) return;
        _active  = true;
        _elapsed = 0f;

        // Drop any queued Era 1/Era 2 gene popups so they don't surface over the civilization layer.
        GeneEvolutionManager.ClearPendingPopups();

        // Seed player civ from Era 2 record.
        PlayerCiv = BuildCivFromCommunity(0, isPlayer: true);
        Debug.Log($"[Era3] Player civ '{PlayerCiv.Name}' follows the {PlayerCiv.Path} path.");

        // Every OTHER real community that has ALREADY crossed its own Era 2 threshold becomes a real,
        // agent-backed civ too — not the old two hardcoded placeholder "rivals" (fixed ids 10/11 with
        // no actual population behind them). A community that crosses its threshold LATER, after Era 3
        // has begun, is promoted the moment it qualifies (see Update()'s PromoteNewlyQualifiedCivs).
        // This is what makes "civilized" in Era 3 mean something real for every species, not just the
        // player's.
        NpcCivs.Clear();
        if (Era2Manager.Instance != null)
        {
            foreach (var rec in Era2Manager.Instance.AllRecords)
            {
                if (rec.communityId == 0) continue; // player already seeded above
                if (!rec.HasCrossedEndOfEra2Threshold) continue;
                if (CountLivingMembers(rec.communityId) <= 0) continue;
                NpcCivs.Add(BuildCivFromCommunity(rec.communityId, isPlayer: false));
            }
        }

        _allCivs.Clear();
        _allCivs.Add(PlayerCiv);
        _allCivs.AddRange(NpcCivs);

        // Initialize economy models for all civs (policy-allocation-spec §0).
        foreach (var civ in _allCivs)
        {
            civ.Economy = new CivilizationEconomy(civ.Architecture);
            // era3-policy-catalog-spec: seed neutral-default policies immediately rather than waiting
            // for the first PolicyTickInterval — otherwise the Policy tab reads "not yet initialized"
            // for the opening seconds of Era 3, and GetVar would be querying empty slots regardless.
            EnsurePolicyDefaults(civ);
        }

        // Seed Idea Emergence engine with all Era 3 Ideas (idea-emergence-spec §3).
        SeedIdeas();
        Ideas.OnIdeaEmerged += (civ, idea) =>
        {
            LogEvent($"Idea emerged: {idea.DisplayName}");
            if (civ.IsPlayer) _pendingIdeas.Add((civ, idea));
            AudioManager.Instance?.OnCivFounded();  // nearest available positive SFX
        };

        AudioManager.Instance?.OnCivFounded();

        BuildAutoEventDefs();
        EraPostProcessManager.Instance?.OnEra3Begin();
        AudioManager.Instance?.OnEraShiftToEra3();

        LogEvent("Era 3 begins — The Commerce Engine.");
        Debug.Log($"[Era3Manager] Era 3 BEGINS. Player: {PlayerCiv.Name} ({PlayerCiv.Architecture}).");
        GameLog.Snapshot(_spawner);
    }

    void Update()
    {
        if (!_active) return;
        _elapsed += Time.deltaTime;
        if (_eventFlashTimer > 0f) _eventFlashTimer -= Time.deltaTime;

        PromoteNewlyQualifiedCivs();
        TickSettlementAbsorption();
        TickCohorts();
        TickConflict();
        TickEcologicalPaths();
        TickPolity();
        TickResearch();
        TickWarfare();
        TickPolicies();
        TickHostGuestProposalAI();

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
            // host-guest-relation-spec: reads TradeHealth, which this same trade tick just updated —
            // naturally synchronized on the same cadence rather than a separate timer.
            TickHostGuestRelations();
        }

        // ── Idea Emergence tick (idea-emergence-spec §3) ──────────────────────
        Ideas.Tick(_allCivs, Time.deltaTime);

        // ── Settlement growth tick ────────────────────────────────────────────
        _settlementTimer += Time.deltaTime;
        if (_settlementTimer >= SettlementTickInterval)
        {
            _settlementTimer = 0f;
            TickSettlements();
        }

        // ── Culture spread tick ───────────────────────────────────────────────
        _cultureTimer += Time.deltaTime;
        if (_cultureTimer >= CultureTickInterval)
        {
            _cultureTimer = 0f;
            TickCultureSpread();
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
        // era3-systems-implementation-spec §4: this grant is no longer an ungated bypass — the card's
        // own ChoiceGate now requires I3a first (Era3HUD.cs "d3_war_or_diplomacy"), so reaching this
        // call already means the real gate cleared. T2a's independent OnNodeUnlocked grant remains a
        // second, separate earned path (research T2a without ever picking this card).
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

    // SetSectorAllocation/SetCasteAllocation deleted (era3-systems-implementation-spec §2/§4) — their
    // only caller was the "Labor Allocation" card (d3_caste_labor, both the Era3HUD.cs Card and its
    // orphaned GeneCatalog.cs duplicate), deleted outright per §4's Card-retirement disposition;
    // SectorMilitary/CasteForager/CasteBuilder/CasteSoldier were the fields they wrote, also deleted.

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
                        CommunicationMedium.ChemicalPheromonal       => 0.8f,
                        CommunicationMedium.BioluminescentElectrical => 1.1f,
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

    // Channel index → the matching era3-policy-catalog-spec capability hook.
    private static readonly string[] CapabilityVarByChannel =
    {
        Era3PolicyCatalog.Var.EconCapability, Era3PolicyCatalog.Var.BioCapability,
        Era3PolicyCatalog.Var.InfoCapability, Era3PolicyCatalog.Var.ExistCapability,
    };

    // §1.1 capability(civ, channel) = StructureInvestment × SubtrackModifier × policy multiplier
    public static float Capability(CivilizationState civ, int ch)
    {
        float baseCap = civ.StructureInvest[ch] * SubtrackModifier(civ, ch);
        if (ch < 0 || ch >= CapabilityVarByChannel.Length) return baseCap; // ch==4 (Kinetic) handled separately below
        float cap = baseCap * Era3PolicyCatalog.GetVar(civ, CapabilityVarByChannel[ch]);
        // era3-systems-implementation-spec §2: ReproductiveSuppressRatio's other side (Collective
        // only, Economic channel ch==0) — eusocial reproductive suppression funds economic capability.
        if (ch == 0 && civ.Architecture == CognitiveArchitecture.Collective)
            cap *= 1f + civ.ReproductiveSuppressRatio * 0.15f;
        return cap;
    }

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
        return civ.StructureInvest[4] * MobilityFactor[archIdx] * Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.KineticCapability);
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
    // appearance-generation-spec §4.7.3: each non-Genetic channel maps to a building category.
    // Genetic/Biological (StructureInvest index 1) deliberately has no entry — that channel is
    // Layer 1 "residential density" in the spec's model, not a functional-differentiation category,
    // so it's excluded here rather than silently generating a structure type the spec never names.
    // Commerce Engine gets the full institutional set; Apex Predator gets the small non-urban
    // vocabulary the spec explicitly calls for (§4.6: "without the institutional-building set — no
    // government/administrative structures").
    private static readonly string[][] StructureNamesCommerceEngine = new[]
    {
        new[] { "Workshop", "Market", "Granary" },                  // Economic (ch 0)
        null,                                                        // Biological (ch 1) — residential, not a category
        new[] { "Archive", "Forum" },                                // Informational (ch 2)
        new[] { "Shrine", "State Temple" },                          // Existential (ch 3)
        new[] { "Garrison", "Fortification", "Government Hall" },    // Coercive (ch 4)
    };
    private static readonly string[][] StructureNamesApexPredator = new[]
    {
        new[] { "Cache", "Den" },   // Economic — no markets/granaries, just den/cache
        null,
        null,                        // no archives — no institutions (§4.6)
        null,                        // no shrines — no institutions
        new[] { "Territorial Marker" }, // Coercive — marking, not fortification/government
    };
    private const float StructureInvestPerBuilding = 0.5f; // one new instance per this much accumulated investment, TUNABLE

    // ── §4.7 Settlement Composition & Density Model ──────────────────────────────────────────
    // §4.7.1 slot_capacity per tech tier (Era3TechTree.GetTechTier) — the scale-conservation
    // mechanism: pressure beyond this resolves through height-tier increases or type reallocation,
    // never through unbounded instance growth. Values TUNABLE — the source spec assumes none.
    private static readonly int[] SlotCapacityByTechTier      = { 3, 5, 8, 12, 18 };
    // Max height_tier reachable at this tech tier (0=Low-rise, 1=Mid-rise, 2=High-rise) — pre-
    // industrial tiers cannot reach High-rise, matching the same construction-technique constraint
    // already governing the densification ceiling this table shares (§4.8's "double duty" note).
    private static readonly int[] MaxHeightTierByTechTier     = { 0, 0, 1, 2, 2 };
    // §4.7.4 rebuild hazard — reuses idea-emergence-spec §3.3's exact Weibull-hazard constants
    // (IdeaEmergenceEngine.IdeaDef: BaseRate=0.03, K=1.15) for design consistency, per the spec's
    // own explicit instruction, rather than inventing a second aging model.
    private const float RebuildBaseRate = 0.03f;
    private const float RebuildK = 1.15f;

    private static int CountInCategory(CivilizationState civ, int category)
    {
        int n = 0;
        foreach (var s in civ.BuiltStructures) if (s.Category == category) n++;
        return n;
    }

    /// Turns the EXISTING per-channel StructureInvest accumulation into real, named
    /// BuiltStructures instances via the §4.7 slot-capacity + two-layer allocation model, and rolls
    /// each existing instance's §4.7.4 rebuild hazard every tick. Only the two discrete-structure
    /// tracks (appearance-generation-spec §4.2) get named buildings; Living Reef/Terraformer/Bloom
    /// Front have no settlement/building concept to populate (§4.4/§4.5).
    private void TickStructures()
    {
        foreach (var civ in _allCivs)
        {
            if (civ.HasCollapsed) continue;
            string[][] table = civ.Path == Era3Path.CommerceEngine ? StructureNamesCommerceEngine
                              : civ.Path == Era3Path.ApexPredator  ? StructureNamesApexPredator
                              : null;
            if (table == null) continue;

            int techTier = Era3TechTree.GetTechTier(civ);
            // host-guest-relation-spec §5 resolved decision: slots ceded to guests count against the
            // HOST's own slot_capacity — hosting trades away some of the host's own settlement-growth
            // headroom, the real cost of hosting, rather than drawing from a separate pool.
            int slotCapacity = Mathf.Max(0, SlotCapacityByTechTier[techTier] - TotalGuestFootprint(civ));
            int maxHeightTier = MaxHeightTierByTechTier[techTier];

            // §4.7.2 Layer 1 — residential slots claim a share of slot_capacity proportional to the
            // Genetic/population-pressure channel's weight (index 1) among all five channels.
            float[] chWeight = { civ.InvestEconomic, civ.InvestBiological, civ.InvestInformation, civ.InvestReligion, civ.InvestCoercive };
            float totalWeight = chWeight[0] + chWeight[1] + chWeight[2] + chWeight[3] + chWeight[4];
            float residentialFraction = totalWeight > 0.0001f ? chWeight[1] / totalWeight : 0.2f;
            int residentialSlots = Mathf.Clamp(Mathf.RoundToInt(slotCapacity * residentialFraction), 0, slotCapacity);

            // §4.7.3 Layer 2 — remaining slots split across the four non-Genetic channels
            // proportional to their own channel weight, giving each a concrete category_slot_target.
            int remainingSlots = slotCapacity - residentialSlots;
            float nonGeneticWeight = totalWeight - chWeight[1];
            var categoryTarget = new int[5];
            if (nonGeneticWeight > 0.0001f)
                for (int ch = 0; ch < 5; ch++)
                    if (ch != 1) categoryTarget[ch] = Mathf.RoundToInt(remainingSlots * (chWeight[ch] / nonGeneticWeight));

            // §4.7.4 age + roll the rebuild hazard for every existing instance.
            foreach (var inst in civ.BuiltStructures)
            {
                inst.Age += TradeTickInterval;
                float ageMinutes = inst.Age / 60f;
                float p = RebuildBaseRate * Mathf.Pow(ageMinutes, RebuildK);
                if (UnityEngine.Random.value >= p) continue;

                inst.Age = 0f;
                inst.HeightTier = maxHeightTier; // rebuilds in place at the CURRENT tech tier's cap
                // Over-represented relative to its category's target? Reallocate to whichever
                // category is currently most under-represented instead of rebuilding in place —
                // this is the concrete mechanism behind "high Coercive investment -> more military
                // structures at the next reallocation pass."
                if (categoryTarget[inst.Category] > 0 && CountInCategory(civ, inst.Category) <= categoryTarget[inst.Category]) continue;
                int mostUnder = -1; float worstRatio = 1f;
                for (int ch = 0; ch < 5; ch++)
                {
                    if (ch == 1 || categoryTarget[ch] <= 0) continue;
                    float ratio = (float)CountInCategory(civ, ch) / categoryTarget[ch];
                    if (ratio < worstRatio) { worstRatio = ratio; mostUnder = ch; }
                }
                if (mostUnder >= 0 && mostUnder != inst.Category && table[mostUnder] != null && table[mostUnder].Length > 0)
                {
                    inst.Category = mostUnder;
                    inst.Name = table[mostUnder][UnityEngine.Random.Range(0, table[mostUnder].Length)];
                }
            }

            // Grow new instances as investment accrues, capped by slot_capacity (the scale-
            // conservation ceiling — pressure beyond this waits for a height-tier/reallocation pass
            // above, never spawns unbounded instances).
            if (civ.BuiltStructures.Count >= slotCapacity) continue;
            for (int ch = 0; ch < 5; ch++)
            {
                var names = table[ch];
                if (names == null) continue;
                int earnedCount = Mathf.FloorToInt(civ.StructureInvest[ch] / StructureInvestPerBuilding);
                int currentCount = CountInCategory(civ, ch);
                while (currentCount < earnedCount && civ.BuiltStructures.Count < slotCapacity)
                {
                    civ.BuiltStructures.Add(new CivilizationState.StructureInstance
                    {
                        Name = names[UnityEngine.Random.Range(0, names.Length)],
                        Category = ch,
                        Age = 0f,
                        HeightTier = 0, // new construction starts at Low-rise regardless of tech tier
                    });
                    currentCount++;
                }
            }
        }
    }

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
            float buildRateMult = Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.BuildRate);
            for (int ch = 0; ch < 5; ch++)
            {
                civ.StructureInvest[ch] = Mathf.Clamp(
                    civ.StructureInvest[ch] + dialAlloc[ch] * BuildRate * buildRateMult - DecayRate,
                    0f, 2f);
            }
        }
        TickStructures();
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

    // ── Idea Emergence (idea-emergence-spec §3) ───────────────────────────────

    private void SeedIdeas()
    {
        // Planned Colonization (§4 worked example):
        //   Preconditions: e3_settlement_expansion acquired.
        //   Pressure:      time-in-era proxy that peaks at 1 after ~10 minutes.
        //   Threshold:     0.35 (moderate sustained pressure required).
        //   Base rate:     0.04 per 12-second sample tick.
        //   k:             1.20 (slight Weibull uptick over sustained exposure).
        Ideas.Register(new IdeaEmergenceEngine.IdeaDef
        {
            Id          = "planned_colonization",
            DisplayName = "Planned Colonization",
            Description = "Your population pressure has sustained long enough to drive deliberate expansion. " +
                          "Unlike reactive fission, a planned colony can be placed at a chosen site.",
            Preconditions = new Func<CivilizationState, bool>[]
            {
                civ => civ.Has("e3_settlement_expansion"),
            },
            PressureSource   = civ => Mathf.Clamp01(_elapsed / 600f),   // peaks at ~10 min
            Threshold        = 0.35f,
            BaseRate         = 0.04f,
            K                = 1.20f,
            ExposureDecayRate = 0.010f,
        });
    }

    /// Pending ideas (emerged, player hasn't acted on them yet). Read by Era3HUD.
    public IReadOnlyList<(CivilizationState Civ, IdeaEmergenceEngine.IdeaDef Idea)> PendingIdeas
        => _pendingIdeas;

    /// Called by Era3HUD when the player adopts or dismisses a pending Idea.
    public void ResolvePendingIdea(string ideaId, bool adopted)
    {
        _pendingIdeas.RemoveAll(p => p.Idea.Id == ideaId);
        if (adopted)
        {
            Ideas.Adopt(PlayerCiv.CommunityId, ideaId);
            PlayerCiv.Acquire(ideaId);
            LogEvent($"Adopted: {ideaId}");
        }
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
                    // era3-systems-implementation-spec §6: redirected from Stockpile to Economic output.
                    if (civ.Economy != null) civ.Economy.Stock[CivilizationEconomy.Industry] += 0.1f;
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_permanent_settlement", MinTime = 25f,
                Prereqs = new[] { "e3_agriculture" },
                OnFire = (mgr, civ) =>
                {
                    civ.BeliefTier = Mathf.Max(civ.BeliefTier, 2);
                    civ.RitualInvestment = Mathf.Min(civ.RitualInvestment + 0.2f, 1f);
                    mgr.FoundSettlementsFromDensity(civ, SettlementTier.Village);
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_settlement_expansion", MinTime = 55f,
                Prereqs = new[] { "e3_permanent_settlement", "e3_surplus_economy" },
                OnFire = (mgr, civ) =>
                {
                    // Promote existing village to town, or spawn new town.
                    var village = mgr.Settlements.Find(s => s.FounderCivId == civ.CommunityId
                                                         && s.Tier == SettlementTier.Village);
                    // Grow from whatever REAL (possibly absorbed) population the village already has,
                    // rather than resetting it to a flat abstract number — a village that absorbed 40
                    // organisms at founding shouldn't shrink to 5 on promotion to Town.
                    if (village != null) {
                        village.Tier = SettlementTier.Town;
                        // Bump the CivPopulation cohort's biomass (the real source of truth), not
                        // Population directly — Population is just a cache resynced from it below.
                        var cohort = mgr.FindOrCreateCivPopulationCohort(village, civ.CommunityId);
                        cohort.Biomass = Mathf.Max(cohort.Biomass * 1.5f, 5f);
                        mgr.RecomputeSettlementPopulation(village);
                        mgr.CheckSettlementJoin(village);
                    }
                    else mgr.SpawnSettlement(civ, SettlementTier.Town);
                    civ.InvestEconomic = Mathf.Min(civ.InvestEconomic + 0.10f, 1f);
                }
            },

            new Era3AutoEvent
            {
                Id = "e3_urbanization", MinTime = 90f,
                Prereqs = new[] { "e3_settlement_expansion", "e3_specialized_economy" },
                OnFire = (mgr, civ) =>
                {
                    var town = mgr.Settlements.Find(s => s.FounderCivId == civ.CommunityId
                                                      && s.Tier == SettlementTier.Town);
                    if (town != null) {
                        town.Tier = SettlementTier.City;
                        var cohort = mgr.FindOrCreateCivPopulationCohort(town, civ.CommunityId);
                        cohort.Biomass = Mathf.Max(cohort.Biomass * 1.5f, 20f);
                        mgr.RecomputeSettlementPopulation(town);
                        mgr.CheckSettlementJoin(town);
                    }
                    else mgr.SpawnSettlement(civ, SettlementTier.City);
                    civ.DomainEconomic = Mathf.Min(civ.DomainEconomic + 0.15f, 1f);
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
                    // era3-systems-implementation-spec §6: redirected from Stockpile to Economic output.
                    if (civ.Economy != null) civ.Economy.Stock[CivilizationEconomy.Industry] += 0.3f;
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

            // e3_family_norms_emerge deleted (era3-systems-implementation-spec follow-up correction)
            // — its only role was gating d3_kinship_policy's IsEligible, now retargeted to the real
            // I1a node; nothing else ever read this flag.

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
                Id = "e3_state_formation",
                Prereqs = new[] { "e3_chiefdom", "e3_writing" },
                // era3-systems-implementation-spec §9: replaces the old cheap MinTime=100f timer with
                // a density-based trigger mirroring RunawayExposure's exact shape. density = this
                // civ's largest settlement's Population ÷ that settlement's last-computed
                // K_effective (Era3Manager.TickCohortGroup). Threshold 0.4, not 0.7 — K_effective is
                // LOCAL cell carrying capacity, not civ-wide population, and real settlements form
                // well before approaching a hard ecological ceiling. base_rate/exponent match the
                // order of magnitude of RunawayExposure's own (0.0006/1.6), slightly gentler since
                // state formation should read as emergent rather than explosive. Provisional
                // constants, pending a tuning pass once running.
                ExtraGate = civ =>
                {
                    Settlement largest = null;
                    foreach (var s in Settlements)
                        if (s.OwnerCivId == civ.CommunityId && (largest == null || s.Population > largest.Population))
                            largest = s;
                    if (largest == null) return false;

                    float density = largest.Population / Mathf.Max(0.01f, largest.LastKEffective);
                    civ.StateFormationPressure = density > 0.4f
                        ? civ.StateFormationPressure + Time.deltaTime
                        : Mathf.Max(0f, civ.StateFormationPressure - Time.deltaTime * 2f);

                    float probability = 0.0008f * Mathf.Pow(civ.StateFormationPressure, 1.5f);
                    return UnityEngine.Random.value < probability * Time.deltaTime;
                },
                OnFire = (mgr, civ) =>
                {
                    civ.InvestCoercive = Mathf.Min(civ.InvestCoercive + 0.10f, 1f);
                }
            },

            new Era3AutoEvent
            {
                // era3-track-parity-gating-spec §2.1: repointed from e3_diplomacy (which
                // d3_war_or_diplomacy's Diplomacy choice was the ONLY thing that ever set) to
                // FormalAllianceActive, which has a real alternate path (Era3Manager.ProposeAlliance)
                // — same fix as d3_negotiate_treaty, so this event can never be permanently stranded
                // even if d3_war_or_diplomacy is ever gated/unreachable in the future.
                Id = "e3_empire", MinTime = 120f,
                Prereqs = new string[0],
                ExtraGate = civ => civ.FormalAllianceActive,
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

    // ── Settlement system ─────────────────────────────────────────────────────

    // ── Density-based founding ──────────────────────────────────────────────────
    // A species spread across several real population centers should found SEVERAL settlements, not
    // one at the global centroid (which can land in empty space between two clusters). Radius/density
    // are TUNABLE — a "not necessarily high" density bar per design direction, not a strict city test,
    // but MinClusterDensity=3 with a tight ClusterRadius=6 was too permissive in practice: a diffusely
    // scattered population qualified dozens of trivial 3-organism pockets as "settlements" (each seed
    // only looks at its OWN immediate neighborhood, not a merged/flood-filled region), producing many
    // single-digit-population villages instead of a few real centers. Widened radius + raised density
    // bar + a hard cap on settlements founded per event (below) fixes the proliferation.
    private const float ClusterRadius = 14f;
    private const int MinClusterDensity = 6;
    private const int MaxSettlementsPerFounding = 3; // top-N largest clusters only; the rest stay wild

    /// Founds one settlement per real population cluster of a civ's own species (its "centers of
    /// gravity"), absorbing each cluster's members into that settlement's population (the actual
    /// organisms are despawned — they're now represented by Population, not simulated individually;
    /// this is the Era 3 lag fix). Capped to the MaxSettlementsPerFounding largest clusters so a
    /// widely-spread population doesn't spawn a dozen tiny villages at once — smaller pockets stay
    /// wild individuals and can still be absorbed later if they wander near an existing settlement.
    /// Falls back to the old single-centroid, no-absorption placement if the population is too sparse
    /// for even one cluster to meet the density minimum, so a settlement always still forms.
    public void FoundSettlementsFromDensity(CivilizationState civ, SettlementTier tier)
    {
        var clusters = FindPopulationClusters(civ.CommunityId);
        if (clusters.Count == 0) { SpawnSettlement(civ, tier); return; }
        clusters.Sort((a, b) => b.members.Count.CompareTo(a.members.Count)); // largest first
        int n = Mathf.Min(clusters.Count, MaxSettlementsPerFounding);
        for (int i = 0; i < n; i++)
            FoundSettlementAt(civ, tier, clusters[i].center, clusters[i].members);
    }

    private List<(Vector3 center, List<AgentController> members)> FindPopulationClusters(int communityId)
    {
        var result = new List<(Vector3, List<AgentController>)>();
        if (_spawner == null) return result;

        var living = new List<AgentController>();
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == communityId) living.Add(a);

        var claimed = new HashSet<AgentController>();
        var buf = new List<AgentController>();
        foreach (var seed in living)
        {
            if (claimed.Contains(seed)) continue;
            _spawner.QueryNearby(seed.transform.position, ClusterRadius, buf);
            var group = new List<AgentController>();
            foreach (var a in buf)
                if (a != null && a.communityId == communityId && !claimed.Contains(a)) group.Add(a);
            if (group.Count < MinClusterDensity) continue;

            foreach (var a in group) claimed.Add(a);
            Vector3 sum = Vector3.zero;
            foreach (var a in group) sum += a.transform.position;
            result.Add((sum / group.Count, group));
        }
        return result;
    }

    private void FoundSettlementAt(CivilizationState civ, SettlementTier tier, Vector3 clusterCenter, List<AgentController> members)
    {
        Vector3 planetCenter = _spawner != null ? _spawner.planetCenter : Vector3.zero;
        float radius = _spawner != null ? _spawner.planetRadius : 20f;

        var s = new Settlement
        {
            Id           = _nextSettlementId++,
            Name         = $"{civ.Name ?? "Settlement"} {SettlementTierLabel(civ.Path, tier)}",
            Tier         = tier,
            FounderCivId = civ.CommunityId,
            OwnerCivId   = civ.CommunityId,
            RecognizedOwnerCivId = civ.CommunityId, // recognized from the moment it's founded — not occupied
            Population   = members.Count, // real absorbed population, not an abstract flat number
            PlayerCultureFraction = civ.CommunityId == PlayerCiv.CommunityId ? 1f : 0f,
            Position     = SphereSurface.ProjectToSurface(clusterCenter, planetCenter, radius),
        };
        s.ContributingCommunities.Add(civ.CommunityId);
        Settlements.Add(s);

        foreach (var a in members) if (a != null) Destroy(a.gameObject); // absorbed, not simulated individually anymore

        Debug.Log($"[Settlement] {s.Name} founded by civ {civ.CommunityId} at {s.Position} (absorbed {members.Count} organisms).");
    }

    // ── Ongoing absorption growth ───────────────────────────────────────────────
    // After founding, a settlement keeps absorbing nearby population over time — its own species
    // always eligible; OTHER intelligent species only if the settlement's civ has chosen the
    // Multispecies admission policy (see d3_settlement_admission_policy). This is the concrete
    // mechanical payoff of that policy choice: Multispecies settlements draw from a larger pool and
    // grow faster; Species-Locked ones grow only from their own species' local reproduction.
    // Widened from the original 5-unit/8s pass: with reproduction now redirected to abstract growth
    // the moment a civ has a settlement (see AgentController.Reproduce), the only remaining job of
    // this tick is clearing the EXISTING backlog of not-yet-absorbed stragglers — a tight, slow sweep
    // left most of a large population stuck as live individual agents indefinitely (the actual Era 3
    // lag report). TUNABLE.
    private const float AbsorptionRadius = 12f;
    private const float AbsorptionTickInterval = 4f;
    private float _absorptionTimer;

    private void TickSettlementAbsorption()
    {
        _absorptionTimer -= Time.deltaTime;
        if (_absorptionTimer > 0f) return;
        _absorptionTimer = AbsorptionTickInterval;
        if (_spawner == null) return;

        foreach (var s in Settlements)
        {
            var civ = FindCivById(s.OwnerCivId >= 0 ? s.OwnerCivId : s.FounderCivId);
            bool multispecies = civ != null && civ.MultispeciesSettlements;
            int founderCivId = s.FounderCivId;

            // Live position, not the stale founding-time s.Position — see GetCurrentWorldPosition.
            Vector3 currentPos = Era3VisualManager.Instance != null
                ? Era3VisualManager.Instance.GetCurrentWorldPosition(s) : s.Position;
            int absorbed = _spawner.AbsorbNearby(currentPos, AbsorptionRadius, a =>
            {
                bool eligible;
                if (a.communityId == founderCivId) eligible = true; // own species — always eligible
                // Multispecies policy: any OTHER recognized civ's population is eligible too — not
                // random uncivilized wildlife, only fellow "intelligent species" per design direction.
                else if (!multispecies) eligible = false;
                else eligible = _allCivs.Exists(c => c.CommunityId == a.communityId);
                if (eligible)
                {
                    s.ContributingCommunities.Add(a.communityId); // drives the multispecies visual tell
                    // population-energy-aggregation-spec.md §2.1: fold this real organism's actual
                    // trait state into its lineage's cohort at this settlement before it's destroyed,
                    // rather than a flat +1 headcount with no biological data behind it.
                    var cohort = FindOrCreateCivPopulationCohort(s, a.communityId);
                    cohort.SeedOrNudge(a.transform.localScale.x, a.Metabolism, a.Backbone,
                                        a.PhotoEfficiency, a.ChemoEfficiency, 1f);
                }
                return eligible;
            });
            if (absorbed > 0) RecomputeSettlementPopulation(s);
        }
    }

    /// population-energy-aggregation-spec.md §2.0: find-or-create the CivPopulation cohort for a
    /// given lineage at a settlement. Every code path that used to write Settlement.Population or
    /// Settlement.PopulationByCommunity directly now goes through a cohort instead.
    private Cohort FindOrCreateCivPopulationCohort(Settlement s, int lineageCivId)
    {
        foreach (var c in s.Cohorts)
            if (c.Role == CohortRole.CivPopulation && c.LineageId == lineageCivId) return c;
        var created = new Cohort
        {
            LineageId = lineageCivId,
            LocationProxy = s.Id,
            IsZoneBased = false,
            Role = CohortRole.CivPopulation,
            ManagementTier = CohortManagementTier.Wild, // meaningless for CivPopulation — never extracted from
            ManagedByCivId = s.OwnerCivId >= 0 ? s.OwnerCivId : s.FounderCivId,
            Biomass = 1f, // a lineage's first presence at a settlement is at least one founding individual —
                          // also avoids the logistic tick's growth formula being stuck at a permanent 0×(1-0/K)=0
        };
        s.Cohorts.Add(created);
        return created;
    }

    /// Resyncs the plain Settlement.Population cache from the real source of truth (Σ CivPopulation
    /// cohort biomass) — called after anything nudges cohort biomass so every other reader of
    /// Population (rendering, HUD, roster) keeps seeing an up-to-date number without itself knowing
    /// about cohorts.
    private void RecomputeSettlementPopulation(Settlement s)
    {
        float total = 0f;
        foreach (var c in s.Cohorts)
            if (c.Role == CohortRole.CivPopulation) total += c.Biomass;
        s.Population = total;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // COHORT TICK (population-energy-aggregation-spec.md §3/§4) — mean-field logistic growth +
    // Settlement Energy Balance. Replaces per-agent simulation as the population/energy substrate
    // for every lineage once it has a Cohort (i.e. once per-agent absorption has begun).
    // ══════════════════════════════════════════════════════════════════════════
    private const float CohortTickInterval = 5f; // same "slow simulation" cadence family as PolityTickInterval
    private float _cohortTimer;

    private const float LogisticGrowthRate = 0.15f;  // r — TUNABLE, spec leaves this open
    private const float TrophicEfficiency  = 0.10f;  // consumer cohort yield discount — spec's own ~0.1
    // §4.3 extraction tiers: wild (real depletion risk) < LLFP < domesticated (highest cap, ~no
    // depletion risk per domestication-spec.md §2). Exact values TUNABLE, spec doesn't give numbers.
    private static float ExtractionMultiplier(CohortManagementTier tier) => tier switch
    {
        CohortManagementTier.Wild        => 0.4f,
        CohortManagementTier.LLFP        => 0.7f,
        CohortManagementTier.Domesticated => 1.0f,
        _ => 0.4f,
    };
    // Design decision (flagged — see CohortEnergyModel's own header comment on the individual-to-
    // aggregate bridge): managed Wild/LLFP/Domesticated cohorts are self-sufficient producers/
    // consumers whose own local environment sets their ceiling, not the settlement's aggregate
    // energy balance (that only gates CivPopulation growth, per the spec's literal formula — see
    // below). Since photosynthesis has no natural depletion analog at cohort granularity (only
    // ChemicalNutrientPool does) there is no in-code "local flux ceiling" to port 1:1; this constant
    // stands in for the physical crowding/space cap the real per-agent simulation got for free from
    // GameObjects not being able to occupy the same space. TUNABLE.
    private const float ReferenceCarryingDensity = 40f;

    private void TickCohorts()
    {
        _cohortTimer -= Time.deltaTime;
        if (_cohortTimer > 0f) return;
        _cohortTimer = CohortTickInterval;

        Vector3 planetCenter = _spawner != null ? _spawner.planetCenter : Vector3.zero;

        foreach (var s in Settlements)
        {
            if (s.Cohorts.Count == 0) continue;
            Vector3 pos = Era3VisualManager.Instance != null
                ? Era3VisualManager.Instance.GetCurrentWorldPosition(s) : s.Position;
            TickCohortGroup(s.Cohorts, pos, planetCenter, false, s);
            RecomputeSettlementPopulation(s);
        }

        // population-energy-aggregation-spec.md §3.1: zone-based tracks' per-cell cohorts, same
        // energy-balance + logistic-growth model, just no Settlement.Population cache to resync.
        foreach (var cell in _territoryCells.Values)
        {
            if (cell.Cohorts.Count == 0) continue;
            Vector3 pos = _cellWorldPosLookup != null ? _cellWorldPosLookup(cell.CellId) : planetCenter;
            TickCohortGroup(cell.Cohorts, pos, planetCenter, cell.IsContested);
        }
    }

    /// Shared Settlement Energy Balance + mean-field logistic growth body (population-energy-
    /// aggregation-spec.md §4.2-§4.4), reused for both Settlement.Cohorts and TerritoryCell.Cohorts —
    /// the formulas don't care which kind of place a cohort lives at, only its own trait_snapshot and
    /// the aggregate energy available there.
    // era3-sovereignty-interaction-gaps-spec.md §3.1: "if combined draw exceeds regen rate, the
    // cohort depletes faster — real overexploitation, emergent from existing math." A contested cell
    // has at least one other civ's claim also reaching it, but that civ's own separate extraction
    // isn't independently simulated (out of scope — see TerritoryCell.IsContested's own comment);
    // this multiplier stands in for that unmodeled second draw by tightening the shared ceiling,
    // the same qualitative effect a second real extractor would have. TUNABLE.
    private const float ContestedCarryingCapacityPenalty = 0.6f;

    private void TickCohortGroup(List<Cohort> cohorts, Vector3 pos, Vector3 planetCenter, bool contested = false, Settlement settlement = null)
    {
        // ── Settlement Energy Balance (§4.2/§4.3) ──────────────────────────────────────────────
        // settlement_energy_input = Σ producer cohort yield + Σ consumer cohort yield×trophic_efficiency,
        // both discounted by their management tier's extraction multiplier. The civ's own population
        // is never "extracted from" — it's excluded from this sum entirely.
        float energyInput = 0f;
        foreach (var c in cohorts)
        {
            if (c.Role == CohortRole.CivPopulation) continue;
            float extractMult = ExtractionMultiplier(c.ManagementTier);
            if (c.MetabolicClass == CohortMetabolicClass.Producer)
                energyInput += CohortEnergyModel.ComputeProducerYieldPerBiomass(c.Traits, pos, planetCenter) * c.Biomass * extractMult;
            else
                energyInput += CohortEnergyModel.ComputeDemandPerBiomass(c.Traits, pos) * c.Biomass * TrophicEfficiency * extractMult;
        }

        // settlement_energy_demand = civ_population_biomass × Kleiber_BMR_rate (+ Σ upkeep — no
        // existing per-structure energy-upkeep field to ground this against, so omitted rather than
        // inventing one; StructureInstance's UpkeepCost is a warfare/StandingForce concept, not an
        // energy concept).
        float civPopDemand = 0f;
        foreach (var c in cohorts)
        {
            if (c.Role != CohortRole.CivPopulation) continue;
            civPopDemand += CohortEnergyModel.ComputeDemandPerBiomass(c.Traits, pos) * c.Biomass;
        }
        // netEnergyBalance (energyInput - civPopDemand) is a diagnostic quantity only. Open item
        // (flagged in population-energy-aggregation-spec.md): no spec'd consequence for a negative
        // balance beyond gating CivPopulation's own carrying capacity below — a starving settlement's
        // population growth stalls/reverses via K_effective; there is no additional famine-event
        // mechanic layered on top here.

        // ── Mean-field logistic growth: dBiomass/dt = r × Biomass × (1 − Biomass/K_effective) ──────
        foreach (var c in cohorts)
        {
            float demandPerBiomass = CohortEnergyModel.ComputeDemandPerBiomass(c.Traits, pos);
            if (demandPerBiomass <= 0.0001f) continue;

            float kEffective;
            float r = LogisticGrowthRate;
            if (c.Role == CohortRole.CivPopulation)
            {
                // Spec's literal formula: this cohort's carrying capacity is the settlement's
                // aggregate harvested energy divided by its own per-biomass BMR rate.
                kEffective = energyInput / demandPerBiomass;
                var civ = FindCivById(c.ManagedByCivId ?? -1);
                if (civ != null)
                {
                    r *= Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.PopulationGrowth);
                    // era3-systems-implementation-spec §1: Biological channel's direct cost is
                    // "slows PopGrowth" — the one channel dial not routed through CivilizationEconomy.
                    r *= Mathf.Max(0.1f, 1f - civ.InvestBiological * 0.20f);
                    // era3-systems-implementation-spec §2: ParentalInvestment r/K tradeoff (Individuated
                    // A1/A3 only) — low investment favors more/faster offspring, high investment trades
                    // PopGrowth for the GenDMin/ResilienceFloor bonus applied at its other consumption
                    // sites (plague defense, ExtractionTax floor).
                    if (civ.Architecture == CognitiveArchitecture.Individuated)
                        r *= Mathf.Lerp(1.15f, 0.85f, civ.ParentalInvestment);
                    // ReproductiveSuppressRatio (Collective only) — trades PopGrowth down for
                    // EconCapability up (Capability() below applies the other side).
                    if (civ.Architecture == CognitiveArchitecture.Collective)
                        r *= Mathf.Max(0.3f, 1f - civ.ReproductiveSuppressRatio * 0.25f);

                    // era3-systems-implementation-spec §8: Large Initiative — the three tracks whose
                    // effects live here rather than in CivilizationEconomy/Era3Warfare.
                    if (civ.Path == Era3Path.LivingReef)
                    {
                        if (civ.LargeInitiativeActive) kEffective *= 0.85f;    // -15% K_effective growth, ongoing
                        if (civ.LargeInitiativeCompleted) kEffective *= 1.15f; // +15% K_effective, permanent
                    }
                    else if (civ.Path == Era3Path.BloomFront)
                    {
                        if (civ.LargeInitiativeActive) r *= 0.8f;              // -20% PopGrowth, ongoing
                        if (civ.LargeInitiativeCompleted) kEffective *= 1.05f; // small permanent K_effective boost (the immediate Biomass surge itself fires once in TickLargeInitiative)
                    }
                    else if (civ.Path == Era3Path.ApexPredator && civ.LargeInitiativeActive)
                    {
                        // Ongoing cost: "tax on Biomass (predation/food reserves)".
                        c.Biomass = Mathf.Max(0f, c.Biomass * (1f - 0.02f * CohortTickInterval));
                    }
                }
                if (settlement != null) settlement.LastKEffective = Mathf.Max(0.01f, kEffective);
            }
            else
            {
                // Self-sufficient managed/wild cohort: its own environment (yield-to-demand ratio)
                // sets its ceiling, scaled by the flagged crowding-cap constant above.
                float yieldPerBiomass = c.MetabolicClass == CohortMetabolicClass.Producer
                    ? CohortEnergyModel.ComputeProducerYieldPerBiomass(c.Traits, pos, planetCenter)
                    : demandPerBiomass; // a wild consumer's own throughput ≈ its own maintenance rate in equilibrium
                kEffective = ReferenceCarryingDensity * (yieldPerBiomass / demandPerBiomass);
                if (contested) kEffective *= ContestedCarryingCapacityPenalty;
            }

            float deltaBiomass;
            if (kEffective <= 0.001f)
                deltaBiomass = -r * c.Biomass; // starvation decay — no divide-by-zero cliff
            else
                deltaBiomass = r * c.Biomass * (1f - c.Biomass / kEffective);

            c.Biomass = Mathf.Max(0f, c.Biomass + deltaBiomass * CohortTickInterval);
        }

        TickCohortInteractions(cohorts);
    }

    // ── Interaction Matrix cohort-level port (era3-sovereignty-interaction-gaps-spec.md §4) ─────────
    // §4.2 open item resolved: rides along with the existing cohort update (called from
    // TickCohortGroup above) rather than a separate tick pass — same reasoning as folding
    // TerritoryCell sync into RebuildTerritory's existing loop.
    private const float CohortInteractionEffectMagnitude = 0.02f; // TUNABLE — per-tick fraction of the smaller cohort's biomass moved
    private const float CohortInteractionBaseWeight = 0.4f; // matches SpeciesRelationshipManager.BaseWeight's flat default
    private readonly Dictionary<(int, int), InteractionType> _cohortInteractions = new Dictionary<(int, int), InteractionType>();
    private readonly Dictionary<(int, int), int> _cohortContactTicks = new Dictionary<(int, int), int>();

    private static (int, int) LineagePair(int a, int b) => a <= b ? (a, b) : (b, a);

    /// §4.1: two cohorts sharing a location_proxy (i.e. both present in the SAME cohorts list this
    /// tick — settlement or territory cell) interact per whatever classification applies between
    /// their lineages, established via the same proximity×duration-style trigger
    /// SpeciesRelationshipManager used for physical proximity, just keyed on co-location instead.
    private void TickCohortInteractions(List<Cohort> cohorts)
    {
        for (int i = 0; i < cohorts.Count; i++)
        {
            for (int j = i + 1; j < cohorts.Count; j++)
            {
                var ca = cohorts[i]; var cb = cohorts[j];
                // Unowned synthetic Wild cohorts (LineageId -1, e.g. FindOrSeedDomesticationTarget's
                // target) have no real lineage identity to establish an interspecies relationship
                // with — domestication/extraction already governs their relationship to a civ.
                if (ca.LineageId == cb.LineageId || ca.LineageId < 0 || cb.LineageId < 0) continue;

                var key = LineagePair(ca.LineageId, cb.LineageId);
                if (!_cohortInteractions.TryGetValue(key, out var type))
                {
                    _cohortContactTicks.TryGetValue(key, out int ticks);
                    ticks = Mathf.Min(ticks + 1, 20);
                    _cohortContactTicks[key] = ticks;
                    float durationFactor = ticks >= 15 ? 1f : ticks >= 10 ? 0.75f : ticks >= 5 ? 0.5f : 0.2f;
                    if (UnityEngine.Random.value < CohortInteractionBaseWeight * durationFactor * 0.1f)
                        _cohortInteractions[key] = CohortInteractionModel.RollType(ca, cb);
                    continue; // no effect the tick it establishes — matches the original's own shape
                }
                // Stable ordering (lower LineageId first) so a directional effect (Parasitism) doesn't
                // flip which side benefits from tick to tick based on incidental list order.
                var (first, second) = ca.LineageId <= cb.LineageId ? (ca, cb) : (cb, ca);
                ApplyCohortInteractionEffect(first, second, type);
            }
        }
    }

    /// §4.1's InteractionEffect, applied as a direct biomass modifier (this Cohort model's analog of
    /// SpeciesRelationshipManager.ApplyEffects' ReceiveRelationshipBonus/Drain) — scaled by the
    /// SMALLER of the two cohorts' biomass so a large cohort can't trivially devastate a tiny one
    /// through a flat-rate effect.
    private void ApplyCohortInteractionEffect(Cohort first, Cohort second, InteractionType type)
    {
        if (type == InteractionType.Neutralism) return;
        float amount = CohortInteractionEffectMagnitude * Mathf.Min(first.Biomass, second.Biomass) * CohortTickInterval;
        switch (type)
        {
            case InteractionType.Mutualism:
                first.Biomass += amount; second.Biomass += amount; break;
            case InteractionType.Commensalism:
                first.Biomass += amount; break; // second unaffected
            case InteractionType.Parasitism:
                // first drains from second (same arbitrary-but-stable directionality as
                // SpeciesRelationshipManager's ascending-id MakeKey convention).
                second.Biomass = Mathf.Max(0f, second.Biomass - amount);
                first.Biomass += amount * 0.5f; // assimilation loss
                break;
            case InteractionType.Competition:
                first.Biomass  = Mathf.Max(0f, first.Biomass  - amount * 0.5f);
                second.Biomass = Mathf.Max(0f, second.Biomass - amount * 0.5f);
                break;
            case InteractionType.Amensalism:
                second.Biomass = Mathf.Max(0f, second.Biomass - amount); break; // first unaffected
        }
    }

    // ── Cull policy (era3-sovereignty-interaction-gaps-spec.md §4) ─────────────────────────────
    // Player-facing, spend Coercive/Economic channel investment to suppress a specific cohort's
    // biomass near a settlement. Reuses the extraction-cap math directly (§4.3) — culling is just
    // another draw against the cohort's biomass, same as extraction, just civ-initiated and untethered
    // from yield/food purpose. Reduces, doesn't instantly destroy: the cohort's own logistic regen
    // (TickCohortGroup) applies next tick as normal, so only sustained culling faster than regen drives
    // a cohort toward local extinction — the same emergent over-extraction consequence as wild-tier
    // extraction, not a separate destroy mechanic.
    private const float CullIntensityRate = 0.5f; // TUNABLE (§4.2 open item) — intensity-to-investment conversion rate

    /// The single largest non-CivPopulation cohort at a settlement — the real target Cull acts on.
    /// Same target-selection convention as FindOrSeedDomesticationTarget (auto-select rather than a
    /// full cohort-picker UI).
    private Cohort FindCullTarget(Settlement s)
    {
        Cohort target = null;
        foreach (var c in s.Cohorts)
            if (c.Role != CohortRole.CivPopulation && (target == null || c.Biomass > target.Biomass)) target = c;
        return target;
    }

    public bool HasCullableCohort(Settlement s) => FindCullTarget(s) != null;

    public bool CullCohortAtSettlement(CivilizationState civ, Settlement settlement)
    {
        if (settlement.OwnerCivId != civ.CommunityId) return false;
        var target = FindCullTarget(settlement);
        if (target == null) return false;

        float channelInvestment = Mathf.Max(civ.InvestCoercive, civ.InvestEconomic);
        float cullDraw = Mathf.Min(target.Biomass, CullIntensityRate * channelInvestment);
        target.Biomass -= cullDraw;
        LogEvent($"{civ.Name} culls {cullDraw:F1} biomass near {settlement.Name}.");
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DOMESTICATION (domestication-spec.md, revised by era3-systems-implementation-spec §3) — gated
    // by I_domestication (Era3TechTree, Commerce Engine + Apex Predator — both "builder" tracks) or
    // A_domestication (Era3AdaptationTree, Living Reef/Terraformer/BloomFront — Apex Predator moved
    // here from the Adaptation tree once it lost Adaptation-tree access entirely).
    // ══════════════════════════════════════════════════════════════════════════
    public static bool HasDomesticationGate(CivilizationState civ) =>
        civ.UnlockedNodes.Contains("I_domestication") || civ.UnlockedAdaptations.Contains("A_domestication");

    /// domestication-spec.md §2: sets management_tier = Domesticated (highest extraction cap, ~no
    /// depletion risk per ExtractionMultiplier above). Target scope (§2): must be a non-sovereign wild
    /// population — never CivPopulation (a civ's own people aren't "domesticated"), and its LineageId
    /// must not itself resolve to a recognized civilization (the real proxy this codebase has for
    /// "never crossed the Era 2 intelligence threshold" — a cohort whose lineage IS a civ is by
    /// definition sapient and sovereign, which is conquest/vassalage territory instead, per §2's own
    /// explicit scope note that that mechanic doesn't exist yet).
    public bool DomesticateCohort(Cohort cohort, int civId)
    {
        if (cohort.Role == CohortRole.CivPopulation) return false;
        if (FindCivById(cohort.LineageId) != null) return false; // sovereign civ — not a valid target
        cohort.ManagementTier = CohortManagementTier.Domesticated;
        cohort.ManagedByCivId = civId;
        return true;
    }

    /// The d3_domesticate_species card's target-finding step. Prefers an existing Wild-tier cohort at
    /// the civ's largest settlement; if none exists yet, synthesizes one representing the local wild
    /// species being brought under management — a flagged simplification, since this codebase has no
    /// standalone "ambient wildlife cohort" simulation layer independent of civ population/zone reach
    /// (population-energy-aggregation-spec.md scopes Cohorts to civ population + zone-based reach
    /// only; a full wildlife simulation is out of scope here).
    public Cohort FindOrSeedDomesticationTarget(CivilizationState civ)
    {
        Settlement best = null;
        foreach (var s in Settlements)
            if (s.OwnerCivId == civ.CommunityId && (best == null || s.Population > best.Population))
                best = s;
        if (best == null) return null;

        foreach (var c in best.Cohorts)
            if (c.Role != CohortRole.CivPopulation && c.ManagementTier == CohortManagementTier.Wild
                && FindCivById(c.LineageId) == null)
                return c;

        var wild = new Cohort
        {
            LineageId = -1, // no owning civ of its own — a real wild population, not a lineage's civilization
            LocationProxy = best.Id,
            IsZoneBased = false,
            Role = CohortRole.Resource,
            ManagementTier = CohortManagementTier.Wild,
            // MetabolicClass is computed from Traits.Metabolism, left at its Heterotrophic default —
            // a generic synthesized "local wild species" defaults to livestock-like rather than
            // crop-like, a reasonable default absent any other signal for which kind it should be.
            Biomass = 3f, // a modest founding wild population to bring under management
        };
        // Same-biosphere assumption: the local wild species shares the civ's own backbone chemistry,
        // a reasonable default absent any other per-location wildlife trait data.
        wild.Traits.Backbone = FindCivCoreCohort(civ.CommunityId)?.Traits.Backbone ?? BackboneElement.Carbon;
        best.Cohorts.Add(wild);
        return wild;
    }

    private CivilizationState FindCivById(int civId)
    {
        if (PlayerCiv != null && PlayerCiv.CommunityId == civId) return PlayerCiv;
        foreach (var c in NpcCivs) if (c.CommunityId == civId) return c;
        return null;
    }

    // ── Real conquest / attack resolution ───────────────────────────────────────
    // Requires a conscious DeclareWar (above) between the two civs — no more automatic strikes just
    // from having invested in the doctrine, per the design's explicit "war should be a deliberate
    // choice, not fully automatic" direction. Range is now tech-scaled per civ (ProjectionRange)
    // instead of one fixed constant, with a super-linear Overextension penalty beyond it
    // (era3-warfare-mechanics-spec §3-4) rather than a hard cutoff. Kinetic-leaning civs conquer
    // (flip OwnerCivId); biochemical-leaning civs weaken instead of capturing. Target-subsystem
    // selection (§9) lets the attacker choose what a strike actually damages, gated by
    // attacker.WarTargetSubsystem — the settlement-conquest/population-loss behavior below is the
    // Population subsystem, the pre-existing default.
    private const float ConflictCheckInterval = 15f;
    private const float AttackFlashSeconds = 5f;
    private float _conflictTimer;

    /// Settlement id -> Time.time when its post-attack visual flash should stop. Read by
    /// Era3VisualManager to pulse a marker red for a few seconds after it's struck.
    public readonly Dictionary<int, float> RecentAttackFlash = new Dictionary<int, float>();

    private void TickConflict()
    {
        _conflictTimer -= Time.deltaTime;
        if (_conflictTimer > 0f) return;
        _conflictTimer = ConflictCheckInterval;

        foreach (var attacker in _allCivs)
        {
            if (!attacker.Has("e3_warfare_organized")) continue; // must have chosen the war doctrine
            float strength = attacker.DomainKinetic + attacker.DomainBiochemical * 0.5f;
            if (strength < 0.15f) continue; // needs real investment, not just the doctrine flag
            if (UnityEngine.Random.value > strength * 0.4f) continue; // chance per check, scaled by investment

            // Only settlements belonging to a civ this attacker is ACTUALLY at war with (DeclareWar).
            // Undeclared aggression is handled separately by CovertStrike, which any civ can call
            // regardless of formal war state.
            var target = FindNearestSettlement(attacker, requireAtWar: true, out float dist);
            if (target == null) continue;

            float reach = attacker.ProjectionRange * Era3Warfare.ProjectionRangeWorldScale;
            if (dist > reach * 1.5f) continue; // beyond even an overextended reach

            ApplyStrike(attacker, target, dist);
        }
    }

    /// Nearest enemy settlement to any of the attacker's own — live positions via
    /// GetCurrentWorldPosition, not the stale founding-time Settlement.Position, which drifts wrong
    /// as the planet rotates. requireAtWar=false is CovertStrike's undeclared-aggression path.
    private Settlement FindNearestSettlement(CivilizationState attacker, bool requireAtWar, out float bestDist)
    {
        var vis = Era3VisualManager.Instance;
        Settlement target = null; bestDist = float.MaxValue;
        foreach (var mine in Settlements)
        {
            if (mine.OwnerCivId != attacker.CommunityId) continue;
            Vector3 minePos = vis != null ? vis.GetCurrentWorldPosition(mine) : mine.Position;
            foreach (var enemy in Settlements)
            {
                if (enemy.OwnerCivId == attacker.CommunityId) continue;
                if (requireAtWar && !IsAtWar(attacker.CommunityId, enemy.OwnerCivId)) continue;
                Vector3 enemyPos = vis != null ? vis.GetCurrentWorldPosition(enemy) : enemy.Position;
                float d = Vector3.Distance(minePos, enemyPos);
                if (d < bestDist) { bestDist = d; target = enemy; }
            }
        }
        return target;
    }

    /// Resolves one strike against a settlement per the attacker's chosen WarTargetSubsystem —
    /// shared by TickConflict's declared-war strikes and CovertStrike's undeclared ones.
    private void ApplyStrike(CivilizationState attacker, Settlement target, float distance)
        => ApplyStrike(attacker, target, distance, attacker.WarTargetSubsystem);

    private void ApplyStrike(CivilizationState attacker, Settlement target, float distance, Era3Warfare.WarSubsystem subsystem)
    {
        float overext = Era3Warfare.OverextensionMultiplier(distance, attacker);
        bool bioweapon = attacker.DomainBiochemical > attacker.DomainKinetic;
        var targetCiv = GetCiv(target.OwnerCivId);

        switch (subsystem)
        {
            case Era3Warfare.WarSubsystem.Military when targetCiv != null:
                float forceLoss = Mathf.Min(targetCiv.StandingForce, Mathf.Max(0.5f, targetCiv.StandingForce * 0.3f * overext));
                targetCiv.StandingForce -= forceLoss;
                LogEvent($"{attacker.Name} engages {target.Name}'s forces (-{forceLoss:F1} standing force).");
                break;

            case Era3Warfare.WarSubsystem.Production when targetCiv != null:
                // era3-systems-implementation-spec §6: War cost redirected from Stockpile to Economic
                // output — min(currentEconomicOutput, 0.4×overextension), drained from Industry stock.
                float targetOutput = targetCiv.Economy?.Output[CivilizationEconomy.Industry] ?? 0f;
                float drain = Mathf.Min(targetOutput, 0.4f * overext);
                if (targetCiv.Economy != null)
                    targetCiv.Economy.Stock[CivilizationEconomy.Industry] = Mathf.Max(0f, targetCiv.Economy.Stock[CivilizationEconomy.Industry] - drain);
                LogEvent($"{attacker.Name} raids {target.Name}'s production (-{drain:F2}).");
                break;

            case Era3Warfare.WarSubsystem.Structures when targetCiv != null && targetCiv.BuiltStructures.Count > 0:
                // Destroys the oldest instance — a real target for a strike, not an arbitrary pick.
                CivilizationState.StructureInstance oldest = null;
                foreach (var s in targetCiv.BuiltStructures)
                    if (oldest == null || s.Age > oldest.Age) oldest = s;
                if (oldest != null) targetCiv.BuiltStructures.Remove(oldest);
                LogEvent($"{attacker.Name} destroys a structure in {target.Name}.");
                break;

            default: // Population — the settlement itself changes hands either way now (§9
                     // Occupation, not Annexation — still needs FormalizeOccupiedTerritory to stick).
                int previousOwner = target.OwnerCivId;
                if (bioweapon)
                {
                    // "There is no military primitive" (warfare spec §7): a biochemical/colonial
                    // track doesn't storm a settlement, it OVERGROWS it — lower population cost than
                    // a kinetic conquest, but still a real, mechanical occupation (flips OwnerCivId),
                    // not just a plague that leaves the target's territory untouched. Previously this
                    // branch only killed population and never captured anything.
                    // era3-policy-catalog-spec GenDMin (Public Health Investment, Quarantine Regime,
                    // Immune Caste Investment, ...): raises the target's floor of damage resistance
                    // against Genetic-channel attacks specifically.
                    // era3-systems-implementation-spec §2: ParentalInvestment's high-investment side
                    // (Individuated only) — fewer, higher-quality offspring reads as sturdier defense.
                    float parentalBonus = targetCiv != null && targetCiv.Architecture == CognitiveArchitecture.Individuated
                        ? targetCiv.ParentalInvestment * 0.15f : 0f;
                    float defense = targetCiv != null ? Mathf.Clamp01(Era3PolicyCatalog.GetVar(targetCiv, Era3PolicyCatalog.Var.GenDMin) + parentalBonus) : 0f;
                    int loss = Mathf.Max(1, Mathf.RoundToInt(target.Population * 0.15f * overext * (1f - defense)));
                    target.Population = Mathf.Max(1f, target.Population - loss);
                    target.OwnerCivId = attacker.CommunityId;
                    target.ContributingCommunities.Add(attacker.CommunityId);
                    LogEvent($"{attacker.Name} overgrows {target.Name} (-{loss} population).");
                    Debug.Log($"[Era3][War] {attacker.Name} overgrew {target.Name} from civ {previousOwner}: -{loss} pop.");
                }
                else
                {
                    int loss = Mathf.Max(1, Mathf.RoundToInt(target.Population * 0.20f * overext));
                    target.Population = Mathf.Max(1f, target.Population - loss);
                    target.OwnerCivId = attacker.CommunityId;
                    target.ContributingCommunities.Add(attacker.CommunityId);
                    LogEvent($"{attacker.Name} conquers {target.Name} from civ {previousOwner}.");
                    Debug.Log($"[Era3][War] {attacker.Name} conquered {target.Name} from civ {previousOwner} (-{loss} pop in the fighting).");
                }
                // era3-diplomacy-ai-spec §1.2 "sustained hostile occupation" — a conquest that hasn't
                // yet been ratified by FormalizeOccupiedTerritory IS the sustained-occupation state
                // (Settlement.IsOccupied), so this fires each re-trigger.
                if (previousOwner >= 0) ApplyRelationEvent(attacker.CommunityId, previousOwner, Era3Diplomacy.ValenceHostileOccupation);
                break;
        }
        RecentAttackFlash[target.Id] = Time.time + AttackFlashSeconds;
    }

    /// Sum of every settlement's Population, across all civs. Once a community's organisms are
    /// absorbed into a settlement (see FoundSettlementAt/TickSettlementAbsorption) they're removed
    /// from AgentSpawner.ActiveAgents — that's the whole point, it's the Era 3 lag fix — but it means
    /// any population readout that only counts ActiveAgents silently UNDERCOUNTS by however many
    /// organisms have been folded into settlements. Callers that want a true total must add this.
    public float TotalSettlementPopulation()
    {
        float sum = 0f;
        foreach (var s in Settlements) sum += s.Population;
        return sum;
    }

    // ── Polity Model (era3-polity-model-spec §2-§5) ─────────────────────────────────────────
    // AdministrativeReach/SplinterPressure/Administrative Crisis, the population roster, and the
    // SpeciesDisposition ledger. Vassalization (TryVassalize above) and Conquest (TickConflict's
    // OwnerCivId flip) already implement two of the spec's three unification paths; Federation is
    // added below as the third — a full merge, distinct from a vassal's tribute relationship.
    private const float PolityTickInterval = 5f;
    private float _polityTimer;
    private const float AdminCrisisThreshold = 0.75f;

    // Species-pair disposition, keyed by ordered (min,max) FOUNDING community id — this persists
    // even after a Federation merges one civ's roster into another's, since it's about the species'
    // history, not the current polity boundary. Lazily seeded from actual Era 1/2 interaction
    // history the moment either species is first asked about (see GetSpeciesDisposition).
    private readonly Dictionary<(int, int), float> _speciesDisposition = new Dictionary<(int, int), float>();

    private static (int, int) OrderedPair(int a, int b) => a <= b ? (a, b) : (b, a);

    /// [-1,1] species-level disposition between two founding communities. Seeded once from
    /// SpeciesRelationshipManager's actual recorded interaction (predation/mutualism/etc. history),
    /// then slow-drifted by Era3Diplomacy's bidirectional feedback (diplomacy-ai-spec §1.3) once
    /// polities of these species start interacting directly.
    public float GetSpeciesDisposition(int communityA, int communityB)
    {
        var key = OrderedPair(communityA, communityB);
        if (_speciesDisposition.TryGetValue(key, out float v)) return v;

        float seed = 0f;
        if (SpeciesRelationshipManager.Instance != null)
            seed = Era3Polity.SeedFromInteraction(
                SpeciesRelationshipManager.Instance.GetRelationship(communityA, communityB));
        _speciesDisposition[key] = seed;
        return seed;
    }

    public void SetSpeciesDisposition(int communityA, int communityB, float value)
        => _speciesDisposition[OrderedPair(communityA, communityB)] = Mathf.Clamp(value, -1f, 1f);

    // ── PolityRelation (era3-diplomacy-ai-spec §1) ───────────────────────────────────────────
    // Dominant, fast-moving tier — what two POLITIES have actually done to each other, layered on
    // top of the slow SpeciesDisposition background above. Seeded from SpeciesDisposition at first
    // contact (§1.1), then updated by ApplyRelationEvent (§1.2) at the concrete event sites below.
    private readonly Dictionary<(int, int), float> _polityRelation = new Dictionary<(int, int), float>();

    public float GetPolityRelation(int civA, int civB)
    {
        var key = OrderedPair(civA, civB);
        if (_polityRelation.TryGetValue(key, out float v)) return v;
        float seed = GetSpeciesDisposition(civA, civB); // §1.1 first-contact seeding
        _polityRelation[key] = seed;
        return seed;
    }

    /// §1.2 update: EMA toward event_valence, plus a weak continuous drag toward the (possibly
    /// still-drifting) SpeciesDisposition baseline. Also feeds §1.3's bidirectional write-back —
    /// applied lazily the next time GetSpeciesDisposition-adjacent code reads it would be more
    /// faithful to a continuous EMA, but a direct blend-on-event keeps this a single call site
    /// rather than a third always-on tick, and reaches the same qualitative behavior (grudges/
    /// friendships generalize proportional to how much the pair's relation actually moved).
    public void ApplyRelationEvent(int civA, int civB, float eventValence)
    {
        var key = OrderedPair(civA, civB);
        float prev = GetPolityRelation(civA, civB);
        float speciesBaseline = GetSpeciesDisposition(civA, civB);
        float updated = prev * (1f - Era3Diplomacy.LambdaPr) + Era3Diplomacy.LambdaPr * eventValence
                      + Era3Diplomacy.PullToSpecies * (speciesBaseline - prev);
        updated = Mathf.Clamp(updated, -1f, 1f);
        _polityRelation[key] = updated;

        // §1.3 bidirectional feedback, size-weighted by the two polities' population.
        var a = GetCiv(civA); var b = GetCiv(civB);
        if (a == null || b == null) return;
        float popA = TotalSettlementPopulationForCiv(civA), popB = TotalSettlementPopulationForCiv(civB);
        float sizeWeight = Mathf.Clamp01((popA + popB) / 200f); // normalized against a nominal "large polity" scale
        float newSpecies = speciesBaseline * (1f - Era3Diplomacy.LambdaSd) + Era3Diplomacy.LambdaSd * updated * sizeWeight;
        SetSpeciesDisposition(civA, civB, newSpecies);
    }

    /// Lineage-aggregated average of Phase 1's new contestPropensity/boldness traits (era3-
    /// primitives-spec §2) — samples currently-live organisms of a community; falls back to the
    /// species default once a community's population is fully absorbed into settlements (Phase 1's
    /// whole point) and no live samples remain.
    public float AverageContestPropensity(int communityId, float fallback = 30f)
    {
        if (_spawner == null) return fallback;
        float sum = 0f; int n = 0;
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == communityId) { sum += a.contestPropensity; n++; }
        return n > 0 ? sum / n : fallback;
    }

    public float AverageBoldness(int communityId, float fallback = 50f)
    {
        if (_spawner == null) return fallback;
        float sum = 0f; int n = 0;
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == communityId) { sum += a.boldness; n++; }
        return n > 0 ? sum / n : fallback;
    }

    /// era3-adaptation-trees-spec §2.2: ReproductiveRate — reuses eatsToReproduce (lower = faster
    /// reproduction, already the existing per-organism reproduction-speed trait) rather than
    /// inventing a new stat. Fallback (5) doubles as R_reference — a mid-range generation time.
    public float AverageEatsToReproduce(int communityId, float fallback = 5f)
    {
        if (_spawner == null) return fallback;
        float sum = 0f; int n = 0;
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == communityId) { sum += a.eatsToReproduce; n++; }
        return n > 0 ? sum / n : fallback;
    }

    /// era3-adaptation-trees-spec §2.2: "VariationFactor reads genetic diversity, not
    /// structural_variation." Real (if narrow) proxy: normalized standard deviation of
    /// contestPropensity across the civ's currently-live population — a single evolvable trait
    /// standing in for overall genetic diversity, not a full multi-trait composite (documented
    /// simplification, same honest-approximation pattern as Era3EcologicalPaths' own header note).
    public float GeneticDiversity(int communityId)
    {
        if (_spawner == null) return 0.3f;
        var vals = new List<float>();
        foreach (var a in _spawner.ActiveAgents)
            if (a != null && a.communityId == communityId) vals.Add(a.contestPropensity);
        if (vals.Count < 2) return 0.2f;
        float mean = 0f; foreach (var v in vals) mean += v; mean /= vals.Count;
        float variance = 0f; foreach (var v in vals) variance += (v - mean) * (v - mean); variance /= vals.Count;
        return Mathf.Clamp01(Mathf.Sqrt(variance) / 25f);
    }

    /// era3-adaptation-trees-spec §2.2/§3 open item 1: SelectionPressure, resolved with real (if
    /// approximate) civ-level signals actually available — a fully faithful per-pressure-type read
    /// would need per-cell resource/competitor data this codebase doesn't track at civ granularity.
    public float SelectionPressure(CivilizationState civ, string adaptationNodeId)
    {
        // era3-systems-implementation-spec §6: Stockpile retired — GDP (already a real "how
        // well-resourced is this civ" indicator, CivilizationEconomy.Tick) is the replacement proxy.
        float gdp       = civ.Economy?.GDP ?? 1f;
        float scarcity  = Mathf.Clamp01(1f - (gdp - 1f) / 2f);   // low GDP = scarce
        float surplus   = Mathf.Clamp01((gdp - 1f) / 3f);        // high GDP = surplus
        float crowding  = Mathf.Clamp01(civ.SplinterPressure);      // proxy for intraspecific competition/saturation
        float instability = Mathf.Clamp01(1f - civ.Resilience);     // environmental/existential stress
        // Conflict Posture chosen and not the de-escalation option (index 2 in every ecological
        // path's ConflictPosture table) — the real, already-tracked "is this civ currently fighting"
        // signal for tracks that don't use IsAtWar (that's CommerceEngine-only).
        float conflict  = (civ.EcoConflictPosture >= 0 && civ.EcoConflictPosture != 2) ? 1f : 0f;

        return adaptationNodeId switch
        {
            "A1a" => Mathf.Clamp01(scarcity * 0.6f + crowding * 0.4f), // patchiness ≈ scarcity+crowding
            "A1b" => scarcity,
            "A1c" => instability,
            "A2a" => crowding,
            "A2b" => Mathf.Clamp01((scarcity + surplus) * 0.5f),       // volatility ≈ oscillation proxy
            "A2c" => Mathf.Clamp01(crowding * 0.5f + conflict * 0.5f),
            "A3a" => surplus,
            "A3b" => conflict,
            "A4a" => conflict,
            "A4b" => crowding,
            _ => 0.3f,
        };
    }

    /// era3-primitives-spec §3: ConnectionStrength's full 7-input formula needs two grid-dependent
    /// inputs (border_adjacency, network_proximity) that don't exist — no geodesic grid (the
    /// warfare spec's own §13 open item). Approximated from what real data DOES exist: trade
    /// health as a trade_volume/contact_frequency proxy, CommMedium match as signal_legibility, and
    /// alliance formality as alliance_depth. Same flagged-placeholder pattern already used for
    /// war-strike distance (StrikeRange) elsewhere in this file — reused by Tech/Idea diffusion
    /// (Era3TechTree.DiffusionBonus) and available for any future Diffuse-ring consumer.
    public float ConnectionStrength(CivilizationState a, CivilizationState b)
    {
        float tradeProxy = a.TradeHealth.TryGetValue(b.CommunityId, out float th) ? Mathf.Clamp01((th + 1f) / 2f) : 0.2f;
        float legibility  = (a.CommMedium == b.CommMedium ? 1f : 0.4f)
                          * Era3PolicyCatalog.GetVar(a, Era3PolicyCatalog.Var.SignalLegibility);
        float allianceDepth = a.FormalAllianceActive && b.FormalAllianceActive ? 1f
                             : a.FormalTradeActive && b.FormalTradeActive ? 0.5f : 0.2f;
        float raw = tradeProxy * 0.4f + Mathf.Clamp01(legibility) * 0.3f + allianceDepth * 0.3f;
        // era3-policy-catalog-spec: Open Routes/Sealed Network/Autarky etc. — a real, direct scale
        // on how connected this civ is to everyone, not just its Informational legibility.
        return Mathf.Clamp01(raw * Era3PolicyCatalog.GetVar(a, Era3PolicyCatalog.Var.ConnectionStrength));
    }

    /// Recomputes a civ's population roster from the CivPopulation cohort biomass of every
    /// settlement it owns. Called each polity tick — cheap, settlement counts are small.
    /// population-energy-aggregation-spec.md §2.0: Roster is a computed rollup VIEW over cohorts,
    /// not a parallel structure — cohort biomass (not the older flat PopulationByCommunity counter)
    /// is now the one real number behind both Settlement.Population and this roster breakdown.
    private void RecomputeRoster(CivilizationState civ)
    {
        var totals = new Dictionary<int, float>();
        float grand = 0f;
        foreach (var s in Settlements)
        {
            if (s.OwnerCivId != civ.CommunityId) continue;
            foreach (var c in s.Cohorts)
            {
                if (c.Role != CohortRole.CivPopulation) continue;
                totals.TryGetValue(c.LineageId, out float cur);
                totals[c.LineageId] = cur + c.Biomass;
                grand += c.Biomass;
            }
        }
        civ.Roster.Clear();
        if (grand <= 0f)
        {
            // No absorbed-population data yet (e.g. an abstract NPC civ with no settlements ticked
            // through absorption) — fall back to the civ being entirely its own founding species.
            civ.Roster.Add(new Era3Polity.RosterEntry { CommunityId = civ.CommunityId, Fraction = 1f });
            return;
        }
        foreach (var kv in totals)
            civ.Roster.Add(new Era3Polity.RosterEntry { CommunityId = kv.Key, Fraction = kv.Value / grand });
    }

    private void TickPolity()
    {
        _polityTimer -= Time.deltaTime;
        if (_polityTimer > 0f) return;
        _polityTimer = PolityTickInterval;

        foreach (var civ in _allCivs)
        {
            if (civ.HasCollapsed) continue;

            int settlementCount = 0;
            foreach (var s in Settlements) if (s.OwnerCivId == civ.CommunityId) settlementCount++;

            RecomputeRoster(civ);

            float demand   = Era3Polity.ComputeReachDemand(settlementCount, TotalSettlementPopulationForCiv(civ.CommunityId));
            float capacity = Era3Polity.ComputeReachCapacity(civ, settlementCount, civ.DecentralizeBonus)
                           * Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.AdministrativeReach);
            civ.AdministrativeReach = capacity;
            float splinterMult = Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.SplinterPressure);
            float pressure = Era3Polity.TickSplinterPressure(civ.SplinterPressure, demand, capacity, PolityTickInterval);
            // era3-systems-implementation-spec §7: WarWeariness raises SplinterPressure while actively
            // at war — an exhausted population pulls away from the center faster than reach/demand
            // alone predicts. §7's "×0.02 per tick" is per TickPolity firing (already gated by
            // _polityTimer above), not per-second — no additional dt scaling. Provisional coefficient,
            // pending a tuning pass once running.
            if (civ.Economy != null && IsAtWarWithAnyone(civ.CommunityId))
                pressure += civ.Economy.WarWeariness * 0.02f;
            civ.SplinterPressure = Mathf.Clamp01(pressure * splinterMult);

            if (civ.SplinterPressure >= AdminCrisisThreshold && !civ.Has("e3_admin_crisis_active"))
            {
                civ.Acquire("e3_admin_crisis_active");
                civ.AcquiredEvents.Remove("d3_administrative_crisis"); // re-eligible even if resolved before
                LogEvent($"{civ.Name} strains under its own administrative reach — crisis brewing.");
                if (civ.IsPlayer) AudioManager.Instance?.OnCrisisWarning();
            }

            TickLargeInitiative(civ);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LARGE INITIATIVE (era3-systems-implementation-spec §8) — universal across all five tracks,
    // gated by I4b (track-flavored, same pattern as domestication/host-guest-tolerance). One tick
    // here = one PolityTickInterval firing, matching the "30-year/6-tick" commitment. Ongoing costs
    // and the one-shot completion bonus are both applied directly at each track's own real formula
    // site (CivilizationEconomy.Tick, TickCohortGroup, TickRunawayRisk) via civ.LargeInitiativeActive
    // / .LargeInitiativeCompleted checks, rather than a parallel bespoke effect system.
    // ══════════════════════════════════════════════════════════════════════════
    private const int LargeInitiativeDurationTicks = 6;

    public bool TryStartLargeInitiative(int communityId)
    {
        var civ = GetCiv(communityId);
        if (civ == null || civ.LargeInitiativeActive || civ.LargeInitiativeCompleted) return false;
        if (!civ.UnlockedNodes.Contains("I4b")) return false;
        civ.LargeInitiativeActive = true;
        civ.LargeInitiativeTicksRemaining = LargeInitiativeDurationTicks;
        LogEvent($"{civ.Name} commits to a Large Initiative.");
        return true;
    }

    private void TickLargeInitiative(CivilizationState civ)
    {
        if (!civ.LargeInitiativeActive) return;
        civ.LargeInitiativeTicksRemaining--;
        if (civ.LargeInitiativeTicksRemaining <= 0)
        {
            civ.LargeInitiativeActive = false;
            civ.LargeInitiativeCompleted = true;
            // Bloom Front: "immediate Biomass surge" — the one-shot half of its completion bonus
            // (the small permanent K_effective boost is applied continuously in TickCohortGroup).
            if (civ.Path == Era3Path.BloomFront)
                foreach (var s in Settlements)
                    if (s.OwnerCivId == civ.CommunityId)
                        foreach (var c in s.Cohorts)
                            if (c.Role == CohortRole.CivPopulation)
                                c.Biomass *= 1.25f;
            LogEvent($"{civ.Name} completes its Large Initiative.");
        }
    }

    private float TotalSettlementPopulationForCiv(int civId)
    {
        float sum = 0f;
        foreach (var s in Settlements) if (s.OwnerCivId == civId) sum += s.Population;
        return sum;
    }

    /// Federation/Union (era3-polity-model-spec §3, third unification path alongside the existing
    /// Conquest and Vassalization): unlike TryVassalize, this is a full merge — both civs' rosters
    /// combine, AdministrativeReach pools, and the joining civ ceases to exist as a separate polity.
    /// Player-initiated only (mirrors TryVassalize's scope). Requires a reasonably positive
    /// relationship — approximated here via SpeciesDisposition since Era3Diplomacy's PolityRelation
    /// tracks direct history on top of this once civs interact (era3-diplomacy-ai-spec §1).
    public bool TryFederate(int targetId)
    {
        var target = GetCiv(targetId);
        if (target == null || target == PlayerCiv || target.HasCollapsed) return false;
        if (target.SuzerainId >= 0) return false; // a vassal isn't free to federate
        // era3-tech-idea-trees-spec §5: I3c gates Federation too — same fallback reasoning as
        // TryVassalize above (an existing milestone as a bounded safety valve).
        if (!HasFormalMediation(PlayerCiv) || !HasFormalMediation(GetCiv(targetId))) return false;
        if (PlayerCiv.Path == Era3Path.CommerceEngine
            && !PlayerCiv.UnlockedNodes.Contains("I3c") && !PlayerCiv.Has("e3_state_formation"))
        {
            LogEvent("[Federation] Formal diplomacy norms not yet established.");
            return false;
        }

        // era3-diplomacy-ai-spec §3: the target civ's actual accept_probability, not just a raw
        // disposition threshold — reads PolityRelation/SpeciesDisposition, relative power, and the
        // target's own species-derived traits (a highly Territorial or low-Sociality target is
        // harder to talk into a full merge even at decent relations). "Collective Security Alliance"
        // is the closest-shaped action in §3.1's table (Sociality-dominant, relationship-heavy) —
        // Federation has no dedicated row of its own in the spec.
        float accept = Era3Diplomacy.AcceptProbability(this, target, PlayerCiv, Era3Diplomacy.ActionType.CollectiveSecurityAlliance);
        if (accept < 0.5f)
        {
            LogEvent($"[Federation] {target.Name} does not trust you enough to federate.");
            return false;
        }

        // Roster union — recompute both first so the merge starts from real, current data.
        RecomputeRoster(PlayerCiv);
        RecomputeRoster(target);
        float playerPop = TotalSettlementPopulationForCiv(PlayerCiv.CommunityId);
        float targetPop = TotalSettlementPopulationForCiv(target.CommunityId);
        float totalPop  = Mathf.Max(1f, playerPop + targetPop);

        var merged = new Dictionary<int, float>();
        foreach (var e in PlayerCiv.Roster) { merged.TryGetValue(e.CommunityId, out float c); merged[e.CommunityId] = c + e.Fraction * playerPop; }
        foreach (var e in target.Roster)    { merged.TryGetValue(e.CommunityId, out float c); merged[e.CommunityId] = c + e.Fraction * targetPop; }
        PlayerCiv.Roster.Clear();
        foreach (var kv in merged)
            PlayerCiv.Roster.Add(new Era3Polity.RosterEntry { CommunityId = kv.Key, Fraction = kv.Value / totalPop });

        // Every settlement the target owned transfers to the player civ outright (a merge, not a
        // conquest — no occupation/recognition split, see Settlement.IsOccupied).
        foreach (var s in Settlements)
        {
            if (s.OwnerCivId != target.CommunityId) continue;
            s.OwnerCivId = PlayerCiv.CommunityId;
            s.RecognizedOwnerCivId = PlayerCiv.CommunityId;
            s.ContributingCommunities.Add(PlayerCiv.CommunityId);
        }

        // §3: pooled reach minus a flat 20% merge-efficiency penalty — a merged polity is never
        // quite as coordinated as the sum of its parts (encoded as a durable negative decentralize
        // adjustment rather than a one-off reach value, so it keeps applying as reach is recomputed).
        PlayerCiv.DecentralizeBonus -= 0.20f;
        PlayerCiv.SplinterPressure = Mathf.Clamp01(PlayerCiv.SplinterPressure + 0.15f);

        // era3-diplomacy-ai-spec §1.2: closest event_valence analog to a federation is an honored
        // alliance call — recorded before removing target so the relation write-back (§1.3) still
        // has a valid pair to update.
        ApplyRelationEvent(PlayerCiv.CommunityId, target.CommunityId, Era3Diplomacy.ValenceHonoredAllianceCall);

        _allCivs.Remove(target);
        NpcCivs.Remove(target);
        LogEvent($"{target.Name} federates into {PlayerCiv.Name} — polities merged.");
        AudioManager.Instance?.OnTreatyFormed();
        return true;
    }

    // ── Tech / Idea Tree (era3-tech-idea-trees-spec §7) ──────────────────────────────────────
    private const float ResearchTickInterval = 10f;
    private float _researchTimer;
    private int   _researchTickCount;

    private void TickResearch()
    {
        _researchTimer -= Time.deltaTime;
        if (_researchTimer > 0f) return;
        _researchTimer = ResearchTickInterval;
        _researchTickCount++;

        foreach (var civ in _allCivs)
        {
            if (civ.HasCollapsed) continue;
            if (civ.PatronageNodeId != null && _researchTickCount > civ.PatronageExpiryTick)
                civ.PatronageNodeId = null;

            foreach (var n in Era3TechTree.Nodes)
            {
                if (civ.UnlockedNodes.Contains(n.Id)) continue;
                float rate = Era3TechTree.AcquisitionRate(this, civ, n);
                if (rate <= 0f) continue;

                civ.ResearchProgress.TryGetValue(n.Id, out float prog);
                prog += rate;
                civ.ResearchProgress[n.Id] = prog;
                if (prog >= Era3TechTree.ResearchCost(n.Tier))
                {
                    civ.UnlockedNodes.Add(n.Id);
                    OnNodeUnlocked(civ, n);
                }
            }

            // era3-adaptation-trees-spec §2: the ecological tracks' third tree — evolved, not
            // learned, same tick, separate progress/unlock sets and formula.
            foreach (var n in Era3AdaptationTree.Nodes)
            {
                if (civ.UnlockedAdaptations.Contains(n.Id)) continue;
                float rate = Era3AdaptationTree.AcquisitionRate(this, civ, n);
                if (rate <= 0f) continue;

                civ.AdaptationProgress.TryGetValue(n.Id, out float prog);
                prog += rate;
                civ.AdaptationProgress[n.Id] = prog;
                if (prog >= Era3AdaptationTree.ResearchCost(n.Tier))
                {
                    civ.UnlockedAdaptations.Add(n.Id);
                    LogEvent($"{civ.Name} evolves {Era3AdaptationTree.GetNodeName(n.Id, civ)}.");
                    if (civ.IsPlayer) AudioManager.Instance?.OnCivFounded();
                }
            }
        }
    }

    /// Sponsor a Tech or Idea node with the active patronage bonus (§7.1) — the d3_tech_patronage /
    /// d3_idea_patronage sibling cards' shared target-setting call.
    public void SetPatronageTarget(CivilizationState civ, string nodeId)
    {
        civ.PatronageNodeId = nodeId;
        civ.PatronageExpiryTick = _researchTickCount + Era3TechTree.PatronageDurationTicks;
    }

    private void OnNodeUnlocked(CivilizationState civ, Era3TechTree.Node n)
    {
        string name = Era3TechTree.GetNodeName(n.Id, civ);
        LogEvent($"{civ.Name} unlocks {(n.IsIdea ? "Idea" : "Tech")}: {name}");
        if (civ.IsPlayer) AudioManager.Instance?.OnCivFounded(); // nearest available positive sfx

        // Retrofit hooks (spec §4-§5): a small, deliberately-bounded set of nodes plug into EXISTING
        // mechanics as additive bonuses/unlocks rather than hard gates on already-working auto-event
        // progression — gating e.g. e3_chiefdom itself on this brand-new tree would risk stalling
        // the whole Era 3 graph on an unplaytested formula. Per §7.6 rule 4, unlocking only makes
        // the associated mechanic REACHABLE where it isn't already.
        switch (n.Id)
        {
            case "I2b":
                // "directly raises AdministrativeReach — the actual mechanism behind splinter
                // pressure" (spec §5 cross-ref to era3-polity-model-spec). Durable capacity bump,
                // same slot DecentralizeBonus already uses in Era3Polity.ComputeReachCapacity.
                civ.DecentralizeBonus += 0.15f;
                // era3-track-parity-gating-spec §1.7: retires the old Individuated-only
                // d3_writing_adoption card — its one-time bonus folds directly into I2b acquisition
                // instead of a separate decision, since Individuated already gets an I2b-gated Policy
                // Catalog option (ind_know_scribal) the same way Distributed/Collective do
                // (dis_know_protocol/col_know_encoded).
                if (civ.Architecture == CognitiveArchitecture.Individuated)
                {
                    civ.InvestInformation   = Mathf.Min(civ.InvestInformation + 0.12f, 1f);
                    civ.DomainInformational = Mathf.Min(civ.DomainInformational + 0.10f, 1f);
                }
                break;
            case "T2a":
                // "Makes War/Conflict a real capability" — bridges into the EXISTING
                // e3_warfare_organized flag TickConflict/war Cards already gate on, rather than
                // rewiring every war-maneuver card's IsEligible individually.
                if (!civ.Has("e3_warfare_organized")) civ.Acquire("e3_warfare_organized");
                break;
        }
    }

    // ── Warfare (era3-warfare-mechanics-spec) ────────────────────────────────────────────────
    // Real, deliberate declare-war/peace state — TickConflict (below) now only strikes between
    // civs actually in this set, replacing the old "invest in the doctrine and it just happens"
    // behavior with the conscious war-declaration flow the design explicitly asked for.
    private readonly HashSet<(int, int)> _atWar = new HashSet<(int, int)>();
    public bool IsAtWar(int civA, int civB) => _atWar.Contains(OrderedPair(civA, civB));
    /// era3-systems-implementation-spec §7: WarWeariness's SplinterPressure contribution needs
    /// "at war with anyone," not a specific pair.
    public bool IsAtWarWithAnyone(int civId)
    {
        foreach (var pair in _atWar) if (pair.Item1 == civId || pair.Item2 == civId) return true;
        return false;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HOST/GUEST RELATIONS (host-guest-relation-spec.md) — a third relationship type
    // alongside PolityRelation, for a guest civilization physically nested in a host's territory.
    // ══════════════════════════════════════════════════════════════════════════
    public enum HostGuestState { Thriving, Stable, Strained, Withdrawing, Terminated }

    public class HostGuestRelation
    {
        public int HostCivId;
        public int GuestCivId;
        public float AllocationLevel = 0.5f; // host-set dial [0,1] — Directed-tier control granularity
        public int SubstrateFootprint;       // slots ceded from the HOST's own slot_capacity pool
        public HostGuestState State = HostGuestState.Stable;
    }

    private readonly List<HostGuestRelation> _hostGuestRelations = new List<HostGuestRelation>();
    public IReadOnlyList<HostGuestRelation> HostGuestRelations => _hostGuestRelations;

    public HostGuestRelation GetHostGuestRelation(int hostCivId, int guestCivId)
    {
        foreach (var r in _hostGuestRelations)
            if (r.HostCivId == hostCivId && r.GuestCivId == guestCivId) return r;
        return null;
    }

    /// §4 initiation: piggybacks on existing first-contact/diplomacy flow — requires the two civs to
    /// already have TradeHealth contact (EnsureTradeInit), the same "already know each other" gate
    /// every other diplomatic action uses. No new initiation UI/flow.
    public bool ProposeHostGuestRelation(CivilizationState host, int guestId)
    {
        var guest = GetCiv(guestId);
        if (guest == null || guest.HasCollapsed || host.HasCollapsed || guest == host) return false;
        if (IsAtWar(host.CommunityId, guestId)) return false; // §5: disallowed while at war
        if (!host.TradeHealth.ContainsKey(guestId) || !guest.TradeHealth.ContainsKey(host.CommunityId)) return false;
        if (GetHostGuestRelation(host.CommunityId, guestId) != null) return false; // already exists

        _hostGuestRelations.Add(new HostGuestRelation { HostCivId = host.CommunityId, GuestCivId = guestId });
        LogEvent($"{guest.Name} settles as a guest within {host.Name}'s territory.");
        AudioManager.Instance?.OnTreatyFormed();
        return true;
    }

    public void SetHostGuestAllocation(int hostCivId, int guestCivId, float allocationLevel)
    {
        var r = GetHostGuestRelation(hostCivId, guestCivId);
        if (r != null) r.AllocationLevel = Mathf.Clamp01(allocationLevel);
    }

    /// §3.1/§3.2: exchange_rate reuses the existing TradeHealth mutualism-parasitism spectrum (already
    /// -1..1, already the thing this pair's ordinary trade/contact ticks drift), with the host's
    /// allocation_level dial as an ADDITIONAL input for this specific relation — not a new formula.
    /// §5's resolved thresholds (exchange_rate normalized -1..1): Thriving>=0.6, Stable 0.2-0.6,
    /// Strained -0.2-0.2, Withdrawing -0.6..-0.2, Terminated <-0.6.
    private const float AllocationNudgeStrength = 0.4f;
    private const float FootprintGrowPerTick = 1f;
    private const float FootprintShrinkPerTick = 1f;

    private void TickHostGuestRelations()
    {
        if (_hostGuestRelations.Count == 0) return;
        _cleanupRelations.Clear();
        foreach (var r in _hostGuestRelations)
        {
            var host = GetCiv(r.HostCivId);
            var guest = GetCiv(r.GuestCivId);
            if (host == null || guest == null || host.HasCollapsed || guest.HasCollapsed || IsAtWar(r.HostCivId, r.GuestCivId))
            {
                r.State = HostGuestState.Terminated;
                r.SubstrateFootprint = 0;
                _cleanupRelations.Add(r);
                if (host != null && guest != null) LogEvent($"{guest.Name}'s guest presence in {host.Name}'s territory ends.");
                continue;
            }

            float baseTradeHealth = guest.TradeHealth.TryGetValue(r.HostCivId, out var th) ? th : 0f;
            float exchangeRate = Mathf.Clamp(baseTradeHealth + (r.AllocationLevel - 0.5f) * 2f * AllocationNudgeStrength, -1f, 1f);

            HostGuestState newState =
                exchangeRate >= 0.6f ? HostGuestState.Thriving :
                exchangeRate >= 0.2f ? HostGuestState.Stable :
                exchangeRate >= -0.2f ? HostGuestState.Strained :
                exchangeRate >= -0.6f ? HostGuestState.Withdrawing :
                HostGuestState.Terminated;

            if (newState != r.State && (newState == HostGuestState.Thriving || newState == HostGuestState.Withdrawing || newState == HostGuestState.Terminated))
                LogEvent($"{guest.Name}'s guest relationship with {host.Name} is now {newState}.");
            r.State = newState;

            switch (r.State)
            {
                case HostGuestState.Thriving:
                    // §5 resolved: draws from the HOST's own slot_capacity pool, not a separate
                    // allocation — growth is capped by whatever headroom TickStructures reports.
                    int headroom = HostSlotHeadroom(host);
                    if (headroom > 0) r.SubstrateFootprint += Mathf.Min(Mathf.CeilToInt(FootprintGrowPerTick), headroom);
                    break;
                case HostGuestState.Withdrawing:
                    r.SubstrateFootprint = Mathf.Max(0, r.SubstrateFootprint - Mathf.CeilToInt(FootprintShrinkPerTick));
                    break;
                case HostGuestState.Terminated:
                    r.SubstrateFootprint = 0;
                    _cleanupRelations.Add(r);
                    LogEvent($"{guest.Name} fully emigrates from {host.Name}'s territory.");
                    break;
                // Stable/Strained: no footprint change.
            }
        }
        foreach (var r in _cleanupRelations) _hostGuestRelations.Remove(r);
    }
    private readonly List<HostGuestRelation> _cleanupRelations = new List<HostGuestRelation>();

    /// Total slots this civ has ceded to guests, across every relation where it's the host —
    /// TickStructures subtracts this from the civ's own slot_capacity (§5 resolved: hosting costs
    /// the host some of its own settlement-growth headroom, not a free/separate pool).
    public int TotalGuestFootprint(CivilizationState civ)
    {
        int total = 0;
        foreach (var r in _hostGuestRelations) if (r.HostCivId == civ.CommunityId) total += r.SubstrateFootprint;
        return total;
    }

    private int HostSlotHeadroom(CivilizationState host)
    {
        int techTier = Era3TechTree.GetTechTier(host);
        int capacity = SlotCapacityByTechTier[techTier];
        int used = host.BuiltStructures.Count + TotalGuestFootprint(host);
        return Mathf.Max(0, capacity - used);
    }

    /// slot_capacity_utilization (host-guest-trigger-spec.md §3/§4.2): fraction of a civ's own
    /// slot_capacity already committed — its own structures plus any guests it already hosts.
    public float SlotCapacityUtilization(CivilizationState civ)
    {
        int techTier = Era3TechTree.GetTechTier(civ);
        int capacity = SlotCapacityByTechTier[techTier];
        if (capacity <= 0) return 1f;
        int used = civ.BuiltStructures.Count + TotalGuestFootprint(civ);
        return Mathf.Clamp01((float)used / capacity);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HOST/GUEST TRIGGER SURFACE (host-guest-trigger-spec.md) — the actual proposal→acceptance
    // pipeline. ProposeHostGuestRelation above only creates a relation once one side already agreed;
    // this is what decides whether that agreement happens, for both the player Card and AI-autonomous
    // trigger paths (§3/§4).
    // ══════════════════════════════════════════════════════════════════════════
    public enum HostGuestProposalRole { Host, Guest }

    public class HostGuestProposal
    {
        public int ProposerCivId;
        public HostGuestProposalRole ProposerRole;
        public int TargetCivId;
        public float InitialAllocationLevel = 0.5f; // Host role only
        public int RequestedFootprintEstimate;      // informational only — real footprint is resolved by slot_capacity at accept time (see TickHostGuestRelations)
    }

    // §8 open items: accept_threshold/friendly_threshold/base_propose_chance/cooldown length are all
    // flagged tunable, none assumed by the spec — first-pass values, same standing as every other
    // TUNABLE constant in this file.
    private const float HostGuestAcceptThreshold = 0.5f;
    private const float HostGuestFriendlyThreshold = 0.3f;
    private const float HostGuestBaseProposeChance = 0.1f;
    private const int HostGuestCooldownTicks = 20; // in HostGuestAI ticks (§4.3) — see TickHostGuestProposalAI's own interval
    // "small enough not to dominate the decision, large enough to visibly bias outcomes" (§5.1) —
    // TUNABLE, no numeric value given in the spec.
    private const float HostGuestTrackCompatibilityBonus = 0.15f;

    private readonly Dictionary<(int proposer, int target, HostGuestProposalRole role), int> _hostGuestCooldowns
        = new Dictionary<(int, int, HostGuestProposalRole), int>();

    /// §4.1's territorial_pressure proxy. SplinterPressure is already this codebase's real
    /// "intraspecific competition/saturation" gauge (see its use in TickPolity, commented exactly
    /// that way) — settlement-growth-spec.md's densification ceiling doesn't exist as a separate
    /// tracked value, so reusing SplinterPressure avoids building a second, parallel pressure gauge.
    private float TerritorialPressure(CivilizationState civ) => civ.SplinterPressure;

    private float HostCapacityHeadroomBonus(CivilizationState host) =>
        Mathf.Clamp01(1f - SlotCapacityUtilization(host)) * 0.5f; // more headroom → more willing to host

    private float GuestPressureReliefBonus(CivilizationState guest) =>
        TerritorialPressure(guest) * 0.5f; // more pressure → more relief from finding a host, more willing to move

    /// §5.1: a mobile guest nesting on a Living Reef/Distributed host is a stronger fit than two
    /// Individuated civs sharing territory — flat, non-dominant bonus.
    private float TrackCompatibilityBonus(CivilizationState hostCiv, CivilizationState guestCiv)
    {
        bool hostFits = hostCiv.Path == Era3Path.LivingReef
            || (hostCiv.Path == Era3Path.CommerceEngine && hostCiv.Architecture == CognitiveArchitecture.Distributed);
        bool guestMobile = guestCiv.Path == Era3Path.ApexPredator || guestCiv.Path == Era3Path.Terraformer || guestCiv.Path == Era3Path.BloomFront
            || (guestCiv.Path == Era3Path.CommerceEngine && guestCiv.Architecture == CognitiveArchitecture.Individuated);
        return hostFits && guestMobile ? HostGuestTrackCompatibilityBonus : 0f;
    }

    /// §5: shared acceptance evaluation for both trigger paths. disposition_toward_proposer reuses
    /// GetPolityRelation — the existing bilateral "what have these two civs actually done to each
    /// other" ledger already used by every other diplomatic accept-probability check in this file.
    public float ComputeHostGuestAcceptScore(HostGuestProposal proposal)
    {
        var proposer = GetCiv(proposal.ProposerCivId);
        var target = GetCiv(proposal.TargetCivId);
        if (proposer == null || target == null) return -999f;

        float score = GetPolityRelation(proposal.ProposerCivId, proposal.TargetCivId);

        // The evaluating side (target) takes on whichever role the proposer ISN'T asking for.
        bool targetBecomesHost = proposal.ProposerRole == HostGuestProposalRole.Guest;
        var hostCiv  = targetBecomesHost ? target : proposer;
        var guestCiv = targetBecomesHost ? proposer : target;

        score += targetBecomesHost ? HostCapacityHeadroomBonus(hostCiv) : GuestPressureReliefBonus(guestCiv);
        score += TrackCompatibilityBonus(hostCiv, guestCiv);
        return score;
    }

    /// The one proposal→acceptance pipeline (§2: "there is only one... the two sections just
    /// describe the two ways a proposal enters it"). Both the player Card and the AI-autonomous tick
    /// call this exact method. Handles the war/contact preconditions ProposeHostGuestRelation already
    /// enforces, plus acceptance scoring and rejection cooldown, which are new here.
    public bool SubmitHostGuestProposal(HostGuestProposal proposal)
    {
        var key = (proposal.ProposerCivId, proposal.TargetCivId, proposal.ProposerRole);
        if (_hostGuestCooldowns.TryGetValue(key, out int untilTick) && _researchTickCount < untilTick) return false;

        var proposer = GetCiv(proposal.ProposerCivId);
        var target = GetCiv(proposal.TargetCivId);
        if (proposer == null || target == null || proposer.HasCollapsed || target.HasCollapsed) return false;
        if (IsAtWar(proposal.ProposerCivId, proposal.TargetCivId)) return false;
        if (!CanUseHostGuestRelation(proposer) || !CanUseHostGuestRelation(target)) return false;
        // era3-sovereignty-interaction-gaps-spec.md §1.3: foreign-policy lockout — one more
        // precondition alongside the ones already enforced above, not a separate gate.
        if (proposer.SuzerainId >= 0) { LogEvent($"{proposer.Name} cannot propose Host/Guest relations independently — bound by vassalage."); return false; }

        bool targetBecomesHost = proposal.ProposerRole == HostGuestProposalRole.Guest;
        var hostCiv  = targetBecomesHost ? target : proposer;
        var guestCiv = targetBecomesHost ? proposer : target;

        if (ComputeHostGuestAcceptScore(proposal) < HostGuestAcceptThreshold)
        {
            _hostGuestCooldowns[key] = _researchTickCount + HostGuestCooldownTicks;
            return false;
        }

        bool created = ProposeHostGuestRelation(hostCiv, guestCiv.CommunityId);
        if (created && proposal.ProposerRole == HostGuestProposalRole.Host)
            SetHostGuestAllocation(hostCiv.CommunityId, guestCiv.CommunityId, proposal.InitialAllocationLevel);
        return created;
    }

    /// era3-civilization-tracks-spec §1/§2: the "formal" mediation layer (Cards, treaties,
    /// Representative-brokered proposals — alliance, joint research, gifts, insults, tech theft,
    /// formal war declaration/peace, vassalage, federation) is CommerceEngine-only. The ecological
    /// paths (and Living Reef without Symbiotic Integration) still exchange resources — see
    /// TickTradeEngine, which correctly runs Tacit Exchange for every track unconditionally
    /// (civilization-tracks-spec §2.1: "do NOT gate TickTradeEngine on Track") — they just can't
    /// negotiate a treaty about it. Server-side guard backing the same restriction the HUD applies.
    public bool HasFormalMediation(CivilizationState civ) => civ != null && civ.Path == Era3Path.CommerceEngine;

    /// era3-sovereignty-interaction-gaps-spec.md §2 (Apex Predator revised by era3-systems-
    /// implementation-spec §3): HostGuestRelation eligibility, independent of HasFormalMediation
    /// (which also gates unrelated formal-diplomacy actions and currently excludes Living Reef even
    /// with Symbiotic Integration — a separate, pre-existing inconsistency out of scope here).
    /// Commerce Engine gets it via I1c; Living Reef via Symbiotic Integration (both per
    /// host-guest-relation-spec.md's original eligibility); Terraformer/Bloom Front via the
    /// Adaptation-tree A_host_guest_tolerance; Apex Predator via the Idea-tree
    /// I_host_guest_tolerance instead of that same Adaptation node, since it lost Adaptation-tree
    /// access entirely once grouped with Commerce Engine as a builder track.
    public bool CanUseHostGuestRelation(CivilizationState civ)
    {
        if (civ == null) return false;
        if (civ.Path == Era3Path.CommerceEngine) return civ.UnlockedNodes.Contains("I1c");
        if (civ.Path == Era3Path.LivingReef) return civ.EcoResourcePolicy == 2; // Symbiotic Integration
        if (civ.Path == Era3Path.ApexPredator) return civ.UnlockedNodes.Contains("I_host_guest_tolerance");
        return civ.UnlockedAdaptations.Contains("A_host_guest_tolerance"); // Terraformer/BloomFront
    }

    /// Closest pair of settlements between two civs, world units — the placeholder distance model
    /// (no geodesic grid exists; see Era3Warfare's header comment) that both DeclareWar's reach
    /// check and TickConflict's overextension penalty read.
    public float SettlementDistance(CivilizationState a, CivilizationState b)
    {
        var vis = Era3VisualManager.Instance;
        float best = float.MaxValue;
        foreach (var mine in Settlements)
        {
            if (mine.OwnerCivId != a.CommunityId) continue;
            Vector3 p1 = vis != null ? vis.GetCurrentWorldPosition(mine) : mine.Position;
            foreach (var theirs in Settlements)
            {
                if (theirs.OwnerCivId != b.CommunityId) continue;
                Vector3 p2 = vis != null ? vis.GetCurrentWorldPosition(theirs) : theirs.Position;
                float d = Vector3.Distance(p1, p2);
                if (d < best) best = d;
            }
        }
        return best;
    }

    /// The conscious, player- (or AI-)initiated act of going to war — early-game civs need
    /// adjacency (small ProjectionRange), tech expands reach over time (Era3Warfare.ComputeProjectionRange).
    public bool DeclareWar(CivilizationState declarer, int targetId)
    {
        var target = GetCiv(targetId);
        if (target == null || target.HasCollapsed || declarer.HasCollapsed || target == declarer) return false;
        if (IsAtWar(declarer.CommunityId, targetId)) return false;
        // era3-sovereignty-interaction-gaps-spec.md §1.3: foreign-policy lockout — a vassal can't
        // independently declare war; only its overlord's own foreign policy applies.
        if (declarer.SuzerainId >= 0) { LogEvent($"{declarer.Name} cannot declare war independently — bound by vassalage."); return false; }
        // era3-civilization-tracks-spec §1/§2: ecological paths have no negotiated war at all — their
        // "war" already resolves automatically every tick via Conflict Posture (ResolveConflictPosture),
        // not a declare/peace flow. Formal war declaration is CommerceEngine-only.
        if (!HasFormalMediation(declarer) || !HasFormalMediation(target)) return false;

        float dist = SettlementDistance(declarer, target);
        float reach = declarer.ProjectionRange * Era3Warfare.ProjectionRangeWorldScale;
        if (dist == float.MaxValue) { LogEvent($"{declarer.Name} has no path to reach {target.Name}."); return false; }
        if (dist > reach * 1.5f) // hard ceiling — beyond 1.5x nominal range, even the Overextension penalty can't reach
        {
            LogEvent($"{declarer.Name} cannot yet project force that far.");
            return false;
        }

        _atWar.Add(OrderedPair(declarer.CommunityId, targetId));
        ApplyRelationEvent(declarer.CommunityId, targetId, Era3Diplomacy.ValenceWarDeclaration);
        LogEvent($"{declarer.Name} declares war on {target.Name}.");
        if (declarer.IsPlayer || target.IsPlayer) AudioManager.Instance?.OnWarDeclared();

        // host-guest-relation-spec §5: war auto-terminates any active HostGuestRelation for this
        // pair (either direction — either could be the other's host), same as a natural Terminated
        // state; TickHostGuestRelations' own IsAtWar check would catch this next tick regardless,
        // but ending it immediately here avoids a one-tick lag where a wartime host/guest pair
        // still shows as active.
        var rel1 = GetHostGuestRelation(declarer.CommunityId, targetId);
        var rel2 = GetHostGuestRelation(targetId, declarer.CommunityId);
        if (rel1 != null) { rel1.State = HostGuestState.Terminated; rel1.SubstrateFootprint = 0; }
        if (rel2 != null) { rel2.State = HostGuestState.Terminated; rel2.SubstrateFootprint = 0; }

        return true;
    }

    /// Sue for peace — NOT an instant white peace. The other side actually decides, via the same
    /// accept_probability machinery any other diplomatic action uses (era3-diplomacy-ai-spec §3.1
    /// "Accept Peace" row, which already weighs relative power heavily — a losing target is
    /// realistically more likely to accept, but isn't guaranteed to).
    public bool ProposePeace(int proposerId, int targetId)
    {
        if (!IsAtWar(proposerId, targetId)) return false;
        var proposer = GetCiv(proposerId); var target = GetCiv(targetId);
        if (proposer == null || target == null) return false;
        if (!HasFormalMediation(proposer) || !HasFormalMediation(target)) return false;

        float accept = Era3Diplomacy.AcceptProbability(this, target, proposer, Era3Diplomacy.ActionType.AcceptPeace);
        if (UnityEngine.Random.value > accept)
        {
            LogEvent($"{target.Name} rejects {proposer.Name}'s peace offer.");
            return false;
        }
        _atWar.Remove(OrderedPair(proposerId, targetId));
        ApplyRelationEvent(proposerId, targetId, Era3Diplomacy.ValenceAcceptedPeace);
        LogEvent($"{proposer.Name} and {target.Name} make peace.");
        AudioManager.Instance?.OnTreatyFormed();
        return true;
    }

    /// era3-sovereignty-interaction-gaps-spec.md §1.2: vassalization as an ADDITIONAL peace-term path
    /// alongside ProposePeace above — both coexist, this doesn't replace the plain-peace option.
    /// Player-only, matching TryVassalize's existing scope (this literally calls it, not a parallel
    /// mechanic). Reuses TryVassalize's own power-advantage gate as the "is this civ actually able to
    /// impose this term" check rather than a separate resilience-collapse threshold — a war a civ is
    /// genuinely losing already shows up as the same power gap TryVassalize tests for.
    public bool ProposeVassalagePeace(int targetId)
    {
        if (!IsAtWar(PlayerCiv.CommunityId, targetId)) return false;
        if (!TryVassalize(targetId)) return false; // TryVassalize already logs its own rejection reason
        _atWar.Remove(OrderedPair(PlayerCiv.CommunityId, targetId));
        LogEvent($"War with {GetCiv(targetId)?.Name} ends in vassalage.");
        return true;
    }

    /// A real diplomatic "talk" option beyond war/peace — proposes a formal alliance, resolved by
    /// the target's own accept_probability (era3-diplomacy-ai-spec §3.1 "Collective Security
    /// Alliance" row). FormalAllianceActive is a single flag per civ (pre-existing model, not
    /// per-partner) — fine for now, matches how the rest of the codebase already reads it.
    public bool ProposeAlliance(CivilizationState proposer, int targetId)
    {
        var target = GetCiv(targetId);
        if (target == null || target.HasCollapsed) return false;
        if (!HasFormalMediation(proposer) || !HasFormalMediation(target)) return false;
        float accept = Era3Diplomacy.AcceptProbability(this, target, proposer, Era3Diplomacy.ActionType.CollectiveSecurityAlliance);
        if (UnityEngine.Random.value > accept)
        {
            LogEvent($"{target.Name} declines {proposer.Name}'s alliance proposal.");
            return false;
        }
        proposer.FormalAllianceActive = true;
        target.FormalAllianceActive = true;
        ApplyRelationEvent(proposer.CommunityId, targetId, Era3Diplomacy.ValenceHonoredAllianceCall);
        LogEvent($"{proposer.Name} and {target.Name} form a collective security alliance.");
        AudioManager.Instance?.OnTreatyFormed();
        return true;
    }

    /// Joint Research (era3-diplomacy-ai-spec §3.1) — resolved by the target's accept_probability;
    /// on success both civs get a real, concrete research injection (era3-tech-idea-trees-spec §7)
    /// rather than an abstract relation-only effect.
    public bool ProposeJointResearch(CivilizationState proposer, int targetId)
    {
        var target = GetCiv(targetId);
        if (target == null || target.HasCollapsed) return false;
        if (!HasFormalMediation(proposer) || !HasFormalMediation(target)) return false;
        float accept = Era3Diplomacy.AcceptProbability(this, target, proposer, Era3Diplomacy.ActionType.JointResearch);
        if (UnityEngine.Random.value > accept)
        {
            LogEvent($"{target.Name} declines joint research with {proposer.Name}.");
            return false;
        }
        GrantResearchBoost(proposer);
        GrantResearchBoost(target);
        ApplyRelationEvent(proposer.CommunityId, targetId, Era3Diplomacy.ValenceFavorableTrade);
        LogEvent($"{proposer.Name} and {target.Name} begin joint research.");
        AudioManager.Instance?.OnTreatyFormed();
        return true;
    }

    /// Flat ResearchProgress injection into whichever node the civ has patronized, or (if none)
    /// whichever eligible not-yet-unlocked node is currently closest to completing.
    private void GrantResearchBoost(CivilizationState civ)
    {
        string nodeId = civ.PatronageNodeId;
        if (nodeId == null)
        {
            float bestRatio = -1f;
            foreach (var n in Era3TechTree.Nodes)
            {
                if (civ.UnlockedNodes.Contains(n.Id) || !Era3TechTree.IsApplicable(civ, n) || !Era3TechTree.PrereqsUnlocked(civ, n)) continue;
                civ.ResearchProgress.TryGetValue(n.Id, out float prog);
                float ratio = prog / Era3TechTree.ResearchCost(n.Tier);
                if (ratio > bestRatio) { bestRatio = ratio; nodeId = n.Id; }
            }
        }
        if (nodeId == null) return;

        var node = Era3TechTree.Get(nodeId);
        civ.ResearchProgress.TryGetValue(nodeId, out float p);
        p += Era3TechTree.ResearchCost(node.Tier) * 0.15f;
        civ.ResearchProgress[nodeId] = p;
        if (p >= Era3TechTree.ResearchCost(node.Tier) && !civ.UnlockedNodes.Contains(nodeId))
        {
            civ.UnlockedNodes.Add(nodeId);
            OnNodeUnlocked(civ, node);
        }
    }

    /// Steal Tech/Idea (era3-diplomacy-ai-spec §3.1 "Steal Tech/Idea" — AI-as-actor: the THIEF'S own
    /// risk tolerance/relation to the target governs the odds, not the target's consent). Picks the
    /// target's most-advanced node the thief doesn't already have, since the player only sees "steal
    /// from X" rather than picking a specific node up front — a real simplification of the target-
    /// node-picker UI this would otherwise need.
    public bool TryStealTech(CivilizationState thief, int targetId)
    {
        var target = GetCiv(targetId);
        if (target == null || target.HasCollapsed) return false;
        // era3-diplomacy-ai-spec_1 §5 item 3: non-cognitive tracks "cannot evaluate a proposal" — the
        // same limit applies to evaluating their OWN theft risk/reward via accept_probability below,
        // so the actor (not necessarily the target) needs formal mediation to attempt this at all.
        if (!HasFormalMediation(thief)) return false;

        string bestNode = null; int bestTier = -1;
        foreach (var id in target.UnlockedNodes)
        {
            if (thief.UnlockedNodes.Contains(id)) continue;
            var n = Era3TechTree.Get(id);
            if (!Era3TechTree.IsApplicable(thief, n)) continue; // can't use a node your own track has no slot for
            if (n.Tier > bestTier) { bestTier = n.Tier; bestNode = id; }
        }
        if (bestNode == null)
        {
            LogEvent($"{thief.Name} finds nothing worth stealing from {target.Name}.");
            return false;
        }

        // era3-systems-implementation-spec §2: HonestSignalWeight (Distributed only) feeds
        // StealTechDefense — an honestly-signaling network is also a well-monitored one.
        float honestSignalBonus = target.Architecture == CognitiveArchitecture.Distributed
            ? 1f + target.HonestSignalWeight * 0.3f : 1f;
        float chance = Era3Diplomacy.AcceptProbability(this, thief, target, Era3Diplomacy.ActionType.StealTech)
                     * Era3PolicyCatalog.GetVar(thief, Era3PolicyCatalog.Var.StealTechOffense)
                     / Mathf.Max(0.1f, Era3PolicyCatalog.GetVar(target, Era3PolicyCatalog.Var.StealTechDefense) * honestSignalBonus);
        string name = Era3TechTree.GetNodeName(bestNode, target);
        if (UnityEngine.Random.value > chance)
        {
            LogEvent($"{thief.Name}'s attempt to steal {name} from {target.Name} fails.");
            return false;
        }

        thief.UnlockedNodes.Add(bestNode);
        LogEvent($"{thief.Name} steals {name} from {target.Name}.");
        // A real breach whether or not it's ever formally "discovered" in-fiction — this is a
        // hostile act against the target's interests, not a neutral one.
        ApplyRelationEvent(thief.CommunityId, targetId, Era3Diplomacy.ValenceTreatyBetrayal);
        return true;
    }

    /// Gift — era3-systems-implementation-spec §6: redirected from a direct Stockpile transfer to
    /// Economic output (Industry stock), same shape — the concrete "improve relations" lever the
    /// diplomacy spec's accept/reject actions all assume exists but never itself defines as an action.
    public bool SendGift(CivilizationState giver, int targetId, float amount)
    {
        var target = GetCiv(targetId);
        float giverStock = giver.Economy?.Stock[CivilizationEconomy.Industry] ?? 0f;
        if (target == null || target.HasCollapsed || giverStock < amount || amount <= 0f) return false;
        // A deliberate, symbolic goodwill gesture requires the same communicative/Representative
        // capacity as any other formal action — a Terraformer's atmosphere doesn't "mean" anything.
        if (!HasFormalMediation(giver)) return false;
        giver.Economy.Stock[CivilizationEconomy.Industry] -= amount;
        if (target.Economy != null) target.Economy.Stock[CivilizationEconomy.Industry] += amount;
        // Scales mildly with generosity relative to the giver's own stock — a token gift from a
        // rich civ means less than the same amount from one that can barely spare it.
        float generosity = Mathf.Clamp01(amount / Mathf.Max(0.5f, giverStock));
        ApplyRelationEvent(giver.CommunityId, targetId, Era3Diplomacy.ValenceFavorableTrade * (1f + generosity));
        LogEvent($"{giver.Name} sends a gift of {amount:F1} to {target.Name}.");
        return true;
    }

    /// Insult — a deliberate diplomatic affront with no other mechanical cost, the direct
    /// "injure relations" lever paired with Gift above.
    public void SendInsult(CivilizationState sender, int targetId)
    {
        var target = GetCiv(targetId);
        if (target == null || !HasFormalMediation(sender)) return;
        ApplyRelationEvent(sender.CommunityId, targetId, -0.4f);
        LogEvent($"{sender.Name} formally insults {target.Name}.");
    }

    /// A strike that doesn't require DeclareWar — early, deniable aggression rather than open
    /// warfare. Has a real detection chance (higher for Kinetic strikes — a physical attack is
    /// harder to hide than a biochemical one); if detected, the target (and everyone else) becomes
    /// aware and the relation hit lands as if it were an open war declaration, plus a real chance
    /// the target retaliates by formally declaring war back.
    public bool CovertStrike(CivilizationState attacker, int targetCivId, Era3Warfare.WarSubsystem subsystem)
    {
        var targetCivForCheck = GetCiv(targetCivId);
        if (targetCivForCheck == null || targetCivForCheck.HasCollapsed || attacker.HasCollapsed || targetCivForCheck == attacker) return false;
        // Ecological paths already have their own undeclared aggression — Conflict Posture resolves
        // automatically every tick (ResolveConflictPosture) with no button to press. A manual covert
        // strike is the CommerceEngine-specific alternative to formal DeclareWar, not an addition on
        // top of the ecological paths' existing automatic aggression.
        if (!HasFormalMediation(attacker)) return false;

        // Nearest settlement of THIS specific target civ (not just any enemy — a covert strike is
        // aimed, unlike TickConflict's opportunistic "at war with anyone in range" scan).
        var vis = Era3VisualManager.Instance;
        Settlement target = null; float bestDist = float.MaxValue;
        foreach (var mine in Settlements)
        {
            if (mine.OwnerCivId != attacker.CommunityId) continue;
            Vector3 minePos = vis != null ? vis.GetCurrentWorldPosition(mine) : mine.Position;
            foreach (var enemy in Settlements)
            {
                if (enemy.OwnerCivId != targetCivId) continue;
                Vector3 enemyPos = vis != null ? vis.GetCurrentWorldPosition(enemy) : enemy.Position;
                float d = Vector3.Distance(minePos, enemyPos);
                if (d < bestDist) { bestDist = d; target = enemy; }
            }
        }
        if (target == null || bestDist > attacker.ProjectionRange * Era3Warfare.ProjectionRangeWorldScale * 1.5f)
        {
            LogEvent($"{attacker.Name} cannot yet project force that far.");
            return false;
        }

        bool kinetic = subsystem != Era3Warfare.WarSubsystem.Population || attacker.DomainKinetic >= attacker.DomainBiochemical;
        // Kinetic action is hard to hide; biochemical/informational covert action is genuinely
        // deniable for longer — matches the "especially if obvious and kinetic" framing directly.
        float detectionChance = kinetic ? 0.75f : 0.30f;

        ApplyStrike(attacker, target, bestDist, subsystem);

        var targetCiv = GetCiv(targetCivId);
        if (UnityEngine.Random.value < detectionChance)
        {
            LogEvent($"{attacker.Name}'s covert action against {targetCiv.Name} is exposed!");
            ApplyRelationEvent(attacker.CommunityId, targetCivId, Era3Diplomacy.ValenceWarDeclaration);
            // Real chance of open retaliation — this is what makes covert action a gamble, not a
            // free action; a caught aggressor can end up in the formal war it was trying to avoid.
            float retaliateChance = Mathf.Clamp01(0.4f + targetCiv.DomainKinetic * 0.3f);
            if (UnityEngine.Random.value < retaliateChance)
                DeclareWar(targetCiv, attacker.CommunityId);
        }
        else
        {
            LogEvent($"{attacker.Name} strikes {targetCiv.Name} — the attack's origin remains unconfirmed.");
        }
        return true;
    }

    private const float WarfareTickInterval = 8f;
    private float _warfareTimer;

    private void TickWarfare()
    {
        _warfareTimer -= Time.deltaTime;
        if (_warfareTimer > 0f) return;
        _warfareTimer = WarfareTickInterval;

        foreach (var civ in _allCivs)
        {
            if (civ.HasCollapsed) continue;
            civ.ProjectionRange = Era3Warfare.ComputeProjectionRange(civ);

            if (Era3Warfare.IsStandingForcePhase(civ))
            {
                float target = civ.StructureInvest[4] * Era3Warfare.ComputeMaxSustainableForce(civ);
                civ.StandingForce = Mathf.MoveTowards(civ.StandingForce, target, 0.5f * WarfareTickInterval);
            }
            else
            {
                civ.StandingForce = 0f; // Levy phase — no continuous force, no upkeep
            }

            civ.UpkeepCost = Era3Warfare.ComputeUpkeepCost(civ);
            float maxSustainable = Era3Warfare.ComputeMaxSustainableForce(civ);
            if (civ.StandingForce > maxSustainable)
            {
                float overRatio = (civ.StandingForce - maxSustainable) / Mathf.Max(1f, maxSustainable);
                civ.DrainResilience(overRatio * 0.02f); // fiscal ceiling exceeded — real bleed, not a wall
            }
            else if (civ.UpkeepCost > 0f && civ.Economy != null)
            {
                // era3-systems-implementation-spec §6: military upkeep redirected from Stockpile to
                // Military output, same additive drain shape.
                civ.Economy.Stock[CivilizationEconomy.Military] = Mathf.Max(0f, civ.Economy.Stock[CivilizationEconomy.Military] - civ.UpkeepCost * 0.1f);
            }

            bool atWarNow = false;
            foreach (var other in _allCivs)
                if (other != civ && IsAtWar(civ.CommunityId, other.CommunityId)) { atWarNow = true; break; }
            civ.WarVariationSuppression = atWarNow
                ? Mathf.MoveTowards(civ.WarVariationSuppression, 0.3f, 0.05f)
                : Mathf.MoveTowards(civ.WarVariationSuppression, 0f, 0.02f);
        }

        TickWarDeclarationAI();
    }

    /// NPC war-declaration AI: reuses the same accept_probability machinery a proposed action would
    /// (era3-diplomacy-ai-spec §3.1 "Declare War" row), scaled down since this should be a rare,
    /// deliberate event rolled periodically, not a coin-flip every tick.
    private void TickWarDeclarationAI()
    {
        foreach (var ai in NpcCivs)
        {
            if (ai.HasCollapsed || !ai.Has("e3_warfare_organized")) continue;
            foreach (var target in _allCivs)
            {
                if (target == ai || target.HasCollapsed || IsAtWar(ai.CommunityId, target.CommunityId)) continue;
                float dist = SettlementDistance(ai, target);
                if (dist == float.MaxValue || dist > ai.ProjectionRange * Era3Warfare.ProjectionRangeWorldScale * 1.5f) continue;

                float powerDelta = Mathf.Clamp((ai.Resilience + ai.DomainKinetic * 0.5f) - (target.Resilience + target.DomainKinetic * 0.5f), -1f, 1f);
                float p = Era3Diplomacy.AcceptProbability(this, ai, target, Era3Diplomacy.ActionType.DeclareWar, powerDelta, 0f, 0.3f, 0.4f);
                if (UnityEngine.Random.value < p * 0.05f)
                    DeclareWar(ai, target.CommunityId);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HOST/GUEST TRIGGER PATH B — AI-AUTONOMOUS (host-guest-trigger-spec.md §4). Mirrors
    // TickWarDeclarationAI's shape: per-civ × per-target loop, precondition check, flat probabilistic
    // fire — "an ordinary AI decision rather than an emergent phenomenon" (§4.3), not a hazard model.
    // ══════════════════════════════════════════════════════════════════════════
    private const float HostGuestAITickInterval = 8f; // same coarse-cadence order of magnitude as WarfareTickInterval — no literal shared timer with idea-emergence sampling exists to hook into (see verification notes)
    private float _hostGuestAITimer;

    private void TickHostGuestProposalAI()
    {
        _hostGuestAITimer -= Time.deltaTime;
        if (_hostGuestAITimer > 0f) return;
        _hostGuestAITimer = HostGuestAITickInterval;

        foreach (var ai in _allCivs)
        {
            // NPCs always evaluated; the player only when a Collective civ has opted into the coarse
            // dial (§3: "the engine runs Trigger Path B's evaluation logic on the player's behalf").
            bool isCollectiveOptIn = ai.IsPlayer && ai.Architecture == CognitiveArchitecture.Collective && ai.SeekSymbioticHosts;
            if (ai.IsPlayer && !isCollectiveOptIn) continue;
            if (ai.HasCollapsed || !CanUseHostGuestRelation(ai)) continue;

            float intensity = isCollectiveOptIn ? ai.SeekSymbioticHostsIntensity : 1f;

            foreach (var target in _allCivs)
            {
                if (target == ai || target.HasCollapsed || !CanUseHostGuestRelation(target)) continue;
                if (IsAtWar(ai.CommunityId, target.CommunityId)) continue;
                if (GetHostGuestRelation(ai.CommunityId, target.CommunityId) != null
                 || GetHostGuestRelation(target.CommunityId, ai.CommunityId) != null) continue;

                float disposition = GetPolityRelation(ai.CommunityId, target.CommunityId);
                if (disposition < HostGuestFriendlyThreshold) continue;

                // §4.1 Guest-side: approaching saturation, seeks a friendly host.
                if (TerritorialPressure(ai) >= 0.85f)
                    TryFireHostGuestAI(ai, target, HostGuestProposalRole.Guest, intensity);

                // §4.2 Host-side: comfortable headroom, willing to host a friendly neighbor.
                if (SlotCapacityUtilization(ai) <= 0.5f)
                    TryFireHostGuestAI(ai, target, HostGuestProposalRole.Host, intensity);
            }
        }
    }

    private void TryFireHostGuestAI(CivilizationState proposer, CivilizationState target, HostGuestProposalRole role, float intensityScale)
    {
        if (UnityEngine.Random.value >= HostGuestBaseProposeChance * intensityScale) return;
        SubmitHostGuestProposal(new HostGuestProposal
        {
            ProposerCivId = proposer.CommunityId, ProposerRole = role, TargetCivId = target.CommunityId,
        });
    }

    // ── Policy Catalog (era3-policy-catalog-spec) — ten named-policy slots, derived multipliers,
    // gated by Tech/Idea nodes, with real switching cost/lockout and legacy decay ─────────────────
    private const float PolicyTickInterval = 10f; // matches ResearchTickInterval's "local tick" cadence
    private float _policyTimer;

    /// Fills in any slot this civ's track has but hasn't been assigned a policy yet — its neutral
    /// (ungated) default — so GetVar never reads a null ActiveId. Idempotent; safe to call every tick.
    private void EnsurePolicyDefaults(CivilizationState civ)
    {
        foreach (var slot in Era3PolicyCatalog.SlotsForTrack(civ))
        {
            if (civ.PolicySlots.ContainsKey(slot)) continue;
            string def = Era3PolicyCatalog.NeutralDefault(civ, slot);
            if (def != null) civ.PolicySlots[slot] = new PolicySlotState { ActiveId = def };
        }
    }

    private void TickPolicies()
    {
        _policyTimer -= Time.deltaTime;
        if (_policyTimer > 0f) return;
        _policyTimer = PolicyTickInterval;

        foreach (var civ in _allCivs)
        {
            if (civ.HasCollapsed) continue;
            EnsurePolicyDefaults(civ);
            foreach (var state in civ.PolicySlots.Values)
            {
                state.TicksSinceSwitch += 1f;
                if (state.LockoutTicksRemaining > 0) state.LockoutTicksRemaining--;
            }

            // §1's additive PassivePolityDrain/UpkeepDrain hooks — applied here rather than at
            // switch-time since they're properties of whichever policy is CURRENTLY active.
            float drain = Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.PassivePolityDrain);
            if (Mathf.Abs(drain) > 0.0001f)
                foreach (var other in _allCivs)
                    if (other != civ && !other.HasCollapsed) ApplyRelationEvent(civ.CommunityId, other.CommunityId, drain);

            float upkeepDrain = Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.UpkeepDrain);
            // era3-systems-implementation-spec §6: policy upkeep redirected from Stockpile to Military
            // output — UpkeepDrain-bearing policies (Garrison State, ...) skew Coercive in practice;
            // approximated as Military rather than truly per-policy domain-matched.
            if (upkeepDrain > 0f && civ.Economy != null)
                civ.Economy.Stock[CivilizationEconomy.Military] = Mathf.Max(0f, civ.Economy.Stock[CivilizationEconomy.Military] - upkeepDrain);
        }
    }

    /// Switches one policy slot — enforces the gate, the lockout, and charges the real switching
    /// cost (era3-policy-catalog-spec §1.4): higher-Conformity civs pay more, mirroring the
    /// exploration/exploitation tradeoff already encoded in VariationScore.
    public bool SwitchPolicy(CivilizationState civ, Era3PolicyCatalog.PolicySlot slot, string newOptionId)
    {
        EnsurePolicyDefaults(civ);
        if (!Era3PolicyCatalog.TryGet(newOptionId, out var opt) || opt.Slot != slot) return false;
        if (!Era3PolicyCatalog.IsUnlocked(civ, newOptionId)) return false;
        if (!civ.PolicySlots.TryGetValue(slot, out var state)) { state = new PolicySlotState(); civ.PolicySlots[slot] = state; }
        if (state.ActiveId == newOptionId) return false;
        if (state.LockoutTicksRemaining > 0)
        {
            LogEvent($"{civ.Name} cannot switch {slot} policy yet — still settling in from the last change.");
            return false;
        }

        float conformity = 1f - Era3TechTree.VariationScore(civ);
        float cost = Era3PolicyCatalog.BaseSwitchCost * (1f + conformity);
        // era3-systems-implementation-spec §6: policy switch cost redirected from Stockpile to
        // Economic output (Industry stock) first, same spillover to Resilience if short.
        float available = civ.Economy?.Stock[CivilizationEconomy.Industry] ?? 0f;
        if (available >= cost) civ.Economy.Stock[CivilizationEconomy.Industry] -= cost;
        else
        {
            civ.DrainResilience(cost - available);
            if (civ.Economy != null) civ.Economy.Stock[CivilizationEconomy.Industry] = 0f;
        }

        state.PreviousId = state.ActiveId;
        state.ActiveId = newOptionId;
        state.TicksSinceSwitch = 0f;
        state.LockoutTicksRemaining = Era3PolicyCatalog.BaseLockoutTicks;

        LogEvent($"{civ.Name} adopts {opt.Name}.");
        return true;
    }

    // ── Ecological paths (era3-ecological-paths-spec §1-§4) ─────────────────────────────────
    // Terraformer/BloomFront/ApexPredator/LivingReef resource growth + conflict-posture resolution.
    // See Era3EcologicalPaths.cs for the effect-resolution engine and option catalogs this drives.
    private const float EcoTickInterval = 6f;
    private float _ecoTimer;

    private void TickEcologicalPaths()
    {
        _ecoTimer -= Time.deltaTime;
        if (_ecoTimer > 0f) return;
        _ecoTimer = EcoTickInterval;

        foreach (var civ in _allCivs)
        {
            if (civ.Path == Era3Path.CommerceEngine) continue;

            float magnitude = civ.Path switch
            {
                Era3Path.Terraformer   => Era3EcologicalPaths.TerraformerMagnitude(this, civ),
                Era3Path.BloomFront    => Era3EcologicalPaths.BloomFrontMagnitude(this, civ),
                Era3Path.ApexPredator  => Era3EcologicalPaths.ApexPredatorMagnitude(this, _spawner, civ),
                // LivingReef has no dedicated §3 formula — reuses the Terraformer biomass-scale
                // proxy at reduced weight (a colony's growth is population-driven, same shape).
                Era3Path.LivingReef => Era3EcologicalPaths.TerraformerMagnitude(this, civ) * 0.5f,
                _ => 0f,
            };
            if (magnitude <= 0f) continue;

            // Self-ring baseline growth — this IS the path's normal population growth each tick.
            Era3EcologicalPaths.ApplyEffect(this, civ, Era3EcologicalPaths.EcoRing.Self, magnitude);

            ResolveConflictPosture(civ, magnitude);

            if (civ.Path == Era3Path.Terraformer)
                Era3EcologicalPaths.TickRunawayRisk(this, civ, EcoTickInterval);
        }
    }

    /// Resolves a civ's currently-selected conflict-posture choice. Index 2 is the de-escalation
    /// option for every path's 3-option conflict table (Substrate Partition / Neutral Terraforming /
    /// Migratory Avoidance / Trophic Coexistence) — never fires an offensive effect. Everything else
    /// fires probabilistically (not every tick, same cadence style as the Commerce Engine's
    /// TickConflict) as a Target-ring strike, except Bloom Front's Toxic Bloom (posture 1), the one
    /// maneuver the spec explicitly calls out as inherently wide-radius (§4.4).
    // era3-civilization-tracks-spec §6 open item: no severity_tier assigned to any of the ecological
    // paths' named maneuvers yet, flagged as blocking real magnitude computation. Resolved here with
    // real (TUNABLE) values: posture 0 is each path's primary/direct maneuver (Smother, Biochemical
    // Warfare, Shade-Out, Territorial Exclusion) at full severity; posture 1 is the narrower/
    // specialized or contact-escalating alternative (Chemical Defense, Niche Hoarding, Toxic Bloom,
    // Kleptoparasitism) at reduced immediate severity — Toxic Bloom's much wider Diffuse reach and
    // Kleptoparasitism's resource-transfer already compensate mechanically for hitting softer.
    private static readonly float[] SeverityByPosture = { 1.0f, 0.6f };

    private void ResolveConflictPosture(CivilizationState civ, float magnitude)
    {
        int posture = civ.EcoConflictPosture;
        if (posture < 0 || posture == 2) return; // unset, or the de-escalation option
        if (UnityEngine.Random.value > 0.35f) return;

        magnitude *= SeverityByPosture[posture];
        bool diffuse = civ.Path == Era3Path.BloomFront && posture == 1; // Toxic Bloom
        Era3EcologicalPaths.ApplyEffect(this, civ,
            diffuse ? Era3EcologicalPaths.EcoRing.Diffuse : Era3EcologicalPaths.EcoRing.Target, magnitude);

        // Kleptoparasitism (Apex Predator, posture 1): a resource TRANSFER per §4.5, not pure
        // destruction — the target's loss is also a gain for the predator.
        if (civ.Path == Era3Path.ApexPredator && posture == 1)
            Era3EcologicalPaths.ApplyEffect(this, civ, Era3EcologicalPaths.EcoRing.Self, magnitude * 0.5f);
    }

    /// Sum of Population across settlements OWNED by one specific civ — the per-community version of
    /// TotalSettlementPopulation, for a "how big is MY species really" readout.
    public float SettlementPopulationForCiv(int civId)
    {
        float sum = 0f;
        foreach (var s in Settlements) if (s.OwnerCivId == civId) sum += s.Population;
        return sum;
    }

    /// True if this civ currently holds at least one occupied-but-unrecognized settlement — gates
    /// the "Recognize Occupied Territory" decision.
    public bool HasOccupiedTerritory(int civId)
    {
        foreach (var s in Settlements)
            if (s.OwnerCivId == civId && s.IsOccupied) return true;
        return false;
    }

    /// Formally ratifies every settlement this civ currently occupies — a peace treaty/international
    /// recognition. RecognizedOwnerCivId catches up to OwnerCivId, so the striped/contested territory
    /// rendering (see Era3VisualManager) converts to solid, permanent color.
    public void FormalizeOccupiedTerritory(int civId)
    {
        int n = 0;
        foreach (var s in Settlements)
        {
            if (s.OwnerCivId != civId || !s.IsOccupied) continue;
            int formerOwner = s.RecognizedOwnerCivId;
            s.RecognizedOwnerCivId = s.OwnerCivId;
            n++;
            // era3-diplomacy-ai-spec §1.2 "accepted peace" — the formal treaty is what actually
            // ratifies the conquest, so the relation event fires here, not at the strike itself.
            if (formerOwner >= 0) ApplyRelationEvent(civId, formerOwner, Era3Diplomacy.ValenceAcceptedPeace);
        }
        if (n > 0) LogEvent($"Occupation formally recognized — {n} settlement(s) permanently annexed.");
    }

    /// The alternative to formalizing: withdraw from every settlement this civ currently occupies,
    /// handing ownership back to whoever last held recognized title. Ends the dispute without keeping
    /// the territory.
    public void WithdrawFromOccupiedTerritory(int civId)
    {
        int n = 0;
        foreach (var s in Settlements)
        {
            if (s.OwnerCivId != civId || !s.IsOccupied) continue;
            s.OwnerCivId = s.RecognizedOwnerCivId; // hand back to the last recognized owner
            n++;
        }
        if (n > 0) LogEvent($"Withdrew from occupied territory — {n} settlement(s) returned.");
    }

    public void SpawnSettlement(CivilizationState civ, SettlementTier tier)
    {
        var s = new Settlement
        {
            Id           = _nextSettlementId++,
            Name         = $"{civ.Name ?? "Settlement"} {SettlementTierLabel(civ.Path, tier)}",
            Tier         = tier,
            FounderCivId = civ.CommunityId,
            OwnerCivId   = civ.CommunityId,   // starts owned by founder
            RecognizedOwnerCivId = civ.CommunityId, // recognized from the moment it's founded — not occupied
            Population   = tier == SettlementTier.Village ? 1f
                         : tier == SettlementTier.Town    ? 5f : 20f,
            PlayerCultureFraction = civ.CommunityId == PlayerCiv.CommunityId ? 1f : 0f,
            Position     = ChooseSettlementPosition(civ),
        };
        s.ContributingCommunities.Add(civ.CommunityId);
        Settlements.Add(s);
        Debug.Log($"[Settlement] {s.Name} founded by civ {civ.CommunityId} at {s.Position}.");
    }

    /// Chooses a civilization's Era 3 dominance PATH from its Era 2 archetype (not from which
    /// thresholds it happened to cross, so it's stable under the debug skip too). Priority: a
    /// Distributed colony is a LivingReef; a tool-user or motile-social species runs the social
    /// Commerce Engine; otherwise it's an ecological power keyed to metabolism + motility.
    public static Era3Path DeterminePath(CommunityIntelligence rec)
    {
        if (rec == null) return Era3Path.CommerceEngine;

        if (rec.Architecture == CognitiveArchitecture.Distributed
            && rec.Sociality >= SocialityBaseline.Aggregating)
            return Era3Path.LivingReef;

        if (rec.Manipulation >= ManipulationLevel.Simple
            || (rec.HasMotility && rec.Sociality >= SocialityBaseline.Aggregating))
            return Era3Path.CommerceEngine;

        bool producer = rec.EnergyStrategy == MetabolismType.Chemosynthetic
                     || rec.EnergyStrategy == MetabolismType.Phototrophic;
        bool consumer = rec.EnergyStrategy == MetabolismType.Heterotrophic
                     || rec.EnergyStrategy == MetabolismType.Mixotrophic;
        if (consumer && rec.HasMotility) return Era3Path.ApexPredator;
        if (producer && rec.HasMotility) return Era3Path.BloomFront;
        if (producer)                    return Era3Path.Terraformer;
        return Era3Path.CommerceEngine;
    }

    /// True once a community has an actual Era 3 settlement (as founder or current owner). Used to
    /// retire the older, cruder TerritorialityManager colony marker/population-number for that
    /// community — a proper settlement marker supersedes it as the "civilization here" visual, and
    /// showing both was drawing two independent overlapping text labels at nearly the same spot.
    public bool CivHasSettlement(int civId)
    {
        foreach (var s in Settlements)
            if (s.FounderCivId == civId || s.OwnerCivId == civId) return true;
        return false;
    }

    /// Records one abstract birth for a civilized community. population-energy-aggregation-spec.md
    /// §2/§4 migration: population MAGNITUDE growth is now cohort-tick-driven (TickCohorts' mean-field
    /// logistic growth), not birth-event-driven — a birth event no longer adds population directly.
    /// What it DOES still do: guarantee the lineage's CivPopulation cohort exists at its largest
    /// settlement (so the logistic tick has something to grow), and fold the still-living parent's
    /// real trait state into that cohort's trait_snapshot — the parent hasn't been absorbed yet (see
    /// AgentController.Reproduce), so this is real biological data, not a synthetic default. The
    /// era3-policy-catalog-spec PopulationGrowth dial that used to scale this per-birth increment now
    /// scales the logistic tick's growth rate instead (see TickCohorts) — same policy, new location.
    public void RegisterAbstractBirth(int civId, float parentSizeScale, MetabolismType parentMetabolism,
                                       BackboneElement parentBackbone, float parentPhotoEff, float parentChemoEff)
    {
        Settlement best = null;
        foreach (var s in Settlements)
            if (s.OwnerCivId == civId && (best == null || s.Population > best.Population))
                best = s;
        if (best == null) return;
        var cohort = FindOrCreateCivPopulationCohort(best, civId);
        // biomassContribution: 0 — magnitude comes from the logistic tick only; this is a trait-only nudge.
        cohort.SeedOrNudge(parentSizeScale, parentMetabolism, parentBackbone, parentPhotoEff, parentChemoEff, 0f);
    }

    /// The Era 3 path of the civ that owns/founded a given community id (for settlement re-skinning).
    public Era3Path GetCivPath(int civId)
    {
        if (PlayerCiv != null && PlayerCiv.CommunityId == civId) return PlayerCiv.Path;
        foreach (var c in NpcCivs) if (c.CommunityId == civId) return c.Path;
        return Era3Path.CommerceEngine;
    }

    /// Path-appropriate name for a settlement tier — the "re-skin" so each path's settlements read as
    /// what they actually are (a Terraformer builds metabolic provinces, not villages).
    public static string SettlementTierLabel(Era3Path path, SettlementTier tier)
    {
        switch (path)
        {
            case Era3Path.LivingReef: return tier == SettlementTier.City ? "Nexus"    : tier == SettlementTier.Town ? "Cluster" : "Node";
            case Era3Path.Terraformer:   return tier == SettlementTier.City ? "Province" : tier == SettlementTier.Town ? "Field"   : "Vent";
            case Era3Path.BloomFront:    return tier == SettlementTier.City ? "Front"    : tier == SettlementTier.Town ? "Swarm"   : "Bloom";
            case Era3Path.ApexPredator:  return tier == SettlementTier.City ? "Domain"   : tier == SettlementTier.Town ? "Range"   : "Den";
            default:                     return tier == SettlementTier.City ? "City"     : tier == SettlementTier.Town ? "Town"    : "Village";
        }
    }

    /// Picks a world-surface location for a new settlement. A civ with living member agents (the
    /// player community) settles at that population's centroid; a civ with no agents on the map
    /// (an abstract NPC civ) gets a stable pseudo-random surface point seeded by its id. Successive
    /// settlements of the same civ are nudged apart so they don't stack.
    private Vector3 ChooseSettlementPosition(CivilizationState civ)
    {
        Vector3 center = _spawner != null ? _spawner.planetCenter : Vector3.zero;
        float radius   = _spawner != null ? _spawner.planetRadius : 20f;

        Vector3 basePos;
        Vector3 sum = Vector3.zero; int n = 0;
        if (_spawner != null)
            foreach (var a in _spawner.ActiveAgents)
                if (a != null && a.communityId == civ.CommunityId) { sum += a.transform.position; n++; }

        if (n > 0)
        {
            basePos = sum / n;
        }
        else
        {
            // Abstract civ (no agents) — deterministic point from its id so its settlements cluster.
            UnityEngine.Random.State prev = UnityEngine.Random.state;
            UnityEngine.Random.InitState(civ.CommunityId * 7919 + 17);
            basePos = SphereSurface.RandomPointOnSphere(center, radius);
            UnityEngine.Random.state = prev;
        }

        // Nudge apart from this civ's existing settlements so tiers/multiples don't overlap.
        int existing = 0;
        foreach (var s in Settlements) if (s.FounderCivId == civ.CommunityId) existing++;
        if (existing > 0)
        {
            Vector3 normal = (basePos - center).normalized;
            Vector3 tangent = Vector3.Cross(normal, UnityEngine.Random.onUnitSphere).normalized;
            basePos = center + Quaternion.AngleAxis(existing * 9f, tangent) * (basePos - center);
        }

        return SphereSurface.ProjectToSurface(basePos, center, radius);
    }

    /// Called whenever a settlement is promoted; checks if it qualifies for join prompt.
    public void CheckSettlementJoin(Settlement s)
    {
        // Only unaffiliated settlements with 100% player culture prompt the player.
        if (s.OwnerCivId != -1) return;
        if (s.PlayerCultureFraction < 0.999f) return;
        if (_pendingSettlementJoin != null) return; // one prompt at a time
        _pendingSettlementJoin = s;
        string tierName = s.Tier.ToString();
        LogEvent($"[Settlement] {s.Name} ({tierName}) is 100% your culture — integrate or recognise?");
        Debug.Log($"[Settlement] Join prompt: {s.Name}");
    }

    private void TickSettlements()
    {
        foreach (var s in Settlements)
        {
            // Cultural drift: unaffiliated settlements absorb player culture from spread.
            if (s.OwnerCivId == -1 || s.OwnerCivId != PlayerCiv.CommunityId)
            {
                // Use the CulturalInfluence already computed by TickCultureSpread.
                // Proxy: average player influence in all civs with exchange contact.
                float playerPressure = 0f;
                int   count = 0;
                foreach (var civ in _allCivs)
                {
                    if (civ.CulturalInfluence.TryGetValue(PlayerCiv.CommunityId, out float inf))
                    { playerPressure += inf; count++; }
                }
                if (count > 0) playerPressure /= count;

                float prev = s.PlayerCultureFraction;
                s.PlayerCultureFraction = Mathf.Clamp01(
                    Mathf.Lerp(s.PlayerCultureFraction, playerPressure, 0.05f));

                // Check join threshold when culture just hit 100%.
                if (prev < 0.999f && s.PlayerCultureFraction >= 0.999f)
                    CheckSettlementJoin(s);
            }

            // Vassal loyalty decay: tribute burden + proximity to a stronger power.
            if (s.IndependentCivId >= 0)
            {
                var indCiv = GetCiv(s.IndependentCivId);
                if (indCiv != null && indCiv.SuzerainId >= 0)
                    TickVassalLoyalty(indCiv);
            }
        }

        // Also tick loyalty for all NPC civs that are vassals.
        foreach (var npc in NpcCivs)
            if (npc.SuzerainId >= 0)
                TickVassalLoyalty(npc);
    }

    private void TickVassalLoyalty(CivilizationState vassal)
    {
        var suzerain = GetCiv(vassal.SuzerainId);
        if (suzerain == null) { vassal.SuzerainId = -1; return; }

        // era3-sovereignty-interaction-gaps-spec.md §1.3 / era3-systems-implementation-spec §6:
        // tribute — 5% of the vassal's current Economic output per tick, flowing to the overlord's
        // Economic stock (Stockpile retired). The payment feeds directly into TradeHealth, the exact
        // input the loyalty-recovery check below already reads — closing the loop instead of adding a
        // second, disconnected tribute number. A flow measurement, not a drawable stock, so it isn't
        // subtracted from the vassal's own accumulated Stock — only added to the suzerain's.
        float tribute = (vassal.Economy?.Output[CivilizationEconomy.Industry] ?? 0f) * TributeRate;
        if (tribute > 0f)
        {
            if (suzerain.Economy != null)
                suzerain.Economy.Stock[CivilizationEconomy.Industry] += tribute;
            float curTh = suzerain.TradeHealth.TryGetValue(vassal.CommunityId, out float thv) ? thv : 0f;
            suzerain.TradeHealth[vassal.CommunityId] = Mathf.Clamp(curTh + TributeTradeHealthBoost, -1f, 1f);
        }

        // Loyalty decays with resilience gap: weaker vassal relative to suzerain = more resentment.
        float gap   = Mathf.Max(0f, suzerain.Resilience - vassal.Resilience);
        float drain = 0.005f + gap * 0.01f;  // per settlement tick (~1 min)
        vassal.VassalLoyalty = Mathf.Clamp01(vassal.VassalLoyalty - drain);

        // Recover loyalty if suzerain is trading fairly.
        if (suzerain.TradeHealth.TryGetValue(vassal.CommunityId, out float th) && th > 0.25f)
            vassal.VassalLoyalty = Mathf.Clamp01(vassal.VassalLoyalty + 0.003f);

        if (vassal.VassalLoyalty < VassalRebellionThreshold && _pendingVassalRebellion == null)
        {
            // Only surface rebellion to player if player is suzerain or is the vassal.
            if (suzerain.CommunityId == PlayerCiv.CommunityId
             || vassal.CommunityId   == PlayerCiv.CommunityId)
            {
                _pendingVassalRebellion = vassal;
                LogEvent($"[Vassal] {vassal.Name ?? $"Civ {vassal.CommunityId}"} is on the verge of rebellion!");
                AudioManager.Instance?.OnCrisisWarning();
            }
            else
            {
                // NPC auto-resolve: vassal breaks free silently.
                vassal.SuzerainId    = -1;
                vassal.VassalLoyalty = 0.5f;
            }
        }
    }

    // ── Public settlement / vassal API ────────────────────────────────────────

    /// Non-null when an unaffiliated settlement has reached 100% player culture.
    public Settlement PendingSettlementJoin => _pendingSettlementJoin;

    /// Non-null when a player-relevant vassal is about to rebel.
    public CivilizationState PendingVassalRebellion => _pendingVassalRebellion;

    /// Player accepts or declines a settlement's petition to join their state.
    public void ResolveSettlementJoin(bool accept)
    {
        var s = _pendingSettlementJoin;
        if (s == null) return;
        _pendingSettlementJoin = null;

        if (accept)
        {
            s.OwnerCivId = PlayerCiv.CommunityId;
            PlayerCiv.InvestEconomic = Mathf.Min(PlayerCiv.InvestEconomic + 0.05f * (int)s.Tier + 0.05f, 1f);
            LogEvent($"[Settlement] {s.Name} joins your state.");
            AudioManager.Instance?.OnCivFounded();
        }
        else
        {
            // Declined: becomes an independent civilisation.
            s.OwnerCivId = -1;
            int newId = 200 + s.Id;
            var indCiv = new CivilizationState
            {
                CommunityId  = newId,
                Name         = s.Name,
                Architecture = PlayerCiv.Architecture, // cultural kin — same cognitive type
                IsPlayer     = false,
                Resilience   = 0.4f + 0.1f * (int)s.Tier,
                InvestEconomic = 0.3f,
            };
            indCiv.InitNativeDomains();
            s.IndependentCivId = newId;
            NpcCivs.Add(indCiv);
            _allCivs.Add(indCiv);
            EnsureTradeInit(PlayerCiv, newId);
            EnsureTradeInit(indCiv, PlayerCiv.CommunityId);
            LogEvent($"[Settlement] {s.Name} declares independence.");
            AudioManager.Instance?.OnCivFounded();
            Debug.Log($"[Settlement] {s.Name} → independent civ {newId}.");
        }
    }

    /// Player attempts to vassalize a target civ (must be weaker).
    public bool TryVassalize(int targetId)
    {
        var target = GetCiv(targetId);
        if (target == null || target.HasCollapsed) return false;
        if (target.SuzerainId >= 0) return false; // already a vassal
        if (!HasFormalMediation(PlayerCiv) || !HasFormalMediation(target)) return false;
        // era3-tech-idea-trees-spec §5: I3c (Formal Diplomacy Norms) is what's supposed to gate
        // this. Not hard-gated solely on I3c — an unplaytested formula stalling this brand-new tree
        // shouldn't silently break already-working Vassalization — so e3_state_formation (an
        // existing, reliably-reached milestone) is accepted as an equivalent fallback.
        if (PlayerCiv.Path == Era3Path.CommerceEngine
            && !PlayerCiv.UnlockedNodes.Contains("I3c") && !PlayerCiv.Has("e3_state_formation"))
        {
            LogEvent("[Vassal] Formal diplomacy norms not yet established.");
            return false;
        }

        // Require meaningful power advantage: player resilience + kinetic domain > target's.
        float playerStrength = PlayerCiv.Resilience + PlayerCiv.DomainKinetic * 0.5f;
        float targetStrength = target.Resilience    + target.DomainKinetic    * 0.5f;
        if (playerStrength <= targetStrength * 1.3f)
        {
            LogEvent($"[Vassal] {target.Name} is too strong to vassalize yet.");
            return false;
        }

        target.SuzerainId    = PlayerCiv.CommunityId;
        target.VassalLoyalty = 0.6f;  // starts at uneasy compliance
        PlayerCiv.DomainEconomic = Mathf.Min(PlayerCiv.DomainEconomic + 0.08f, 1f);
        // era3-sovereignty-interaction-gaps-spec.md §1.3/§1.6: overlord basing/extraction rights in
        // vassal territory reuse HostGuestRelation's substrate_footprint machinery rather than a
        // parallel structure (the spec's own recommendation, implemented here as the resolution).
        ForceVassalHostGuestRelation(PlayerCiv.CommunityId, target.CommunityId);
        LogEvent($"[Vassal] {target.Name ?? $"Civ {targetId}"} becomes your vassal.");
        AudioManager.Instance?.OnTreatyFormed();
        return true;
    }

    /// Forces a HostGuestRelation representing the overlord's basing/extraction rights in the
    /// vassal's territory — vassal is Host (owns the territory), overlord is Guest (operates within
    /// it). Bypasses ProposeHostGuestRelation's voluntary preconditions (mutual agreement, not-at-war)
    /// since this is a coerced consequence of vassalage, not a treaty either side agreed to — matters
    /// specifically for the war-peace-term path (§1.2), where the war hasn't been removed from
    /// _atWar yet at the moment TryVassalize runs.
    private void ForceVassalHostGuestRelation(int overlordId, int vassalId)
    {
        if (GetHostGuestRelation(vassalId, overlordId) != null) return; // already exists
        _hostGuestRelations.Add(new HostGuestRelation { HostCivId = vassalId, GuestCivId = overlordId });
    }

    /// Resolve a vassal rebellion: suppress (drain their resilience) or grant independence.
    public void ResolveVassalRebellion(bool suppress)
    {
        var vassal = _pendingVassalRebellion;
        if (vassal == null) return;
        _pendingVassalRebellion = null;

        if (suppress)
        {
            vassal.DrainResilience(0.15f);
            vassal.VassalLoyalty = 0.35f; // still unhappy but subdued
            PlayerCiv.DomainKinetic = Mathf.Min(PlayerCiv.DomainKinetic + 0.03f, 1f);
            LogEvent($"[Vassal] Rebellion in {vassal.Name} suppressed by force.");
            AudioManager.Instance?.OnWarDeclared();
        }
        else
        {
            // Grant independence: lose tribute but gain trade-health goodwill.
            vassal.SuzerainId    = -1;
            vassal.VassalLoyalty = 0.8f;
            if (!PlayerCiv.TradeHealth.ContainsKey(vassal.CommunityId)) PlayerCiv.TradeHealth[vassal.CommunityId] = 0f;
            PlayerCiv.TradeHealth[vassal.CommunityId] =
                Mathf.Clamp(PlayerCiv.TradeHealth[vassal.CommunityId] + 0.25f, -1f, 1f);
            // §1.3/§1.6: independence also ends the overlord's forced basing/extraction rights —
            // the ordinary Withdrawing/Terminated state machine (TickHostGuestRelations) then handles
            // the actual footprint wind-down the same way any other ending relation does.
            var forced = GetHostGuestRelation(vassal.CommunityId, PlayerCiv.CommunityId);
            if (forced != null) forced.State = HostGuestState.Withdrawing;
            LogEvent($"[Vassal] {vassal.Name} granted independence — trade relations improved.");
            AudioManager.Instance?.OnTreatyFormed();
        }
    }

    // ── Cultural spread ───────────────────────────────────────────────────────

    /// Outward radiation from each civ into civs it has trade contact with,
    /// plus exclave detection when penetration crosses ExclaveThreshold.
    private void TickCultureSpread()
    {
        foreach (var source in _allCivs)
        {
            // How strongly this civ projects its culture outward.
            float radiation = source.BeliefTier      * 0.15f
                            + source.SectorCulture   * 0.20f
                            + source.NarrativePlasticity * 0.10f
                            + source.ForeignOpenness * 0.10f;
            // era3-systems-implementation-spec §2: ProselytizePosture (Individuated only) is the base
            // diffusion contribution Missionary Outreach/Doctrinal Supremacy-style policies build on.
            if (source.Architecture == CognitiveArchitecture.Individuated)
                radiation += source.ProselytizePosture * 0.15f;
            if (radiation <= 0f) continue;

            foreach (var target in _allCivs)
            {
                if (target.CommunityId == source.CommunityId) continue;
                if (!source.Has("e3_exchange_contact") || !target.Has("e3_exchange_contact")) continue;

                // Resistance: orthodoxy (Individuated) and censorship dampen intake. Censorship is
                // now read off the gated State Doctrine Control / Open Academy policies' effect on
                // SignalLegibility (era3-adaptation-trees-spec §1.1 retires the free slider) — low
                // legibility to outsiders reads as high resistance to their cultural influence.
                float resistance = 1f + target.OrthodoxyLevel * 1.5f
                                      + (1f - Mathf.Clamp01(Era3PolicyCatalog.GetVar(target, Era3PolicyCatalog.Var.SignalLegibility))) * 1.0f;

                // Scale so a maxed-out source takes ~5 real minutes to reach threshold.
                float delta   = radiation * CultureTickInterval / resistance * 0.003f;
                float decay   = 0.0015f * CultureTickInterval; // passive decay toward zero

                int   srcId   = source.CommunityId;
                float prev    = target.CulturalInfluence.TryGetValue(srcId, out float cv) ? cv : 0f;
                float next    = Mathf.Clamp01(prev + delta - prev * decay);

                if (next < 0.005f)
                    target.CulturalInfluence.Remove(srcId);
                else
                    target.CulturalInfluence[srcId] = next;

                // Exclave detection: first crossing of threshold this tick.
                if (prev < ExclaveThreshold && next >= ExclaveThreshold)
                    OnExclaveThresholdCrossed(source, target, next);
            }
        }
    }

    private void OnExclaveThresholdCrossed(CivilizationState source,
                                            CivilizationState target, float fraction)
    {
        if (target.HasCollapsed) return;

        bool sourceAggressive = source.DomainKinetic      > 0.55f
                             || source.DiplomaticPosture   > 0.65f;
        bool majorExclave     = fraction >= ExclaveMajorThreshold;

        bool playerInvolved = source.CommunityId == PlayerCiv.CommunityId
                           || target.CommunityId == PlayerCiv.CommunityId;

        // NPC-only crossings: auto-resolve silently (drain target slightly).
        if (!playerInvolved)
        {
            if (sourceAggressive)
                target.DrainResilience(majorExclave ? 0.05f : 0.02f);
            return;
        }

        bool sourceIsPlayer = source.CommunityId == PlayerCiv.CommunityId;
        string srcName = source.Name ?? $"Civ {source.CommunityId}";
        string tgtName = target.Name ?? $"Civ {target.CommunityId}";

        _pendingExclaveSource        = source;
        _pendingExclaveTarget        = target;
        _pendingExclavePlayerIsSource = sourceIsPlayer;

        if (sourceIsPlayer)
        {
            // Player's culture is spreading into an NPC — offer a choice.
            string hint = sourceAggressive
                ? $"Your culture now claims {fraction*100f:F0}% of {tgtName}. Press your advantage?"
                : $"Your culture has taken root in {tgtName} ({fraction*100f:F0}%). Assert influence?";
            LogEvent(hint);
            Debug.Log($"[Culture] Exclave: player→{tgtName} @ {fraction*100f:F0}%");
        }
        else
        {
            // NPC's culture has spread into the player — NPC decides whether to act.
            if (sourceAggressive)
            {
                string action = source.DomainKinetic > 0.65f
                    ? $"{srcName} threatens to seize its cultural exclave by force."
                    : $"{srcName} demands formal recognition of its cultural exclave.";
                LogEvent(action);
                AudioManager.Instance?.OnCrisisWarning();
                if (majorExclave)
                    target.DrainResilience(0.04f);  // immediate pressure
            }
            else
            {
                LogEvent($"{srcName} culture has quietly spread to {fraction*100f:F0}% of your population.");
            }
            Debug.Log($"[Culture] Exclave: {srcName}→player @ {fraction*100f:F0}%");
        }
    }

    // ── Public exclave API ────────────────────────────────────────────────────

    /// Non-null when a player-relevant exclave threshold was just crossed.
    /// HUD should poll this and present Claim / Diplomacy / Withdraw options.
    public (CivilizationState source, CivilizationState target, bool playerIsSource)?
        PendingExclave => (_pendingExclaveSource != null)
            ? (_pendingExclaveSource, _pendingExclaveTarget, _pendingExclavePlayerIsSource)
            : null;

    /// Resolve the pending exclave. Call from HUD after player makes a choice.
    public void ResolveExclave(bool claimByForce, bool claimByDiplomacy)
    {
        if (_pendingExclaveSource == null) return;
        var source = _pendingExclaveSource;
        var target = _pendingExclaveTarget;
        bool playerIsSource = _pendingExclavePlayerIsSource;
        _pendingExclaveSource = null;
        _pendingExclaveTarget = null;

        string srcName = source.Name ?? $"Civ {source.CommunityId}";
        string tgtName = target.Name ?? $"Civ {target.CommunityId}";

        if (claimByForce)
        {
            // Annexation attempt: drains target resilience, hardens source kinetic domain.
            source.DomainKinetic = Mathf.Min(1f, source.DomainKinetic + 0.06f);
            target.DrainResilience(0.10f);
            // Cultural influence locks in at current level — hard to reverse now.
            int srcId = source.CommunityId;
            if (target.CulturalInfluence.TryGetValue(srcId, out float f))
                target.CulturalInfluence[srcId] = Mathf.Min(1f, f + 0.15f);
            LogEvent($"[Exclave] {srcName} seizes exclave in {tgtName} by force.");
            AudioManager.Instance?.OnWarDeclared();
        }
        else if (claimByDiplomacy)
        {
            // Recognised autonomy: improves trade health, may seed formal alliance.
            int srcId = source.CommunityId;
            if (!target.TradeHealth.ContainsKey(srcId)) target.TradeHealth[srcId] = 0f;
            target.TradeHealth[srcId] = Mathf.Clamp(target.TradeHealth[srcId] + 0.20f, -1f, 1f);
            if (!source.TradeHealth.ContainsKey(target.CommunityId)) source.TradeHealth[target.CommunityId] = 0f;
            source.TradeHealth[target.CommunityId] = Mathf.Clamp(source.TradeHealth[target.CommunityId] + 0.10f, -1f, 1f);
            source.FormalAllianceActive = true;
            LogEvent($"[Exclave] {srcName} integrates {tgtName} exclave diplomatically.");
            AudioManager.Instance?.OnTreatyFormed();
        }
        else
        {
            // Withdraw: accelerate cultural decay in that target.
            int srcId = source.CommunityId;
            if (target.CulturalInfluence.TryGetValue(srcId, out float f))
                target.CulturalInfluence[srcId] = f * 0.25f;
            LogEvent($"[Exclave] {srcName} withdraws cultural claim on {tgtName}.");
        }
    }

    private void TickTradeEngine()
    {
        // Tick economy model for every civilization (policy-allocation-spec §3). era3-systems-
        // implementation-spec §1: four of the five channel dials feed directly in as sector-targeted
        // opportunity costs (Biological's own direct cost lands on PopGrowth instead — TickCohortGroup).
        foreach (var civ in _allCivs)
        {
            // era3-systems-implementation-spec §8: Large Initiative ongoing cost/completion bonus for
            // the two tracks with a real CivilizationEconomy (CommerceEngine→Industry, Terraformer→
            // Environment) — the other three tracks' effects live in TickCohortGroup/TickRunawayRisk.
            float industryMult = 1f, environmentMult = 1f;
            if (civ.Path == Era3Path.CommerceEngine)
            {
                if (civ.LargeInitiativeActive) industryMult *= 0.85f;
                if (civ.LargeInitiativeCompleted) industryMult *= 1.15f;
            }
            else if (civ.Path == Era3Path.Terraformer && civ.LargeInitiativeCompleted)
            {
                environmentMult *= 1.15f;
            }
            civ.Economy?.Tick(TradeTickInterval, civ.InvestEconomic, civ.InvestInformation, civ.InvestReligion, civ.InvestCoercive, industryMult, environmentMult);
        }

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
        {
            if (civ.HasCollapsed) continue;
            civ.RecoverResilience(RecoveryRate * Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.ResilienceRecoveryRate));
            // era3-policy-catalog-spec's additive "Resilience floor +X" policies (Subsistence
            // Distribution, Even Distribution, Public Health Investment, etc.) — pulls Resilience up
            // toward that floor a bit faster whenever it's below it, without making collapse
            // impossible (a civ that starts well under its floor can still fall further from a
            // crisis landing the same tick; DrainResilience always runs after this recovery step).
            float floor = Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.ResilienceFloor);
            // era3-systems-implementation-spec §7: ExtractionTax pulls the effective floor down — a
            // civ over-extracting from its environment can't lean on a policy-granted resilience
            // floor as safely. Provisional coefficient, pending a tuning pass once running.
            floor = Mathf.Max(0f, floor - (civ.Economy?.ExtractionTax ?? 0f) * 0.1f);
            if (floor > 0f && civ.Resilience < floor)
                civ.RecoverResilience(Mathf.Min(floor - civ.Resilience, floor * 0.1f));
        }

        stockpile: ; // label kept (goto target above); §4.2's Stockpile accumulation loop removed
        // (era3-systems-implementation-spec §6) — CivilizationEconomy.Tick already accumulates
        // Industry/Housing stock organically every trade tick, making a second parallel accumulator
        // redundant now that Stockpile itself is gone. e3_surplus_economy is untouched — it still
        // gates FormalTradeActive, Large Initiative eligibility, etc.
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
            // Mutualism: both parties benefit. era3-systems-implementation-spec §6: redirected from
            // Stockpile to Economic output (Industry stock) — Trade itself was never a "consumer,"
            // only ever adds, so this inflow still needs somewhere real to land.
            if (a.Economy != null) a.Economy.Stock[CivilizationEconomy.Industry] += 0.005f;
        }
        else if (health < -0.25f)
        {
            // Parasitism: disadvantaged party loses resilience slowly.
            DrainWeighted(a, "trade_parasitism_slow", 0.001f, 0 /* Economic */);
        }

        // §4.5 Arbitrage — high-info party exploits low-openness partner.
        if (a.ForeignOpenness < 0.25f && b.InvestInformation > b.InvestEconomic && b.Economy != null)
            b.Economy.Stock[CivilizationEconomy.Industry] += 0.015f;
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
            // era3-adaptation-trees-spec §1.1: the free Public Health Investment slider is retired —
            // superseded by the gated Public Health Investment / Immune Caste Investment / Quarantine
            // Regime policies (all set GenDMin), so plague resistance is now earned, not free.
            drain *= Mathf.Clamp01(1f - Era3PolicyCatalog.GetVar(PlayerCiv, Era3PolicyCatalog.Var.GenDMin));
            DrainWeighted(PlayerCiv, "plague", drain, 1 /* Bio */, novel: true);
            PlayerCiv.AcquiredEvents.Remove("d3_plague_response");
            PlayerCiv.Acquire("e3_plague_active");
            LogEvent($"Plague outbreak  (weighted drain ≈{drain * ChannelSeverityWeight[1]:P0})");
        }
        // §1 famine — Economic channel (ch=0, weight=0.6), gated on stockpile. era3-systems-
        // implementation-spec §6: Stockpile retired — same shape, reads Industry stock instead
        // (not one of the seven named consumers, but the same "low reserves" pattern).
        else if (roll < 0.22f && PlayerCiv.Has("e3_trade_network"))
        {
            float industryStock = PlayerCiv.Economy?.Stock[CivilizationEconomy.Industry] ?? 0f;
            if (industryStock < 0.3f)
            {
                DrainWeighted(PlayerCiv, "famine", 0.08f, 0 /* Economic */);
                LogEvent("Famine — reserves exhausted");
            }
            else
            {
                DrainWeighted(PlayerCiv, "trade_disruption", 0.04f, 0 /* Economic */);
                LogEvent("Trade route disrupted");
            }
        }
        // §1 climate shock — Economic channel. era3-systems-implementation-spec §6: Stockpile
        // cushioning retired entirely — full drain now, no mitigation.
        else if (roll < 0.30f)
        {
            float drain = 0.06f;
            DrainWeighted(PlayerCiv, "climate_shock", drain, 0 /* Economic */, novel: true);
            LogEvent("Climate shock");
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

    // Fading announcement banner (mirrors EraManager's era-transition flash) so Era 3 events are
    // actually SEEN when they happen, not just buried in the Civilization panel's scrolling log that
    // the player may not have open.
    private const float EventFlashDuration = 4f;
    public string LastEventFlashText { get; private set; } = "";
    private float _eventFlashTimer;
    public bool EventFlashActive => _eventFlashTimer > 0f;
    public float EventFlashAlpha => Mathf.Clamp01(_eventFlashTimer / EventFlashDuration * 2f); // fades in the last half

    private void LogEvent(string msg)
    {
        EventLog.Add((_elapsed, msg));
        if (EventLog.Count > 24) EventLog.RemoveAt(0);
        GameLog.LogGlobal($"[Era3] {msg}");
        LastEventFlashText = msg;
        _eventFlashTimer = EventFlashDuration;
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
        // e3_family_norms_emerge removed — the auto-event itself was deleted.
        "e3_chiefdom"              => "Chiefdom government",
        "e3_religion_organized"    => "Organized religion (tier 3)",
        "e3_writing"               => "Writing system",
        "e3_state_formation"       => "State formation",
        "e3_warfare_organized"     => "Organized warfare doctrine",
        "e3_diplomacy"             => "Diplomatic institutions",
        "e3_empire"                => "Empire — hegemonic expansion",
        // d3_trade_policy removed (era3-systems-implementation-spec §4) — its card was deleted.
        // d3_caste_labor removed (era3-systems-implementation-spec §4) — its card was deleted.
        "d3_kinship_policy"        => "▸ Kinship policy established",
        "d3_government_transition" => "▸ Government form chosen",
        "d3_idea_patronage"        => "▸ Idea patronage directed",
        "d3_war_or_diplomacy"      => "▸ War or diplomacy chosen",
        "d3_domain_investment"     => "▸ War domain investment set",
        // era3-systems-implementation-spec §8: rebuilt as 5 track-specific ids (see Era3HUD.cs).
        "d3_large_initiative_commerce"    => "▸ Great Public Works committed",
        "d3_large_initiative_apex"        => "▸ Coordinated Territory Network committed",
        "d3_large_initiative_reef"        => "▸ Colony Fusion Event committed",
        "d3_large_initiative_terraformer" => "▸ Planetary Chemistry Cascade committed",
        "d3_large_initiative_bloomfront"  => "▸ Mass Synchronized Bloom committed",
        _                          => id,
    };
}
