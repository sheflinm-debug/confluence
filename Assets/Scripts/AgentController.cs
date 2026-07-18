using System.Collections.Generic;
using UnityEngine;

public enum MetabolismType
{
    Chemosynthetic, // default: absorbs dissolved chemicals from ChemicalNutrientPool
    Phototrophic,   // photosynthesis: harvests solar energy (evolved later)
    Heterotrophic,  // consumer: osmotrophy → saprotrophy → predation/herbivory
    Mixotrophic,    // combines photosynthesis + heterotrophy at ~70% efficiency each
}

/// Respiration evolutionary sequence (respiration-evolutionary-sequence-fix-spec): every organism
/// starts undifferentiated/primitive rather than pre-assigned a fully-efficient gas pair; lineages
/// specialize into a real anaerobic pathway matching locally-available substrate (an OR-gate branch,
/// not a single swap); aerobic respiration is a late, atmosphere-gated unlock causally downstream of
/// accumulated photosynthetic O2 output, not available from tick one.
public enum RespirationTier
{
    Primitive,          // spawn state — low-efficiency generic anaerobic metabolism
    SpecializedAnaerobic, // evolved into a real pathway (Methanogenesis/SulfateReduction/etc.) matching local substrate
    Aerobic,            // late unlock, gated on atmospheric O2 — highest yield, historically came to dominate
}

// Manipulation tier: how dexterous the organism's appendages are (Era 2 input).
/// Locomotion medium axis (morphology axis M4). New values added by gene events.
public enum LocomotionMedium
{
    Sessile,    // no self-directed movement
    Terrestrial,// ground locomotion (default for motile land organisms)
    Aquatic,    // swimming (default for motile aquatic organisms)
    Gliding,    // passive membrane — controlled descent, not powered flight (e2_gliding_adaptation)
    Aerial,     // powered flight (e2_aerial_locomotion_emergence)
}

public enum ManipulationLevel
{
    None,        // no manipulators — sessile, flagella only
    Simple,      // cilia, pseudopods — can push/pull but not grip
    Articulated, // limbs, tentacles — can grip, carry, use tools crudely
    Dexterous,   // grasping digits, prehensile structures — precision manipulation
}

// Social baseline: how naturally this lineage forms group structures (Era 2 input).
public enum SocialityBaseline
{
    Solitary,     // default — no attraction to conspecifics
    Aggregating,  // passive clustering near same-community members; quorum sensing
    GroupForming, // active cohesion, coordinated behavior, division of roles
}

// Neural complexity stage: how sophisticated the organism's signaling architecture is.
// Drives Era 2 intelligence index starting value.
public enum NeuralComplexityStage
{
    DiffuseSignaling  = 0, // chemical gradients only — no discrete nervous structures
    NerveNet          = 1, // distributed nerve net (jellyfish equivalent)
    NerveCord         = 2, // centralized cord / ganglia (flatworm/annelid equivalent)
    GanglionicCephalization = 3, // cephalic ganglion — proto-brain (pre-vertebrate)
    HighlyCentralized = 4, // appearance-generation-spec §2.2 M7 ceiling value — dorsal-CNS-equivalent
                           // or beyond; this is the value that gates the Era 2 intelligence track
                           // (§3.3). Left unfired by any Era 1 event by design: crossing into this
                           // tier is an Era 2/3 concern (Cognitive Architecture assignment), not an
                           // Era 1 one — appended here only so the axis's full value range exists.
}

/// Segmentation axis (morphology axis M2, appearance-generation-spec §2.2). Tagmatized requires
/// Metameric as a hard prerequisite — a body cannot fuse segments into functional tagmata (head/
/// thorax/abdomen) before it has repeated segments to fuse in the first place.
public enum SegmentationType
{
    Unsegmented,           // default — most Era 1 body plans never segment at all
    Metameric,             // repeated body segments (annelid-equivalent) — e1_metameric_segmentation
    Tagmatized,            // segments fused into specialized functional regions — e1_tagmatization
    SecondarilySimplified, // segmented ancestor, smoothed-over descendant — rare reversion, small-bodied lineages
}

/// Primary sensory modality (M5/M6 — appearance-generation-spec §2.2). Distinct from CommunicationMedium
/// (Era 2 §6.2, outward signaling) — this axis is about dominant sensory *input* channel. Multimodal
/// is a ceiling reached only after 2+ other modalities have already been acquired, not a starting option.
public enum SensoryModality
{
    Chemosensory,       // default — taste/smell gradients, the most primitive detection channel
    Visual,             // e1_primary_sensory_modality (Visual choice) — requires developed vision
    Mechanosensory,     // e1_primary_sensory_modality (Mechanosensory choice) — vibration/pressure, aquatic-favored
    Electroreceptive,   // e1_primary_sensory_modality (Electroreceptive choice) — aquatic, rare
    Thermoreceptive,    // e1_primary_sensory_modality (Thermoreceptive choice) — high thermal-variance habitats
    Magnetoreceptive,   // e1_primary_sensory_modality (Magnetoreceptive choice) — long-range dispersers
    Multimodal,         // e1_multimodal_sensory_integration — requires 2+ prior modalities acquired
}

/// Feeding apparatus (M9, appearance-generation-spec §2.2) — HOW an organism physically takes in
/// food. Distinct from MetabolismType (the energy-source axis): a Heterotrophic organism can be a
/// grazer, a detritivore, an active predator, or a parasite, and this axis tracks which.
public enum FeedingApparatus
{
    FilterPassive,   // default — passive uptake, no active feeding behavior yet
    Grazer,          // e1_feeding_apparatus_specialization — steady consumption of abundant low-defense food
    Detritivore,     // e1_feeding_apparatus_specialization — consumes dead organic matter, low-competition niche
    PredatorActive,  // e1_feeding_apparatus_specialization — requires motility + Consumer metabolism
    Parasitic,       // e1_feeding_apparatus_specialization — rare, requires extreme resource scarcity
    Chemosymbiotic,  // e1_feeding_apparatus_specialization — requires Chemosynthetic metabolism
    Photosymbiotic,  // e1_feeding_apparatus_specialization — requires Phototrophic/Mixotrophic metabolism
}

/// Integument elaboration (M10, appearance-generation-spec §2.2) — surface texture/type only; color
/// and bioluminescence remain a separate (still-unimplemented, circulatory-chromophore-spec) concern.
/// Chitin/ShellExternal/Crystalline mirror an existing BodyPlanType; FilamentsFur is the one genuinely
/// independent branch (a thermoregulatory covering, not a structural-support byproduct).
public enum IntegumentType
{
    BareMucous,    // default
    Scales,        // e1_integument_elaboration
    Chitin,        // e1_integument_elaboration — mirrors BodyPlanType.Exoskeleton
    FilamentsFur,  // e1_integument_elaboration — thermoregulatory, favored by hardiness specialists
    ShellExternal, // e1_integument_elaboration — mirrors BodyPlanType.Shell
    Crystalline,   // e1_integument_elaboration — mirrors BodyPlanType.Crystalline
}

/// Size class (M8, appearance-generation-spec §2.2) — a tiered read of the organism's continuous
/// physical scale (transform.localScale.x), not independent tracked state, per the spec's "state is
/// derived, not authored" principle. See AgentController.BodySizeClass for the derivation + the
/// M3-driven ceiling (unprotected body plans cap below Mega).
public enum SizeClass
{
    Micro  = 0,
    Small  = 1,
    Medium = 2,
    Large  = 3,
    Mega   = 4,
}

public enum HabitatMedium
{
    Sea,  // submerged in liquid — drift driven by liquid currents, UV attenuated
    Land, // above liquid surface on terrain — drift driven by wind
    Air,  // airborne (flying, future era) — not yet implemented, treated as Land
}

/// Protective structure evolved at the Cambrian body-plan fork (Era 1) — appearance-generation-spec
/// §2.7's M3 "structural support" axis. Inherited by all offspring; drives stat trade-offs, Era 2
/// manipulation bonuses, and (MorphologyGenerator) the rig/silhouette treatment. Exoskeleton/Shell
/// map to the spec's exo-chitin/exo-mineral; Endoskeleton kept as the EndoCartilage precursor value
/// (renaming the enum member would touch every existing reference for no behavioral gain) with
/// EndoMineralized as its follow-on upgrade.
public enum BodyPlanType
{
    None,           // hydrostatic — starting value, remains valid permanently
    Exoskeleton,    // exo-chitin: hardened cuticle: str+, hard+, speed-
    Shell,          // exo-mineral: mineralized test: hard++, speed--
    Endoskeleton,   // endo-cartilage: internal support-tissue precursor: str++, speed neutral/+; requires germ layers
    SoftBody,       // no protection: speed+
    EndoMineralized,// endo-cartilage's mineralized upgrade — requires Endoskeleton first (e1_endoskeleton_mineralization)
    MixedArmor,     // dermal ossification over an existing exoskeleton — requires extreme sustained predation
    Crystalline,    // silicon-backbone only, hard-gated at backbone-chemistry level, not a lineage choice
}

public enum BiologicalSex
{
    Asexual, // default before ReproductiveStrategyShift fires
    Male,
    Female,
}

public class AgentController : MonoBehaviour
{
    /// Set once at world genesis (SimulationBootstrap) from SolarSystemDef: L/d² normalized
    /// so Earth at 1 AU = 1. Shared by all agents; no per-organism copy needed.
    public static float WorldSolarFluxFactor = 1f;

    [Header("Sphere")]
    [HideInInspector] public Vector3 planetCenter;
    [HideInInspector] public float planetRadius;

    [Header("Movement")]
    public float turnSpeed = 8f;

    [Header("Sensing")]
    public float eatRadius = 0.5f;
    // Body-contact distance for MATING — an organism must physically reach a partner to reproduce,
    // not conceive at sense range. Scales a little with body size so larger organisms "touch" from
    // slightly further (their bodies are bigger), floored at eatRadius so it's never tighter than the
    // predation contact tolerance. TUNABLE.
    private float MatingContactRadius => Mathf.Max(eatRadius, transform.localScale.x * 1.5f);

    [Header("Wander")]
    public float wanderTurnRate = 60f; // max degrees/sec random heading change

    [Header("Energy efficiency traits (Tier 1 lineage, evolvable via SI drift)")]
    [Tooltip("Photosynthetic conversion efficiency at spawn. Matched to chemoEfficiencySpawn: an organism's enzymatic efficiency is a property of the lineage, not the energy source, so a chemo→photo switch should not reset it 20× lower. The day/night duty cycle (photoAcq→0 at night) is what balances the two strategies, not a lower base efficiency.")]
    public float photoEfficiencySpawn = 0.50f;  // starting value; evolves up to PhotoEfficiencyCeiling
    [Tooltip("Chemosynthetic uptake efficiency. Tunable design constant ceiling.")]
    public float chemoEfficiencySpawn = 0.50f;
    [Tooltip("Heterotrophic assimilation efficiency base [0.2, 0.9]. Carnivore-biased mid-range default.")]
    public float assimilationEfficiencySpawn = 0.55f;

    // Efficiency property ceilings — SI drift cannot exceed these.
    public const float PhotoEfficiencyCeiling  = 0.80f;  // matched to ChemoEfficiencyCeiling so carried-over/inherited photo efficiency isn't clamped down to a non-viable band
    public const float ChemoEfficiencyCeiling  = 0.80f;  // tunable design constant
    public const float AssimEfficiencyMin      = 0.20f;
    public const float AssimEfficiencyMax      = 0.90f;

    /// Evolvable photosynthetic efficiency [0, PhotoEfficiencyCeiling]. Inherited by offspring; drifts on SI events.
    public float PhotoEfficiency        { get; set; }
    /// Evolvable chemosynthetic uptake efficiency [0, ChemoEfficiencyCeiling]. Inherited; drifts on SI events.
    public float ChemoEfficiency        { get; set; }
    /// Evolvable assimilation efficiency [AssimEfficiencyMin, AssimEfficiencyMax]. Inherited; drifts on SI events.
    public float AssimilationEfficiency { get; set; }

    [Header("Trait dimensions (0-100, Section 6a)")]
    public float visionTrait = 50f;
    public float speedTrait = 50f;
    public float strengthTrait = 50f; // currently inert (Section 7 strength-value formula, no predation/combat yet)
    public float hardinessTrait = 50f; // generalist (high) vs specialist (low) climate tolerance breadth
    public float temperaturePreference = 50f; // preferred local temperature
    public float moisturePreference = 50f;    // preferred local moisture

    // ── era3-primitives-spec §2: real behavioral axes, not proxied from unrelated systems ──────
    // Interference-competition disposition — contest/deny resources rather than avoid/share. NOT
    // predation (a feeding strategy); this is a real evolvable axis with its own selection pressure
    // (see ResolveActivityBudget's contest draw + the scarcity-conditioned uptake bonus below), so
    // Era 3's Aggression readout is a genuine consequence of Era 1/2 evolution, not an alias.
    public float contestPropensity = 30f;
    // Boldness–shyness axis (behavioral ecology's best-documented animal-personality trait). Governs
    // willingness to forage exposed, disperse into the unknown, and skip fleeing early — real
    // frequency/environment-dependent selection (bold pays when resources are rich/predation is low;
    // shy pays under heavy predation), so this should oscillate across a run rather than converge.
    public float boldness = 50f;

    [Header("Trait -> world-unit mapping")]
    public float minSenseRadius = 2f;
    public float maxSenseRadius = 16f;
    public float minMoveSpeed = 0.5f;
    public float maxMoveSpeed = 5f;

    [Header("Reproduction")]
    public int eatsToReproduce = 1;
    public float mutationStdDev = 5f; // offspring trait drift from parent, per Section 6a drift framing
    public float offspringSpawnOffset = 1.5f;

    [Header("Lifespan")]
    [Tooltip("Mean maximum age in seconds before senescence death. Actual lifespan is randomized ±40% per individual so generations overlap rather than dying in lockstep.")]
    public float meanLifespan = 45f;
    [Tooltip("Hardiness above 50 extends max lifespan (up to +25%); below 50 shortens it (down to -25%). Models the generalist/specialist tradeoff: hardy generalists are longer-lived.")]
    public float lifespanHardinessInfluence = 0.25f;

    [Header("Starvation / Solar energy")]
    public float starvationTime = 15f; // seconds without eating before death (consumers)
    [Tooltip("Max solar energy a producer can accumulate (seconds of survival).")]
    public float maxSolarEnergy = 3f;
    [Tooltip("Rate at which producers gain solar energy on the day side (energy/sec at full solar exposure).")]
    public float solarChargeRate = 2f;
    [Tooltip("Rate at which producers drain energy at night (energy/sec).")]
    public float solarDrainRate = 0.5f;
    [Tooltip("Beer-Lambert light extinction per unit liquid depth. Higher = light dies off faster underwater, pushing phototrophs toward the shallows. TUNABLE.")]
    public float PhoticExtinctionCoeff = 0.10f;
    [Tooltip("Fraction of a non-motile organism's drift step that survives when it would cross into a fatal medium (0 = hard anchor, 1 = no boundary). Keeps sessile life near its niche. TUNABLE.")]
    public float HabitatReturnBias = 0.05f;
    [Tooltip("Global multiplier on MOTILE organism movement speed. Lower = slower traversal of the world. TUNABLE.")]
    public float globalMoveSpeedScale = 0.45f;
    // Fraction of a motile step allowed when it would carry the organism across the shoreline into
    // its non-viable medium. Strong block (most species stay in their medium), not absolute (rare
    // amphibious crossings still possible). TUNABLE.
    private const float MediumCrossBias = 0.10f;
    [Tooltip("How strongly visual size reflects the organism's strength trait (species size class). 0 = all one size, 1 = strong span. TUNABLE.")]
    public float sizeTraitInfluence = 0.5f;

    [Header("Climate fitness pressure")]
    [Tooltip("Baseline strength of climate-mismatch's effect on starvation rate, before hardiness scaling.")]
    public float climateFitnessMultiplierRange = 0.8f;
    [Tooltip("How much hardiness can widen (specialist) or narrow (generalist) the baseline range above.")]
    public float hardinessRangeMin = 0.3f; // multiplier at hardiness=100 (generalist - shallow penalty)
    public float hardinessRangeMax = 1.6f; // multiplier at hardiness=0 (specialist - steep penalty)
    [Tooltip("Half-width of the zero-discomfort tolerance PLATEAU (on the 0-100 climate scale). Within " +
             "±band of the preferred value the organism is fully comfortable; only beyond it does mismatch " +
             "bite. Scales with hardiness: specialists (stenotherms) get Narrow, generalists (eurytherms) Wide.")]
    public float toleranceBandNarrow = 10f; // hardiness = 0  (stenotherm)
    public float toleranceBandWide   = 35f; // hardiness = 100 (eurytherm)

    [Header("Comfort-seeking (territoriality)")]
    [Tooltip("How strongly discomfort biases wander direction toward better-matching climate.")]
    public float comfortSeekingStrength = 2.5f;
    public float comfortSampleDistance = 2f; // how far ahead to sample candidate climate

    [Header("UV tolerance")]
    [Tooltip("0-100 randomized per agent: 0 = deep-water origin (max UV harm), 100 = surface-adapted.")]
    public Vector2 uvToleranceSpawnRange = new Vector2(5f, 50f);

    [Header("Pressure tolerance")]
    [Tooltip("0-100: how well the organism handles deviation from its natal atmospheric pressure.")]
    public Vector2 pressureToleranceSpawnRange = new Vector2(20f, 80f);

    [Header("Thermal cycle tolerance")]
    [Tooltip("0-100: how well the organism handles day/night temperature swings.")]
    public Vector2 thermalCycleToleranceSpawnRange = new Vector2(20f, 80f);

    [Header("Atmospheric fitness pressure")]
    [Tooltip("Baseline strength of atmosphere-mismatch's effect on survival, before tolerance scaling.")]
    public float atmosFitnessMultiplierRange = 1.2f;
    public float toleranceRangeMin = 0.3f; // multiplier at gasTolerance=100 (generalist - shallow penalty)
    public float toleranceRangeMax = 2.2f; // multiplier at gasTolerance=0 (specialist - steep penalty)
    [Tooltip("0-100 randomized per agent at spawn; how tolerant this lineage is of deviation from its locked-in ideal gas mix.")]
    public Vector2 gasToleranceSpawnRange = new Vector2(20f, 80f);

    [Header("Stress accumulation (sustained-adversity state, 0-100)")]
    [Tooltip("0-100 randomized per agent at spawn; how much accumulated stressLevel it takes to cause a given amount of extra harm. High = resilient (shallow penalty), low = fragile (steep penalty) - same generalist/specialist role hardinessTrait and gasTolerance play for their own discomfort calcs.")]
    public Vector2 stressToleranceSpawnRange = new Vector2(20f, 80f);
    [Tooltip("Seconds of sustained max adversity (climate + atmosphere + near-starvation all maxed) for stressLevel to climb to ~95% of its ceiling. Picked so stress reflects a PROLONGED hardship, not a single bad tick - roughly a third of the default starvationTime, so a starving agent's stress is meaningfully elevated by the time it actually starves.")]
    public float stressRiseTime = 5f;
    [Tooltip("Seconds of zero adversity for stressLevel to decay back down to ~5% of its prior value. Slower than the rise (recovery lags onset), so a population under intermittent stress trends upward over time rather than fully resetting between bad patches.")]
    public float stressDecayTime = 10f;
    [Tooltip("Baseline strength of stressLevel's effect on starvation rate, before stressTolerance scaling. Kept modest relative to climateFitnessMultiplierRange/atmosFitnessMultiplierRange since stress is a derived/secondary pressure layered on top of those, not a replacement for them.")]
    public float stressFitnessMultiplierRange = 0.5f;
    public float stressToleranceRangeMin = 0.3f; // multiplier at stressTolerance=100 (resilient - shallow penalty)
    public float stressToleranceRangeMax = 1.8f; // multiplier at stressTolerance=0 (fragile - steep penalty)

    [Header("Atmospheric speciation")]
    [Tooltip("Atmospheric discomfort (0-1) above which speciation becomes possible. Raised from 0.25 - the old value let the whole population cross threshold together as the atmosphere drifted, producing a single burst of many simultaneous color changes instead of a gradual trickle.")]
    public float speciationStressThreshold = 0.45f;
    [Tooltip("Chance per second (scaled by excess stress) that a stressed individual re-locks its ideal mix to the current atmosphere, founding a new lineage. Lowered from 0.15 for the same reason.")]
    public float speciationChanceScale = 0.025f;

    [Header("Gene events (Section 6b)")]
    [Tooltip("Random per-agent eat-count thresholds so genes don't all fire in the same order or at the same time.")]
    public Vector2Int sensoryGeneEatThresholdRange = new Vector2Int(4, 9);
    public Vector2Int locomotorGeneEatThresholdRange = new Vector2Int(4, 9);
    // Lowered from (20,60): the Kingdom Fork (photosynthesis vs heterotrophy) is a
    // primordial metabolic choice that should happen very early in the Abiogenesis era,
    // well before the default 15 s starvation clock runs out on the first consumer.
    public Vector2 kingdomForkAgeThresholdRange = new Vector2(5f, 12f); // seconds
    // Stand-in "sustained adversity" gate for ReproductiveStrategyShift until a real
    // per-agent stress accumulator exists - see GeneCatalog.cs TODO.
    public Vector2 reproductiveShiftAgeThresholdRange = new Vector2(30f, 70f); // seconds
    public Vector2Int reproductiveShiftEatThresholdRange = new Vector2Int(6, 12);

    /// Genes this agent's lineage has already acquired (inherited by offspring).
    public readonly HashSet<string> AcquiredGenes = new HashSet<string>();

    public int LifetimeEats => _lifetimeEats;
    public float AgeSeconds { get; private set; }

    // Randomized per-agent trigger thresholds, set at spawn - see GeneCatalog.
    public int sensoryGeneEatThreshold;
    public int locomotorGeneEatThreshold;
    public float kingdomForkAgeThreshold;
    public float reproductiveShiftAgeThreshold;
    public int reproductiveShiftEatThreshold;

    // Current metabolic strategy. Defaults to Chemosynthetic (absorbs dissolved organics).
    // Evolves to Phototrophic via PhotosynthesisEmergence gene, or Heterotrophic via KingdomFork.
    public MetabolismType Metabolism { get; private set; } = MetabolismType.Chemosynthetic;

    // Computed from Metabolism for backwards compatibility with AtmosphereManager etc.
    // Mixotrophic is both producer and consumer (handled separately in update).
    public bool IsProducer => Metabolism == MetabolismType.Chemosynthetic
                           || Metabolism == MetabolismType.Phototrophic
                           || Metabolism == MetabolismType.Mixotrophic;

    // Set by the Kingdom Fork gene (Photosynthesis vs Heterotroph) - see GeneCatalog.
    public string Kingdom { get; private set; }

    // Set by the MotilityEmergence gene (e1_motility_emergence in spec).
    // False = passive drifter (wind-carried, no seeking); true = self-directed movement.
    // Consumer behavior and predation hard-require this to be true.
    public bool HasMotility { get; private set; }
    /// Current locomotion medium — updated by gene events.
    public LocomotionMedium LocomotionMedium { get; private set; } = LocomotionMedium.Sessile;
    /// Hard mass ceiling for flight eligibility (addendum §1.3). Evaluated live each frame.
    /// Organisms that grow past this ceiling lose flight eligibility dynamically.
    public const float FlightMassCeiling = 0.008f; // tune during playtesting

    // Set by the Reproductive Strategy Shift gene - see GeneCatalog. Asexual (false, the
    // default) clones a single parent with mutation drift; Sexual (true) requires finding
    // an opposite-sex mate in range and blends both parents' traits before drift is applied.
    public bool IsSexual { get; private set; }

    // Sexual DIFFERENTIATION: the lineage has split into separate sexes (male/female), a prerequisite
    // for — and earlier than — sexual REPRODUCTION (IsSexual). A differentiated organism HAS a sex but
    // may still reproduce asexually until it also adopts sexual reproduction. Invariant: IsSexual ⇒
    // IsDifferentiated.
    public bool IsDifferentiated { get; private set; }

    // Biological sex: Asexual until DifferentiateSex() assigns Male or Female (50/50). Re-rolled per
    // offspring (among differentiated lineages) so the sex ratio drifts naturally and can create the
    // imbalance that triggers SequentialHermaphroditism.
    public BiologicalSex Sex { get; private set; } = BiologicalSex.Asexual;

    // Set by the SequentialHermaphroditism gene. When true, organism autonomously switches
    // sex in response to local population imbalance, improving mating availability.
    public bool CanChangeSex { get; private set; }
    private float _sexSwitchTimer;

    // Which spawned community this agent belongs to (0 = player community).
    public int communityId;

    // Monotonic identity — set by AgentSpawner immediately after Init(); never reused.
    // Use this as the dictionary key and log identifier, NOT the GameObject name.
    public long AgentId { get; set; } = -1;

    // Atmospheric adaptation (speciation): the gas-fraction mix this lineage is adapted
    // to, locked in at genesis and re-locked whenever AttemptAtmosphericSpeciation fires.
    // Inherited by offspring (see InheritGenesFrom) - NOT resampled every birth.
    private Dictionary<string, float> _idealGasMix = new Dictionary<string, float>();
    public float gasTolerance = 50f;
    public string AtmoLineage { get; private set; } = "Primordial";

    // Sustained-adversity accumulator (STATE, not a trait): builds up gradually under
    // combined climate/atmosphere/starvation pressure and decays gradually when conditions
    // improve - see UpdateStressLevel. Intended gate for a future "Reproductive Strategy
    // Shift" gene event (see GeneCatalog.cs TODO) once sustained hardship should be able to
    // push a lineage toward sexual reproduction, not just age + lifetime eats.
    public float StressLevel { get; private set; }
    private bool _stressRegistered;

    /// 0-1 how favorable this agent's CURRENT location is against its own tolerance profile —
    /// reuses the existing StressLevel adversity signal (already blends climate/atmospheric/UV/
    /// pressure/thermal-cycle/starvation discomfort) rather than a second scoring system. Drives
    /// TerritorialityManager's settle/roam decision: high favorability trends a community toward
    /// LooseRange/StrictSite, low favorability keeps it Nomadic.
    public float LocalFavorability => 1f - StressLevel / 100f;

    // 0-100 randomized per agent at spawn; how tolerant this individual is of its OWN
    // accumulated stressLevel (resilient generalist vs fragile specialist) - same
    // generalist/specialist role hardinessTrait and gasTolerance play for their discomfort calcs.
    public float stressTolerance = 50f;

    // World-unit values derived from the trait dimensions above.
    public float senseRadius { get; private set; }
    public float moveSpeed { get; private set; }

    // Founder/lineage color (Tier 2: one of 8-10 distinct founder hues; inherited by
    // offspring unchanged until Tier 3 visual speciation is implemented).
    public Color lineageColor = Color.white;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// Which physical medium this organism currently occupies. Updated every frame from
    /// FluidDynamicsManager liquid depth. Drives drift source (current vs wind) and
    /// medium-mismatch fitness penalty.
    public HabitatMedium CurrentMedium { get; private set; } = HabitatMedium.Sea;

    // Cached liquid depth at this agent's position, refreshed once per Update (single vertex
    // scan reused by medium classification, photic-zone attenuation, and drift logic).
    private float _currentLiquidDepth;

    // True if this lineage is aquatic (adapted to live in liquid). Set at spawn for early
    // eras; transitions to false when a TerrestrialAdaptation gene is acquired (future).
    private bool _isAquatic = true;
    /// True if this lineage's locked habitat is aquatic (all life starts here); false once it has
    /// colonized land (LandColonization gene). Public so gene eligibility (e.g. Fire Mastery, which
    /// requires actually being ON land) and the HUD can read it.
    public bool IsAquatic => _isAquatic;

    private Vector3 _heading; // tangent direction, world space
    private CorpseSpawner _corpseSpawner;
    private AgentSpawner _spawner;
    private int _eatsSinceReproduction;
    private float _timeSinceLastMeal;
    private int _lifetimeEats;
    private float _reproCooldownTimer;
    private bool _traitsRegistered;
    private float _solarEnergy; // phototrophic producers use this
    private float _chemEnergy;  // chemosynthetic organisms use this
    // Most recent chemosynthetic gross absorption rate (energy/s), cached each tick a chemosynthetic
    // organism runs its metabolism. Read by IsPhotosynthesisLocallyViable so a lineage only abandons
    // chemosynthesis for photosynthesis when photo actually OUT-YIELDS the chemo it's giving up — not
    // merely when photo covers demand. Without this, organisms on chemo-rich worlds (rich vents, high
    // SO2, etc.) switch to a strictly-worse photo income and slowly starve. 0 until first chemo tick.
    private float _lastChemoAbsorb;

    /// 0–1 fraction of max energy reserve; used by GameLog periodic snapshots.
    public float EnergyFraction => _chemEnergy / Mathf.Max(maxSolarEnergy, 0.001f);

    /// Last-computed net energy (u/s): income minus demand. Positive = thriving, negative = starving.
    public float NetEnergy { get; private set; }

    public float MaxLifespanSeconds => _maxLifespan;
    public int MorphSeedValue => _morphSeed;
    public float ChemEnergyRaw => _chemEnergy;
    public float SolarEnergyRaw => _solarEnergy;

    // Shared scratch buffer for AgentSpawner.QueryNearby results. Static/shared (not per-instance)
    // because Unity is single-threaded and no proximity-scan method here calls another one
    // mid-loop — each call fills the buffer and fully consumes it before returning, so reuse across
    // every agent's Update() this frame is safe and avoids a per-call List allocation.
    private static readonly List<AgentController> _queryBuffer = new List<AgentController>();

    // ── Sense-scan throttling (perf) ──────────────────────────────────────────────────────────
    // The prey/mate/threat proximity scans are the dominant per-agent cost at high population (one,
    // FindNearestMate, even scanned the whole population = O(n²)). None of them need 60 Hz — a target
    // acquired ~7 times/second is indistinguishable in movement. Each scan is memoized per SENSE CYCLE:
    // it recomputes at most once per cycle and returns the cached target (validated) otherwise.
    // Movement steers toward the cached target every frame; contact resolution (eat/mate) still runs
    // against LIVE positions each frame, so nothing conceives or feeds off a stale snapshot.
    private const float SenseInterval = 0.15f;   // ~6.7 Hz target reacquisition
    private float _senseTimer;                    // phased at Init so agents don't all scan together
    private int _senseCycle;                       // advances each SenseInterval
    private int _preyCycle = -1, _mateCycle = -1, _threatCycle = -1;
    private AgentController _cachedPrey, _cachedMate, _cachedThreat;
    private float _geneCheckElapsed;               // real time accumulated between throttled gene scans

    private static AgentController ValidCached(AgentController c) => (c != null && c.gameObject != null) ? c : null;

    // Simulated body mass (game-unit kg). Drives Kleiber BMR demand and biomass transfer
    // on predation. Initialized from era scale; grows/shrinks with sustained net balance.
    // Viability floor = SpawnMass * MassViabilityFloor — death below this threshold.
    private float _currentMass;
    private float _spawnMass;
    private const float MassViabilityFloor = 0.10f; // 10% of spawn mass = death threshold
    private const float MassGrowthRate     = 0.02f; // fraction of spawn mass gained per second of positive surplus
    private const float MassShrinkRate     = 0.03f; // fraction of spawn mass lost per second of negative surplus

    /// Simulated body mass in game units. Read by Kleiber demand, biomass-transfer (§7), and surface-area terms.
    public float CurrentMass => _currentMass;

    // Environmental pressure variables — refreshed every 3 s to avoid per-frame O(N) cost.
    private float _pressureRefreshTimer;
    private bool _metabolismLogged; // fires one diagnostic log per agent on first chemo tick
    private bool _photoMetabolismLogged; // fires one PhotoMeta log per agent on first phototrophic tick
    private bool _updateLogged;     // fires one diagnostic log per agent on first Update tick
    private float _reproTimer;      // fallback reproduction timer — fires TryReproduce every 20s
    private bool _reproFallbackLogged; // one diagnostic log the first time the fallback path fires
    private bool _dispersalLogged;  // one diagnostic log the first time this agent begins a dispersal journey

    // ── Density-dependent dispersal (dispersal-colonization spec) ─────────────────────────
    // When a lineage saturates its local patch, well-fed motile members commit to a directed
    // long-range journey AWAY from the local cluster toward open space, at an energy cost — so a
    // resource-secure population seeds new, distant population centers instead of staying pinned to
    // its spawn cluster forever. Non-motile life spreads via a passive prevailing drift instead.
    private int _localSameCommunity;      // same-community neighbors within scan radius (set in UpdatePressureVariables)
    private float _dispersalTimer;        // >0 while actively on a dispersal journey
    private float _dispersalCheckTimer;   // throttles entry checks
    private Vector3 _dispersalDir;        // world-space unit direction of the current journey
    public bool IsDispersing => _dispersalTimer > 0f;
    private const int   DispersalCrowdCap         = 8;     // same-community neighbors within scan radius that = locally saturated
    private const float DispersalPressureThresh   = 0.75f; // fraction of local cap that triggers dispersal pressure (TUNABLE)
    private const float DispersalChancePerCheck   = 0.25f; // probability per check once pressure exceeds threshold
    private const float DispersalCheckInterval    = 4f;    // seconds between entry checks
    private const float DispersalDuration         = 25f;   // seconds a committed journey lasts
    private const float DispersalEnergyCostPerSec = 0.02f; // journey metabolic cost — real risk, not a free teleport
    private const float PassiveDispersalBias      = 0.12f; // small prevailing-current drift weight for non-motile life (Issue 1b)

    // ── Predation economics (size/strength/hardiness → cost & yield) ──────────────────────
    // A successful kill's net energy shrinks as prey grows larger/tougher RELATIVE to the predator:
    // small weak prey are cheap efficient meals, near-own-size hardy prey are barely worth the
    // effort. Bounded to [0, gross] so a kill never costs more than it yields — the real *risk* of
    // big prey is the counter-kill in ResolvePredatorAttack, not negative energy here. TUNABLE:
    // these need calibration against real ChemoMeta/PhotoMeta net values from a run with confirmed
    // predation activity (see the [Corpse] SCAVENGE / predation logs) before final tuning.
    private const float PredationCostFraction  = 0.5f;  // yield share lost subduing an equal-size, equal-tough prey
    private const float PredationCostExponent  = 1.2f;  // how steeply cost rises with prey/predator mass ratio
    // Fallback reproduction cadence. The primary reproduction path only fires after the energy
    // reserve completes THREE 90%-fill cycles (eatsToReproduce). On slow-metabolism exotic worlds
    // (cold, low stellar flux, sparse substrate) an agent can have healthy positive net energy yet
    // never complete three fills within its lifespan — producing zero births despite good energy.
    // This timer guarantees a mature, well-fed agent still attempts reproduction on a fixed cadence.
    private const float ReproFallbackInterval   = 20f;   // seconds between fallback attempts
    private const float ReproFallbackEnergyFrac = 0.6f;  // reserve fraction required to attempt
    private const float ReproFallbackMinAgeFrac = 0.15f; // must be at least this far into its lifespan
    private const float PressureRefreshInterval = 3f;
    /// 0 = abundant chemical energy; 1 = substrate fully depleted (drives metabolic innovation).
    public float ResourceScarcity  { get; private set; }
    /// Inverse of local same-community population density — high value = few nearby mates (mate-search difficulty).
    /// Primary trigger for SequentialHermaphroditism (low-density mate-limitation signal).
    public float MateScarcity      { get; private set; }
    /// 0 = no predators nearby; 1 = overwhelmingly consumer-dominated (drives armor/escape).
    public float PredationPressure { get; private set; }
    /// 0 = sparse population; 1 = dense (density is the real-world proxy for pathogen load).
    public float PathogenPressure  { get; private set; }

    // Flee state: tracks the current threat and how long to keep fleeing after losing sight.
    private AgentController _fleeTarget;
    private float _fleeCooldown;

    // Sexual isolation: counts seconds without any opposite-sex community member present.
    // When this exceeds the threshold, the lineage is ecologically extinct and the organism dies.
    private float _noMateTimer;
    private float _noMateCheckTimer; // throttle the O(N) scan to every 5 s

    // New tolerance traits — inherited, not re-rolled at birth.
    public float uvTolerance;            // 0-100: vulnerability to UV radiation
    public float pressureTolerance;      // 0-100: pressure generalist vs specialist
    public float thermalCycleTolerance;  // 0-100: day/night thermal swing tolerance
    public float pressurePreference;     // bar: natal atmospheric pressure, locked at spawn

    // Randomized per agent at spawn; influenced by hardinessTrait so specialists die younger.
    private float _maxLifespan;

    /// Standard normal draw (mean 0, stdDev 1) via Box-Muller. Signed and symmetric about zero —
    /// deliberately NOT wrapped in Abs, so callers get both positive and negative values.
    private static float SampleStandardNormal()
    {
        float u1 = Mathf.Max(Random.value, 1e-6f);
        float u2 = Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
    }

    /// One-time lifespan-jitter distribution self-test. If a build is actually running THIS code,
    /// the log will show a ~50/50 positive/negative split with mean≈0. If this line is ABSENT from a
    /// run's log, the binary is stale (predates this code) — which is the real explanation whenever
    /// jitter appears one-sided despite the symmetric draw.
    /// Called explicitly from GameLog.Init() (not [RuntimeInitializeOnLoadMethod]) — that attribute
    /// fires before GameLog exists to capture Debug.Log output, so the line could never reach the
    /// session's gamelog_*.txt file; calling it after GameLog.Init() guarantees it's captured.
    public static void SelfTestLifespanJitter()
    {
        const int n = 1000;
        int pos = 0, neg = 0;
        float min = float.MaxValue, max = float.MinValue, sum = 0f;
        for (int i = 0; i < n; i++)
        {
            float j = Mathf.Clamp(SampleStandardNormal() * 0.28f, -0.50f, 0.50f);
            if (j > 0f) pos++; else if (j < 0f) neg++;
            min = Mathf.Min(min, j); max = Mathf.Max(max, j); sum += j;
        }
        Debug.Log($"[EvoSim] JITTER_SELFTEST n={n} positive={pos} negative={neg} " +
                  $"min={min:+0.000;-0.000} max={max:+0.000;-0.000} mean={sum / n:+0.000;-0.000} " +
                  $"(expect ~50/50 split, mean≈0; if positive==0 the draw is genuinely broken)");
    }
    // Normal-distributed lifespan multiplier offset rolled once at spawn (see Init); logged in
    // FIRST_UPDATE so the realized jitter distribution can be verified against the intended stdDev.
    private float _lifespanJitter;

    // Locked survival needs — set once at Init from world state, inherited by offspring.
    private string _breathedGasName      = "";  // Name of the Breathed gas at genesis
    private string _expelledGasName      = "";  // Name of the Expelled gas at genesis
    private float  _minBreathableFraction;       // fraction below which asphyxia begins
    private string _requiredLiquidKind   = "";  // liquid Name this lineage evolved in

    public string BreathedGasName    => _breathedGasName;
    public string ExpelledGasName    => _expelledGasName;
    public string RequiredLiquidKind => _requiredLiquidKind;

    // Backbone element: set from AtmosphereManager.RolledBiochemistry at genesis,
    // inherited by all offspring. Drives gas-effect compatibility and Era 2 neural pathway.
    public BackboneElement Backbone { get; private set; } = BackboneElement.Carbon;

    // Era 1 trait axes — set by gene events, inherited by offspring.
    public ManipulationLevel Manipulation { get; private set; } = ManipulationLevel.None;
    public SocialityBaseline Sociality    { get; private set; } = SocialityBaseline.Solitary;
    public NeuralComplexityStage NeuralComplexity { get; private set; } = NeuralComplexityStage.DiffuseSignaling;

    // Era 1 body-plan attributes — set by protective structure gene events, inherited.
    public BodyPlanType BodyPlan          { get; private set; } = BodyPlanType.None;
    public bool HasGermLayers             { get; private set; } = false; // triploblastic development
    public bool IsAnoxicRefugeLineage     { get; private set; } = false; // retreated to anoxic refuges at GOE

    // ── appearance-generation-spec §2.2 remaining morphological axes — set by gene events below,
    // inherited by offspring like every other Era 1 evolved attribute. ──────────────────────────
    public SegmentationType Segmentation  { get; private set; } = SegmentationType.Unsegmented; // M2
    public SensoryModality PrimarySense   { get; private set; } = SensoryModality.Chemosensory;  // M6
    private readonly HashSet<SensoryModality> _sensesAcquired = new HashSet<SensoryModality> { SensoryModality.Chemosensory };
    public FeedingApparatus Feeding       { get; private set; } = FeedingApparatus.FilterPassive; // M9
    public IntegumentType Integument      { get; private set; } = IntegumentType.BareMucous;       // M10
    // M5 sub-variables (§2.4/§2.8): undifferentiated until e1_limb_differentiation fires — before
    // that, any appendages present serve both roles at once, so neither pair count is meaningful yet.
    public int LocomotorPairs             { get; private set; } = 0;
    public int ManipulatorPairs           { get; private set; } = 0;
    public bool VocalApparatus            { get; private set; } = false; // §2.8 e1_vocal_structure_emergence
    // Non-bilaterian symmetry branches (M1) — mutually exclusive with each other and with the
    // ordinary motile/sessile-derived Bilateral/Radial read in ApplyMorphology.
    public bool IsColonialModular         { get; private set; } = false;
    public bool IsBiradial                { get; private set; } = false;

    /// Tiered read of physical size (M8) — derived from continuous scale, not separately tracked
    /// state (appearance-generation-spec §1's "state is derived, not authored"). Breakpoints TUNABLE.
    /// M3-driven ceiling: a body with no structural support (hydrostatic/soft) cannot sustain a Mega
    /// frame — same size-vs-skeleton constraint already governing the EndoMineralized event above.
    public SizeClass BodySizeClass
    {
        get
        {
            float scale = transform.localScale.x;
            SizeClass tier = scale < 0.03f ? SizeClass.Micro
                : scale < 0.08f ? SizeClass.Small
                : scale < 0.18f ? SizeClass.Medium
                : scale < 0.35f ? SizeClass.Large
                : SizeClass.Mega;
            if ((BodyPlan == BodyPlanType.None || BodyPlan == BodyPlanType.SoftBody) && tier > SizeClass.Large)
                tier = SizeClass.Large;
            return tier;
        }
    }

    public void Init(Vector3 center, float radius, CorpseSpawner corpseSpawner, AgentSpawner spawner,
        float visionTraitValue, float speedTraitValue, float strengthTraitValue, float hardinessTraitValue,
        float temperaturePreferenceValue, float moisturePreferenceValue, int community = 0, Color? color = null)
    {
        planetCenter = center;
        planetRadius = radius;
        _corpseSpawner = corpseSpawner;
        _spawner = spawner;
        Debug.Log($"[EvoSim] INIT agentId={AgentId} community={community} enabled={enabled} name={name}");
        if (!enabled) { Debug.LogWarning("[EvoSim] AgentController was DISABLED — forcing enabled=true. Check prefab!"); enabled = true; }
        // Grace period: start starve-clock negative so new agents (especially the very
        // first organism, which has no food yet) have time for KingdomFork to fire.
        _timeSinceLastMeal = -starvationTime;
        maxSolarEnergy = 3f; // bypass any stale prefab-serialized value
        _solarEnergy = maxSolarEnergy * 0.5f;
        _chemEnergy  = maxSolarEnergy * 0.5f; // all organisms start with half chemical reserve
        _pressureRefreshTimer = Random.Range(0f, PressureRefreshInterval); // stagger refreshes
        _senseTimer = Random.Range(0f, SenseInterval); // stagger prey/mate/threat scans across frames
        AgeSeconds = 0f;
        communityId = community;

        lineageColor = color ?? Color.white;
        ApplyLineageColor();
        // Default morph seed from the founding community; offspring overwrite this with the parent's
        // seed in InheritGenesFrom, and speciation drifts it — so shape tracks lineage, not raw id.
        _morphSeed = communityId + 1;
        ApplyMorphology();

        // Lock in the current atmosphere as this lineage's "ideal mix" (genesis adaptation).
        // Offspring overwrite this via InheritGenesFrom rather than resampling at birth.
        _idealGasMix = AtmosphereManager.Instance != null ? AtmosphereManager.Instance.SnapshotMix() : new Dictionary<string, float>();
        gasTolerance = Random.Range(gasToleranceSpawnRange.x, gasToleranceSpawnRange.y);
        stressTolerance = Random.Range(stressToleranceSpawnRange.x, stressToleranceSpawnRange.y);

        StressLevel = 0f;
        PopulationStats.RegisterStress(StressLevel);
        PopulationStats.RegisterStressTolerance(stressTolerance);
        _stressRegistered = true;

        // Default: all primordial organisms are chemosynthetic — absorbing dissolved
        // organic chemicals from the ocean/vent pool. Photosynthesis evolves later
        // via the PhotosynthesisEmergence gene; heterotrophy via KingdomFork.
        Metabolism = MetabolismType.Chemosynthetic;

        // UV: early life evolved in deep water to escape surface radiation — spawn
        // with low-to-moderate UV tolerance. Pressure locked to natal atmosphere.
        uvTolerance           = Random.Range(uvToleranceSpawnRange.x, uvToleranceSpawnRange.y);
        pressureTolerance     = Random.Range(pressureToleranceSpawnRange.x, pressureToleranceSpawnRange.y);
        thermalCycleTolerance = Random.Range(thermalCycleToleranceSpawnRange.x, thermalCycleToleranceSpawnRange.y);
        pressurePreference    = AtmosphereManager.Instance != null ? AtmosphereManager.Instance.PressureBar : 1f;

        // Randomized per-agent thresholds so genes don't all fire in the same order or
        // at the same time across the population (Section 14e).
        sensoryGeneEatThreshold = Random.Range(sensoryGeneEatThresholdRange.x, sensoryGeneEatThresholdRange.y + 1);
        locomotorGeneEatThreshold = Random.Range(locomotorGeneEatThresholdRange.x, locomotorGeneEatThresholdRange.y + 1);
        kingdomForkAgeThreshold = Random.Range(kingdomForkAgeThresholdRange.x, kingdomForkAgeThresholdRange.y);
        reproductiveShiftAgeThreshold = Random.Range(reproductiveShiftAgeThresholdRange.x, reproductiveShiftAgeThresholdRange.y);
        reproductiveShiftEatThreshold = Random.Range(reproductiveShiftEatThresholdRange.x, reproductiveShiftEatThresholdRange.y + 1);

        SetTraits(visionTraitValue, speedTraitValue, strengthTraitValue, hardinessTraitValue, temperaturePreferenceValue, moisturePreferenceValue);

        // Lifespan: randomized ±40% around the mean, then scaled by hardiness so generalists
        // (high hardiness) live longer and specialists (low hardiness) burn bright but brief.
        float hardinessLifeBonus = Mathf.Lerp(-lifespanHardinessInfluence, lifespanHardinessInfluence, hardinessTrait / 100f);
        // Quarter-power lifespan scaling (West, Brown & Enquist metabolic theory): larger organisms
        // live proportionally longer, anchored to spawn scale so Era 0 lifespan = meanLifespan.
        float lifespanSizeScale = Mathf.Pow(Mathf.Max(transform.localScale.x, 0.001f) / 0.05f, 0.25f);
        // Symmetric normal-distribution jitter (stdDev=0.28, clamp ±0.50) breaks cohort
        // synchronization: agents born in the same burst die across a wide, non-uniform spread
        // rather than a tight wave. SampleStandardNormal() is a plain signed Gaussian (no Abs
        // wrapper) and the clamp is symmetric, so the distribution is centred on zero — roughly
        // half the population is longer-lived, half shorter-lived, not a one-sided shortening.
        float jitter = Mathf.Clamp(SampleStandardNormal() * 0.28f, -0.50f, 0.50f);
        _lifespanJitter = jitter; // stored for FIRST_UPDATE diagnostic so the rolled distribution is verifiable
        GameLog.RecordLifespanJitter(jitter); // Priority 6: running distribution summary
        _maxLifespan = meanLifespan * lifespanSizeScale * (1f + hardinessLifeBonus) * (1f + jitter);

        // Lock survival needs from current world state so offspring inherit them and can
        // accumulate asphyxiation pressure if the atmosphere drifts away from the genesis mix.
        _breathedGasName = "";
        _expelledGasName = "";
        if (AtmosphereManager.Instance != null)
            foreach (var g in AtmosphereManager.Instance.Gases)
            {
                if (g.Role == GasRole.Breathed && string.IsNullOrEmpty(_breathedGasName)) _breathedGasName = g.Name;
                if (g.Role == GasRole.Expelled && string.IsNullOrEmpty(_expelledGasName)) _expelledGasName = g.Name;
            }
        _minBreathableFraction = Mathf.Lerp(0.12f, 0.03f, hardinessTrait / 100f);
        _requiredLiquidKind = FluidDynamicsManager.Instance?.CurrentLiquid?.Name ?? "";

        // Backbone: derive from the world's rolled biochemistry (set once at genesis).
        Backbone = AtmosphereManager.Instance?.RolledBiochemistry?.Backbone ?? BackboneElement.Carbon;

        Vector3 normal = (transform.position - planetCenter).normalized;
        _heading = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        AlignToSurface();

        // Apply current era's visual scale so new agents (including offspring) always
        // spawn at the right size, not at the prefab's default size.
        if (EraManager.Instance != null)
        {
            transform.localScale = Vector3.one * EraManager.Instance.AgentTargetScale;

            // In early eras (Abiogenesis / Prokaryotic) bias toward wet areas — life
            // begins in liquid, not on dry land. moisturePreference=90 drives comfort-
            // seeking toward the wettest available terrain.
            if (EraManager.Instance.CurrentEra <= 1)
                moisturePreference = 90f;
        }

        // Seed current_mass from visual scale — era scale (0.05–0.32) maps to game-unit mass.
        // Cubed because scale is a linear dimension and mass scales with volume.
        float s = transform.localScale.x;
        _spawnMass   = s * s * s;
        _currentMass = _spawnMass;

        // Seed efficiency traits at spawn values. InheritGenesFrom overwrites these for
        // offspring, so founders get the inspector defaults; children inherit parent values.
        PhotoEfficiency        = photoEfficiencySpawn;
        ChemoEfficiency        = chemoEfficiencySpawn;
        AssimilationEfficiency = assimilationEfficiencySpawn;
        // Force tuned gameplay values regardless of any prefab-serialized overrides.
        ChemoEfficiency = 0.50f;
        // Reproduce after ONE successful energy-fill cycle, not three. Requiring three fills before
        // the first offspring meant an organism spent ~36s of its ~46s life just to reproduce once —
        // expected-offspring-per-lifetime fell below replacement, so every population declined to
        // extinction regardless of world. The density-dependent reproduction COOLDOWN (5s at low
        // population up to 30s at high) is what actually rate-limits breeding; this was double-gating
        // it into non-viability. TUNABLE.
        eatsToReproduce = 1;
    }

    /// Founder-only: seed this organism at a random point in its lifespan instead of age 0, so a
    /// founding population is age-MIXED (like any real population) rather than a cohort of newborns
    /// that all reach old age in the same ~30-second window and die in one synchronized wave — the
    /// structural cause of the founder boom-then-crash that killed the population every run. Spreads
    /// founder deaths into a smooth background rate that offspring can replace. Offspring (born via
    /// Reproduce) correctly start at age 0; only the initial founders call this.
    public void StaggerFounderAge()
    {
        AgeSeconds = Random.Range(0f, _maxLifespan * 0.7f);
    }

    /// Sets this agent's trait dimensions and registers them with the live population stats.
    /// Safe to call again later (e.g. a gene event changing traits post-spawn) - the
    /// previously registered values are removed first so the population mean stays accurate.
    public void SetTraits(float visionTraitValue, float speedTraitValue, float strengthTraitValue, float hardinessTraitValue,
        float temperaturePreferenceValue, float moisturePreferenceValue)
    {
        if (_traitsRegistered)
        {
            PopulationStats.UnregisterVision(visionTrait);
            PopulationStats.UnregisterSpeed(speedTrait);
            PopulationStats.UnregisterStrength(strengthTrait);
            PopulationStats.UnregisterHardiness(hardinessTrait);
        }

        visionTrait = Mathf.Clamp(visionTraitValue, 0f, 100f);
        speedTrait = Mathf.Clamp(speedTraitValue, 0f, 100f);
        strengthTrait = Mathf.Clamp(strengthTraitValue, 0f, 100f);
        hardinessTrait = Mathf.Clamp(hardinessTraitValue, 0f, 100f);
        temperaturePreference = Mathf.Clamp(temperaturePreferenceValue, 0f, 100f);
        moisturePreference = Mathf.Clamp(moisturePreferenceValue, 0f, 100f);

        senseRadius = Mathf.Lerp(minSenseRadius, maxSenseRadius, visionTrait / 100f);
        moveSpeed = Mathf.Lerp(minMoveSpeed, maxMoveSpeed, speedTrait / 100f);

        PopulationStats.RegisterVision(visionTrait);
        PopulationStats.RegisterSpeed(speedTrait);
        PopulationStats.RegisterStrength(strengthTrait);
        PopulationStats.RegisterHardiness(hardinessTrait);
        _traitsRegistered = true;
    }

    private void ApplyLineageColor()
    {
        Renderer r = GetComponentInChildren<Renderer>();
        if (r == null) return;
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, lineageColor);
        block.SetColor(ColorId, lineageColor);
        r.SetPropertyBlock(block);
    }

    private MeshFilter _meshFilter;
    // Per-lineage morphology seed: initialized from the founding community, inherited unchanged by
    // offspring (so descendants resemble their parents), and drifted on speciation (so a branch
    // event produces a visibly divergent body). This is the spec's "seed evolves over generations."
    private int _morphSeed;

    /// Replaces the static capsule mesh with a procedurally-generated body shaped by this organism's
    /// state (symmetry, motility, appendages, structural body plan) and its lineage identity, so
    /// species look distinct and evolve their silhouette as they gain motility/appendages/structure.
    /// (appearance-generation-spec §3.) Called at spawn and after each major morphological transition.
    /// Cheap: the generator caches one mesh per (lineage × morphology) signature and shares it.
    public void ApplyMorphology()
    {
        if (_meshFilter == null) _meshFilter = GetComponentInChildren<MeshFilter>();
        if (_meshFilter == null) return;

        // Defensive: a fault in procedural mesh generation must NEVER prevent an organism from
        // spawning/living — it is a purely cosmetic layer. On error, keep the existing (capsule)
        // mesh and log once, rather than letting the exception propagate up through Init/SpawnAgent
        // and abort the whole spawn loop (which would empty the world).
        try
        {
            _meshFilter.sharedMesh = MorphologyGenerator.GetMesh(
                lineageSeed: _morphSeed,
                symmetry: GetEffectiveSymmetry(),
                motile: HasMotility,
                appendageLevel: (int)Manipulation,
                structureType: (int)BodyPlan,
                segmentation: (int)Segmentation,
                integument: (int)Integument,
                pairCount: LocomotorPairs + ManipulatorPairs,
                networkForeshadowBucket: ComputeNetworkForeshadowBucket());

            // appearance-generation-spec §2.4/§3.4: build (and, for the player's own lineage, log)
            // the appearance descriptor every time the underlying state actually changes — the
            // Historical Record UI (§3.4, Era 2 scope, not yet built) will read this same Build()
            // call once it exists. Player-only to avoid a per-NPC descriptor build on every
            // population-wide morphological tick.
            if (communityId == 0)
                Debug.Log($"[Appearance] {name} descriptor updated:\n{AppearanceDescriptor.Build(this).ToYamlString()}");
        }
        catch (System.Exception e)
        {
            if (!_morphErrorLogged) { _morphErrorLogged = true; Debug.LogWarning($"[Morphology] generation failed for {name}, keeping default mesh: {e.Message}"); }
        }
    }
    private static bool _morphErrorLogged;

    /// Resolves this organism's current M1 symmetry — shared by ApplyMorphology (feeds the mesh
    /// generator) and AppearanceDescriptor.Build (feeds the descriptor), so the two can never drift
    /// apart. ColonialModular/Biradial are rare OR-gate sibling branches (§2.2); otherwise symmetry
    /// derives from motility plus a seeded low-probability Asymmetric roll.
    public MorphologyGenerator.Symmetry GetEffectiveSymmetry()
    {
        if (IsColonialModular) return MorphologyGenerator.Symmetry.ColonialModular;
        if (IsBiradial) return MorphologyGenerator.Symmetry.Biradial;
        MorphologyGenerator.Symmetry sym = HasMotility ? MorphologyGenerator.Symmetry.Bilateral : MorphologyGenerator.Symmetry.Radial;
        if (((((uint)_morphSeed * 2654435761u) >> 3) & 7u) == 0u)
            sym = MorphologyGenerator.Symmetry.Asymmetric;
        return sym;
    }

    /// appearance-generation-spec §3.3: "legible foreshadowing" — how far a Distributed-architecture
    /// lineage has progressed toward its eventual network/colonial visual language, bucketed 0-10 for
    /// the mesh cache. Era2Manager.AssignFork1 resolves a provisional Architecture at Era 2's very
    /// start (sessile → Distributed immediately; motile lineages may still be reassigned by Fork 2),
    /// so this reads real, already-available Era 2 state rather than guessing ahead of it. Zero for
    /// every other architecture, before Era 2 begins, or once the lineage already has the hard
    /// ColonialModular symmetry (§2.2 M1) — this is deliberately a separate, gradual signal from that
    /// rare Era 1 event, not a replacement for it.
    private int ComputeNetworkForeshadowBucket()
    {
        if (IsColonialModular) return 0;
        if (Era2Manager.Instance == null || !Era2Manager.Instance.IsActive) return 0;
        var rec = Era2Manager.Instance.GetRecord(communityId);
        if (rec == null || rec.Architecture != CognitiveArchitecture.Distributed) return 0;
        return Mathf.RoundToInt(Mathf.Clamp01(rec.II / 15f) * 10f);
    }

    /// Natural death (starvation, energy depletion, atmosphere crisis): leaves a
    /// decaying corpse for scavengers before removing this agent. Direct predation
    /// kills (see UpdateConsumer) skip this - the prey is eaten immediately instead.
    /// Founder survival rescue: instead of dying, a last-ditch member of a founding lineage clings
    /// on — reset to a younger age and its energy reserve topped up to a viable level. "Not at full
    /// strength, but alive." Called by FounderSurvivalManager when this death would drop one of the 8
    /// founding communities below its survival floor before Era 3.
    public void RejuvenateForFounderRescue()
    {
        AgeSeconds             = Random.Range(0.05f, 0.35f) * _maxLifespan;
        float refill           = maxSolarEnergy * 0.5f;
        _chemEnergy            = Mathf.Max(_chemEnergy, refill);
        _solarEnergy           = Mathf.Max(_solarEnergy, refill);
        _timeSinceLastMeal     = 0f;
        _dormancyTimer         = 0f;
        _mediumMismatchExposure = 0f;
    }

    public void Die(DeathCause cause = DeathCause.Unknown)
    {
        // Founder survival guarantee: the 8 founding lineages (community 0–7) are protected from
        // EXTINCTION before Era 3 regardless of cause (OldAge waves, starvation, an acute swing). If
        // this death would push the lineage below its survival floor, the organism clings on instead
        // of dying. This is the hard backstop behind "founders must survive to Era 3."
        if (FounderSurvivalManager.TryRescueFounderFromDeath(this))
            return;

        int popGlobalBefore = _spawner?.ActiveAgents.Count ?? 0;
        int popGlobalAfter  = popGlobalBefore - 1; // this agent is still in ActiveAgents until OnDestroy
        GameLog.LogDeath(communityId, cause, popGlobalAfter);
        Debug.Log($"[EvoSim] DEATH agentId={AgentId} community={communityId} cause={cause} pop_global_before={popGlobalBefore} pop_global_after={popGlobalAfter}");
        if (_corpseSpawner != null) _corpseSpawner.SpawnCorpseAt(transform.position, _currentMass);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        PopulationStats.UnregisterVision(visionTrait);
        PopulationStats.UnregisterSpeed(speedTrait);
        PopulationStats.UnregisterStrength(strengthTrait);
        PopulationStats.UnregisterHardiness(hardinessTrait);
        if (_stressRegistered)
        {
            PopulationStats.UnregisterStress(StressLevel);
            PopulationStats.UnregisterStressTolerance(stressTolerance);
        }
        if (_spawner != null) _spawner.Unregister(this);
    }

    void Update()
    {
        AgeSeconds += Time.deltaTime;
        if (!_updateLogged) { _updateLogged = true; Debug.Log($"[EvoSim] FIRST_UPDATE agent={name} motility={HasMotility} metabolism={Metabolism} maxSolar={maxSolarEnergy} chemoEff={ChemoEfficiency} lifespanJitter={_lifespanJitter:+0.000;-0.000} maxLifespan={_maxLifespan:F1}s"); }
        if (AgeSeconds >= _maxLifespan) { Die(DeathCause.OldAge); return; }

        // Update habitat medium before movement so drift source and fitness multipliers
        // are based on where the organism actually is this frame. Cache the liquid depth
        // from the single lookup so metabolism/drift don't each re-scan the vertex grid.
        var fluid = FluidDynamicsManager.Instance;
        _currentLiquidDepth = fluid != null ? fluid.GetLiquidDepthNearPosition(transform.position) : 0f;
        CurrentMedium = fluid != null && _currentLiquidDepth >= fluid.minVolumeToRender
            ? HabitatMedium.Sea
            : HabitatMedium.Land;

        // Advance the throttled sense cycle. When it ticks, the prey/mate/threat scans are allowed to
        // recompute once; between ticks they return cached targets. Phased by _senseTimer's Init offset
        // so the population's scans spread across frames instead of spiking on one. (Perf: option #3.)
        _senseTimer -= Time.deltaTime;
        bool senseTick = _senseTimer <= 0f;
        if (senseTick) { _senseTimer += SenseInterval; _senseCycle++; }
        _geneCheckElapsed += Time.deltaTime;

        UpdateStressLevel();
        UpdatePressureVariables();
        UpdateDispersalState();
        ApplyMediumMismatchDrain();
        CheckSexualIsolation();
        CheckGasSurvival();
        CheckBackboneGasTolerance();

        if (_dormancyTimer > 0f) _dormancyTimer -= Time.deltaTime;

        if (!HasMotility)
            UpdatePassiveDrift();
        else if (IsProducer)
            UpdateProducer();
        else
            UpdateConsumer();

        // Keep visual size synced to era × species-size-class × growth every frame (covers era
        // transitions and zero-net ticks that skip ApplyGrowthShrinkage's scale update).
        RefreshVisualScale();

        // Fallback reproduction cadence (see field docs): guarantees a mature, well-fed PRODUCER
        // reproduces even when the primary three-fill energy-threshold path never triggers within
        // its lifespan on slow-metabolism worlds. TryReproduce() still enforces cooldown/pop-cap
        // /mate gates, so this cannot over-breed a saturated or mate-starved population.
        //
        // CONSUMERS (heterotrophs) are deliberately EXCLUDED: their reproduction must be earned by
        // actual consumption (OnEat from predation/scavenging), never by a flat timer. Heterotrophs
        // passively top up _chemEnergy via osmotrophy (UpdateConsumer Layer 1) with no eat-count, so
        // an unconditional fallback let them reproduce free at reserveFrac=1.00 regardless of prey
        // availability — the monoculture exploit where a "predator" out-breeds everyone without ever
        // hunting. Gating the fallback to producers ties heterotroph population to real food supply.
        _reproTimer += Time.deltaTime;
        if (IsProducer && _reproTimer >= ReproFallbackInterval)
        {
            _reproTimer = 0f;
            float reserveFrac = Mathf.Max(_chemEnergy, _solarEnergy) / Mathf.Max(maxSolarEnergy, 0.001f);
            if (AgeSeconds >= _maxLifespan * ReproFallbackMinAgeFrac && reserveFrac >= ReproFallbackEnergyFrac)
            {
                // Only log the pathological case: an agent that has never completed a single
                // energy-fill cycle (_lifetimeEats == 0) yet is well-fed — i.e. the primary path is
                // genuinely stuck, not merely supplemented. Avoids one-line-per-agent spam on
                // healthy worlds where the fallback just runs alongside a working fill-cycle.
                if (!_reproFallbackLogged && _lifetimeEats == 0)
                {
                    _reproFallbackLogged = true;
                    Debug.Log($"[EvoSim] REPRO_FALLBACK agent={name} community={communityId} age={AgeSeconds:F0}s " +
                              $"reserveFrac={reserveFrac:F2} maxSolar={maxSolarEnergy:F2} metabolism={Metabolism} " +
                              $"— primary fill-cycle path never triggered (0 fills); time-based fallback reproducing.");
                }
                TryReproduce();
            }
        }

        // Force-grant Nucleus and Multicellularity on schedule — required prerequisites
        // before any gene event can fire; this path is separate from GeneEvolutionManager, so it
        // bypasses the normal "GENE QUEUED" log line. Log explicitly here instead so eukaryogenesis
        // (Nucleus) and the multicellularity transition are still visible in diagnostics — these
        // are foundational milestones, not less real just because they're not player-facing choices.
        float _se = GeneEvolutionManager.SessionElapsed;
        if (_se >= 3f && !AcquiredGenes.Contains("Nucleus"))
        {
            AcquiredGenes.Add("Nucleus");
            Debug.Log($"[EvoSim] GENE FORCE-GRANTED: Nucleus community={communityId} elapsed={_se:F0}s");
        }
        if (_se >= 8f && !AcquiredGenes.Contains("Multicellularity"))
        {
            AcquiredGenes.Add("Multicellularity");
            Debug.Log($"[EvoSim] GENE FORCE-GRANTED: Multicellularity community={communityId} elapsed={_se:F0}s");
        }

        // Separation only matters for motile organisms; drifters are carried by the same
        // currents so packing them is fine and the O(N²) check is wasted on sessile life.
        if (HasMotility) ApplySeparation();
        AttemptAtmosphericSpeciation();
        MaybeChangeSex();

        // Gene eligibility is a 49-gene scan per agent — a top CPU cost that gains nothing from 60 Hz.
        // Run it on the sense cadence, passing the REAL elapsed time since the last scan so the
        // probabilistic auto-gene origination rate (chance/sec) is exactly preserved. (Perf: option #1.)
        if (senseTick)
        {
            GeneEvolutionManager.CheckEligibleGenes(this, _geneCheckElapsed);
            _geneCheckElapsed = 0f;
        }
    }

    /// Passive dispersal: no directed locomotion. Medium determines the carrying agent:
    /// Sea → liquid current (viscosity-scaled, identity-specific per LiquidDef);
    /// Land → wind (WindManager). A small Brownian component prevents perfect clumping
    /// in both cases. Producers still metabolize while drifting.
    private void UpdatePassiveDrift()
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        bool inSea = CurrentMedium == HabitatMedium.Sea;

        if (inSea)
        {
            // Liquid current: gradient-descent direction weighted by liquid's FlowSpeedFactor.
            // Organisms in slow liquids (molten sulfur) barely move; fast liquids (methane) carry them quickly.
            Vector3 current = FluidDynamicsManager.Instance != null
                ? FluidDynamicsManager.Instance.GetLiquidCurrentAt(transform.position)
                : Vector3.zero;

            if (current.sqrMagnitude > 0.0001f)
                _heading = Vector3.Slerp(_heading, current.normalized, 1.5f * Time.deltaTime).normalized;
            else
            {
                // Still water / equilibrium basin: slow Brownian diffusion.
                float angle = Random.Range(-wanderTurnRate * 0.15f, wanderTurnRate * 0.15f) * Time.deltaTime;
                _heading = (Quaternion.AngleAxis(angle, normal) * _heading).normalized;
            }
        }
        else
        {
            // Land / air: wind-carried.
            Vector3 wind = WindManager.GetWind(transform.position);
            Vector3 windTangent = wind - Vector3.Dot(wind, normal) * normal;

            if (windTangent.sqrMagnitude > 0.001f)
                _heading = Vector3.Slerp(_heading, windTangent.normalized, 2f * Time.deltaTime).normalized;
            else
            {
                float angle = Random.Range(-wanderTurnRate * 0.2f, wanderTurnRate * 0.2f) * Time.deltaTime;
                _heading = (Quaternion.AngleAxis(angle, normal) * _heading).normalized;
            }
        }

        // Liquid drag: organisms in high-viscosity media move more slowly.
        // FlowSpeedFactor ≈ 1.0 for water, 0.02 for molten sulfur.
        float liquidDrag = (inSea && FluidDynamicsManager.Instance?.CurrentLiquid != null)
            ? FluidDynamicsManager.Instance.CurrentLiquid.FlowSpeedFactor
            : 1.0f;

        // Sessile organisms can't override their medium-driven drift, but if a predator is
        // immediately adjacent, a weak flee impulse can redirect the drift heading.
        float fleeWeight = RefreshFleeState(out Vector3 fleeDir);
        if (fleeWeight > 0.3f)
            _heading = Vector3.Slerp(_heading, fleeDir, fleeWeight * 0.5f * Time.deltaTime).normalized;

        // Passive dispersal (Issue 1b): a weak prevailing "current" biases non-motile drift in one
        // consistent global direction, so simple pre-motility life slowly colonizes new regions over
        // geological time instead of oscillating in place forever waiting for motility to evolve.
        Vector3 pdNormal = (transform.position - planetCenter).normalized;
        Vector3 prevailing = Vector3.Cross(pdNormal, Vector3.up);
        if (prevailing.sqrMagnitude < 0.0001f) prevailing = Vector3.Cross(pdNormal, Vector3.forward);
        _heading = Vector3.Slerp(_heading, prevailing.normalized, PassiveDispersalBias).normalized;

        float eraMult = EraManager.Instance != null ? EraManager.Instance.MoveSpeedMultiplier : 1f;
        // Sessile organisms can drift slightly faster when threatened (turbulence response).
        float fleeBoost = 1f + fleeWeight * 0.3f;
        Vector3 newPos = SphereSurface.MoveAlongSurface(
            transform.position, _heading, moveSpeed * eraMult * 0.12f * liquidDrag * fleeBoost * Time.deltaTime,
            planetCenter, planetRadius);

        // Habitat boundary: a non-motile organism has no directed locomotion to escape a fatal
        // medium, so it should not drift across the sea/land boundary like a free particle.
        // If the proposed step would exit viable medium, damp it to a near-zero anchor jitter and
        // steer the heading back toward viable habitat — modeling a tethered sessile organism
        // rather than one that random-walks onto land and dies of MediumMismatch.
        if (!HasMotility && FluidDynamicsManager.Instance != null)
        {
            bool proposedSubmerged = FluidDynamicsManager.Instance.IsSubmerged(newPos);
            bool proposedViable = _isAquatic ? proposedSubmerged : !proposedSubmerged;
            if (!proposedViable)
            {
                // Reverse heading (bias back toward the viable side) and keep position nearly put.
                _heading = Vector3.Reflect(_heading, (transform.position - planetCenter).normalized).normalized;
                newPos = Vector3.Lerp(transform.position, newPos, HabitatReturnBias);
            }
        }

        transform.position = newPos;
        AlignToSurface();

        switch (Metabolism)
        {
            case MetabolismType.Chemosynthetic: UpdateChemosyntheticMetabolism(); break;
            case MetabolismType.Phototrophic:   UpdateProducerMetabolism();       break;
            case MetabolismType.Heterotrophic:  UpdateOsmotrophy();               break;
            case MetabolismType.Mixotrophic:    UpdateMixotrophicMetabolism();    break;
        }
    }

    // Soft repulsion: push apart any two agents closer than their combined radii.
    // Uses surface distance so the separation force stays tangent to the sphere.
    // Only checks agents already in _spawner.ActiveAgents — no extra broad-phase needed
    // since populations are small and we short-circuit at sense radius.
    private void ApplySeparation()
    {
        if (_spawner == null) return;
        float agentRadius = transform.localScale.x * 0.5f;
        float minSep = agentRadius * 2f;
        const float repulseStrength = 4f; // world units per second at full overlap
        Vector3 myPos = transform.position;
        Vector3 myNormal = (myPos - planetCenter).normalized;

        // Bound the grid query to a generous max agent size rather than the true requiredSep (which
        // varies per-pair) — the inner distance check below still applies the exact per-pair radius.
        _spawner.QueryNearby(myPos, 2f, _queryBuffer);
        foreach (var other in _queryBuffer)
        {
            if (other == null || other == this) continue;
            float otherRadius = other.transform.localScale.x * 0.5f;
            float requiredSep = agentRadius + otherRadius;
            float surfDist = SphereSurface.SurfaceDistance(myPos, other.transform.position, planetCenter, planetRadius);
            if (surfDist >= requiredSep || surfDist < 0.001f) continue;

            // Push direction: tangent away from the other agent along the sphere surface.
            Vector3 awayWorld = (myPos - other.transform.position).normalized;
            Vector3 tangentAway = (awayWorld - Vector3.Dot(awayWorld, myNormal) * myNormal).normalized;
            if (tangentAway.sqrMagnitude < 0.0001f) continue;

            float overlap = requiredSep - surfDist;
            float push = repulseStrength * (overlap / requiredSep);
            Vector3 newPos = SphereSurface.MoveAlongSurface(myPos, tangentAway, push * Time.deltaTime, planetCenter, planetRadius);
            transform.position = newPos;
            myPos = newPos;
            myNormal = (myPos - planetCenter).normalized;
        }
    }

    /// Public read-only view of this agent's current atmospheric stress, for HUD display.
    public float AtmosphericDiscomfort => GetAtmosphericDiscomfort();

    /// 0 (current atmosphere matches this lineage's locked-in ideal mix exactly) .. 1
    /// (maximally divergent). Computed against EVERY gas present, not just one
    /// "poisonous"-tagged gas - any drift away from the ideal mix matters.
    private float GetAtmosphericDiscomfort()
    {
        if (AtmosphereManager.Instance == null || _idealGasMix.Count == 0) return 0f;
        float totalDiff = 0f;
        foreach (var gas in AtmosphereManager.Instance.Gases)
        {
            float idealFrac = _idealGasMix.TryGetValue(gas.Name, out float v) ? v : 0f;
            totalDiff += Mathf.Abs(gas.Fraction - idealFrac);
        }
        return Mathf.Clamp01(totalDiff / 2f); // max possible total diff between two distributions is 2
    }

    /// Same hardiness-style scaling as climate fitness: a generalist (high gasTolerance)
    /// takes a shallow penalty for a given mismatch, a specialist (low gasTolerance) a steep one.
    /// Chooses between EnergyDepletion and ClimateStress when an agent's energy reserve hits
    /// zero. If climate or atmospheric discomfort is severe at the moment of death, the deficit
    /// was climate-driven (the Q10 demand inflation / atmospheric penalty outran income) rather
    /// than ordinary foraging bad luck — attribute it distinctly so climate mortality is visible
    /// in death-cause breakdowns. Threshold is TUNABLE.
    private DeathCause EnergyDeathCause()
    {
        const float SevereDiscomfort = 0.6f;
        float climate = GetDiscomfort(transform.position);
        float atmo    = GetAtmosphericDiscomfort();
        return (climate >= SevereDiscomfort || atmo >= SevereDiscomfort)
            ? DeathCause.ClimateStress
            : DeathCause.EnergyDepletion;
    }

    private float GetAtmosphericFitnessMultiplier()
    {
        float discomfort = GetAtmosphericDiscomfort();
        float effectiveRange = atmosFitnessMultiplierRange * Mathf.Lerp(toleranceRangeMax, toleranceRangeMin, gasTolerance / 100f);
        float multiplier = 1f + (discomfort * 2f - 1f) * effectiveRange;
        return Mathf.Clamp(multiplier, 0.2f, 3f);
    }

    /// Updates the sustained-adversity accumulator (StressLevel, 0-100). Each tick combines
    /// three independent adversity sources - climate discomfort, atmospheric discomfort, and
    /// starvation proximity - into a single 0-1 signal, then runs that signal through a
    /// leaky-bucket / exponential moving average so StressLevel rises gradually under
    /// sustained adversity and decays gradually when conditions improve, rather than
    /// spiking from one bad tick. Producers don't track _timeSinceLastMeal, so starvation
    /// proximity is omitted for them (climate + atmosphere only).
    private void UpdateStressLevel()
    {
        float climateDiscomfort  = GetDiscomfort(transform.position);
        float atmosphericDiscomfort = GetAtmosphericDiscomfort();
        float uvDiscomfort       = Mathf.Clamp01((GetUVFitnessMultiplier() - 1f) / 4f);
        float pressureDiscomfort = Mathf.Clamp01((GetPressureFitnessMultiplier() - 1f) / 2f);
        float thermalDiscomfort  = Mathf.Clamp01((GetThermalCycleFitnessMultiplier() - 1f) / 2f);
        float starvationProximity = IsProducer ? 0f : Mathf.Clamp01(_timeSinceLastMeal / starvationTime);

        float adversity = (climateDiscomfort + atmosphericDiscomfort + uvDiscomfort
                         + pressureDiscomfort + thermalDiscomfort + starvationProximity) / 6f;
        float targetStress = adversity * 100f; // 0-100 dimension, matching trait convention

        // Exponential moving average toward targetStress, with separate rise/decay rates so
        // stress climbs faster than it falls. tau ~= time to reach ~95% of the way to a new
        // target (3 time constants); using a per-frame factor keeps this framerate-independent.
        float tau = targetStress > StressLevel ? stressRiseTime : stressDecayTime;
        float alpha = tau > 0f ? 1f - Mathf.Exp(-Time.deltaTime / (tau / 3f)) : 1f;

        float newStress = Mathf.Lerp(StressLevel, targetStress, alpha);
        newStress = Mathf.Clamp(newStress, 0f, 100f);

        if (_stressRegistered)
        {
            PopulationStats.UnregisterStress(StressLevel);
            PopulationStats.RegisterStress(newStress);
        }
        StressLevel = newStress;
    }

    /// Refreshes ResourceScarcity, PredationPressure, and PathogenPressure every 3 s.
    /// Called from Update(); per-agent refreshes are staggered by a random offset set at Init
    /// so 350 agents don't all scan on the same frame.
    private void UpdatePressureVariables()
    {
        _pressureRefreshTimer -= Time.deltaTime;
        if (_pressureRefreshTimer > 0f) return;
        _pressureRefreshTimer = PressureRefreshInterval;

        ResourceScarcity = 1f - Mathf.Clamp01(ChemicalNutrientPool.Sample(transform.position) * 4f);

        if (_spawner == null) return;
        const float ScanRadius = 8f;
        int total = 0, consumers = 0, sameCommunity = 0;
        _spawner.QueryNearby(transform.position, ScanRadius, _queryBuffer);
        foreach (var a in _queryBuffer)
        {
            if (a == null || a == this) continue;
            total++;
            if (!a.IsProducer && a.HasMotility) consumers++;
            if (a.communityId == communityId) sameCommunity++;
        }
        PredationPressure = Mathf.Clamp01(total > 0 ? (float)consumers / total : 0f);
        PathogenPressure  = Mathf.Clamp01(total / 12f);
        // MateScarcity: high when same-community density is low (mate-search difficulty).
        // Cap at 6 same-community neighbors = no scarcity; 0 neighbors = maximum scarcity.
        MateScarcity = 1f - Mathf.Clamp01(sameCommunity / 6f);
        _localSameCommunity = sameCommunity; // local crowding signal for density-dependent dispersal

        // appearance-generation-spec §3.3: re-checked on this same staggered cadence because it
        // drifts continuously as Era 2's Intelligence Index rises, unlike every other
        // ApplyMorphology trigger (which fires from discrete gene-choice events instead).
        int networkBucket = ComputeNetworkForeshadowBucket();
        if (networkBucket != _lastNetworkForeshadowBucket)
        {
            _lastNetworkForeshadowBucket = networkBucket;
            ApplyMorphology();
        }
    }
    private int _lastNetworkForeshadowBucket;

    /// Same hardiness/gasTolerance-style scaling: a resilient individual (high
    /// stressTolerance) takes a shallow extra penalty for a given accumulated StressLevel,
    /// a fragile one (low stressTolerance) a steep one. This is an ADDITIONAL layer on top
    /// of GetClimateStarvationMultiplier/GetAtmosphericFitnessMultiplier, not a replacement -
    /// it makes sustained adversity compound into something worse than the sum of any one
    /// instantaneous discomfort, which is the whole point of tracking it separately.
    private float GetStressFitnessMultiplier()
    {
        float stress01 = StressLevel / 100f;
        float effectiveRange = stressFitnessMultiplierRange * Mathf.Lerp(stressToleranceRangeMax, stressToleranceRangeMin, stressTolerance / 100f);
        float multiplier = 1f + stress01 * effectiveRange; // stress only ever adds penalty, never a bonus
        return Mathf.Clamp(multiplier, 1f, 2.5f);
    }

    /// Speciation: under sustained atmospheric stress, a stressed individual has a random
    /// chance per second (scaling with how far past the stress threshold it is) to re-lock
    /// its ideal mix to the CURRENT atmosphere and found a new lineage. Skipped when
    /// SpeciationManager is active — it owns the timing in that case.
    private void AttemptAtmosphericSpeciation()
    {
        if (SpeciationManager.Instance != null) return; // SpeciationManager owns speciation

        if (AtmosphereManager.Instance == null) return;
        float discomfort = GetAtmosphericDiscomfort();
        float excess = discomfort - speciationStressThreshold;
        if (excess <= 0f) return;

        float chancePerSecond = excess * speciationChanceScale;
        if (Random.value < chancePerSecond * Time.deltaTime)
        {
            _idealGasMix = AtmosphereManager.Instance.SnapshotMix();
            gasTolerance = Mathf.Clamp(PopulationStats.SampleDimension(gasTolerance, mutationStdDev), 0f, 100f);
            AtmoLineage = KingdomNameGenerator.Generate();

            lineageColor = Color.HSVToRGB(Random.value, Random.Range(0.65f, 0.95f), Random.Range(0.85f, 1f));
            ApplyLineageColor();
            _morphSeed = _morphSeed * 31 + Random.Range(1, 100000); // branch → divergent body
            ApplyMorphology();

            Debug.Log($"[Speciation] {name} adapted to the shifted atmosphere -> new lineage '{AtmoLineage}'");
        }
    }

    /// Called by SpeciationManager when the SI formula selects this agent to found a new
    /// lineage. Re-locks ideal gas mix, assigns new lineage name/color, and drifts 1–2
    /// traits to represent niche divergence.
    public void TriggerSpeciation(string lineageName, Color color)
    {
        _idealGasMix = AtmosphereManager.Instance != null
            ? AtmosphereManager.Instance.SnapshotMix()
            : new Dictionary<string, float>();
        gasTolerance = Mathf.Clamp(PopulationStats.SampleDimension(gasTolerance, mutationStdDev), 0f, 100f);
        AtmoLineage = lineageName;
        lineageColor = color;
        ApplyLineageColor();
        _morphSeed = _morphSeed * 31 + Random.Range(1, 100000); // branch → divergent body
        ApplyMorphology();

        // Drift 1 or 2 randomly chosen traits to represent ecological niche divergence.
        int driftCount = Random.Range(1, 3);
        float[] current = { visionTrait, speedTrait, strengthTrait, hardinessTrait, temperaturePreference, moisturePreference };
        // Shuffle indices using Fisher-Yates.
        int[] indices = { 0, 1, 2, 3, 4, 5 };
        for (int i = indices.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
        }
        for (int k = 0; k < driftCount; k++)
            current[indices[k]] = Mathf.Clamp(PopulationStats.SampleDimension(current[indices[k]], mutationStdDev * 2f), 0f, 100f);

        SetTraits(current[0], current[1], current[2], current[3], current[4], current[5]);
    }

    private void UpdateConsumer()
    {
        // Layer 1 — osmotrophy/saprotrophy: absorb dissolved organics passively.
        // This is the consumer fallback that bridges pre-predation abiogenesis.
        float nutrients = ChemicalNutrientPool.Sample(transform.position);
        float osmotrophySlowdown = Mathf.Clamp01(nutrients * 0.45f); // up to 45% starvation reduction
        if (nutrients > 0.01f)
        {
            ChemicalNutrientPool.Deplete(transform.position, 0.00008f * Time.deltaTime);
            // Passive absorption trickle-charges the energy reserve even without active predation.
            float passiveGain = nutrients * ChemoEfficiency * 0.3f * Time.deltaTime;
            _chemEnergy = Mathf.Clamp(_chemEnergy + passiveGain, 0f, maxSolarEnergy);
        }

        // Layer 2 — active foraging: predation hard-requires motility per spec
        // (e1_heterotrophic_predation_emergence needs e1_motility_emergence).
        AgentController preyTarget = HasMotility ? FindPreyInRange() : null;
        CorpseItem corpseTarget = preyTarget == null ? FindNearestCorpseInRange() : null;

        if (HasMotility)
        {
            // Even predators flee stronger predators (apex predator hierarchy).
            float fleeWeight = RefreshFleeState(out Vector3 fleeDir);

            Vector3 desiredTangent;
            if (fleeWeight > 0.7f)
            {
                desiredTangent = fleeDir;
            }
            else if (IsDispersing)
            {
                // Committed dispersal journey away from the saturated home cluster (well-fed
                // dispersers forgo local hunting/mating for the duration; yields only to flee).
                desiredTangent = fleeWeight > 0f
                    ? (ComputeDispersalTangent() * (1f - fleeWeight) + fleeDir * fleeWeight).normalized
                    : ComputeDispersalTangent();
            }
            else
            {
                Vector3 huntTangent = preyTarget != null
                    ? ComputeMovementTangentToPrey(preyTarget)
                    : ComputeMovementTangentToCorpse(corpseTarget);

                // Mate-seeking: when sexual and energetically ready, redirect toward nearest
                // mate if none is already in sense range. Prey-hunting takes priority when
                // starving (< half the starvation window elapsed); mating wins otherwise.
                if (IsSexual && _eatsSinceReproduction >= eatsToReproduce
                    && _timeSinceLastMeal < starvationTime * 0.5f
                    && FindMateInRange() == null)
                {
                    AgentController target = FindNearestMate();
                    if (target != null)
                    {
                        Vector3 norm = (transform.position - planetCenter).normalized;
                        Vector3 toMate = (target.transform.position - transform.position);
                        Vector3 mateTangent = (toMate - Vector3.Dot(toMate, norm) * norm).normalized;
                        if (mateTangent.sqrMagnitude > 0.01f)
                            huntTangent = Vector3.Slerp(huntTangent, mateTangent, 0.6f).normalized;
                    }
                }

                desiredTangent = fleeWeight > 0f
                    ? (huntTangent * (1f - fleeWeight) + fleeDir * fleeWeight).normalized
                    : huntTangent;
                // Territorial tether — see UpdateProducer for the full rationale. Applied after flee
                // blending so predator escape always still takes priority over heading home.
                desiredTangent = ApplyTerritorialBias(desiredTangent);
            }

            _heading = Vector3.Slerp(_heading, desiredTangent, turnSpeed * Time.deltaTime).normalized;
            float eraMult = EraManager.Instance != null ? EraManager.Instance.MoveSpeedMultiplier : 1f;
            float fleeBoost = 1f + fleeWeight * 0.5f;
            Vector3 newPos = SphereSurface.MoveAlongSurface(transform.position, _heading,
                moveSpeed * eraMult * fleeBoost * globalMoveSpeedScale * Time.deltaTime, planetCenter, planetRadius);
            newPos = ApplyMediumBoundary(newPos);
            transform.position = newPos;
            AlignToSurface();
        }

        // Layer 3 — eat if in range.
        if (preyTarget != null && preyTarget.gameObject != null)
        {
            float dist = SphereSurface.SurfaceDistance(transform.position, preyTarget.transform.position,
                planetCenter, planetRadius);
            if (dist <= eatRadius)
            {
                if (preyTarget.IsProducer)
                {
                    GrazeOn(preyTarget); // herbivory: drain energy without killing
                }
                else
                {
                    // Strength-based combat: strong prey may escape or kill the predator.
                    // ResolvePredatorAttack() returns true only if kill succeeds.
                    // Note: if the predator was killed (counter-attack), 'this' is now dead
                    // and gameObject is destroyed — do not access 'this' after returning true
                    // from the counter-kill branch. The null-check on preyTarget guards enough.
                    if (preyTarget.ResolvePredatorAttack(this))
                    {
                        float energyGained = TransferBiomassFrom(preyTarget);
                        Destroy(preyTarget.gameObject);
                        OnEat(energyGained);
                    }
                    // else: prey escaped or this predator was killed — either way, no eat
                }
            }
        }
        else if (corpseTarget != null)
        {
            float dist = SphereSurface.SurfaceDistance(transform.position, corpseTarget.transform.position,
                planetCenter, planetRadius);
            if (dist <= eatRadius)
            {
                float corpseMass = corpseTarget.Consume();
                float energyGained = corpseMass * 0.8f * AssimilationEfficiency; // corpse = lower caloric density
                _chemEnergy = Mathf.Clamp(_chemEnergy + energyGained, 0f, maxSolarEnergy);
                // Priority 2: distinct consumption log every time a corpse is actually eaten, so
                // "is anything eating corpses / is heterotroph reproduction tied to real scavenging"
                // is directly answerable instead of inferred.
                Debug.Log($"[Corpse] CONSUMED agent={name} community={communityId} nutrientGained={energyGained:F4} corpseMass={corpseMass:F4}");
                OnEat(energyGained);
            }
        }

        if (_reproCooldownTimer > 0f) _reproCooldownTimer -= Time.deltaTime;

        // Starvation tick: non-climate pressures combined (Q10 handles temperature via ComputeDemand).
        float drain = GetNonClimateFitnessMultipliers() * (1f - osmotrophySlowdown);
        _timeSinceLastMeal += Time.deltaTime * drain;
        if (_timeSinceLastMeal >= starvationTime)
            Die(DeathCause.Starvation);

        // Grow/shrink from net energy: consumers lose mass when starving, gain when fed.
        // Net is negative here (no continuous acquisition — onEat pulses _chemEnergy instead).
        float consumerNet = (_chemEnergy > maxSolarEnergy * 0.5f) ? ComputeDemand() * 0.1f : -ComputeDemand();
        NetEnergy = consumerNet; // previously never set for consumers — avgNet read a permanent 0.0 for any all-heterotroph community
        ApplyGrowthShrinkage(consumerNet);
    }

    /// Herbivory: drain a producer's energy reserve without killing it.
    /// Models grazing — the producer is weakened but survives.
    private void GrazeOn(AgentController producer)
    {
        // Refuge effect: if the prey community is down to ≤4 agents they're effectively
        // unfindable (sparse + evasive). This prevents consumers from eating a species
        // to absolute zero, preserving producer diversity.
        if (_spawner != null && producer.communityId >= 0)
        {
            int preyPop = 0;
            foreach (var a in _spawner.ActiveAgents)
                if (a != null && a.communityId == producer.communityId) preyPop++;
            if (preyPop <= 4) return;
        }

        float drained = producer.DrainEnergy(maxSolarEnergy * 0.25f);
        if (drained > 0f)
        {
            // Grazing transfers drained energy × herbivore assimilation efficiency into consumer reserve.
            float gained = drained * AssimilationEfficiency * 0.5f; // lower efficiency for plant material
            _chemEnergy = Mathf.Clamp(_chemEnergy + gained, 0f, maxSolarEnergy);
            OnEat(gained);
        }
    }

    /// Compute energy transferred from prey to this predator on a successful kill (spec §7).
    private float TransferBiomassFrom(AgentController prey)
    {
        // Caloric density: producers are energy-rich (full reserve); consumers vary by reserve.
        float preyBiomass = prey._currentMass;
        float caloricDensity = prey.IsProducer ? 1.2f : 1.0f;
        float grossEnergy = preyBiomass * caloricDensity * AssimilationEfficiency;

        // Predation cost: subduing larger/tougher prey (relative to this predator's mass/strength)
        // consumes a bigger share of the yield. costFraction rises with the prey/predator mass ratio
        // and with prey hardiness vs. predator strength, clamped so net stays non-negative.
        float predatorMass = Mathf.Max(_currentMass, 1e-4f);
        float massRatio    = Mathf.Pow(Mathf.Max(preyBiomass, 1e-4f) / predatorMass, PredationCostExponent);
        float toughness    = (prey.hardinessTrait + 1f) / (strengthTrait + 1f);
        float costFraction = Mathf.Clamp01(PredationCostFraction * massRatio * toughness);

        float energyGained = grossEnergy * (1f - costFraction);
        _chemEnergy = Mathf.Clamp(_chemEnergy + energyGained, 0f, maxSolarEnergy);
        return energyGained;
    }

    /// Drain up to <amount> from this organism's active energy reserve; returns amount actually drained.
    public float DrainEnergy(float amount)
    {
        if (Metabolism == MetabolismType.Phototrophic)
        {
            float d = Mathf.Min(_solarEnergy, amount);
            _solarEnergy -= d;
            return d;
        }
        else
        {
            float d = Mathf.Min(_chemEnergy, amount);
            _chemEnergy -= d;
            return d;
        }
    }

    public void ReceiveRelationshipBonus(float amount)
    {
        _chemEnergy = Mathf.Clamp(_chemEnergy + amount, 0f, maxSolarEnergy);
    }

    public void ReceiveRelationshipDrain(float amount) => DrainEnergy(amount);

    private void OnEat(float energyGained = 0f)
    {
        _timeSinceLastMeal = 0f;
        _lifetimeEats++;

        // Energy-threshold reproduction: active eating (predation, scavenging, grazing) fills
        // the reserve; reproduce when it's full, same model as chemosynthetics and osmotrophy.
        if (_chemEnergy >= maxSolarEnergy * 0.90f)
        {
            _chemEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            if (_eatsSinceReproduction >= eatsToReproduce)
                TryReproduce();
        }
    }

    /// Gate in front of Reproduce(): asexual agents reproduce immediately, same as before.
    /// Sexual agents need a mate in range first - if none is found, reproduction is simply
    /// delayed (the eats-since-reproduction counter is NOT reset) so the agent keeps trying
    /// on every subsequent qualifying tick/meal until a mate turns up.
    private void TryReproduce()
    {
        // Emergency dormancy trades reproduction for survival — see EnterDormancy.
        if (_dormancyTimer > 0f) return;

        // Minimum time between successive reproductions — prevents heterotrophs that graze
        // rapidly from out-breeding chemosynthetics by orders of magnitude.
        if (_reproCooldownTimer > 0f) return;

        // Technical safety valve only (NOT an ecological carrying-capacity cap — that's now purely
        // emergent from energy math). This is a pure frame-rate protection ceiling and should rarely
        // if ever actually trigger in normal play.
        if (_spawner != null && _spawner.ActiveAgents.Count >= AgentSpawner.MaxIndividualAgents)
        {
            GameLog.LogReproFail(communityId, "IndividualCountSafetyValve");
            return;
        }

        // Small communities recover faster after mass-die events: at ≤5 survivors
        // the cooldown drops to 5s, scaling linearly back to 30s at 50+ agents.
        int commPop = 0;
        if (_spawner != null)
            foreach (var a in _spawner.ActiveAgents)
                if (a != null && a.communityId == communityId) commPop++;
        float cooldown = Mathf.Lerp(5f, 30f, Mathf.InverseLerp(5, 50, commPop));

        if (!IsSexual)
        {
            _eatsSinceReproduction = 0;
            _reproCooldownTimer = cooldown;
            GameLog.LogBirth(communityId);
            Debug.Log($"[EvoSim] BIRTH asexual community={communityId} pop_global={_spawner?.ActiveAgents.Count}");
            Reproduce(null);
            return;
        }

        AgentController mate = FindMateInRange();
        if (mate == null)
        {
            GameLog.LogReproFail(communityId, "NoMate");
            _noMateStreak++;
            // Facultative parthenogenesis: a sexual lineage that repeatedly can't find a mate falls
            // back to asexual cloning rather than being reproductively locked out (real-world: aphids,
            // Komodo dragons, some sharks/snakes under mate scarcity). This is the fix for the Allee
            // dead-end where a small sexual population froze — reproduction stalled to zero while
            // energy stayed healthy and the founder floor blocked extinction, pinning it at ~2-4
            // members indefinitely. Cloning restores growth, which restores mate availability, at which
            // point sexual reproduction naturally resumes. TUNABLE streak threshold.
            if (_noMateStreak >= NoMateParthenogenesisStreak)
            {
                _noMateStreak = 0;
                _eatsSinceReproduction = 0;
                _reproCooldownTimer = cooldown;
                GameLog.LogBirth(communityId);
                Debug.Log($"[Reproduction] {name} community={communityId} — no mate after {NoMateParthenogenesisStreak} attempts; facultative parthenogenesis (asexual clone).");
                Reproduce(null);
            }
            return;
        }

        _noMateStreak = 0; // mate found — sexual reproduction proceeds normally
        _eatsSinceReproduction = 0;
        _reproCooldownTimer = cooldown;
        GameLog.LogBirth(communityId);
        Debug.Log($"[EvoSim] BIRTH sexual community={communityId} pop_global={_spawner?.ActiveAgents.Count}");
        Reproduce(mate);
    }

    private int _noMateStreak;
    private const int NoMateParthenogenesisStreak = 3; // failed fill-cycles before an asexual fallback clone

    /// Enters/maintains a density-dependent dispersal journey for motile agents. Called each tick
    /// from Update() after UpdatePressureVariables() has refreshed the local-crowding signal.
    private void UpdateDispersalState()
    {
        if (!HasMotility) return;

        if (_dispersalTimer > 0f)
        {
            _dispersalTimer -= Time.deltaTime;
            // Journey cost — dispersal is a real energetic gamble, not a free relocation.
            _solarEnergy = Mathf.Max(0f, _solarEnergy - DispersalEnergyCostPerSec * Time.deltaTime);
            _chemEnergy  = Mathf.Max(0f, _chemEnergy  - DispersalEnergyCostPerSec * Time.deltaTime);
            return;
        }

        _dispersalCheckTimer -= Time.deltaTime;
        if (_dispersalCheckTimer > 0f) return;
        _dispersalCheckTimer = DispersalCheckInterval;

        // Density-dependent trigger: only crowded, well-fed adults disperse. A starving or
        // sparsely-surrounded organism has no reason to gamble on a long journey.
        float pressure = _localSameCommunity / (float)DispersalCrowdCap;
        float reserveFrac = Mathf.Max(_chemEnergy, _solarEnergy) / Mathf.Max(maxSolarEnergy, 0.001f);
        if (pressure < DispersalPressureThresh || reserveFrac < 0.5f) return;
        // era3-primitives-spec §2.2: boldness raises willingness to disperse into unknown habitat —
        // a bold organism gambles on the journey more readily; a shy one needs more pressure to move.
        float boldDispersalMult = Mathf.Lerp(0.5f, 1.6f, Mathf.Clamp01(boldness / 100f));
        if (Random.value > DispersalChancePerCheck * boldDispersalMult) return;

        _dispersalDir = ComputeDispersalDirection();
        if (_dispersalDir.sqrMagnitude < 0.0001f) return;
        _dispersalTimer = DispersalDuration;
        GameLog.LogDispersal(communityId); // Priority 4: per-interval dispersal rate rollup
        if (!_dispersalLogged)
        {
            _dispersalLogged = true;
            Debug.Log($"[EvoSim] DISPERSAL agent={name} community={communityId} localKin={_localSameCommunity} " +
                      $"pressure={pressure:F2} — beginning long-range dispersal journey away from cluster.");
        }
    }

    /// World-space direction pointing AWAY from the local same-community centroid (toward open
    /// space), projected onto the surface tangent plane. Random direction if no kin are nearby.
    private Vector3 ComputeDispersalDirection()
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 away;
        if (_spawner != null)
        {
            Vector3 kinCentroid = Vector3.zero; int n = 0;
            const float scan = 8f;
            _spawner.QueryNearby(transform.position, scan, _queryBuffer);
            foreach (var a in _queryBuffer)
            {
                if (a == null || a == this || a.communityId != communityId) continue;
                kinCentroid += a.transform.position; n++;
            }
            away = n > 0 ? (transform.position - kinCentroid / n) : Random.onUnitSphere;
        }
        else away = Random.onUnitSphere;

        Vector3 tangent = away - Vector3.Dot(away, normal) * normal;
        if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.Cross(normal, Random.onUnitSphere);
        return tangent.normalized;
    }

    /// Current surface-tangent heading for an in-progress dispersal journey (re-projected each tick
    /// as the agent moves around the sphere).
    private Vector3 ComputeDispersalTangent()
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 tangent = _dispersalDir - Vector3.Dot(_dispersalDir, normal) * normal;
        return tangent.sqrMagnitude < 0.0001f ? _heading : tangent.normalized;
    }

    private void UpdateProducer()
    {
        // Flee drive: dominates all other drives when a predator is close and this organism
        // is weaker. A strong producer (high strengthTrait) has a lower flee weight and
        // may stand its ground rather than abandoning a good feeding patch.
        float fleeWeight = RefreshFleeState(out Vector3 fleeDir);

        Vector3 desiredTangent;
        if (fleeWeight > 0.7f)
        {
            // Flee dominates: ignore comfort/sun, just run.
            desiredTangent = fleeDir;
        }
        else if (IsDispersing)
        {
            // Dispersal overrides foraging/social/mate pulls: a committed directed journey away
            // from the saturated home cluster toward open space (yields only to flee, handled below).
            desiredTangent = fleeWeight > 0f
                ? (ComputeDispersalTangent() * (1f - fleeWeight) + fleeDir * fleeWeight).normalized
                : ComputeDispersalTangent();
        }
        else
        {
            // Normal producer movement, blended with partial flee if needed.
            Vector3 forageTangent = Metabolism == MetabolismType.Phototrophic
                ? ComputeProducerMovementTangent()
                : (HasMotility ? ComputeVentSeekTangent() : ComputeWanderTangent());
            // Social/formation pull (zero if solitary) — Era 2 social structure overrides the Era 1
            // baseline where the community has actually chosen one; otherwise falls back to it.
            Vector3 socialBias = ComputeFormationBias(out float socialWeight);
            Vector3 baseTangent = socialWeight > 0f && socialBias.sqrMagnitude > 0.01f
                ? (forageTangent * (1f - socialWeight) + socialBias * socialWeight).normalized
                : forageTangent;
            // Territorial tether: LooseRange/StrictSite communities pull back toward their home site
            // once far enough outside it. Nomadic communities (the default/original behavior) are
            // completely unaffected — this only activates once TerritorialityManager has settled a
            // community, so it's additive, not a behavior change for anyone who stays nomadic.
            baseTangent = ApplyTerritorialBias(baseTangent);

            // Mate-seeking: sexual organisms that are energetically ready to reproduce
            // bias movement toward the nearest eligible mate. Overrides foraging but yields
            // to flee; weight drops to zero once a mate is already within sense range.
            if (IsSexual && _eatsSinceReproduction >= eatsToReproduce && FindMateInRange() == null)
            {
                AgentController target = FindNearestMate();
                if (target != null)
                {
                    Vector3 norm = (transform.position - planetCenter).normalized;
                    Vector3 toMate = (target.transform.position - transform.position);
                    Vector3 mateTangent = (toMate - Vector3.Dot(toMate, norm) * norm).normalized;
                    if (mateTangent.sqrMagnitude > 0.01f)
                        baseTangent = Vector3.Slerp(baseTangent, mateTangent, 0.6f).normalized;
                }
            }

            desiredTangent = fleeWeight > 0f
                ? (baseTangent * (1f - fleeWeight) + fleeDir * fleeWeight).normalized
                : baseTangent;
        }

        _heading = Vector3.Slerp(_heading, desiredTangent, turnSpeed * Time.deltaTime).normalized;

        // Speed boost when fleeing: adrenaline-analog — up to 1.5× normal speed at full flee weight.
        float eraMult = EraManager.Instance != null ? EraManager.Instance.MoveSpeedMultiplier : 1f;
        float fleeBoost = 1f + fleeWeight * 0.5f;
        Vector3 newPos = SphereSurface.MoveAlongSurface(transform.position, _heading,
            moveSpeed * eraMult * fleeBoost * globalMoveSpeedScale * Time.deltaTime, planetCenter, planetRadius);
        newPos = ApplyMediumBoundary(newPos);
        transform.position = newPos;
        AlignToSurface();

        switch (Metabolism)
        {
            case MetabolismType.Chemosynthetic: UpdateChemosyntheticMetabolism(); break;
            case MetabolismType.Phototrophic:   UpdateProducerMetabolism();       break;
            case MetabolismType.Mixotrophic:    UpdateMixotrophicMetabolism();    break;
        }
    }

    // FissionFusion state: periodically flips between "joined" (cohesion bias active) and "solo"
    // (wanders independently) so the group visibly splits and re-merges over time, matching the real
    // fission-fusion pattern (chimps, dolphins) rather than a single constant formation.
    private bool _fissionFusionJoined = true;
    private float _fissionFusionTimer;
    private const float FissionFusionPeriod = 25f; // seconds per phase, TUNABLE

    /// Formation bias for this tick: Era 2's SocialStructureType (once the community has chosen one)
    /// overrides the Era 1 SocialityBaseline formation below it, since it's a more specific, later
    /// decision — same precedence rule used for herd eligibility. Falls back to the Era 1 baseline
    /// (GroupForming=schooling / Aggregating=clustering / Solitary=none) otherwise.
    private Vector3 ComputeFormationBias(out float weight)
    {
        SocialStructureType structure = SocialStructureType.Unset;
        if (Era2Manager.Instance != null)
            structure = Era2Manager.Instance.GetRecord(communityId)?.SocialStructure ?? SocialStructureType.Unset;

        switch (structure)
        {
            case SocialStructureType.PairBonded:
                weight = 0.30f;
                return ComputePairBondBias();
            case SocialStructureType.MultiMemberTroop:
                weight = 0.30f;
                return ComputeTroopBias();
            case SocialStructureType.FissionFusion:
                return ComputeFissionFusionBias(out weight);
            // EusocialColonial doesn't need a formation bias here — it's tethered to its colony site
            // by ApplyTerritorialBias instead (a fixed-site pull is the actually-correct behavior for
            // workers around a nest, not a moving-flock formation).
            default:
                // appearance-generation-spec §3.2: Solitary now gets a real (if subtle) formation
                // signature too — "Territorial/dispersed: wide, roughly even spacing, minimal
                // alignment" — rather than zero bias/indifference. Weight kept below Aggregating's
                // since this is a mild spacing tendency, not active clustering.
                weight = Sociality == SocialityBaseline.GroupForming ? 0.25f
                       : Sociality == SocialityBaseline.Aggregating  ? 0.15f : 0.10f;
                return ComputeSocialAggregationBias();
        }
    }

    /// PairBonded: bias toward the single nearest same-community individual (a pair, not a crowd) —
    /// visually distinct from troop/school formations, which pull toward a many-member centroid.
    private Vector3 ComputePairBondBias()
    {
        if (_spawner == null) return Vector3.zero;
        float searchR = senseRadius * 2f;
        _spawner.QueryNearby(transform.position, searchR, _queryBuffer);
        AgentController nearest = null;
        float nearestDist = searchR;
        foreach (var other in _queryBuffer)
        {
            if (other == null || other == this || other.communityId != communityId) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist) { nearestDist = dist; nearest = other; }
        }
        if (nearest == null) return Vector3.zero;
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 toward = nearest.transform.position - transform.position;
        return (toward - Vector3.Dot(toward, normal) * normal).normalized;
    }

    /// MultiMemberTroop: broad cohesion + alignment, same shape as Era 1 schooling but over a wider
    /// radius — a stable multi-member group, not a tight synchronized shoal.
    private Vector3 ComputeTroopBias()
    {
        if (_spawner == null) return Vector3.zero;
        float searchR = senseRadius * 3.5f;
        Vector3 centroid = Vector3.zero, headingSum = Vector3.zero;
        int count = 0;
        _spawner.QueryNearby(transform.position, searchR, _queryBuffer);
        foreach (var other in _queryBuffer)
        {
            if (other == null || other == this || other.communityId != communityId) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > searchR || dist < 0.5f) continue;
            centroid += other.transform.position; headingSum += other._heading; count++;
        }
        if (count == 0) return Vector3.zero;
        centroid /= count;
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 towardCentroid = (centroid - transform.position);
        towardCentroid = (towardCentroid - Vector3.Dot(towardCentroid, normal) * normal).normalized;
        if (headingSum.sqrMagnitude > 0.001f)
        {
            Vector3 align = headingSum.normalized;
            align = (align - Vector3.Dot(align, normal) * normal).normalized;
            if (align.sqrMagnitude > 0.001f) return (towardCentroid * 0.6f + align * 0.4f).normalized;
        }
        return towardCentroid;
    }

    /// FissionFusion: alternates between joining a subgroup (troop-style cohesion) and going solo
    /// (zero bias, independent wander) every FissionFusionPeriod seconds — the group visibly splits
    /// and re-merges over time instead of holding one constant formation.
    private Vector3 ComputeFissionFusionBias(out float weight)
    {
        _fissionFusionTimer += Time.deltaTime;
        if (_fissionFusionTimer >= FissionFusionPeriod)
        {
            _fissionFusionTimer = 0f;
            _fissionFusionJoined = !_fissionFusionJoined;
        }
        if (!_fissionFusionJoined) { weight = 0f; return Vector3.zero; }
        weight = 0.25f;
        return ComputeTroopBias();
    }

    /// Territorial tether (orthogonal to social structure): once TerritorialityManager has settled
    /// this community into LooseRange or StrictSite, pull back toward the home site when outside its
    /// radius. Nomadic communities (including everyone before TerritorialityManager first evaluates)
    /// are completely unaffected — this never fires until a community has actually settled.
    private Vector3 ApplyTerritorialBias(Vector3 baseTangent)
    {
        if (TerritorialityManager.Instance == null) return baseTangent;
        TerritorialityRecord rec = TerritorialityManager.Instance.GetRecord(communityId);
        if (rec == null || rec.Strictness == TerritorialityStrictness.Nomadic) return baseTangent;

        float distFromHome = SphereSurface.SurfaceDistance(transform.position, rec.HomeSite, planetCenter, planetRadius);
        if (distFromHome <= rec.HomeRadius) return baseTangent;

        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 towardHome = rec.HomeSite - transform.position;
        towardHome = (towardHome - Vector3.Dot(towardHome, normal) * normal).normalized;
        if (towardHome.sqrMagnitude < 0.0001f) return baseTangent;

        // Pull strength scales with how far outside the range the agent has strayed, and is much
        // stronger for a StrictSite (colony workers stay close) than a LooseRange (loose ecosystem
        // range — a gentle nudge back, not a hard wall).
        float overshoot = Mathf.Clamp01((distFromHome - rec.HomeRadius) / rec.HomeRadius);
        float pullStrength = rec.Strictness == TerritorialityStrictness.StrictSite
            ? Mathf.Lerp(0.4f, 0.9f, overshoot)
            : Mathf.Lerp(0.1f, 0.4f, overshoot);
        return (baseTangent * (1f - pullStrength) + towardHome * pullStrength).normalized;
    }

    /// Aggregating/GroupForming sociality: bias heading toward the centroid of nearby
    /// same-community members. Blended with other drives — it nudges, not forces.
    /// Herd/group formation as an appearance+behavior output (appearance-generation-spec §4.2).
    /// The formation TOPOLOGY differs by social pattern: GroupForming lineages SCHOOL — tight
    /// cohesion plus heading alignment, so the group moves as an oriented shoal; Aggregating
    /// lineages merely CLUSTER (cohesion, no alignment) — a loose defensive huddle; Solitary
    /// lineages don't aggregate at all (separation keeps them dispersed with even spacing).
    private Vector3 ComputeSocialAggregationBias()
    {
        if (_spawner == null) return Vector3.zero;
        if (Sociality == SocialityBaseline.Solitary) return ComputeTerritorialDispersionBias();
        float searchR = senseRadius * (Sociality == SocialityBaseline.GroupForming ? 3f : 2f);
        Vector3 centroid = Vector3.zero;
        Vector3 headingSum = Vector3.zero;
        int count = 0;
        _spawner.QueryNearby(transform.position, searchR, _queryBuffer);
        foreach (var other in _queryBuffer)
        {
            if (other == null || other == this || other.communityId != communityId) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > searchR || dist < 0.5f) continue;
            centroid += other.transform.position;
            headingSum += other._heading;
            count++;
        }
        if (count == 0) return Vector3.zero;
        centroid /= count;
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 towardCentroid = (centroid - transform.position);
        towardCentroid = (towardCentroid - Vector3.Dot(towardCentroid, normal) * normal).normalized;

        // Schooling: blend cohesion with alignment to the group's mean heading (oriented shoal).
        if (Sociality == SocialityBaseline.GroupForming && headingSum.sqrMagnitude > 0.001f)
        {
            Vector3 align = headingSum.normalized;
            align = (align - Vector3.Dot(align, normal) * normal).normalized;
            if (align.sqrMagnitude > 0.001f)
                return (towardCentroid * 0.5f + align * 0.5f).normalized;
        }
        // Aggregating: loose defensive clustering — cohesion only, no alignment. Boldness further
        // differentiates position WITHIN the cluster (appearance-generation-spec §3.2 "Defensive
        // herd: perimeter-weighted density") — bold individuals pull less strongly toward the
        // centroid (a real defensive-vanguard/edge role), shy individuals pull harder toward it
        // (protected interior), rather than every member sitting at a uniform cohesion distance.
        float boldBias = Mathf.Clamp((boldness - 50f) / 50f, -1f, 1f);
        return towardCentroid * (1f - boldBias * 0.4f);
    }

    /// Solitary: appearance-generation-spec §3.2 "Territorial/dispersed: wide, roughly even
    /// spacing, minimal alignment" — active repulsion from nearby same-community individuals
    /// (maintaining distance), the third named formation signature alongside Schooling
    /// (GroupForming) and Defensive-herd (Aggregating). A tighter search radius than the clustering
    /// baselines: this only reacts to genuinely close neighbors (crowding), not the whole local
    /// population, since the point is even spacing, not group cohesion of any kind.
    private Vector3 ComputeTerritorialDispersionBias()
    {
        float searchR = senseRadius * 1.5f;
        Vector3 centroid = Vector3.zero;
        int count = 0;
        _spawner.QueryNearby(transform.position, searchR, _queryBuffer);
        foreach (var other in _queryBuffer)
        {
            if (other == null || other == this || other.communityId != communityId) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > searchR || dist < 0.001f) continue;
            centroid += other.transform.position;
            count++;
        }
        if (count == 0) return Vector3.zero;
        centroid /= count;
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 awayFromCentroid = transform.position - centroid;
        awayFromCentroid = (awayFromCentroid - Vector3.Dot(awayFromCentroid, normal) * normal).normalized;
        return awayFromCentroid;
    }

    // Simple comfort-biased wander for chemosynthetic organisms seeking richer substrate.
    private Vector3 ComputeWanderTangent()
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 current = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
        if (current.sqrMagnitude < 0.0001f) current = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        float randomTurn = Random.Range(-wanderTurnRate, wanderTurnRate) * Time.deltaTime;
        return (Quaternion.AngleAxis(randomTurn, normal) * current).normalized;
    }

    // Chemotaxis steering weight (0=pure drift, 1=straight to vent). Tunable.
    private const float VentSteeringWeight = 0.65f;

    /// Gradient-following movement toward the nearest hydrothermal vent, blended with
    /// random wander so the path is noisy (follows local concentration, not global GPS).
    /// Only called for motile chemosynthetic producers — creates real selection pressure
    /// on sensory/motor genes once those add to detection radius.
    private Vector3 ComputeVentSeekTangent()
    {
        Vector3 wander = ComputeWanderTangent();
        var ventMgr = HydrothermalVentManager.Instance;
        if (ventMgr == null || !ventMgr.NearestVent(transform.position, out Vector3 ventPos))
            return wander;

        // Already at or inside the vent — wander within the hotspot.
        if (Vector3.Distance(transform.position, ventPos) < 1.5f)
            return wander;

        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 toVent = ventPos - transform.position;
        Vector3 ventTangent = (toVent - Vector3.Dot(toVent, normal) * normal).normalized;
        if (ventTangent.sqrMagnitude < 0.001f) return wander;

        return Vector3.Slerp(wander, ventTangent, VentSteeringWeight).normalized;
    }

    /// Estimates whether switching to photosynthesis would actually be energy-positive at THIS
    /// agent's current position and world (star luminosity, orbit distance, atmosphere, depth) —
    /// used to gate photosynthesis adoption so lineages don't blindly convert on light-starved
    /// worlds (e.g. a near-zero-luminosity red dwarf) and strand themselves with no viable energy
    /// source. Uses a day-averaged solar term (0.5, half the planet is always lit) rather than the
    /// instantaneous value, since the adoption decision is a standing lineage choice, not a per-tick
    /// one. Returns true if projected net energy would be positive with a small safety margin.
    public bool IsPhotosynthesisLocallyViable()
    {
        const float dayAveragedSolar = 0.5f;
        float atmosTransparency = AtmosphereManager.Instance != null
            ? Mathf.Clamp01(2f / Mathf.Max(AtmosphereManager.Instance.PressureBar, 0.1f))
            : 1f;
        float photicAttenuation = Mathf.Exp(-PhoticExtinctionCoeff * _currentLiquidDepth);
        float photoSizeScale = Mathf.Pow(Mathf.Max(transform.localScale.x, 0.001f) / 0.05f, 0.80f);
        float projectedAcq = solarChargeRate * dayAveragedSolar * WorldSolarFluxFactor * atmosTransparency
                              * PhotoEfficiency * photoSizeScale * photicAttenuation;
        // Two conditions must BOTH hold to justify abandoning a working energy source:
        //  (1) photosynthesis covers demand with a 20% safety margin (absolute viability), and
        //  (2) photosynthesis actually OUT-YIELDS the chemosynthesis being given up, by 15% (relative
        //      viability). Condition (2) is the fix for the recurring death spiral: previously only
        //      (1) was checked, so on chemo-rich worlds (rich vents, high-SO2 atmospheres) organisms
        //      switched from a high chemo income to a barely-sufficient photo income and slowly
        //      starved — worst on hot worlds where Q10 inflates demand toward the photo ceiling.
        //  _lastChemoAbsorb is 0 for a lineage that has never run chemosynthesis (e.g. seeded photo),
        //  which makes (2) trivially true and correctly falls back to the demand-only test.
        float demand = ComputeDemand();
        bool coversDemand = projectedAcq >= demand * 1.2f;
        bool beatsChemo   = projectedAcq >= _lastChemoAbsorb * 1.15f;
        return coversDemand && beatsChemo;
    }

    /// Solar charging and reproduction logic shared between motile producers (UpdateProducer)
    /// and passive-drifting producers (UpdatePassiveDrift). Separated so energy metabolism
    /// works identically regardless of whether the organism can direct its own movement.
    private void UpdateProducerMetabolism()
    {
        DayNightCycle dayNight = DayNightCycle.Instance;
        float solar = 0f;
        if (dayNight != null)
        {
            Vector3 normal = (transform.position - planetCenter).normalized;
            solar = dayNight.SolarExposure(normal);
        }
        // Atmospheric transparency: thick atmospheres absorb stellar light before it reaches
        // surface-dwelling organisms (1 bar Earth-like = full; 2 bar = ~half; 0.5 bar = full).
        float atmosTransparency = AtmosphereManager.Instance != null
            ? Mathf.Clamp01(2f / Mathf.Max(AtmosphereManager.Instance.PressureBar, 0.1f))
            : 1f;
        float photoSizeScale = Mathf.Pow(Mathf.Max(transform.localScale.x, 0.001f) / 0.05f, 0.80f);
        // Beer-Lambert photic-zone attenuation: light falls off exponentially with liquid depth,
        // so shallow/surface phototrophs get full irradiance while deep-water ones are starved.
        // This is what differentiates the phototroph niche (shallow) from the chemotroph niche
        // (deep, near vents) instead of both competing for the same space.
        float photicAttenuation = Mathf.Exp(-PhoticExtinctionCoeff * _currentLiquidDepth);
        float photoAcq = solarChargeRate * solar * WorldSolarFluxFactor * atmosTransparency
                         * PhotoEfficiency * photoSizeScale * photicAttenuation;
        float demandNow = ComputeDemand();
        float photoNet = photoAcq - demandNow;
        NetEnergy = photoNet;
        if (!_photoMetabolismLogged) { _photoMetabolismLogged = true; Debug.Log($"[EvoSim] PhotoMeta: irradiance={solar * WorldSolarFluxFactor:F3} atten={photicAttenuation:F3} absorb={photoAcq:F4} demand={demandNow:F4} net={photoNet:F4} depth={_currentLiquidDepth:F3} eff={PhotoEfficiency:F3}"); }
        _solarEnergy += photoNet * Time.deltaTime;
        _solarEnergy = Mathf.Clamp(_solarEnergy, 0f, maxSolarEnergy);
        ApplyGrowthShrinkage(photoNet);

        if (_solarEnergy <= 0f)
            Die(EnergyDeathCause());

        if (_solarEnergy >= maxSolarEnergy * 0.90f)
        {
            _solarEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            _lifetimeEats++;
            if (_eatsSinceReproduction >= eatsToReproduce)
                TryReproduce();
        }
    }

    /// Chemosynthesis: absorb dissolved organic chemicals from the local substrate pool.
    /// This is the default pre-photosynthesis energy source for all early life.
    private void UpdateChemosyntheticMetabolism()
    {
        float poolNutrients = ChemicalNutrientPool.Sample(transform.position);
        // Hydrothermal vents supplement the background pool — take whichever source is richer.
        float ventEnergy = HydrothermalVentManager.Instance != null
            ? HydrothermalVentManager.Instance.GetVentEnergyAt(transform.position)
            : 0f;
        float nutrients = Mathf.Max(poolNutrients, ventEnergy);

        // Net energy: absorption (scales with local nutrient density) minus metabolic drain
        // (scales with all environmental stressors).
        // Absorb ∝ scale^0.80: larger surface area per West/Brown/Enquist; anchored to spawn scale so
        // early-era behavior is unchanged, later eras benefit from proportionally higher uptake.
        float chemoScale = Mathf.Max(transform.localScale.x, 0.001f);
        float chemoSizeScale = Mathf.Pow(chemoScale / 0.05f, 0.80f);
        float absorbRate = solarChargeRate * nutrients * ChemoEfficiency * GibbsFactor() * chemoSizeScale * ContestUptakeMultiplier();
        _lastChemoAbsorb = absorbRate; // cache for the photosynthesis-switch viability comparison
        float demand = ComputeDemand();
        float chemoNet = absorbRate - demand;
        NetEnergy = chemoNet;
        if (!_metabolismLogged) { _metabolismLogged = true; Debug.Log($"[EvoSim] ChemoMeta: nutrients={nutrients:F3} absorb={absorbRate:F4} demand={demand:F4} net={chemoNet:F4} eff={ChemoEfficiency:F3} gibbs={GibbsFactor():F3}"); }
        // Log chronic starvation as a reproduction blocker (~once per 10s per agent to avoid spam)
        if (chemoNet < 0f && _chemEnergy < maxSolarEnergy * 0.1f && Mathf.FloorToInt(Time.time * 0.1f) % 10 == 0)
            GameLog.LogReproFail(communityId, "Starving");
        _chemEnergy += chemoNet * Time.deltaTime;
        _chemEnergy  = Mathf.Clamp(_chemEnergy, 0f, maxSolarEnergy);
        ApplyGrowthShrinkage(chemoNet);

        // Deplete local chemical pool proportional to absorption.
        if (nutrients > 0.01f)
            ChemicalNutrientPool.Deplete(transform.position, absorbRate * 0.0008f * Time.deltaTime);

        if (_chemEnergy <= 0f)
            Die(EnergyDeathCause());

        if (_chemEnergy >= maxSolarEnergy * 0.90f)
        {
            _chemEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            _lifetimeEats++;
            if (_eatsSinceReproduction >= eatsToReproduce)
                TryReproduce();
        }
    }

    /// Mixotrophy: blends photosynthesis + heterotrophy at 70% efficiency each.
    /// Flexible fallback when either light or food is scarce, but never excels at either.
    private void UpdateMixotrophicMetabolism()
    {
        // Solar component (70% of phototrophic gain)
        DayNightCycle dayNight = DayNightCycle.Instance;
        float solar = 0f;
        if (dayNight != null)
        {
            Vector3 normal = (transform.position - planetCenter).normalized;
            solar = dayNight.SolarExposure(normal);
        }
        float atmosTransparency = AtmosphereManager.Instance != null
            ? Mathf.Clamp01(2f / Mathf.Max(AtmosphereManager.Instance.PressureBar, 0.1f)) : 1f;
        float mixoSizeScale = Mathf.Pow(Mathf.Max(transform.localScale.x, 0.001f) / 0.05f, 0.80f);
        float mixoPhotoAcq = solarChargeRate * 0.7f * solar * WorldSolarFluxFactor * atmosTransparency
                             * PhotoEfficiency * mixoSizeScale;
        float mixoNet = mixoPhotoAcq - ComputeDemand() * 0.5f;
        _solarEnergy += mixoNet * Time.deltaTime;
        _solarEnergy = Mathf.Clamp(_solarEnergy, 0f, maxSolarEnergy);
        ApplyGrowthShrinkage(mixoNet);

        // Osmotrophic component (70% of heterotrophic absorption)
        float nutrients = ChemicalNutrientPool.Sample(transform.position);
        float osmotrophySlowdown = Mathf.Clamp01(nutrients * 0.35f);
        if (nutrients > 0.01f)
            ChemicalNutrientPool.Deplete(transform.position, 0.00005f * 0.7f * Time.deltaTime);
        _timeSinceLastMeal += Time.deltaTime * GetNonClimateFitnessMultipliers() * 0.5f * (1f - osmotrophySlowdown);

        // Survive from either source
        bool canSurviveSolar = _solarEnergy > 0f;
        bool canSurviveOsmo  = _timeSinceLastMeal < starvationTime;
        if (!canSurviveSolar && !canSurviveOsmo) { Die(EnergyDeathCause()); return; }

        // Reproduce when solar is full
        if (_solarEnergy >= maxSolarEnergy * 0.90f)
        {
            _solarEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            _lifetimeEats++;
            if (_eatsSinceReproduction >= eatsToReproduce)
                TryReproduce();
        }
    }

    /// Osmotrophy: heterotrophs absorb dissolved organics passively at low efficiency.
    /// Called only for non-motile heterotrophs; motile heterotrophs get this as Layer 1
    /// of UpdateConsumer() instead, so no double-drain occurs.
    private void UpdateOsmotrophy()
    {
        float nutrients = ChemicalNutrientPool.Sample(transform.position);
        float osmotrophySlowdown = Mathf.Clamp01(nutrients * 0.35f);
        float absorbed = 0f;
        if (nutrients > 0.01f)
        {
            float drain = 0.00005f * Time.deltaTime;
            ChemicalNutrientPool.Deplete(transform.position, drain);
            // Convert pool drain into chemical energy. Heterotrophs are less efficient than
            // chemosynthetics at extracting energy from dissolved organics.
            absorbed = nutrients * ChemoEfficiency * 0.3f * Time.deltaTime;
        }
        float demand = ComputeDemand();
        NetEnergy = absorbed / Mathf.Max(Time.deltaTime, 1e-4f) - demand; // absorbed is already scaled by dt; normalize back to a per-second rate to match the other metabolism paths
        _chemEnergy = Mathf.Clamp(_chemEnergy + absorbed - demand * Time.deltaTime, 0f, maxSolarEnergy);

        if (_chemEnergy <= 0f) { Die(EnergyDeathCause()); return; }

        // Reproduce when energy reserve is full — same threshold as chemosynthetics.
        if (_chemEnergy >= maxSolarEnergy * 0.90f)
        {
            _chemEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            _lifetimeEats++;
            if (_eatsSinceReproduction >= eatsToReproduce)
                TryReproduce();
        }
    }

    /// Aggregate of all independent fitness pressures. Used wherever a single drain
    /// multiplier is needed rather than one pressure at a time.
    // ── Activity Budget (spec §4) ────────────────────────────────────────────────────────
    // Shared pool with competing draws. Maintenance is a protected floor; non-Maintenance
    // categories are reduced proportionally if their sum would exceed the remaining budget.
    // Returns a total multiplier on BMR that replaces the old flat "activity multiplier."

    // Fraction of BMR budget allocated to each category (all unitless, sum is the total multiplier).
    private const float ActivityMaintenance  = 0.40f; // protected floor — always paid
    private const float ActivityLocoMax      = 0.45f; // active-mobile + chasing prey
    private const float ActivityVigilanceMax = 0.20f; // scales with PredationPressure
    private const float ActivityContestMax   = 0.15f; // era3-primitives-spec §2.1: interference-competition draw
    // Era 2 additions (gated by gene events — hooks only, filled when events land):
    private const float ActivityCognitiveMax = 0.25f; // Cognitive/Neural (NeuralComplexity gate)
    private const float ActivitySocialMax    = 0.10f; // Social/Coordination (Sociality gate)

    // Cost-of-Transport multiplier per locomotion medium (Tucker/Taylor COT scaling):
    //   Swimming=1.0 (buoyancy offsets weight), Flying=1.4 (fast/unit-distance despite high power),
    //   Walking/Gliding=1.8 (weight-bearing adds cost).
    private float LocomotionCOTMultiplier() => LocomotionMedium switch
    {
        LocomotionMedium.Aquatic    => 1.0f,
        LocomotionMedium.Aerial     => 1.4f,
        LocomotionMedium.Gliding    => 1.8f,
        LocomotionMedium.Terrestrial=> 1.8f,
        _                           => 1.0f, // Sessile — no locomotion draw anyway
    };

    // Elevation climb cost added to demand each tick (mgh, positive only for ascent).
    // Aerial organisms gain altitude via COT, not discrete mgh events — excluded here.
    private float ComputeClimbCost()
    {
        if (!HasMotility || LocomotionMedium == LocomotionMedium.Aerial) return 0f;
        float currentElev = ClimateManager.GetElevation(transform.position);
        if (_lastElevation < 0f) { _lastElevation = currentElev; return 0f; }
        float deltaElev = Mathf.Max(0f, currentElev - _lastElevation);
        _lastElevation = currentElev;
        return _currentMass * 9.8f * deltaElev * TerrainHeightWorldUnits * ClimbEfficiencyPenalty;
    }

    private float ResolveActivityBudget()
    {
        // --- Locomotion/Foraging draw (scaled by medium Cost-of-Transport) ---
        float locoDraw = 0f;
        if (HasMotility)
        {
            float baseLocoDraw = (!IsProducer) ? ActivityLocoMax               // predators
                               : (Metabolism == MetabolismType.Phototrophic)   ? ActivityLocoMax * 0.2f
                               : ActivityLocoMax * 0.3f;
            locoDraw = baseLocoDraw * LocomotionCOTMultiplier();
        }

        // --- Vigilance/Predator-avoidance draw (scales with local PredationPressure) ---
        float vigilanceDraw = ActivityVigilanceMax * PredationPressure;

        // --- Era 2 cognitive draw (gated by NeuralComplexity trait) ---
        float cognitiveDraw = 0f;
        if (Era2Manager.Instance != null && Era2Manager.Instance.IsActive)
            cognitiveDraw = ActivityCognitiveMax * Mathf.Clamp01((float)NeuralComplexity / 3f);

        // --- Era 2 social draw (gated by Sociality trait) ---
        float socialDraw = 0f;
        if (Era2Manager.Instance != null && Era2Manager.Instance.IsActive)
            socialDraw = ActivitySocialMax * Mathf.Clamp01((float)Sociality / 3f);

        // --- Interference-competition draw (era3-primitives-spec §2.1) — only actually costly when
        // there's something worth contesting: a patchy/defensible resource under real scarcity. A
        // high-contestPropensity organism on an abundant world pays this for nothing, which is exactly
        // the "selected against when resources are dispersed" pressure the spec calls for. The
        // matching BENEFIT lives in ContestUptakeMultiplier(), applied at the nutrient-uptake sites. ---
        float contestDraw = ActivityContestMax * Mathf.Clamp01(contestPropensity / 100f) * ResourceScarcity;

        // --- Proportional reduction if non-Maintenance sum exceeds remaining budget ---
        float nonMaintTotal = locoDraw + vigilanceDraw + contestDraw + cognitiveDraw + socialDraw;
        float remaining = 1f - ActivityMaintenance; // budget available to non-Maintenance
        float scale = (nonMaintTotal > remaining) ? remaining / nonMaintTotal : 1f;

        return ActivityMaintenance + nonMaintTotal * scale;
    }

    /// era3-primitives-spec §2.1: the payoff side of contestPropensity — interference competitors win
    /// by DENYING access, not consuming faster, so the bonus only exists when the resource is genuinely
    /// contested (high local scarcity). A high-contest organism on a low-scarcity world gets no bonus
    /// here but still pays the Activity Budget draw above — real selection against it in that regime.
    private float ContestUptakeMultiplier()
    {
        float contest01 = Mathf.Clamp01(contestPropensity / 100f);
        return 1f + (contest01 - 0.5f) * ResourceScarcity * 0.6f; // ±30% at trait extremes under max scarcity
    }

    // ── Kleiber BMR demand (§3 energy spec) ─────────────────────────────────────────────
    // k constant per backbone chemistry (Kleiber's Law: demand = K * agentScale^0.75).
    // Anchored so demand at spawn scale (0.05) matches the prior mass-based calibration
    // (K_new = K_mass * 0.05^1.5 ≈ K_mass * 0.01118). Bigger organisms cost proportionally
    // less per unit mass — sublinear scaling as empirically established by West/Brown/Enquist.
    private static readonly float[] KleiberK = { 0.067f, 0.078f, 0.073f, 0.062f, 0.084f, 0.067f, 0.056f, 0.073f };
    // Q10 per backbone (metabolic rate doubling per 10°C — real ectotherm range 2–3).
    private static readonly float[] BackboneQ10 = { 2.0f, 2.5f, 2.2f, 2.0f, 3.0f, 2.5f, 2.0f, 2.2f };
    // Reference temperature per backbone (°C, from solvent-tolerance optimum tables).
    private static readonly float[] BackboneRefTemp = { 20f, 15f, 25f, 30f, 10f, 20f, 35f, 20f };

    // ── Growth / shrinkage (spec §8) ────────────────────────────────────────────────────
    /// Called each tick after energy reserve is updated. Positive net surplus grows mass;
    /// sustained negative shrinks it. Visual scale tracks mass so the change is visible.
    private void ApplyGrowthShrinkage(float netBalance)
    {
        float massChange = 0f;
        if (netBalance > 0f)
            massChange =  MassGrowthRate * _spawnMass * Time.deltaTime;
        else if (netBalance < 0f)
            massChange = -MassShrinkRate * _spawnMass * Time.deltaTime;

        if (massChange == 0f) return;

        float eraMaxMass = _spawnMass * 2.5f; // don't grow beyond 2.5× spawn mass within an era
        _currentMass = Mathf.Clamp(_currentMass + massChange, _spawnMass * MassViabilityFloor, eraMaxMass);

        // Death if mass falls below viability floor.
        if (_currentMass <= _spawnMass * MassViabilityFloor)
        {
            Die(DeathCause.MassViabilityFloor);
            return;
        }

        RefreshVisualScale();
    }

    /// Sets the organism's visual size from three factors: the era's baseline scale, a species
    /// size class read from its strength trait (so a big tough species is visibly larger than a
    /// small frail one), and its individual growth state (current vs. spawn mass). Called every
    /// tick so size always tracks the organism rather than every agent being one uniform era size.
    private void RefreshVisualScale()
    {
        float eraScale = EraManager.Instance != null
            ? EraManager.Instance.AgentTargetScale
            : Mathf.Pow(Mathf.Max(_spawnMass, 1e-6f), 1f / 3f);

        // Species size class from strength: weak → ~0.6×, average → 1×, strong → ~1.6× (at influence 0.5).
        float sizeClass = 1f + (strengthTrait / 100f - 0.5f) * 2f * sizeTraitInfluence;

        // Individual growth: linear scale ∝ cbrt(mass), 1.0 at spawn mass up to ~1.36 at era max.
        float growth = _spawnMass > 0f
            ? Mathf.Pow(Mathf.Max(_currentMass, 1e-6f) / _spawnMass, 1f / 3f)
            : 1f;

        transform.localScale = Vector3.one * Mathf.Max(0.001f, eraScale * sizeClass * growth);
    }

    /// Deflects a MOTILE organism away from a step that would carry it across the shoreline into its
    /// non-viable medium (aquatic onto land, or terrestrial into liquid). Most lineages can't freely
    /// cross the sea/land boundary — they turn back at the water's edge instead of wandering into a
    /// lethal medium. Strong but not absolute, so a rare amphibious crossing is still possible.
    private Vector3 ApplyMediumBoundary(Vector3 proposedPos)
    {
        var fluid = FluidDynamicsManager.Instance;
        if (fluid == null) return proposedPos;
        bool proposedSubmerged = fluid.IsSubmerged(proposedPos);
        bool proposedViable = _isAquatic ? proposedSubmerged : !proposedSubmerged;
        if (proposedViable) return proposedPos;
        _heading = -_heading; // turn back from the shoreline
        return Vector3.Lerp(transform.position, proposedPos, MediumCrossBias);
    }

    // ── Acquisition surface-area helpers (spec §5.3, §6) ────────────────────────────────
    // Photosynthetic surface scales as mass^(2/3) — surface-to-volume law.
    // Shape modifier: flat/sessile organisms get a bonus; compact/spherical get none.
    private float PhotosyntheticSurfaceArea()
    {
        float shapeBonus = HasMotility ? 1.0f : 1.3f; // sessile = broader surface exposure
        return Mathf.Pow(Mathf.Max(_currentMass, 0.0001f), 2f / 3f) * shapeBonus;
    }

    // Uptake surface for chemosynthesis — same scaling, no shape bonus.
    private float UptakeSurfaceArea()
    {
        return Mathf.Pow(Mathf.Max(_currentMass, 0.0001f), 2f / 3f);
    }

    // Gibbs free energy scalar: a lineage-level constant that modulates raw vent flux.
    // In the absence of a per-vent reaction-type field, we derive a per-organism Gibbs
    // factor from backbone chemistry (proxy for which reaction pathway the lineage uses).
    // Range [0.10, 1.00] — sulfide-oxidising lineages get the highest yield.
    // When VentByproductKind is added to HydrothermalVentManager (§6.1 follow-up), replace
    // this with a per-vent lookup table keyed to reaction pair.
    private float GibbsFactor()
    {
        float backboneFactor = Backbone switch
        {
            BackboneElement.Sulfur      => 1.00f, // H₂S oxidation — highest ΔG
            BackboneElement.Carbon      => 0.30f, // H₂ oxidation proxy
            BackboneElement.Tin        => 0.20f, // iron-analog oxidation
            BackboneElement.Silicon     => 0.15f, // manganese-analog
            BackboneElement.Phosphorus  => 0.18f, // anammox-analog
            BackboneElement.Nitrogen    => 0.10f, // methanogenesis — lowest ΔG
            _                           => 0.20f,
        };
        // Respiration-tier multiplier: primitive/undifferentiated metabolism is deliberately worse
        // than any specialized pathway (Issue 1); a real evolved anaerobic specialization reaches
        // full designed efficiency (Issue 2); aerobic respiration yields substantially more once
        // unlocked — real aerobic respiration yields roughly an order of magnitude more ATP per
        // glucose than fermentation, which is what historically drove it to dominance (Issue 3).
        // All TUNABLE — first-pass calibration, not derived from a specific target curve.
        float tierFactor = RespirationTier switch
        {
            RespirationTier.Primitive           => PrimitiveMetabolismPenalty,
            RespirationTier.SpecializedAnaerobic => 1.00f,
            RespirationTier.Aerobic             => AerobicYieldMultiplier,
            _                                    => 1.00f,
        };
        return backboneFactor * tierFactor;
    }

    // ── Respiration evolutionary sequence (Issues 1-3) ────────────────────────────────────
    public RespirationTier RespirationTier { get; private set; } = RespirationTier.Primitive;
    private const float PrimitiveMetabolismPenalty = 0.40f; // TUNABLE — deliberately worse than any specialized pathway
    private const float AerobicYieldMultiplier      = 3.00f; // TUNABLE — real advantage that should drive adoption once unlocked
    // Three-stage O2 progression, deliberately ORDERED so the escape route opens well before the
    // threat gets dangerous — real lineages had genuine time to adapt to the GOE, not a synchronized
    // instant-death moment with the exit locked. Getting this ordering wrong (unlock threshold ==
    // toxicity-max threshold) was a confirmed real bug: it produced a total, simultaneous population
    // wipeout the moment O2 appeared, the opposite of the real outcome (life SURVIVED the GOE).
    public const float AerobicUnlockO2Threshold     = 0.008f; // TUNABLE — AerobicRespiration becomes reachable here
    private const float ToxicityStartFraction       = 0.012f; // TUNABLE — toxicity is negligible below this (real margin after the escape opens)
    private const float ToxicityFullFraction         = 0.020f; // TUNABLE — toxicity reaches full severity here, well after escape was available

    /// Issue 2: evolve into a real anaerobic pathway (Methanogenesis, SulfateReduction, etc.) keyed
    /// to whichever substrate this agent's lineage actually has locally — an OR-gate branch, not a
    /// single hardcoded swap target. Distinct lineages under different local substrate availability
    /// can genuinely diverge, since each agent's breathed/expelled pair is its own field (already
    /// inherited/mutable per-agent, not a single world-wide constant).
    public void SpecializeAnaerobic(string breathedGas, string expelledGas, string pathwayGeneId)
    {
        if (RespirationTier != RespirationTier.Primitive) return; // already specialized or aerobic
        _breathedGasName = breathedGas;
        _expelledGasName = expelledGas;
        RespirationTier = RespirationTier.SpecializedAnaerobic;
        if (AtmosphereManager.Instance != null)
            _idealGasMix = AtmosphereManager.Instance.SnapshotMix();
        Debug.Log($"[Biochemistry] {name} specialized into {pathwayGeneId}: breathes {_breathedGasName}, expels {_expelledGasName} (tier=SpecializedAnaerobic).");
    }

    /// Issue 3: late unlock, gated on atmospheric O2 in GeneCatalog's IsEligible (not here) — this
    /// just applies the switch once the gate has already been satisfied. Real energetic advantage
    /// (AerobicYieldMultiplier) is what should drive it to dominance once available, mirroring the
    /// real Great Oxidation Event / aerobic respiration's actual historical trajectory.
    public void BecomeAerobic()
    {
        if (RespirationTier == RespirationTier.Aerobic) return;
        _breathedGasName = "O2";
        _expelledGasName = "CO2";
        RespirationTier = RespirationTier.Aerobic;
        if (AtmosphereManager.Instance != null)
            _idealGasMix = AtmosphereManager.Instance.SnapshotMix();
        Debug.Log($"[Biochemistry] {name} evolved AerobicRespiration: breathes O2, expels CO2 (tier=Aerobic, yield×{AerobicYieldMultiplier:F1}).");
    }

    /// Oxygen toxicity (Issue 3): as atmospheric O2 rises toward/past the aerobic unlock threshold,
    /// organisms that haven't adapted (no AerobicRespiration, tier still Primitive/SpecializedAnaerobic)
    /// pay a real, escalating cost — the Great Oxidation Event was a mass-extinction pressure for
    /// existing anaerobic life, not just a free new opportunity sitting unused. Reuses the same
    /// quadratic-ramp pattern already established for breathed-gas deficit (CheckGasSurvival's
    /// GAS_DEFICIT), per the spec's explicit request to reuse that cost model rather than invent a
    /// second one. TUNABLE.
    private float ComputeOxygenToxicityCost()
    {
        if (RespirationTier == RespirationTier.Aerobic) return 0f; // adapted — no toxicity cost
        // Retreated to anoxic refuges (the pre-existing EfficientRespiration "Path B" choice at the
        // Great Gas Event) — by definition no longer exposed to atmospheric O2, so no toxicity cost.
        // This is also the real escape route for backbones where O2 is outright lethal (Silicon/
        // Germanium/Tin/Boron/Phosphorus — see WouldGasBeLethal) and can never take the aerobic path.
        if (IsAnoxicRefugeLineage) return 0f;
        if (AtmosphereManager.Instance == null) return 0f;
        float o2Frac = AtmosphereManager.Instance.GetFraction("O2");
        if (o2Frac <= ToxicityStartFraction) return 0f; // negligible trace O2 — real toxicity pressure only builds as it approaches crisis level, not from any nonzero amount
        float severity = Mathf.Clamp01((o2Frac - ToxicityStartFraction) / (ToxicityFullFraction - ToxicityStartFraction));
        return severity * severity * 0.40f; // same deficit²×0.40 shape as GAS_DEFICIT drain
    }

    /// Metabolic demand per second: Kleiber BMR × Q10 temperature scaling × surviving fitness multipliers.
    /// Replaces the old flat `solarDrainRate × GetAllFitnessMultipliers()` drain.
    public float ComputeDemand()
    {
        int bIdx = Mathf.Clamp((int)Backbone, 0, KleiberK.Length - 1);
        // Kleiber: demand ∝ agentScale^0.75 (not mass^2.25 — scale is the linear dimension)
        float agentScaleDemand = Mathf.Max(transform.localScale.x, 0.001f);
        float bmr = KleiberK[bIdx] * Mathf.Pow(agentScaleDemand, 0.75f);

        float localTemp = ClimateManager.GetTemperature(transform.position);
        float q10 = BackboneQ10[bIdx];
        float refTemp = BackboneRefTemp[bIdx];
        float q10Mult = Mathf.Pow(q10, (localTemp - refTemp) / 10f);
        q10Mult = Mathf.Clamp(q10Mult, 0.25f, 4f); // prevent runaway at extreme temperatures

        // Activity Budget replaces flat activity multiplier; non-climate fitness penalties stack on top.
        // Climb cost (mgh) is additive — it is a discrete physics cost, not a budget fraction.
        // Oxygen toxicity (Issue 3) is likewise additive, same treatment as climb cost — a real
        // physical burden on unadapted organisms as O2 rises, not a multiplicative budget fraction.
        float demand = bmr * q10Mult * ResolveActivityBudget() * GetNonClimateFitnessMultipliers()
                       + ComputeClimbCost() + ComputeOxygenToxicityCost();

        // Emergency dormancy (FounderSurvivalManager): metabolic shutdown bought at the cost of
        // reproduction, giving a founder lineage on the brink of extinction time to stabilize
        // instead of finishing the death spiral. See _dormancyTimer for activation.
        if (_dormancyTimer > 0f) demand *= DormancyDemandMultiplier;

        return demand;
    }

    // ── Emergency dormancy (founder-crisis survival mechanic) ─────────────────────────────
    private float _dormancyTimer;
    private const float DormancyDemandMultiplier = 0.25f; // metabolic shutdown, TUNABLE
    public bool IsDormant => _dormancyTimer > 0f;

    /// Puts this agent into emergency dormancy for `seconds`: demand drops sharply (metabolic
    /// shutdown) but reproduction is suppressed for the same window (see TryReproduce) — the
    /// lineage survives at reduced numbers/growth rather than at full strength. Re-applying while
    /// already dormant extends the timer rather than stacking the multiplier.
    public void EnterDormancy(float seconds) => _dormancyTimer = Mathf.Max(_dormancyTimer, seconds);

    /// All fitness multipliers EXCEPT climate-starvation — that is replaced by Q10 scaling above.
    private float GetNonClimateFitnessMultipliers()
    {
        return GetAtmosphericFitnessMultiplier()
             * GetStressFitnessMultiplier()
             * GetUVFitnessMultiplier()
             * GetPressureFitnessMultiplier()
             * GetThermalCycleFitnessMultiplier()
             * GetMediumFitnessMultiplier()
             * GetWrongLiquidMultiplier()
             * GetStrengthConstitutionBonus();
    }

    /// Legacy multiplier stack — retained for callers that still need it pending full migration.
    /// Climate-starvation term is REMOVED here to avoid double-counting with Q10.
    private float GetAllFitnessMultipliers() => GetNonClimateFitnessMultipliers();

    /// Penalty for being submerged in a different liquid chemistry than this lineage evolved in.
    /// Models osmotic stress, incompatible pH, wrong dissolved gases for the organism's biochemistry.
    private float GetWrongLiquidMultiplier()
    {
        if (string.IsNullOrEmpty(_requiredLiquidKind)) return 1f;
        if (CurrentMedium != HabitatMedium.Sea) return 1f;
        var curLiquid = FluidDynamicsManager.Instance?.CurrentLiquid;
        if (curLiquid == null || curLiquid.Name == _requiredLiquidKind) return 1f;
        return 3.5f;
    }

    /// Asphyxiation check. If the atmosphere's breathable gas fraction drops below the
    /// lineage's minimum, drain ramps up proportional to deficit. Below 2% → instant death.
    private void CheckGasSurvival()
    {
        if (AtmosphereManager.Instance == null || string.IsNullOrEmpty(_breathedGasName)) return;

        float fraction = 0f;
        foreach (var gas in AtmosphereManager.Instance.Gases)
            if (gas.Name == _breathedGasName) { fraction = gas.Fraction; break; }

        if (fraction >= _minBreathableFraction) return;
        if (fraction < 0.02f) fraction = 0f;

        // Extra metabolic drain that ramps QUADRATICALLY with how far below the minimum the gas
        // has dropped. Mild depletion (deficit≈0.5 → ~0.10/s) is survivable and mostly offset by
        // metabolism, but severe depletion (gas→0, deficit→1 → ~0.40/s) outruns typical absorb
        // (~0.165/s), forcing net-negative energy. Previously this was a flat 0.15/s that a healthy
        // producer fully offset — so a total gas collapse (e.g. H2S→0%) had no observable effect and
        // exerted no selection pressure to adapt. The quadratic ramp makes non-adaptation genuinely
        // costly under crisis while leaving ordinary drift survivable, which is what turns
        // respiration specialization (Methanogenesis/SulfurRespiration/etc.) from optional flavor
        // into a real evolutionary escape.
        float deficit     = 1f - fraction / _minBreathableFraction;
        float extraDrain  = deficit * deficit * 0.40f * Time.deltaTime;

        if (!_gasDeficitLogged && deficit > 0.5f)
        {
            _gasDeficitLogged = true;
            Debug.Log($"[EvoSim] GAS_DEFICIT agent={name} community={communityId} breathes={_breathedGasName} " +
                      $"fraction={fraction:F3} minBreathable={_minBreathableFraction:F3} deficit={deficit:F2} " +
                      $"drain/s={deficit * deficit * 0.40f:F3} — adapt (respiration specialization) or decline.");
        }

        if (!IsProducer)
            _timeSinceLastMeal += extraDrain;
        else
        {
            _solarEnergy -= extraDrain;
            _chemEnergy  -= extraDrain;
        }
    }
    private bool _gasDeficitLogged;

    /// Backbone-dependent lethal gas check. Some atmospheres are structurally destructive
    /// to non-carbon backbones: O2 fossilizes Si/Ge/Sn, ignites B/P; F2 destroys C/B/N/P/S.
    /// Checked once per second (throttled by _backboneGasCheckTimer) to keep per-frame cost low.
    private float _backboneGasCheckTimer;
    private void CheckBackboneGasTolerance()
    {
        _backboneGasCheckTimer -= Time.deltaTime;
        if (_backboneGasCheckTimer > 0f || AtmosphereManager.Instance == null) return;
        _backboneGasCheckTimer = 1f; // recheck every second

        foreach (var gas in AtmosphereManager.Instance.Gases)
        {
            if (gas.Fraction < 0.02f) continue; // trace amounts are not acutely lethal
            bool lethal = IsGasLethalForBackbone(gas.Name, Backbone);
            if (!lethal) continue;

            // Lethal gas: drain energy fast (less harsh than instant death to allow some
            // lineage to survive via speciation to a different atmosphere before it climbs too high).
            float drain = gas.Fraction * 2f * Time.deltaTime * 1f; // 1-second check, so × 1 not deltaTime
            if (!IsProducer)
                _timeSinceLastMeal += drain;
            else
            {
                _solarEnergy -= drain;
                _chemEnergy  -= drain;
            }
        }
    }

    /// Public wrapper so gene eligibility checks (GeneCatalog) can veto a respiration specialization
    /// that would have the organism breathe something lethal to its own backbone chemistry — e.g. F2
    /// is lethal to Carbon, O2 is lethal to Silicon/Germanium/Tin/Boron/Phosphorus. Without this, a
    /// specialization gene could offer a fatal substrate as if it were a fitness win.
    public bool WouldGasBeLethal(string gasName) => IsGasLethalForBackbone(gasName, Backbone);

    private static bool IsGasLethalForBackbone(string gasName, BackboneElement backbone)
    {
        switch (gasName)
        {
            case "O2":
                return backbone == BackboneElement.Silicon   || backbone == BackboneElement.Germanium
                    || backbone == BackboneElement.Tin       || backbone == BackboneElement.Boron
                    || backbone == BackboneElement.Phosphorus;
            case "F2":
                return backbone == BackboneElement.Carbon    || backbone == BackboneElement.Boron
                    || backbone == BackboneElement.Nitrogen  || backbone == BackboneElement.Phosphorus
                    || backbone == BackboneElement.Sulfur;
            case "H2O":
                return backbone == BackboneElement.Silicon   || backbone == BackboneElement.Germanium
                    || backbone == BackboneElement.Tin       || backbone == BackboneElement.Boron
                    || backbone == BackboneElement.Phosphorus;
            case "NH3":
                return backbone == BackboneElement.Carbon; // corrosive at high conc for carbon
            case "PH3":
                return backbone == BackboneElement.Carbon; // disrupts Fe-S enzymes
            case "B2H6":
                return backbone == BackboneElement.Carbon; // highly toxic to organic tissue
            default:
                return false;
        }
    }

    /// Strong organisms have a more robust constitution: each 10 points of strength above
    /// 50 reduces all-multiplier drain by 2%, to a max 10% reduction at strength 100.
    /// Weakness below 50 adds a slight penalty (fragile physique). This makes strength
    /// matter even outside direct combat, without overshadowing specialized traits.
    private float GetStrengthConstitutionBonus()
    {
        float excess = (strengthTrait - 50f) / 50f; // -1 at str=0, 0 at str=50, +1 at str=100
        return Mathf.Clamp(1f - excess * 0.10f, 0.90f, 1.10f);
    }

    /// Medium mismatch penalty: aquatic organisms beached on land suffer desiccation;
    /// future terrestrial organisms submerged in liquid would drown (reverse penalty).
    /// In early eras (<=1) all life is aquatic — stranding on land is lethal over time.
    /// This naturally creates selection pressure to stay in or near the liquid.
    private float GetMediumFitnessMultiplier()
    {
        bool beached  = _isAquatic  && CurrentMedium == HabitatMedium.Land;
        bool drowning = !_isAquatic && CurrentMedium == HabitatMedium.Sea;
        if (!beached && !drowning) return 1f;
        // Mild energy penalty while in wrong medium — actual death handled by
        // ApplyMediumMismatchDrain's tolerance-band exposure timer (addendum §2.4).
        float fraction = Mathf.Clamp01(_mediumMismatchExposure / MismatchBaseDeadlineSecs);
        return Mathf.Lerp(1.2f, 2.0f, fraction); // ramps from mild to moderate as exposure grows
    }

    /// Sexual isolation extinction: if a sexual organism has no opposite-sex community member
    /// for longer than 3× starvationTime, the lineage is reproductively extinct and it dies.
    /// Throttled to scan the active agent list only every 5 seconds so the O(N) cost is trivial.
    private void CheckSexualIsolation()
    {
        if (!IsSexual || CanChangeSex) return; // hermaphrodites self-mate; not isolated
        _noMateCheckTimer -= Time.deltaTime;
        if (_noMateCheckTimer <= 0f)
        {
            _noMateCheckTimer = 5f;
            BiologicalSex opposite = Sex == BiologicalSex.Male ? BiologicalSex.Female : BiologicalSex.Male;
            bool mateExists = false;
            if (_spawner != null)
                foreach (var a in _spawner.ActiveAgents)
                {
                    if (a == null || a == this || a.communityId != communityId) continue;
                    if (a.Sex == opposite) { mateExists = true; break; }
                }
            if (!mateExists)
                _noMateTimer += 5f;
            else
                _noMateTimer = 0f;
        }
        if (_noMateTimer >= starvationTime * 3f) { Die(DeathCause.SexualIsolation); }
    }

    // ── Medium mismatch — tolerance-band survivable-duration (addendum §2.4) ────────────
    // Physiological failure, NOT a cost multiplier. Models gill collapse / desiccation.
    // Accumulates exposure time; death when exposure exceeds the hardiness-scaled ceiling.
    // Distinct from the Activity Budget energy pathway — this fires regardless of movement.
    private float _mediumMismatchExposure; // seconds in wrong medium
    private const float MismatchBaseDeadlineSecs = 30f; // base survivable duration at hardiness=50
    private float _lastElevation = -1f; // normalized [0,1]; -1 = uninitialized
    private const float ClimbEfficiencyPenalty = 1.3f; // biological inefficiency of uphill movement
    private const float TerrainHeightWorldUnits = 5f; // world-unit scale for 1 unit of normalized elevation

    private void ApplyMediumMismatchDrain()
    {
        bool beached  = _isAquatic  && CurrentMedium == HabitatMedium.Land;
        bool drowning = !_isAquatic && CurrentMedium == HabitatMedium.Sea;

        if (!beached && !drowning)
        {
            // Recovery: exposure timer resets while in correct medium.
            _mediumMismatchExposure = Mathf.Max(0f, _mediumMismatchExposure - Time.deltaTime * 2f);
            return;
        }

        _mediumMismatchExposure += Time.deltaTime;

        // Survivable duration scales with hardiness: high hardiness = longer tolerance window.
        // Range: hardiness=0 → 15s ceiling; hardiness=100 → 45s; hardiness=200 → 90s.
        float ceiling = MismatchBaseDeadlineSecs * (0.5f + hardinessTrait / 100f);
        if (_mediumMismatchExposure >= ceiling)
            Die(DeathCause.MediumMismatch);
    }

    /// UV radiation damage. Day-side organisms without UV tolerance take faster metabolic
    /// drain. Pre-GOE (no ozone) this is the primary selective pressure for deep-water life.
    /// Liquid attenuates UV — submerged organisms receive only a fraction of surface UV.
    /// Attenuation depends on the liquid's density (denser/more opaque = more shielding):
    ///   Water (1000 kg/m³): 85% attenuation — deep ocean is nearly UV-free.
    ///   Ammonia (680): 75%, Hydrocarbon (450): 60%, MoltenSulfur (1820): 95%.
    private float GetUVFitnessMultiplier()
    {
        float uv = UVManager.GetUVExposure(transform.position, planetCenter, DayNightCycle.Instance);
        if (uv < 0.01f) return 1f; // night side: no UV

        // Liquid UV shielding: attenuate surface UV by density-derived factor when submerged.
        if (CurrentMedium == HabitatMedium.Sea && FluidDynamicsManager.Instance?.CurrentLiquid != null)
        {
            float density = FluidDynamicsManager.Instance.CurrentLiquid.DensityKgM3;
            // Attenuation 0.6-0.95 mapped from density range 450-1820 kg/m³.
            float attenuation = Mathf.Lerp(0.60f, 0.95f, Mathf.InverseLerp(450f, 1820f, density));
            uv *= (1f - attenuation);
        }

        // uvTolerance 0 = deep-water specialist (steep UV penalty); 100 = surface-adapted (shallow).
        float sensitivity = Mathf.Lerp(2.8f, 0.15f, uvTolerance / 100f);
        return Mathf.Clamp(1f + uv * sensitivity, 1f, 5f);
    }

    /// Atmospheric pressure discomfort. Organisms adapted to a different pressure band suffer.
    private float GetPressureFitnessMultiplier()
    {
        if (AtmosphereManager.Instance == null) return 1f;
        float current = AtmosphereManager.Instance.PressureBar;
        float mismatch = Mathf.Abs(current - pressurePreference) / Mathf.Max(current, 0.1f);
        float sensitivity = Mathf.Lerp(1.6f, 0.25f, pressureTolerance / 100f);
        return Mathf.Clamp(1f + mismatch * sensitivity, 1f, 3f);
    }

    /// Day/night thermal cycling stress. Night-side cold shock for warm-adapted organisms;
    /// day-side heat stress for cold-adapted ones. Uncorrelated with temperaturePreference
    /// mismatch (which handles average temperature) — this models the SWING.
    private float GetThermalCycleFitnessMultiplier()
    {
        DayNightCycle dayNight = DayNightCycle.Instance;
        if (dayNight == null) return 1f;
        Vector3 normal = (transform.position - planetCenter).normalized;
        float solar = dayNight.SolarExposure(normal);
        float nightCold = (1f - solar) * Mathf.Clamp01(temperaturePreference / 100f) * 0.7f;
        float dayHeat   = solar * Mathf.Clamp01((100f - temperaturePreference) / 100f) * 0.5f;
        float cyclePenalty = nightCold + dayHeat;
        float sensitivity = Mathf.Lerp(1.8f, 0.15f, thermalCycleTolerance / 100f);
        return Mathf.Clamp(1f + cyclePenalty * sensitivity, 1f, 3f);
    }

    private Vector3 ComputeProducerMovementTangent()
    {
        DayNightCycle dayNight = DayNightCycle.Instance;
        Vector3 normal = (transform.position - planetCenter).normalized;

        // Comfort drive (same as consumers).
        float discomfort = GetDiscomfort(transform.position);
        float comfortWeight = Mathf.Clamp01(Mathf.Sqrt(discomfort) * comfortSeekingStrength);
        Vector3 currentTangent = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
        if (currentTangent.sqrMagnitude < 0.0001f)
            currentTangent = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        Vector3 comfortDir = FindMostComfortableDirection(currentTangent, normal, discomfort);

        // Sun-seeking drive: pulls toward sun when energy is low.
        float sunWeight = 0f;
        Vector3 sunDir = currentTangent;
        if (dayNight != null)
        {
            float solar = dayNight.SolarExposure(normal);
            sunWeight = Mathf.Clamp01(1f - _solarEnergy / maxSolarEnergy) * (1f - solar);
            // Project sun direction onto tangent plane.
            Vector3 toSun = dayNight.SunDirection;
            sunDir = (toSun - Vector3.Dot(toSun, normal) * normal).normalized;
            if (sunDir.sqrMagnitude < 0.0001f) sunDir = currentTangent;
        }

        float randomTurn = Random.Range(-wanderTurnRate, wanderTurnRate) * Time.deltaTime;
        Vector3 exploreDir = (Quaternion.AngleAxis(randomTurn, normal) * currentTangent).normalized;
        const float exploreWeight = 0.3f;

        Vector3 sum = exploreWeight * exploreDir + comfortWeight * comfortDir + sunWeight * sunDir;
        Vector3 proj = sum - Vector3.Dot(sum, normal) * normal;
        return proj.sqrMagnitude < 0.0001f ? exploreDir : proj.normalized;
    }

    /// Continuous per-tick selection pressure (Section 6a): an agent whose temperature/
    /// moisture preference matches its current location starves slower there; a mismatched
    /// agent starves faster. Applies every tick based on current position. Hardiness scales
    /// how steep this is per-individual: generalists (high hardiness) take a shallower
    /// penalty for the same mismatch; specialists (low hardiness) take a steeper one.
    private float GetClimateStarvationMultiplier()
    {
        float discomfort = GetDiscomfort(transform.position); // 0 (perfect match) .. 1 (max mismatch)
        float effectiveRange = climateFitnessMultiplierRange * Mathf.Lerp(hardinessRangeMax, hardinessRangeMin, hardinessTrait / 100f);
        float multiplier = 1f + (discomfort * 2f - 1f) * effectiveRange;
        return Mathf.Clamp(multiplier, 0.2f, 2f);
    }

    /// 0 = local climate exactly matches this agent's preference, 1 = maximally mismatched.
    private float GetDiscomfort(Vector3 position)
    {
        float temp = ClimateManager.GetTemperature(position);
        float moisture = ClimateManager.GetMoisture(position);

        // TOLERANCE BAND (plateau): organisms have a comfortable RANGE around their preferred value,
        // not a razor-point optimum. Within ±band, discomfort is zero; only beyond it does mismatch
        // start to cost fitness. Band width scales with hardiness — a generalist (eurytherm) tolerates
        // wide swings, a specialist (stenotherm) only a narrow range. Without this plateau, ANY
        // deviation from the exact preferred temperature caused immediate fitness loss, so every
        // species behaved like an extreme stenotherm and the world's large temperature swings wiped
        // populations out regardless of how well-adapted they were.
        float band = Mathf.Lerp(toleranceBandNarrow, toleranceBandWide, hardinessTrait / 100f);
        float tempDiff     = Mathf.Max(0f, Mathf.Abs(temp - temperaturePreference) - band) / 100f;
        float moistureDiff = Mathf.Max(0f, Mathf.Abs(moisture - moisturePreference) - band) / 100f;
        return Mathf.Clamp01((tempDiff + moistureDiff) / 2f);
    }

    /// LandColonization gene: the lineage's locked habitat flips from aquatic to terrestrial. Without
    /// this every organism stays permanently aquatic (the pre-existing state — _isAquatic was never
    /// flipped anywhere), which is why "land species" never actually existed: agents merely stood on
    /// dry ground temporarily while still counting as aquatic and taking a desiccation penalty. This
    /// is the real, heritable transition — moisture preference swings dry, medium-mismatch penalties
    /// invert (now land is home, sea is the hazard), and land-only content (e.g. Fire Mastery) opens up.
    public void ColonizeLand()
    {
        if (!_isAquatic) return;
        _isAquatic = false;
        moisturePreference = Mathf.Clamp(PopulationStats.SampleDimension(20f, 15f), 0f, 100f); // dry-favoring
        LocomotionMedium = HasMotility ? LocomotionMedium.Terrestrial : LocomotionMedium.Aquatic;
        AcquiredGenes.Add("LandColonization");
        Debug.Log($"[Habitat] {name} colonized land (moisturePref→{moisturePreference:F0}).");
    }

    /// DEBUG: instantly relocates this agent to a point matching its CURRENT habitat (_isAquatic) —
    /// wet if aquatic, dry if terrestrial. Used only by the Era-skip debug path: colonizing land is
    /// normally a gradual, lived transition (the organism walks/drifts there over real time), but an
    /// instant gene-skip leaves land-colonized organisms stranded wherever they happened to be —
    /// visually still sitting in the sea despite "Medium: Land" in the HUD. Scatter-searches random
    /// surface points and jumps to the first one whose wet/dry state matches.
    /// DEBUG: assigns a random sex to a differentiated clone (mirrors Reproduce()'s offspring-sex
    /// assignment, which DebugBulkUpPopulation's clones bypass since they aren't born via Reproduce).
    public void DebugAssignRandomSex()
    {
        if (IsDifferentiated) Sex = Random.value < 0.5f ? BiologicalSex.Male : BiologicalSex.Female;
    }

    public void DebugRelocateToMatchingMedium()
    {
        var fluid = FluidDynamicsManager.Instance;
        if (fluid == null || _spawner == null) return;
        for (int i = 0; i < 24; i++)
        {
            Vector3 candidate = SphereSurface.RandomPointOnSphere(planetCenter, planetRadius);
            bool submerged = fluid.IsSubmerged(candidate);
            if (submerged == _isAquatic) { transform.position = candidate; AlignToSurface(); return; }
        }
        // Fallback: no matching point found in the sample (e.g. an all-ocean or all-dry world) — leave
        // the agent where it is; the ordinary medium-mismatch pressure will apply as normal thereafter.
    }

    /// ReturnToSea gene: a rarer reversal — a terrestrial lineage re-adapts to an aquatic existence
    /// (real precedent: cetaceans, pinnipeds, sea snakes). Flips habitat back and moisture preference
    /// wet-favoring.
    public void ReturnToSea()
    {
        if (_isAquatic) return;
        _isAquatic = true;
        moisturePreference = Mathf.Clamp(PopulationStats.SampleDimension(85f, 10f), 0f, 100f); // wet-favoring
        LocomotionMedium = HasMotility ? LocomotionMedium.Aquatic : LocomotionMedium.Terrestrial;
        AcquiredGenes.Add("ReturnToSea");
        Debug.Log($"[Habitat] {name} returned to the sea (moisturePref→{moisturePreference:F0}).");
    }

    /// e1_motility_emergence gene: organism develops directed locomotion (flagellar/ciliary
    /// analog). Until this fires the organism is a passive drifter; after it, self-directed
    /// movement and (eventually) predation become possible.
    public void BecomeMotile()
    {
        HasMotility = true;
        LocomotionMedium = _isAquatic ? LocomotionMedium.Aquatic : LocomotionMedium.Terrestrial;
        AcquiredGenes.Add("MotilityEmergence");
        ApplyMorphology(); // sessile radial blob → motile bilaterian with a tail
        Debug.Log($"[Motility] {name} developed directed locomotion.");
    }

    public void RemainSessile()
    {
        LocomotionMedium = LocomotionMedium.Sessile;
        AcquiredGenes.Add("MotilityEmergence");
        Debug.Log($"[Motility] {name} remains sessile (wind-dispersed).");
    }

    /// e2_gliding_adaptation: passive membrane extension enables controlled descent.
    public void BecomeGlider()
    {
        LocomotionMedium = LocomotionMedium.Gliding;
        AcquiredGenes.Add("GlidingAdaptation");
        Debug.Log($"[Gliding] {name} evolved gliding membranes.");
    }

    /// e2_aerial_locomotion_emergence: powered flight. Only callable when CurrentMass < FlightMassCeiling.
    public void BecomeAerial()
    {
        if (_currentMass >= FlightMassCeiling)
        {
            Debug.LogWarning($"[Flight] {name} tried to gain flight but mass {_currentMass:F5} >= ceiling {FlightMassCeiling:F5}.");
            return;
        }
        LocomotionMedium = LocomotionMedium.Aerial;
        AcquiredGenes.Add("AerialLocomotion");
        Debug.Log($"[Flight] {name} achieved powered flight (mass={_currentMass:F5}).");
    }

    /// PhotosynthesisEmergence gene: evolve the ability to harvest solar energy.
    /// Shifts metabolism from Chemosynthetic → Phototrophic; seeds solar energy from
    /// accumulated chemical reserve so there is no energy gap at the transition.
    public void BecomePhototrophic()
    {
        Metabolism = MetabolismType.Phototrophic;
        _solarEnergy = _chemEnergy; // carry over accumulated reserves
        // Carry over the lineage's enzymatic efficiency rather than rolling a fresh (much lower)
        // value — the underlying protein machinery doesn't reset just because the energy source
        // changed. Clamp to the photo ceiling in case an outlier chemo efficiency exceeds it.
        PhotoEfficiency = Mathf.Min(ChemoEfficiency, PhotoEfficiencyCeiling);
        AcquiredGenes.Add("PhotosynthesisEmergence");
        Debug.Log($"[Metabolism] {name} evolved photosynthesis (photoEff={PhotoEfficiency:F3} carried from chemoEff).");
    }

    /// Kingdom Fork gene choice: stay as an autotroph (chemosynthetic or phototrophic).
    /// No-op if already a producer — just mints the kingdom name.
    public void BecomeProducer()
    {
        // Metabolism stays as whatever it already is (Chemosynthetic or Phototrophic).
        Kingdom = KingdomNameGenerator.Generate();
    }

    /// Kingdom Fork gene choice: shift to heterotrophy (osmotrophy → saprotrophy → predation).
    public void BecomeConsumer()
    {
        Metabolism = MetabolismType.Heterotrophic;
        _timeSinceLastMeal = 0f; // fresh starvation clock
        Kingdom = KingdomNameGenerator.Generate();
    }

    /// Mixotrophy gene: combine photosynthesis + heterotrophy at ~70% efficiency each.
    public void BecomeMixotrophic()
    {
        Metabolism = MetabolismType.Mixotrophic;
        _timeSinceLastMeal = 0f;
    }

    // ── Era 1 trait setters ───────────────────────────────────────────────────

    /// ThermalToleranceExpansion gene: widen the survivable temperature band. Raises hardiness
    /// (which broadens the climate-mismatch tolerance band in GetClimateStarvationMultiplier) and
    /// thermal-cycle tolerance. Gene ID is registered by GeneEvolutionManager, not here.
    public void ExpandThermalTolerance()
    {
        SetTraits(visionTrait, speedTrait, strengthTrait,
            Mathf.Min(hardinessTrait + 12f, 100f), temperaturePreference, moisturePreference);
        thermalCycleTolerance = Mathf.Min(thermalCycleTolerance + 15f, 100f);
        Debug.Log($"[Biochemistry] {name} broadened thermal tolerance (hardiness→{hardinessTrait:F0}).");
    }

    public void SetManipulation(ManipulationLevel level) { Manipulation = level; ApplyMorphology(); RecordHistory($"Manipulation → {level}"); }
    public void SetSociality(SocialityBaseline level)    => Sociality = level;
    public void SetNeuralComplexity(NeuralComplexityStage stage) => NeuralComplexity = stage;
    public void SetBodyPlan(BodyPlanType plan)  { BodyPlan = plan; ApplyMorphology(); RecordHistory($"Structural support → {plan}"); }
    public void SetGermLayers()                => HasGermLayers = true;
    public void SetAnoxicRefuge()              => IsAnoxicRefugeLineage = true;

    /// appearance-generation-spec §3.4: records a historical-gallery snapshot for the player's own
    /// lineage only (PlayerLineageHistory itself no-ops for any other community). Called from each
    /// Era 1 axis setter below, not from the periodic network-foreshadow re-check in
    /// UpdatePressureVariables — that one is gradual drift, not a discrete "major event."
    private void RecordHistory(string eventLabel) => PlayerLineageHistory.RecordSnapshot(this, eventLabel);

    // ── appearance-generation-spec §2.2/§2.8 remaining-axis setters ──────────────────────────
    public void SetSegmentation(SegmentationType s) { Segmentation = s; ApplyMorphology(); RecordHistory($"Segmentation → {s}"); }
    public void SetPrimarySense(SensoryModality m)  { PrimarySense = m; _sensesAcquired.Add(m); RecordHistory($"Primary sense → {m}"); }
    public void SetFeedingApparatus(FeedingApparatus f) { Feeding = f; RecordHistory($"Feeding apparatus → {f}"); }
    public void SetIntegument(IntegumentType t)     { Integument = t; ApplyMorphology(); RecordHistory($"Integument → {t}"); }
    /// §2.8 e1_limb_differentiation: splits a previously-undifferentiated appendage budget into
    /// dedicated locomotor vs. manipulator pairs. Before this fires, ManipulatorPairs stays 0 and
    /// tool_ceiling (descriptor-derived) stays false even at high Manipulation tiers.
    public void SetLimbDifferentiation(int locomotorPairs, int manipulatorPairs)
    {
        LocomotorPairs = locomotorPairs;
        ManipulatorPairs = manipulatorPairs;
        RecordHistory($"Limb differentiation → {locomotorPairs} locomotor / {manipulatorPairs} manipulator pairs");
    }
    public void SetVocalApparatus() { VocalApparatus = true; RecordHistory("Vocal apparatus emerged"); }
    public void SetColonialModular() { IsColonialModular = true; ApplyMorphology(); RecordHistory("Colonial-modular symmetry emerged"); }
    public void SetBiradial()        { IsBiradial = true; ApplyMorphology(); RecordHistory("Biradial symmetry emerged"); }
    /// How many distinct sensory modalities (beyond the default Chemosensory) this lineage has
    /// acquired — the eligibility test for e1_multimodal_sensory_integration.
    public int AcquiredSenseCount => _sensesAcquired.Count;

    // NOTE: the old single-swap AlternativeRespirationPathway gene/method was superseded by
    // SpecializeAnaerobic()/BecomeAerobic() above (respiration-evolutionary-sequence-fix-spec) — a
    // proper multi-way OR-gate branch plus a late atmosphere-gated aerobic unlock, rather than one
    // hardcoded swap target.

    /// Reproductive Strategy Shift gene choice: remain Asexual (the current default -
    /// Reproduce() clones a single parent with mutation drift, no functional change).
    public void BecomeAsexual()
    {
        IsSexual = false;
        AcquiredGenes.Add("ReproductiveStrategyShift");
    }

    /// Sexual DIFFERENTIATION event: the lineage splits into separate sexes. This organism becomes
    /// Male or Female (50/50); its offspring inherit differentiation and are likewise 50/50. It does
    /// NOT yet reproduce sexually — that's the later ReproductiveStrategyShift event (BecomeSexual),
    /// which requires differentiation first. Seeds a local pool of differentiated both-sex conspecifics
    /// so a viable breeding population already exists by the time sexual reproduction unlocks.
    public void DifferentiateSex()
    {
        AcquiredGenes.Add("SexualDifferentiation");
        if (IsDifferentiated) return;
        IsDifferentiated = true;
        Sex = Random.value < 0.5f ? BiologicalSex.Male : BiologicalSex.Female;
        Debug.Log($"[Reproduction] {name} sexually differentiated (sex={Sex}).");
        SeedLocalDifferentiationPool();
    }

    /// Seeds nearby same-community members as differentiated, with alternating sexes, so both sexes
    /// are present locally — the differentiation-stage analogue of SeedLocalBreedingPool.
    private void SeedLocalDifferentiationPool()
    {
        if (_spawner == null) return;
        float radius = Mathf.Max(senseRadius * 3f, 4f);
        int converted = 0;
        BiologicalSex nextSex = Sex == BiologicalSex.Female ? BiologicalSex.Male : BiologicalSex.Female;
        foreach (var other in _spawner.ActiveAgents)
        {
            if (converted >= 4) break;
            if (other == null || other == this) continue;
            if (other.communityId != communityId || other.IsDifferentiated) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > radius) continue;
            other.AdoptDifferentiationWithSex(nextSex);
            nextSex = nextSex == BiologicalSex.Male ? BiologicalSex.Female : BiologicalSex.Male;
            converted++;
        }
    }

    /// Differentiates this agent with a specified sex — used to seed a local differentiation pool.
    public void AdoptDifferentiationWithSex(BiologicalSex sex)
    {
        AcquiredGenes.Add("SexualDifferentiation");
        if (IsDifferentiated) return;
        IsDifferentiated = true;
        Sex = sex;
    }

    /// Reproductive Strategy Shift gene choice: adopt Sexual REPRODUCTION. Requires the lineage to be
    /// sexually DIFFERENTIATED first (guarded). Reproduce() then requires finding an opposite-sex
    /// IsSexual mate in range and blends both parents' traits (see FindMateInRange / Reproduce).
    public void BecomeSexual()
    {
        if (IsSexual) return;
        if (!IsDifferentiated) DifferentiateSex(); // safety: sexual reproduction presupposes separate sexes
        IsSexual = true;
        AcquiredGenes.Add("ReproductiveStrategyShift");
        Debug.Log($"[Reproduction] {name} adopted sexual reproduction (sex={Sex}).");
        SeedLocalBreedingPool();
    }

    /// Converts a handful of nearby same-community members to sexual reproduction (with the opposite
    /// sex seeded in) so a viable local breeding POOL forms, rather than a single sexual individual
    /// stranded with no possible partner. Sexual reproduction can't bootstrap from one scattered
    /// adopter (an Allee-effect dead-end) — a founder population needs both sexes present locally.
    private void SeedLocalBreedingPool()
    {
        if (_spawner == null) return;
        float radius = Mathf.Max(senseRadius * 3f, 4f);
        int converted = 0;
        BiologicalSex nextSex = Sex == BiologicalSex.Female ? BiologicalSex.Male : BiologicalSex.Female;
        foreach (var other in _spawner.ActiveAgents)
        {
            if (converted >= 4) break;
            if (other == null || other == this) continue;
            if (other.communityId != communityId || other.IsSexual) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > radius) continue;
            other.AdoptSexualWithSex(nextSex);
            nextSex = nextSex == BiologicalSex.Male ? BiologicalSex.Female : BiologicalSex.Male; // alternate so both sexes appear
            converted++;
        }
        if (converted > 0)
            Debug.Log($"[Reproduction] {name} seeded a local breeding pool ({converted} conspecifics turned sexual).");
    }

    /// Turns this agent sexual with a specified biological sex — used to seed a local breeding pool.
    public void AdoptSexualWithSex(BiologicalSex sex)
    {
        if (IsSexual) return;
        IsDifferentiated = true;              // sexual reproduction presupposes differentiation
        IsSexual = true;
        Sex = sex;
        AcquiredGenes.Add("SexualDifferentiation");
        AcquiredGenes.Add("ReproductiveStrategyShift");
    }

    /// SequentialHermaphroditism gene: organism can switch sex in response to local
    /// population imbalance. Sets the ongoing capability; sex switching itself runs
    /// periodically in MaybeChangeSex().
    public void BecomeSequentialHermaphrodite()
    {
        CanChangeSex = true;
        AcquiredGenes.Add("SequentialHermaphroditism");
        Debug.Log($"[SexChange] {name} evolved sequential hermaphroditism (currently {Sex}).");
    }

    /// 0 = perfectly balanced (equal male/female nearby), 1 = entirely same-sex locally.
    /// Searches a 3× sense radius so it samples a meaningful local population, not just
    /// the immediate cluster. Returns 1 (max imbalance) if no opposite-sex agents nearby.
    public float LocalSexImbalance()
    {
        if (!IsSexual || _spawner == null) return 0f;
        int sameCount = 0, oppositeCount = 0;
        float searchR = senseRadius * 3f;
        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this || !other.IsSexual) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > searchR) continue;
            if (other.Sex == Sex) sameCount++;
            else oppositeCount++;
        }
        if (sameCount + oppositeCount == 0) return 1f; // isolated — maximum imbalance
        return (float)sameCount / (sameCount + oppositeCount);
    }

    /// Local male fraction among nearby DIFFERENTIATED same-community members, including self
    /// (0 = all female, 1 = all male, 0.5 = balanced). Returns 0.5 if the local sex is undefined.
    /// Drives frequency-dependent sex determination at birth (see Reproduce).
    private float LocalMaleFraction()
    {
        if (_spawner == null) return 0.5f;
        int male = Sex == BiologicalSex.Male ? 1 : 0;      // count self
        int female = Sex == BiologicalSex.Female ? 1 : 0;
        float searchR = senseRadius * 3f;
        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this || !other.IsDifferentiated) continue;
            if (other.communityId != communityId) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > searchR) continue;
            if (other.Sex == BiologicalSex.Male) male++;
            else if (other.Sex == BiologicalSex.Female) female++;
        }
        int total = male + female;
        return total == 0 ? 0.5f : (float)male / total;
    }
    // How strongly a local sex imbalance biases offspring toward the rarer sex. 0 = always 50/50;
    // 1 = fully skewed toward the minority in an all-one-sex neighborhood. 0.8 self-corrects firmly
    // but keeps some randomness, so the ratio glides back to ~50/50 rather than snapping. TUNABLE.
    private const float SexRatioCorrectionStrength = 0.8f;

    /// Periodic sex-switch check for organisms with CanChangeSex. Runs every 8-20 real
    /// seconds. If the local sex ratio is strongly skewed toward the organism's current sex
    /// (>65% same), there is a scaled chance to switch to the rarer sex, improving local
    /// mating availability. Chance scales linearly with excess imbalance so near-parity
    /// populations almost never trigger it but extreme imbalances (~90%+) trigger quickly.
    private void MaybeChangeSex()
    {
        if (!CanChangeSex || !IsSexual) return;
        _sexSwitchTimer -= Time.deltaTime;
        if (_sexSwitchTimer > 0f) return;
        _sexSwitchTimer = Random.Range(8f, 20f);

        float imbalance = LocalSexImbalance();
        const float threshold = 0.65f;
        float excess = imbalance - threshold;
        if (excess <= 0f) return;

        // Probability scales 0→1 over the excess range 0→0.35 (full certainty at 100% same-sex).
        if (Random.value < excess / (1f - threshold))
        {
            BiologicalSex prev = Sex;
            Sex = Sex == BiologicalSex.Male ? BiologicalSex.Female : BiologicalSex.Male;
            Debug.Log($"[SexChange] {name} switched {prev} → {Sex} (local imbalance {imbalance:P0})");
        }
    }

    /// Copies acquired genes and kingdom assignment from parent to offspring - genes are
    /// inherited once fixed, not re-rolled each generation.
    public void InheritGenesFrom(AgentController parent)
    {
        AcquiredGenes.Clear();
        foreach (var gene in parent.AcquiredGenes) AcquiredGenes.Add(gene);
        Kingdom = parent.Kingdom;
        IsSexual = parent.IsSexual;
        IsDifferentiated = parent.IsDifferentiated; // sex differentiation is heritable; Sex re-rolled in Reproduce
        _isAquatic = parent._isAquatic; // habitat (land/sea) is heritable — set by ColonizeLand/ReturnToSea

        // Inherit the parent's morphology seed so offspring resemble their parents, then refresh the
        // mesh from this child's inherited state (motility/appendages/body-plan copied just below).
        _morphSeed = parent._morphSeed;

        // Atmospheric adaptation is inherited from the parent's locked-in mix, NOT
        // resampled from the current atmosphere - only AttemptAtmosphericSpeciation
        // re-locks it, for either the parent or this child independently thereafter.
        HasMotility      = parent.HasMotility;
        LocomotionMedium = parent.LocomotionMedium;
        Metabolism       = parent.Metabolism;
        CanChangeSex = parent.CanChangeSex;
        // Sex itself is re-rolled at birth (see Reproduce) — only the capability is inherited.
        _idealGasMix = new Dictionary<string, float>(parent._idealGasMix);
        // gasTolerance: primary drift happens at speciation; small per-generation nudge lets
        // lineages track slow atmospheric change without waiting for a speciation event.
        gasTolerance = Mathf.Clamp(PopulationStats.SampleDimension(parent.gasTolerance, 1.25f), 0f, 100f);
        AtmoLineage  = parent.AtmoLineage;

        // Environmental tolerances drift each generation so lineages can adapt to sustained
        // pressure. Half the standard trait stddev keeps drift slower than the primary traits
        // but fast enough to track a gradually changing environment.
        const float tolDrift = 2.5f;
        uvTolerance           = Mathf.Clamp(PopulationStats.SampleDimension(parent.uvTolerance,           tolDrift), 0f, 100f);
        pressureTolerance     = Mathf.Clamp(PopulationStats.SampleDimension(parent.pressureTolerance,     tolDrift), 0f, 100f);
        thermalCycleTolerance = Mathf.Clamp(PopulationStats.SampleDimension(parent.thermalCycleTolerance, tolDrift), 0f, 100f);
        pressurePreference    = parent.pressurePreference;

        // Survival needs are lineage-locked — offspring breathe the same gas and need
        // the same liquid chemistry as the parent line they descended from.
        _breathedGasName       = parent._breathedGasName;
        _expelledGasName       = parent._expelledGasName;
        _minBreathableFraction = parent._minBreathableFraction;
        _requiredLiquidKind    = parent._requiredLiquidKind;

        // Era 1 evolved attributes — all inherited, not re-rolled.
        Backbone          = parent.Backbone;
        Manipulation      = parent.Manipulation;
        Sociality         = parent.Sociality;
        NeuralComplexity  = parent.NeuralComplexity;
        BodyPlan          = parent.BodyPlan;
        HasGermLayers     = parent.HasGermLayers;
        IsAnoxicRefugeLineage = parent.IsAnoxicRefugeLineage;

        // appearance-generation-spec §2.2/§2.8 remaining axes — inherited, not re-rolled.
        Segmentation      = parent.Segmentation;
        PrimarySense      = parent.PrimarySense;
        _sensesAcquired.Clear();
        foreach (var s in parent._sensesAcquired) _sensesAcquired.Add(s);
        Feeding           = parent.Feeding;
        Integument        = parent.Integument;
        LocomotorPairs    = parent.LocomotorPairs;
        ManipulatorPairs  = parent.ManipulatorPairs;
        VocalApparatus    = parent.VocalApparatus;
        IsColonialModular = parent.IsColonialModular;
        IsBiradial        = parent.IsBiradial;

        if (_stressRegistered) PopulationStats.UnregisterStressTolerance(stressTolerance);
        stressTolerance = Mathf.Clamp(PopulationStats.SampleDimension(parent.stressTolerance, 2.5f), 0f, 100f);
        if (_stressRegistered) PopulationStats.RegisterStressTolerance(stressTolerance);

        // Efficiency traits drift slowly each generation in addition to SI-event jumps,
        // so lineages can grind toward better metabolic yield under sustained energy pressure.
        PhotoEfficiency        = Mathf.Clamp(PopulationStats.SampleDimension(parent.PhotoEfficiency,        0.001f), 0f, PhotoEfficiencyCeiling);
        ChemoEfficiency        = Mathf.Clamp(PopulationStats.SampleDimension(parent.ChemoEfficiency,        0.02f),  0f, ChemoEfficiencyCeiling);
        AssimilationEfficiency = Mathf.Clamp(PopulationStats.SampleDimension(parent.AssimilationEfficiency, 0.02f),  AssimEfficiencyMin, AssimEfficiencyMax);

        // Reproduction rate: faster reproduction is the prey-side response to predation pressure.
        // Drifts ±1 with 15% probability per generation, floored at 1.
        eatsToReproduce = Mathf.Max(1, parent.eatsToReproduce + (Random.value < 0.15f ? (Random.value < 0.5f ? -1 : 1) : 0));

        // era3-primitives-spec §2: real evolvable traits, mutating at reproduction like any other —
        // NOT reset per offspring, drifted from the parent so selection can actually act on them.
        contestPropensity = Mathf.Clamp(PopulationStats.SampleDimension(parent.contestPropensity, 5f), 0f, 100f);
        boldness          = Mathf.Clamp(PopulationStats.SampleDimension(parent.boldness,          5f), 0f, 100f);

        // Rebuild the body now that all morphology-driving state (motility, appendages, body plan,
        // inherited seed) is in place, so the offspring's shape matches its inherited lineage.
        ApplyMorphology();
    }

    /// Asexual when mate == null (clones this parent's traits with mutation drift, exactly
    /// the pre-existing behavior). Sexual when mate is provided: each trait dimension is
    /// first averaged between both parents, THEN the same mutation drift is applied on top
    /// of the blended value - this blending is the actual genetic-diversity benefit the
    /// Reproductive Strategy Shift gene event is themed around.
    private void Reproduce(AgentController mate)
    {
        if (_spawner == null) return;

        // Once this community is CIVILIZED (owns at least one Era 3 settlement), a birth becomes
        // abstract settlement population growth instead of a new individually-simulated organism.
        // This is the actual, permanent Era 3 lag fix — settlement absorption alone (a periodic,
        // radius-limited tick) can never keep pace with unthrottled reproduction across a whole living
        // population; population kept exploding into thousands of live agents even after founding.
        // The parent reproducing here is itself still a living straggler (not yet absorbed) — only
        // the OFFSPRING is abstracted, so this doesn't retroactively remove anyone, just stops the
        // bleeding going forward.
        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive
            && Era3Manager.Instance.CivHasSettlement(communityId))
        {
            Era3Manager.Instance.RegisterAbstractBirth(communityId, transform.localScale.x, Metabolism, Backbone, PhotoEfficiency, ChemoEfficiency);
            return;
        }

        if (_spawner.ActiveAgents.Count >= AgentSpawner.MaxIndividualAgents) return; // safety valve, see TryReproduce

        // Offspring SPLITS OFF the parent's body — it emerges right at the parent and buds off
        // just behind it (one body-length back along the reverse of the heading), so a birth reads as
        // fission from the parent, not a child materializing a couple units away in open space. The
        // per-frame separation impulse then gently eases the two apart over the next moments.
        float budOffset = Mathf.Max(transform.localScale.x, 0.08f);
        Vector3 offspringPos = SphereSurface.MoveAlongSurface(transform.position, -_heading, budOffset, planetCenter, planetRadius);

        float baseVision = visionTrait;
        float baseSpeed = speedTrait;
        float baseStrength = strengthTrait;
        float baseHardiness = hardinessTrait;
        float baseTempPref = temperaturePreference;
        float baseMoisturePref = moisturePreference;

        if (mate != null)
        {
            baseVision = (visionTrait + mate.visionTrait) / 2f;
            baseSpeed = (speedTrait + mate.speedTrait) / 2f;
            baseStrength = (strengthTrait + mate.strengthTrait) / 2f;
            baseHardiness = (hardinessTrait + mate.hardinessTrait) / 2f;
            baseTempPref = (temperaturePreference + mate.temperaturePreference) / 2f;
            baseMoisturePref = (moisturePreference + mate.moisturePreference) / 2f;
        }

        float childVision = PopulationStats.SampleDimension(baseVision, mutationStdDev);
        float childSpeed = PopulationStats.SampleDimension(baseSpeed, mutationStdDev);
        float childStrength = PopulationStats.SampleDimension(baseStrength, mutationStdDev);
        float childHardiness = PopulationStats.SampleDimension(baseHardiness, mutationStdDev);

        // Offspring's climate preference drifts from the (blended) parent baseline, also
        // nudged toward the birth location's actual climate - regional adaptation compounds
        // across generations.
        float localTemp = ClimateManager.GetTemperature(offspringPos);
        float localMoisture = ClimateManager.GetMoisture(offspringPos);
        float childTempPref = PopulationStats.SampleDimension((baseTempPref + localTemp) / 2f, mutationStdDev);
        float childMoisturePref = PopulationStats.SampleDimension((baseMoisturePref + localMoisture) / 2f, mutationStdDev);

        AgentController child = _spawner.SpawnAgent(childVision, childSpeed, childStrength, childHardiness, childTempPref, childMoisturePref, offspringPos, communityId, lineageColor);
        child.InheritGenesFrom(this);
        // Per-reproduction mutation: this offspring may ORIGINATE a new trait (gene-adoption spec §B),
        // staggering adoption across lineages rather than a synchronized population-wide flip.
        GeneEvolutionManager.RollReproductionMutations(child);
        // Assign offspring sex randomly if sexual — sex is not inherited, just the capability.
        // If local imbalance is extreme and the parent can change sex, bias offspring toward
        // the rarer sex (density-dependent sex determination, as in some real reptiles/fish).
        // A DIFFERENTIATED lineage assigns its offspring a sex, whether or not it yet reproduces
        // sexually — differentiation is the trait that makes offspring male/female. The ratio is
        // FREQUENCY-DEPENDENT (Fisher's principle): each birth is biased toward the locally rarer sex
        // in proportion to the skew, so a transient shortage of one sex raises that sex's birth odds
        // and the population glides back toward ~50/50, then reverts to 50/50 at balance. This is the
        // realistic, always-on correction; the parthenogenesis fallback in TryReproduce is only the
        // extreme safety net for when no mate exists at all.
        if (child.IsDifferentiated)
        {
            float maleFraction = LocalMaleFraction();                                  // 0.5 = balanced
            float maleBias = Mathf.Clamp01(0.5f + (0.5f - maleFraction) * SexRatioCorrectionStrength);
            child.Sex = Random.value < maleBias ? BiologicalSex.Male : BiologicalSex.Female;
        }
    }

    /// Returns an eligible mate this agent is actually TOUCHING — same community, opposite sex,
    /// within body-contact range (not merely sense range). Mating requires physical contact: an
    /// organism must reach and touch a partner, not conceive from across its visual field. The
    /// mate-seeking steering in UpdateProducer/UpdateConsumer keeps an agent closing on the nearest
    /// mate precisely while this returns null, so the two approach until they meet. Live (not sense-
    /// cached) so contact resolves against current positions — no conceiving from a stale snapshot.
    private AgentController FindMateInRange()
    {
        if (_spawner == null) return null;

        AgentController nearest = null;
        float contact = MatingContactRadius;
        float nearestDist = contact;

        _spawner.QueryNearby(transform.position, contact, _queryBuffer);
        foreach (var other in _queryBuffer)
        {
            if (other == null || other == this) continue;
            if (!other.IsSexual) continue;
            if (other.communityId != communityId) continue;
            // Require opposite biological sex. Asexual organisms can't mate sexually
            // (shouldn't reach here, but guard anyway).
            if (Sex == BiologicalSex.Asexual || other.Sex == BiologicalSex.Asexual) continue;
            if (other.Sex == Sex) continue; // same sex — skip
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
        }
        return nearest;
    }

    /// Returns the nearest eligible mate anywhere in the population (ignores sense radius).
    /// Used for movement targeting only — actual mating still requires being within sense range.
    private AgentController FindNearestMate()
    {
        if (_spawner == null) return null;
        if (Sex == BiologicalSex.Asexual) return null;
        if (_mateCycle == _senseCycle) return ValidCached(_cachedMate); // memoized: this was an O(n) full scan every frame

        AgentController nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this) continue;
            if (!other.IsSexual) continue;
            if (other.communityId != communityId) continue;
            if (other.Sex == BiologicalSex.Asexual || other.Sex == Sex) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist) { nearestDist = dist; nearest = other; }
        }
        _mateCycle = _senseCycle; _cachedMate = nearest;
        return nearest;
    }

    /// Returns the nearest huntable agent within sense range.
    ///
    /// Herbivory (target is a producer): any community, including own — eating plant-equivalents
    /// is not cannibalism and is how the food web forms before speciation creates rivals.
    ///
    /// Predation (target is a heterotroph): different community only — no intra-species warfare.
    ///
    /// This distinction is what allows a food chain to emerge within a single founding lineage:
    /// consumers eat same-lineage producers → creates selection pressure for producers to flee /
    /// invest in chemical defenses → eventually drives speciation along predator/prey lines.
    private AgentController FindPreyInRange()
    {
        if (_spawner == null) return null;
        if (_preyCycle == _senseCycle) return ValidCached(_cachedPrey); // memoized this sense cycle

        AgentController nearest = null;
        float nearestDist = senseRadius;

        _spawner.QueryNearby(transform.position, senseRadius, _queryBuffer);
        foreach (var other in _queryBuffer)
        {
            if (other == null || other == this) continue;
            // Producers: targetable for herbivory regardless of community.
            // Heterotrophs: only hunt across community lines (no cannibalism).
            bool canTarget = other.IsProducer || other.communityId != communityId;
            if (!canTarget) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
        }
        _preyCycle = _senseCycle; _cachedPrey = nearest;
        return nearest;
    }

    private Vector3 ComputeMovementTangentToPrey(AgentController prey)
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 currentTangent = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
        if (currentTangent.sqrMagnitude < 0.0001f)
            currentTangent = Vector3.Cross(normal, Random.onUnitSphere).normalized;

        float discomfort = GetDiscomfort(transform.position);
        float comfortWeight = Mathf.Clamp01(Mathf.Sqrt(discomfort) * comfortSeekingStrength);
        Vector3 comfortDir = FindMostComfortableDirection(currentTangent, normal, discomfort);

        float hungerWeight = Mathf.Clamp01(_timeSinceLastMeal / starvationTime);
        Vector3 hungerDir = SphereSurface.TangentDirectionTo(transform.position, prey.transform.position, planetCenter);

        float randomTurn = Random.Range(-wanderTurnRate, wanderTurnRate) * Time.deltaTime;
        Vector3 exploreDir = (Quaternion.AngleAxis(randomTurn, normal) * currentTangent).normalized;
        const float exploreWeight = 0.3f;

        Vector3 sum = exploreWeight * exploreDir + comfortWeight * comfortDir + hungerWeight * hungerDir;
        Vector3 proj = sum - Vector3.Dot(sum, normal) * normal;
        return proj.sqrMagnitude < 0.0001f ? exploreDir : proj.normalized;
    }

    private CorpseItem FindNearestCorpseInRange()
    {
        if (_corpseSpawner == null) return null;

        CorpseItem nearest = null;
        float nearestDist = senseRadius;

        foreach (var corpse in _corpseSpawner.ActiveCorpses)
        {
            if (corpse == null) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, corpse.transform.position, planetCenter, planetRadius);
            if (dist <= nearestDist)
            {
                nearestDist = dist;
                nearest = corpse;
            }
        }
        return nearest;
    }

    /// Utility-style drive blend: movement each tick is a weighted average of whichever
    /// drives are currently active, weighted by each drive's urgency (0-1). Adding a new
    /// drive later (thirst, safety, mate-seeking) means adding one more (weight, direction)
    /// term here - no other movement code needs to change.
    private Vector3 ComputeMovementTangentToCorpse(CorpseItem corpseTarget)
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 currentTangent = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
        if (currentTangent.sqrMagnitude < 0.0001f)
        {
            currentTangent = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        }

        // Baseline explore drive: always present at a fixed weight so the agent keeps
        // moving even when every other drive's urgency is near zero.
        float randomTurn = Random.Range(-wanderTurnRate, wanderTurnRate) * Time.deltaTime;
        Vector3 exploreDirection = (Quaternion.AngleAxis(randomTurn, normal) * currentTangent).normalized;
        const float exploreWeight = 0.3f;

        // Comfort drive: pulls toward whichever nearby direction best matches climate
        // preference, strength scaling with current discomfort.
        float discomfort = GetDiscomfort(transform.position);
        float comfortWeight = Mathf.Clamp01(Mathf.Sqrt(discomfort) * comfortSeekingStrength);
        Vector3 comfortDirection = FindMostComfortableDirection(currentTangent, normal, discomfort);

        // Hunger drive: pulls toward a sensed corpse, strength scaling with how close to
        // starving the agent is (so a well-fed agent ignores distant carrion and stays
        // near comfortable terrain; a near-starving one chases it regardless of biome).
        float hungerWeight = 0f;
        Vector3 hungerDirection = exploreDirection;
        if (corpseTarget != null)
        {
            hungerWeight = Mathf.Clamp01(_timeSinceLastMeal / starvationTime);
            hungerDirection = SphereSurface.TangentDirectionTo(transform.position, corpseTarget.transform.position, planetCenter);
        }

        Vector3 weightedSum = exploreWeight * exploreDirection + comfortWeight * comfortDirection + hungerWeight * hungerDirection;
        Vector3 tangentSum = weightedSum - Vector3.Dot(weightedSum, normal) * normal; // re-project onto tangent plane

        if (tangentSum.sqrMagnitude < 0.0001f) return exploreDirection;
        return tangentSum.normalized;
    }

    /// Samples a handful of candidate directions around the current heading and returns
    /// whichever reduces climate discomfort most (falls back to the current heading if
    /// nothing nearby is better, e.g. already standing in the best local spot).
    private Vector3 FindMostComfortableDirection(Vector3 currentTangent, Vector3 normal, float currentDiscomfort)
    {
        Vector3 bestDirection = currentTangent;
        float bestDiscomfort = currentDiscomfort;

        const int sampleCount = 5;
        for (int i = 0; i < sampleCount; i++)
        {
            float angle = (360f / sampleCount) * i;
            Quaternion sampleRot = Quaternion.AngleAxis(angle, normal);
            Vector3 candidateTangent = (sampleRot * currentTangent).normalized;
            Vector3 candidatePos = SphereSurface.MoveAlongSurface(transform.position, candidateTangent, comfortSampleDistance, planetCenter, planetRadius);

            float candidateDiscomfort = GetDiscomfort(candidatePos);
            if (candidateDiscomfort < bestDiscomfort)
            {
                bestDiscomfort = candidateDiscomfort;
                bestDirection = candidateTangent;
            }
        }

        return bestDirection;
    }

    // -------------------------------------------------------------------------
    // Flee / fight system
    // -------------------------------------------------------------------------

    /// Nearest motile heterotroph (predator) within threat range, or null.
    /// Search radius is 1.5× sense radius — organisms detect danger slightly
    /// further than they can hunt, giving time to start fleeing.
    private AgentController FindNearestPredatorInRange()
    {
        if (_spawner == null) return null;
        if (_threatCycle == _senseCycle) return ValidCached(_cachedThreat); // memoized this sense cycle
        float threatRange = senseRadius * 1.5f;
        AgentController nearest = null;
        float nearestDist = threatRange;
        _spawner.QueryNearby(transform.position, threatRange, _queryBuffer);
        foreach (var other in _queryBuffer)
        {
            if (other == null || other == this) continue;
            if (other.Metabolism != MetabolismType.Heterotrophic || !other.HasMotility) continue;
            if (other.communityId == communityId) continue; // own community never threatens
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist) { nearestDist = dist; nearest = other; }
        }
        _threatCycle = _senseCycle; _cachedThreat = nearest;
        return nearest;
    }

    /// Tangent direction directly away from the predator along the sphere surface.
    private Vector3 ComputeFleeDirection(AgentController predator)
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 toward = SphereSurface.TangentDirectionTo(transform.position, predator.transform.position, planetCenter);
        Vector3 away = -toward;
        return (away - Vector3.Dot(away, normal) * normal).normalized;
    }

    /// Called by UpdateProducer / UpdatePassiveDrift to refresh flee target and return
    /// a flee drive weight (0 = no threat, 1 = immediate danger, strength-discounted
    /// for organisms that are strong enough to stand their ground).
    private float RefreshFleeState(out Vector3 fleeDir)
    {
        fleeDir = _heading;

        // Decay cooldown — keep fleeing briefly after losing sight of predator.
        if (_fleeCooldown > 0f) _fleeCooldown -= Time.deltaTime;

        AgentController threat = FindNearestPredatorInRange();
        if (threat != null)
        {
            _fleeTarget   = threat;
            _fleeCooldown = 3f; // continue fleeing for 3 s after threat leaves range
        }
        else if (_fleeCooldown <= 0f)
        {
            _fleeTarget = null;
        }

        if (_fleeTarget == null || !_fleeTarget.gameObject) return 0f;

        fleeDir = ComputeFleeDirection(_fleeTarget);

        // Flee weight: scales with proximity (closer = more urgent) and with how much
        // WEAKER this organism is vs the predator (equal strength → 0.5 baseline;
        // much weaker → 1.0; much stronger → fight, so low flee weight).
        float dist = SphereSurface.SurfaceDistance(transform.position, _fleeTarget.transform.position, planetCenter, planetRadius);
        float proximityUrgency = Mathf.Clamp01(1f - dist / (senseRadius * 1.5f));
        float strengthDisadvantage = Mathf.Clamp01(0.5f + (_fleeTarget.strengthTrait - strengthTrait) / 100f);
        // era3-primitives-spec §2.2: boldness lowers flee eagerness — a bold organism keeps foraging
        // longer in an exposed/risky patch instead of fleeing early (more time feeding, more time
        // exposed); a shy one flees sooner. This is the real predation-risk side of the trade — bold
        // organisms genuinely die to predation more often, not just cosmetically "act brave."
        float boldnessDamping = Mathf.Lerp(1.25f, 0.7f, Mathf.Clamp01(boldness / 100f));
        return proximityUrgency * strengthDisadvantage * boldnessDamping;
    }

    /// Combat resolution when a predator tries to eat this organism.
    /// Returns true if predator succeeds (prey dies), false if prey escapes or fights back.
    /// Strong prey can counter-kill a weak predator.
    public bool ResolvePredatorAttack(AgentController attacker)
    {
        // Effective defense folds hardiness into strength: a tough (high-hardiness) organism is
        // harder to bring down even at equal raw strength, giving hardiness a real predation role.
        float preyDefense = strengthTrait + hardinessTrait * 0.5f;

        // Body SIZE matters too: a larger organism is harder to subdue and a larger attacker hits
        // harder. Fold the mass ratio into the balance (~±30 at a 2× size difference, comparable in
        // weight to a large strength gap) so predation is a fight over size AND strength, not strength
        // alone — a big, dull-witted grazer can shrug off a small sharp predator, and vice versa.
        float sizeAdvantage = Mathf.Clamp((attacker._currentMass / Mathf.Max(_currentMass, 0.001f)) - 1f, -1f, 1f) * 30f;
        float diff = (attacker.strengthTrait - preyDefense) + sizeAdvantage; // positive = attacker favored

        // Very strong prey can fight back and kill the attacker (raw strength, counter-attack) — a
        // decisive size edge counts toward that too (a much larger prey can turn on a small attacker).
        if (strengthTrait + Mathf.Max(0f, -sizeAdvantage) > attacker.strengthTrait + 40f)
        {
            // Prey kills attacker — role reversal.
            attacker.Die(DeathCause.Unknown);
            Debug.Log($"[Combat] {name} (str={strengthTrait:F0}) killed attacker {attacker.name} (str={attacker.strengthTrait:F0}).");
            return false; // prey survives
        }

        // Escape probability: 50% when evenly matched (attacker strength ≈ prey defense), 0% when
        // attacker is 50+ ahead, 100% when prey defense is 50+ ahead (but below the counter-kill line).
        float escapeProbability = Mathf.Clamp01(0.5f - diff / 100f);

        if (Random.value < escapeProbability)
        {
            // Prey escapes: gets a brief speed burst and flee cooldown.
            _fleeCooldown = 5f;
            _fleeTarget   = attacker;
            Debug.Log($"[Combat] {name} escaped {attacker.name} (escape chance {escapeProbability:P0}).");
            return false; // prey survives
        }

        return true; // predator kills prey
    }

    private void AlignToSurface()
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        transform.up = normal;
        if (_heading.sqrMagnitude > 0.0001f)
        {
            Vector3 tangentHeading = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
            if (tangentHeading.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(tangentHeading, normal);
            }
        }
    }
}
